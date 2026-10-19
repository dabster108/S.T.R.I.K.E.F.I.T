// npm run dev:safe: the whole stack against the LOCAL dev database only.
//   1. starts (or reuses) the Docker Postgres            (scripts/dev-db.js)
//   2. starts the backend on :4000 as a development process (envLoader.js refuses any remote DATABASE_URL)
//   3. seeds ZZTEST simulator fights if there are none     (scripts/seed-dev.js; --no-seed skips it)
//   4. starts the Vite dev server on :5173 proxying /api to that backend
// Ctrl+C stops the backend and the frontend; the database container keeps running (npm run dev:db -- stop).

const { spawn, spawnSync } = require("child_process");
const path = require("path");

const backendDir = path.join(__dirname, "..");
const frontendDir = path.join(backendDir, "..", "frontend");
const noSeed = process.argv.includes("--no-seed");
const PORT = process.env.PORT || "4000";
const WEB_PORT = process.env.WEB_PORT || "5173";
const net = require("net");

// A clean environment for the children: nothing that could point them at production gets through.
const childEnv = { ...process.env, APP_ENV: "development", NODE_ENV: "development", PORT, BACKEND_URL: `http://localhost:${PORT}` };
for (const k of ["DATABASE_URL", "VITE_API_URL", "ALLOW_PROD_DB", "RENDER"]) delete childEnv[k];

const portFree = (port) =>
  new Promise((resolve) => {
    const s = net.createServer();
    s.once("error", () => resolve(false));
    s.once("listening", () => s.close(() => resolve(true)));
    s.listen(Number(port));
  });

(async () => {
  for (const [label, port] of [["API", PORT], ["site", WEB_PORT]]) {
    if (!(await portFree(port))) {
      console.error(
        `[dev:safe] port ${port} (${label}) is already in use by another process, so nothing was started.\n` +
          `           Stop that process (see: lsof -nP -iTCP:${port} -sTCP:LISTEN), or use other ports:\n` +
          `           PORT=4100 WEB_PORT=5273 npm run dev:safe`
      );
      process.exit(1);
    }
  }
  start();
})();

function start() {
const db = spawnSync("node", [path.join(__dirname, "dev-db.js")], { stdio: "inherit" });
if (db.status !== 0) process.exit(db.status || 1);

const kids = [];
const run = (label, cmd, args, cwd, extra = {}) => {
  const p = spawn(cmd, args, { cwd, env: { ...childEnv, ...extra }, stdio: ["ignore", "pipe", "pipe"] });
  const tag = (d) => String(d).split("\n").filter(Boolean).forEach((l) => console.log(`[${label}] ${l}`));
  p.stdout.on("data", tag);
  p.stderr.on("data", tag);
  p.on("exit", (code) => {
    console.log(`[${label}] exited (${code})`);
    shutdown(code || 0);
  });
  kids.push(p);
  return p;
};
let closing = false;
function shutdown(code = 0) {
  if (closing) return;
  closing = true;
  kids.forEach((k) => k.kill("SIGTERM"));
  setTimeout(() => process.exit(code), 400);
}
process.on("SIGINT", () => shutdown(0));
process.on("SIGTERM", () => shutdown(0));

run("api", "node", ["server.js"], backendDir);

(async () => {
  for (let i = 0; i < 60; i++) {
    const ok = await fetch(`http://localhost:${PORT}/api/health`).then((r) => r.ok).catch(() => false);
    if (ok) break;
    await new Promise((r) => setTimeout(r, 500));
  }
  if (!noSeed) spawnSync("node", [path.join(__dirname, "seed-dev.js"), "--url", `http://localhost:${PORT}`], { stdio: "inherit", env: childEnv });
  run("web", "npx", ["vite", "--port", WEB_PORT, "--strictPort"], frontendDir);
  console.log(`\n  STRIKEFIT dev (local database only)\n    site + dashboard  http://localhost:${WEB_PORT}\n    API               http://localhost:${PORT}\n    Live Arena        http://localhost:${WEB_PORT}/dashboard/live\n    play a fake fight npm run simulate -- --url http://localhost:${PORT} --player ZZTEST-me --level Hard --seconds 70\n    Ctrl+C to stop    (database keeps running: npm run dev:db -- stop)\n`);
})();
}
