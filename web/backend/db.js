const path = require("path");
const { Pool } = require("pg");

// Local dev reads web/backend/.env; on Render the variable comes from the dashboard
// and there is no .env file, which dotenv treats as a no-op. Existing env vars win.
require("dotenv").config({ path: path.join(__dirname, ".env"), quiet: true });

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

// One small shared pool: Neon is serverless and its pooled endpoint multiplexes for us.
// Neon serves publicly trusted certificates, so the certificate is verified.
const pool = new Pool({
  connectionString: connectionUrl.toString(),
  ssl: isLocal ? false : { rejectUnauthorized: true },
  max: 5,
  idleTimeoutMillis: 30_000,
  connectionTimeoutMillis: 10_000,
});

// Neon drops idle connections when the compute scales to zero; without a listener
// that error would crash the process. The pool discards the dead client and reconnects.
pool.on("error", (err) => console.error("[db] idle client error:", err.message));

function query(text, params) {
  return pool.query(text, params);
}

// Runs fn(client) inside BEGIN/COMMIT, rolling back if it throws.
async function withTransaction(fn) {
  const client = await pool.connect();
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
