// npm run dev:safe: the whole stack against the LOCAL dev database only. No Docker.
//   1. starts the local PGlite database on 127.0.0.1:54329 (scripts/dev-db.js; data in web/backend/.dev-db)
//   2. starts the backend as a development process (envLoader.js refuses any remote DATABASE_URL)
//   3. seeds ZZTEST simulator fights if there are none     (scripts/seed-dev.js; --no-seed skips it)
//   4. starts the Vite dev server proxying /api to that backend
// Ctrl+C stops all three.

const { spawn, spawnSync } = require("child_process");
const net = require("net");
const path = require("path");

const backendDir = path.join(__dirname, "..");
const frontendDir = path.join(backendDir, "..", "frontend");
const noSeed = process.argv.includes("--no-seed");
const PORT = process.env.PORT || "4000";
const WEB_PORT = process.env.WEB_PORT || "5173";
const DB_PORT = "54329";

// A clean environment for the children: nothing that could point them at production gets through.
const childEnv = { ...process.env, APP_ENV: "development", NODE_ENV: "development", PORT, BACKEND_URL: `http://localhost:${PORT}` };
for (const k of ["DATABASE_URL", "VITE_API_URL", "ALLOW_PROD_DB", "RENDER"]) delete childEnv[k];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const listening = (port) =>
  new Promise((resolve) => {
    const s = net.connect({ host: "127.0.0.1", port: Number(port) });
    s.once("connect", () => (s.destroy(), resolve(true)));
    s.once("error", () => resolve(false));
  });
const portFree = (port) =>
  new Promise((resolve) => {
    const s = net.createServer();
    s.once("error", () => resolve(false));
    s.once("listening", () => s.close(() => resolve(true)));
    s.listen(Number(port));
  });

const kids = [];
let closing = false;
function shutdown(code = 0) {
  if (closing) return;
  closing = true;
  // the API and the site first, the database last
  for (const k of [...kids].reverse()) k.kill("SIGTERM");
  setTimeout(() => process.exit(code), 800);
}
process.on("SIGINT", () => shutdown(0));
process.on("SIGTERM", () => shutdown(0));

function run(label, cmd, args, cwd) {
  const p = spawn(cmd, args, { cwd, env: childEnv, stdio: ["ignore", "pipe", "pipe"] });
  const tag = (d) => String(d).split("\n").filter(Boolean).forEach((l) => console.log(`[${label}] ${l}`));
  p.stdout.on("data", tag);
  p.stderr.on("data", tag);
  p.on("exit", (code) => {
    console.log(`[${label}] exited (${code})`);
    shutdown(code || 0);
  });
  kids.push(p);
  return p;
}

async function waitFor(check, tries, ms) {
  for (let i = 0; i < tries; i++) {
    if (await check()) return true;
    await sleep(ms);
  }
  return false;
}

(async () => {
  for (const [label, port] of [["database", DB_PORT], ["API", PORT], ["site", WEB_PORT]]) {
    if (!(await portFree(port))) {
      console.error(
        `[dev:safe] port ${port} (${label}) is already in use by another process, so nothing was started.\n` +
          `           Stop that process (see: lsof -nP -iTCP:${port} -sTCP:LISTEN)` +
          (label === "database" ? "." : `, or use other ports:\n           PORT=4100 WEB_PORT=5273 npm run dev:safe`)
      );
      process.exit(1);
    }
  }

  run("db", "node", [path.join(__dirname, "dev-db.js")], backendDir);
  if (!(await waitFor(() => listening(DB_PORT), 120, 250))) {
    console.error("[dev:safe] the dev database did not start (see the [db] lines above)");
    return shutdown(1);
  }

  run("api", "node", ["server.js"], backendDir);
  if (!(await waitFor(() => fetch(`http://localhost:${PORT}/api/health`).then((r) => r.ok).catch(() => false), 60, 500))) {
    console.error("[dev:safe] the API did not become healthy (see the [api] lines above)");
    return shutdown(1);
  }
  if (!noSeed) spawnSync("node", [path.join(__dirname, "seed-dev.js"), "--url", `http://localhost:${PORT}`], { stdio: "inherit", env: childEnv });

  run("web", "npx", ["vite", "--port", WEB_PORT, "--strictPort"], frontendDir);
  console.log(
    `\n  STRIKEFIT dev (local database only)\n    site + dashboard  http://localhost:${WEB_PORT}\n    API               http://localhost:${PORT}\n    Live Arena        http://localhost:${WEB_PORT}/dashboard/live\n` +
      `    play a fake fight npm run simulate -- --url http://localhost:${PORT} --player ZZTEST-me --level Hard --seconds 70\n    Ctrl+C to stop    (database, API and site)\n`
  );
})();
