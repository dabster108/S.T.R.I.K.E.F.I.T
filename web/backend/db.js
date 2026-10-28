const { Pool } = require("pg");
const envLoader = require("./envLoader");
const { sanitize, withRetry } = require("./dbRetry");

// Production (Render): web/backend/.env if present, else the platform's variables, exactly as before. On a laptop: .env.development,
// and a remote DATABASE_URL (the production Neon string in .env) is refused unless ALLOW_PROD_DB=1. See envLoader.js.
envLoader.loadEnv(__dirname);
envLoader.enforce();

if (!process.env.DATABASE_URL) {
  console.error("[db] DATABASE_URL is not set — add it to web/backend/.env locally or to the Render environment.");
  process.exit(1);
}

// Neon URLs carry sslmode=require, which pg would let override the ssl option below
// (with a deprecation warning on every start). Drop it so TLS is configured in one place.
const connectionUrl = new URL(process.env.DATABASE_URL);
connectionUrl.searchParams.delete("sslmode");

// A Postgres on this machine (local testing) has no TLS; everything else must use it.
const isLocal = ["localhost", "127.0.0.1", "::1"].includes(connectionUrl.hostname);
const verifyTlsCert = !/^(0|false|no|off)$/i.test(String(process.env.DB_SSL_REJECT_UNAUTHORIZED ?? "true").trim());

// One small shared pool for the whole backend (a Render free instance and a database that scales to zero need few connections). The
// provider (Neon, Layerbase) serves a publicly trusted certificate, so the certificate is always verified. Idle clients are closed after
// 10 s, so the pool never holds a connection open that would keep the database awake.
const pool = new Pool({
  connectionString: connectionUrl.toString(),
  ssl: isLocal ? false : { rejectUnauthorized: verifyTlsCert },
  // Development sets DB_POOL_MAX=1: the local PGlite database is single-session, and one connection keeps transactions whole.
  max: Number(process.env.DB_POOL_MAX) > 0 ? Number(process.env.DB_POOL_MAX) : 5,
  min: 0, // do not keep idle clients alive on a sleeping database
  idleTimeoutMillis: 10_000,
  // A cold database can take time to wake up; each attempt waits up to 30 s.
  connectionTimeoutMillis: 30_000,
});

// A database that scales to zero drops idle connections; without a listener that error would crash the process.
// The pool discards the dead client and reconnects.
pool.on("error", (err) => console.error("[db] idle client error:", sanitize(err.message)));

// Getting a connection is the only step that is retried (a few times, with a growing pause): nothing has been sent to the database yet, so
// trying again cannot repeat a write. A failure while a statement is running is never retried here.
function acquire() {
  return withRetry(() => pool.connect(), {
    attempts: 5,
    baseMs: 2_000,
    onRetry: ({ attempt, delayMs, error }) => console.warn(`[db] could not connect (attempt ${attempt}), retrying in ${delayMs} ms: ${error.code || ""} ${sanitize(error.message)}`.trim()),
  });
}

async function query(text, params) {
  const client = await acquire();
  try {
    const result = await client.query(text, params);
    client.release();
    return result;
  } catch (err) {
    client.release(err); // a failed statement may have left the connection unusable: drop it, as pool.query does
    throw err;
  }
}

// Runs fn(client) inside BEGIN/COMMIT, rolling back if it throws.
async function withTransaction(fn) {
  const client = await acquire();
  try {
    await client.query("BEGIN");
    const result = await fn(client);
    await client.query("COMMIT");
    return result;
  } catch (err) {
    await client.query("ROLLBACK").catch(() => {});
    throw err;
  } finally {
    client.release();
  }
}

module.exports = { pool, query, withTransaction };
