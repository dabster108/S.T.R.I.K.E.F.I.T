const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const loader = require("../envLoader");

const PROD_URL = "postgres://user:pw@ep-cool-name-123456.eu-central-1.aws.neon.tech/neondb?sslmode=require";
const LOCAL_URL = "postgres://strikefit:strikefit@127.0.0.1:54329/strikefit_dev";
const tmp = (files) => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "sf-env-"));
  for (const [name, text] of Object.entries(files)) fs.writeFileSync(path.join(dir, name), text);
  return dir;
};

test("a development process accepts only this machine", () => {
  for (const host of ["localhost", "127.0.0.1", "[::1]", "host.docker.internal"]) {
    assert.equal(loader.checkDatabaseUrl(`postgres://u:p@${host}:5432/db`, {}).ok, true, host);
  }
  const prod = loader.checkDatabaseUrl(PROD_URL, {});
  assert.equal(prod.ok, false);
  assert.equal(prod.host, "ep-cool-name-123456.eu-central-1.aws.neon.tech");
  assert.match(prod.reason, /remote database/);
  assert.equal(loader.checkDatabaseUrl("not a url", {}).ok, false);
});

test("a dev-only remote host can be allowed by name, and only that host", () => {
  const env = { DEV_DB_ALLOWED_HOSTS: "ep-dev-branch-1.eu-central-1.aws.neon.tech, other.example.com" };
  assert.equal(loader.checkDatabaseUrl("postgres://u:p@ep-dev-branch-1.eu-central-1.aws.neon.tech/db", env).ok, true);
  assert.equal(loader.checkDatabaseUrl(PROD_URL, env).ok, false);
});

test("ALLOW_PROD_DB=1 is the explicit override and is flagged", () => {
  const c = loader.checkDatabaseUrl(PROD_URL, { ALLOW_PROD_DB: "1" });
  assert.equal(c.ok, true);
  assert.equal(c.overridden, true);
  assert.equal(loader.checkDatabaseUrl(PROD_URL, { ALLOW_PROD_DB: "true" }).ok, false); // exactly "1"
});

test("enforce exits on the production host and stays quiet for a local one", () => {
  const exits = [];
  const logs = [];
  const r = loader.enforce({ DATABASE_URL: PROD_URL }, (c) => exits.push(c), (m) => logs.push(m));
  assert.equal(r.ok, false);
  assert.deepEqual(exits, [1]);
  assert.match(logs.join("\n"), /REFUSING TO START/);
  assert.match(logs.join("\n"), /ALLOW_PROD_DB=1/);
  const ok = loader.enforce({ DATABASE_URL: LOCAL_URL }, (c) => exits.push(c), (m) => logs.push(m));
  assert.equal(ok.ok, true);
  assert.deepEqual(exits, [1]);
  const none = [];
  loader.enforce({}, (c) => none.push(c), () => {});
  assert.deepEqual(none, [1]); // no DATABASE_URL in development is also a stop, with a hint to use dev:safe
});

test("production (Render, NODE_ENV or APP_ENV) is never blocked", () => {
  for (const env of [{ RENDER: "true" }, { NODE_ENV: "production" }, { APP_ENV: "production" }]) {
    assert.equal(loader.isProduction(env), true);
    const exits = [];
    const r = loader.enforce({ ...env, DATABASE_URL: PROD_URL }, (c) => exits.push(c), () => {});
    assert.equal(r.ok, true);
    assert.deepEqual(exits, []);
  }
  assert.equal(loader.isProduction({}), false);
});

test("development reads .env.development and takes everything from .env EXCEPT its DATABASE_URL", () => {
  const dir = tmp({ ".env": `DATABASE_URL=${PROD_URL}\nBROADCAST_KEY=abc\n`, ".env.development": `DATABASE_URL=${LOCAL_URL}\nPORT=4000\n` });
  const env = {};
  assert.deepEqual(loader.loadEnv(dir, env), { mode: "development", file: ".env.development" });
  assert.equal(env.DATABASE_URL, LOCAL_URL);
  assert.equal(env.BROADCAST_KEY, "abc");
});

test("development with no .env.development leaves DATABASE_URL unset, so .env's production string is never used", () => {
  const dir = tmp({ ".env": `DATABASE_URL=${PROD_URL}\nBROADCAST_KEY=abc\n` });
  const env = {};
  const r = loader.loadEnv(dir, env);
  assert.equal(r.file, null);
  assert.equal(env.DATABASE_URL, undefined);
  assert.equal(env.BROADCAST_KEY, "abc");
});

test("production reads .env exactly as before, and variables already set win", () => {
  const dir = tmp({ ".env": `DATABASE_URL=${PROD_URL}\nBROADCAST_KEY=abc\n`, ".env.development": `DATABASE_URL=${LOCAL_URL}\n` });
  const env = { RENDER: "true", BROADCAST_KEY: "from-dashboard" };
  assert.deepEqual(loader.loadEnv(dir, env), { mode: "production", file: ".env" });
  assert.equal(env.DATABASE_URL, PROD_URL);
  assert.equal(env.BROADCAST_KEY, "from-dashboard");
});

test("an exported production URL in the shell still cannot reach a development process", () => {
  const dir = tmp({ ".env.development": `DATABASE_URL=${LOCAL_URL}\n` });
  const env = { DATABASE_URL: PROD_URL }; // e.g. `export DATABASE_URL=...` in the shell profile
  loader.loadEnv(dir, env);
  const exits = [];
  assert.equal(loader.enforce(env, (c) => exits.push(c), () => {}).ok, false);
  assert.deepEqual(exits, [1]);
});
