// A throwaway local Postgres in Docker for development, so nothing local ever touches the production database.
//
//   npm run dev:db            start it (creates it the first time) and write .env.development
//   npm run dev:db -- stop    stop it (data is kept in the Docker volume)
//   npm run dev:db -- reset   delete it and its data, then start a fresh empty one
//   npm run dev:db -- status
//
// Container strikefit-dev-pg, image postgres:16-alpine, bound to 127.0.0.1:54329 only. The credentials are local-only and
// meaningless anywhere else. The server applies schema.sql on boot, so a fresh database needs nothing else.

const { spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const NAME = "strikefit-dev-pg";
const VOLUME = "strikefit-dev-pgdata";
const IMAGE = "postgres:16-alpine";
const PORT = 54329;
const URL = `postgres://strikefit:strikefit@127.0.0.1:${PORT}/strikefit_dev`;
const ENV_FILE = path.join(__dirname, "..", ".env.development");

const docker = (...args) => spawnSync("docker", args, { encoding: "utf8" });
const out = (r) => `${r.stdout || ""}${r.stderr || ""}`.trim();

function requireDocker() {
  const r = docker("info", "--format", "{{.ServerVersion}}");
  if (r.error || r.status !== 0) {
    console.error("[dev-db] Docker is not running. Start Docker Desktop, then run this again.\n" + out(r));
    process.exit(1);
  }
}

const state = () => {
  const r = docker("inspect", "-f", "{{.State.Running}}", NAME);
  return r.status !== 0 ? "missing" : out(r) === "true" ? "running" : "stopped";
};

function writeEnvFile() {
  if (fs.existsSync(ENV_FILE)) return false;
  fs.writeFileSync(
    ENV_FILE,
    `# Local development only (git-ignored). Created by npm run dev:db. A development process refuses any remote DATABASE_URL.\nDATABASE_URL=${URL}\nPORT=4000\n`
  );
  return true;
}

async function waitReady() {
  for (let i = 0; i < 60; i++) {
    const r = docker("exec", NAME, "pg_isready", "-U", "strikefit", "-d", "strikefit_dev");
    if (r.status === 0) return;
    await new Promise((res) => setTimeout(res, 500));
  }
  console.error("[dev-db] Postgres did not become ready in 30 s. See: docker logs " + NAME);
  process.exit(1);
}

async function up() {
  requireDocker();
  const s = state();
  if (s === "missing") {
    console.log(`[dev-db] creating ${NAME} (${IMAGE}, 127.0.0.1:${PORT}); the first run downloads the image`);
    const r = docker("run", "-d", "--name", NAME, "-p", `127.0.0.1:${PORT}:5432`, "-e", "POSTGRES_USER=strikefit", "-e", "POSTGRES_PASSWORD=strikefit", "-e", "POSTGRES_DB=strikefit_dev", "-v", `${VOLUME}:/var/lib/postgresql/data`, IMAGE);
    if (r.status !== 0) {
      console.error("[dev-db] could not create the container:\n" + out(r));
      process.exit(1);
    }
  } else if (s === "stopped") {
    console.log(`[dev-db] starting ${NAME}`);
    docker("start", NAME);
  }
  await waitReady();
  const made = writeEnvFile();
  console.log(`[dev-db] ready: ${URL}${made ? "\n[dev-db] wrote web/backend/.env.development" : ""}`);
}

(async () => {
  const cmd = process.argv[2] || "up";
  if (cmd === "up") return up();
  requireDocker();
  if (cmd === "status") return console.log(`[dev-db] ${NAME}: ${state()}`);
  if (cmd === "stop") return console.log(state() === "running" ? out(docker("stop", NAME)) + " stopped" : "[dev-db] not running");
  if (cmd === "reset") {
    docker("rm", "-f", NAME);
    docker("volume", "rm", VOLUME);
    console.log("[dev-db] removed the dev database and its data");
    return up();
  }
  console.error(`[dev-db] unknown command "${cmd}" (up | stop | reset | status)`);
  process.exit(1);
})();
