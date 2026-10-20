// The local development database: PGlite (real Postgres compiled to WebAssembly, an npm dev dependency) served on
// 127.0.0.1:54329 over the normal Postgres protocol, so the backend's `pg` pool talks to it exactly like to Neon.
// No Docker, no system install, no background service: the database runs while this process runs.
//
//   npm run dev:db            run the database in this terminal (Ctrl+C stops it; npm run dev:safe starts it for you)
//   npm run dev:db -- reset   delete the local data (web/backend/.dev-db) so the next start is empty
//   npm run dev:db -- status  is something listening on the port?
//
// Data lives in web/backend/.dev-db (git-ignored). The server applies schema.sql on boot, so an empty database needs nothing else.
// PGlite is a single-session database: the socket server queues queries from up to MAX_CONNECTIONS clients, and the backend
// uses ONE pooled connection in development (DB_POOL_MAX=1 in .env.development) so a transaction is never interleaved.

const fs = require("fs");
const net = require("net");
const path = require("path");

const HOST = "127.0.0.1";
const PORT = Number(process.env.DEV_DB_PORT) || 54329;
const MAX_CONNECTIONS = 4; // the backend (1) plus a script or psql
const DATA_DIR = path.join(__dirname, "..", ".dev-db");
const URL = `postgres://postgres:postgres@${HOST}:${PORT}/postgres`;
const ENV_FILE = path.join(__dirname, "..", ".env.development");
const MARKER = "Created by npm run dev:db";

function writeEnvFile() {
  const want = `# Local development only (git-ignored). ${MARKER}. A development process refuses any remote DATABASE_URL.\nDATABASE_URL=${URL}\nDB_POOL_MAX=1\nPORT=4000\n`;
  const have = fs.existsSync(ENV_FILE) ? fs.readFileSync(ENV_FILE, "utf8") : null;
  if (have === null || (have.includes(MARKER) && have !== want)) {
    fs.writeFileSync(ENV_FILE, want);
    return true;
  }
  return false; // a hand-written .env.development is left alone
}

const portInUse = () =>
  new Promise((resolve) => {
    const s = net.connect({ host: HOST, port: PORT });
    s.once("connect", () => (s.destroy(), resolve(true)));
    s.once("error", () => resolve(false));
  });

async function up() {
  if (await portInUse()) {
    console.error(`[dev-db] port ${PORT} is already in use (is the dev database already running?)`);
    process.exit(1);
  }
  const { PGlite } = await import("@electric-sql/pglite");
  const { PGLiteSocketServer } = await import("@electric-sql/pglite-socket");
  fs.mkdirSync(DATA_DIR, { recursive: true });
  const db = await PGlite.create({ dataDir: DATA_DIR });
  const server = new PGLiteSocketServer({ db, host: HOST, port: PORT, maxConnections: MAX_CONNECTIONS });
  await server.start();
  const wrote = writeEnvFile();
  console.log(`[dev-db] PGlite ready: ${URL}  (data: web/backend/.dev-db)${wrote ? "\n[dev-db] wrote web/backend/.env.development" : ""}`);
  let stopping = false;
  const stop = async () => {
    if (stopping) return;
    stopping = true;
    await server.stop().catch(() => {});
    await db.close().catch(() => {});
    console.log("[dev-db] stopped");
    process.exit(0);
  };
  process.on("SIGINT", stop);
  process.on("SIGTERM", stop);
}

(async () => {
  const cmd = process.argv[2] || "up";
  if (cmd === "up") return up();
  if (cmd === "status") return console.log(`[dev-db] ${HOST}:${PORT} ${(await portInUse()) ? "is listening" : "is not running"}`);
  if (cmd === "reset") {
    if (await portInUse()) {
      console.error("[dev-db] stop the running dev database first (Ctrl+C in its terminal, or stop npm run dev:safe)");
      process.exit(1);
    }
    fs.rmSync(DATA_DIR, { recursive: true, force: true });
    return console.log("[dev-db] deleted web/backend/.dev-db: the next start is an empty database");
  }
  console.error(`[dev-db] unknown command "${cmd}" (up | reset | status)`);
  process.exit(1);
})().catch((e) => {
  console.error("[dev-db] failed:", e.message);
  process.exit(1);
});
