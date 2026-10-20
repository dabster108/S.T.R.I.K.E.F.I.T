const test = require("node:test");
const assert = require("node:assert/strict");
const { isTransient, sanitize, withRetry } = require("../dbRetry");

const err = (message, code) => Object.assign(new Error(message), code ? { code } : {});
const noSleep = () => Promise.resolve();

test("a database that is not ready is transient; a wrong password or a SQL mistake is not", () => {
  assert.equal(isTransient(err("connect ECONNREFUSED", "ECONNREFUSED")), true);
  assert.equal(isTransient(err("timeout exceeded when trying to connect")), true);
  assert.equal(isTransient(err("Connection terminated unexpectedly")), true);
  assert.equal(isTransient(err("the database system is starting up", "57P03")), true);
  assert.equal(isTransient(err("password authentication failed for user", "28P01")), false);
  assert.equal(isTransient(err('relation "x" does not exist', "42P01")), false);
  assert.equal(isTransient(err("self-signed certificate in certificate chain", "DEPTH_ZERO_SELF_SIGNED_CERT")), false); // never retried, never bypassed
  assert.equal(isTransient(null), false);
});

test("sanitize removes a connection string from a message", () => {
  const msg = sanitize("failed for postgresql://user:secret@host.example/db?sslmode=require now");
  assert.doesNotMatch(msg, /secret|host\.example/);
  assert.match(msg, /<connection string>/);
});

test("withRetry tries again after transient failures with a growing pause, then succeeds", async () => {
  let calls = 0;
  const pauses = [];
  const out = await withRetry(async () => {
    if (++calls < 3) throw err("timeout exceeded when trying to connect");
    return "connected";
  }, { attempts: 4, baseMs: 100, sleep: async (ms) => pauses.push(ms) });
  assert.equal(out, "connected");
  assert.equal(calls, 3);
  assert.deepEqual(pauses, [100, 200]);
});

test("withRetry gives up after the last attempt and throws the real error", async () => {
  let calls = 0;
  await assert.rejects(withRetry(async () => { calls++; throw err("connect ECONNREFUSED", "ECONNREFUSED"); }, { attempts: 3, sleep: noSleep }), /ECONNREFUSED/);
  assert.equal(calls, 3);
});

test("withRetry never retries an error that is not transient", async () => {
  let calls = 0;
  await assert.rejects(withRetry(async () => { calls++; throw err("password authentication failed", "28P01"); }, { sleep: noSleep }), /password/);
  assert.equal(calls, 1);
});

test("withRetry reports each retry without exposing anything but the error", async () => {
  const seen = [];
  await withRetry(async () => { if (!seen.length) throw err("Connection terminated unexpectedly"); return 1; }, { sleep: noSleep, onRetry: (r) => seen.push(r) });
  assert.equal(seen.length, 1);
  assert.equal(seen[0].attempt, 1);
});
