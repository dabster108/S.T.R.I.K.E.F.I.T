// Decides which environment file the backend reads and refuses to run a LOCAL process against a remote database.
//
//   production   Render (it always sets RENDER=true) or NODE_ENV=production or APP_ENV=production:
//                .env if present, otherwise the platform's own environment variables. Unchanged from before.
//   development  everything else (a laptop). Reads .env.development FIRST. web/backend/.env is still read for the other
//                keys it holds (BROADCAST_KEY, TURN, ...) but its DATABASE_URL is IGNORED, so the production connection
//                string in .env can never be picked up by accident.
//
// Guard: a development process only starts if DATABASE_URL points at this machine (localhost, 127.0.0.1, ::1)
// or at a host listed in DEV_DB_ALLOWED_HOSTS (comma separated, e.g. a Neon dev branch).
// Anything else, including the production Neon host, stops the process with a message, unless ALLOW_PROD_DB=1 is set.

const fs = require("fs");
const path = require("path");
const dotenv = require("dotenv");

const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1", "::1", "[::1]"]);

function isProduction(env = process.env) {
  return Boolean(env.RENDER) || env.NODE_ENV === "production" || env.APP_ENV === "production";
}

/** { ok, host, reason } for a connection string in a non-production process. Pure, so it can be tested. */
function checkDatabaseUrl(url, env = process.env) {
  let host;
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    return { ok: false, host: null, reason: "DATABASE_URL is not a valid connection string" };
  }
  const allowed = String(env.DEV_DB_ALLOWED_HOSTS || "").split(",").map((h) => h.trim().toLowerCase()).filter(Boolean);
  if (LOCAL_HOSTS.has(host) || allowed.includes(host)) return { ok: true, host };
  if (env.ALLOW_PROD_DB === "1") return { ok: true, host, overridden: true };
  return { ok: false, host, reason: `"${host}" is a remote database, not this machine and not in DEV_DB_ALLOWED_HOSTS` };
}

/** Reads the env files (see the header). Returns { mode, file } and never overwrites a variable that is already set. */
function loadEnv(dir = __dirname, env = process.env) {
  const read = (name) => (fs.existsSync(path.join(dir, name)) ? dotenv.parse(fs.readFileSync(path.join(dir, name))) : null);
  const apply = (parsed, skip = []) => {
    for (const [k, v] of Object.entries(parsed || {})) if (!skip.includes(k) && env[k] === undefined) env[k] = v;
  };
  if (isProduction(env)) {
    apply(read(".env"));
    return { mode: "production", file: ".env" };
  }
  const dev = read(".env.development");
  apply(dev);
  apply(read(".env"), ["DATABASE_URL"]); // never the production string
  return { mode: "development", file: dev ? ".env.development" : null };
}

/** Exits the process with an explanation when a development process was pointed at a remote database. */
function enforce(env = process.env, exit = (code) => process.exit(code), log = console.error) {
  if (isProduction(env)) return { ok: true, mode: "production" };
  if (!env.DATABASE_URL) {
    log(
      "[db] DATABASE_URL is not set for development. Run `npm run dev:safe` (starts the local PGlite database and the whole stack),\n" +
        "     or `npm run dev:db` in one terminal and `npm start` in another. web/backend/.env is deliberately NOT used for DATABASE_URL on a laptop."
    );
    exit(1);
    return { ok: false };
  }
  const c = checkDatabaseUrl(env.DATABASE_URL, env);
  if (!c.ok) {
    log(
      `[db] REFUSING TO START: ${c.reason}.\n` +
        "     This looks like the production database, and a local process must not read or write it.\n" +
        "     Use the local dev database (`npm run dev:safe`), or list a dev-only host in DEV_DB_ALLOWED_HOSTS.\n" +
        "     To override on purpose: ALLOW_PROD_DB=1."
    );
    exit(1);
    return { ok: false };
  }
  if (c.overridden) log(`[db] WARNING: ALLOW_PROD_DB=1, using the remote database ${c.host} from a development process.`);
  return { ok: true, mode: "development", host: c.host };
}

module.exports = { LOCAL_HOSTS, isProduction, checkDatabaseUrl, loadEnv, enforce };
