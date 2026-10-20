const test = require("node:test");
const assert = require("node:assert/strict");
const { createTtlCache } = require("../ttlCache");

test("an answer is reused until it is older than the time limit", async () => {
  let clock = 0;
  let calls = 0;
  const c = createTtlCache({ ttlMs: 30_000, now: () => clock });
  const load = async () => ++calls;
  assert.equal(await c.get("a", load), 1);
  clock = 29_999;
  assert.equal(await c.get("a", load), 1);
  clock = 30_000;
  assert.equal(await c.get("a", load), 2);
});

test("two requests for the same missing key share one computation", async () => {
  const c = createTtlCache({ ttlMs: 1000 });
  let calls = 0;
  const load = () => new Promise((r) => setTimeout(() => r(++calls), 20));
  const [x, y] = await Promise.all([c.get("k", load), c.get("k", load)]);
  assert.deepEqual([x, y, calls], [1, 1, 1]);
});

test("a failed computation is not cached", async () => {
  const c = createTtlCache({ ttlMs: 1000 });
  await assert.rejects(c.get("k", async () => { throw new Error("db down"); }), /db down/);
  assert.equal(await c.get("k", async () => "ok"), "ok");
});

test("invalidate drops by prefix", async () => {
  const c = createTtlCache({ ttlMs: 1000 });
  let n = 0;
  await c.get("rank:Ava", async () => ++n);
  await c.get("rank:Ben", async () => ++n);
  c.invalidate("rank:Ava");
  assert.equal(await c.get("rank:Ava", async () => ++n), 3);
  assert.equal(await c.get("rank:Ben", async () => ++n), 2); // untouched
  c.invalidate();
  assert.equal(c.size(), 0);
});

test("the cache stays bounded", async () => {
  const c = createTtlCache({ ttlMs: 1000, max: 3 });
  for (let i = 0; i < 10; i++) await c.get(`k${i}`, async () => i);
  assert.ok(c.size() <= 3);
});
