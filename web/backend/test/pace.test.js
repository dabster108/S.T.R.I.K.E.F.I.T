const test = require("node:test");
const assert = require("node:assert/strict");
const http = require("node:http");
const express = require("express");
const pace = require("../pace");
const effort = require("../effort");

// A real Express app + real HTTP, with the database replaced by a counting fake.
async function serve(loadBest, cache) {
  const app = express();
  app.get("/api/players/:name/best-pace", pace.createHandler({ loadBest, cache }));
  const server = http.createServer(app);
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const base = `http://127.0.0.1:${server.address().port}`;
  const get = async (path) => {
    const res = await fetch(base + path);
    return { status: res.status, body: await res.json() };
  };
  return { get, close: () => new Promise((r) => server.close(r)) };
}

const BEST = { best: { id: "abc", score: 900, outcome: "victory", durationSec: 60, startedAt: "2026-10-01T10:00:00.000Z", totalDamage: 300 }, points: [{ t: 0, damage: 0 }, { t: 10, damage: 100 }, { t: 30, damage: 300 }] };

test("returns the personal-best curve for a level (number or name)", async () => {
  const calls = [];
  const s = await serve(async (name, level) => (calls.push([name, level]), BEST), pace.createCache());
  try {
    const a = await s.get("/api/players/Kai/best-pace?level=2");
    assert.equal(a.status, 200);
    assert.equal(a.body.level, "Medium");
    assert.equal(a.body.best.score, 900);
    assert.deepEqual(a.body.points.at(-1), { t: 30, damage: 300 });
    assert.equal(a.body.reason, null);
    assert.equal(a.body.cached, false);
    const b = await s.get("/api/players/Kai/best-pace?level=Medium");
    assert.equal(b.body.level, "Medium");
    assert.deepEqual(calls, [["Kai", "Medium"]]); // "2" and "Medium" are one cache key
  } finally {
    await s.close();
  }
});

test("a player with no finished fight on the level gets best: null (the page hides the ghost)", async () => {
  const s = await serve(async () => ({ best: null, points: [] }), pace.createCache());
  try {
    const r = await s.get("/api/players/Newbie/best-pace?level=1");
    assert.equal(r.status, 200);
    assert.equal(r.body.best, null);
    assert.deepEqual(r.body.points, []);
    assert.equal(r.body.reason, "no_finished_fight");
  } finally {
    await s.close();
  }
});

test("a best fight without hit events says so instead of drawing nothing silently", async () => {
  const s = await serve(async () => ({ best: BEST.best, points: [{ t: 0, damage: 0 }] }), pace.createCache());
  try {
    assert.equal((await s.get("/api/players/Kai/best-pace?level=1")).body.reason, "no_events");
  } finally {
    await s.close();
  }
});

test("results are cached for 60 s, per player and per level", async () => {
  let clock = 1_000_000;
  let loads = 0;
  const cache = pace.createCache({ ttlMs: 60_000, now: () => clock });
  const s = await serve(async () => (loads++, BEST), cache);
  try {
    assert.equal((await s.get("/api/players/Kai/best-pace?level=1")).body.cached, false);
    assert.equal((await s.get("/api/players/Kai/best-pace?level=1")).body.cached, true);
    clock += 59_000;
    assert.equal((await s.get("/api/players/Kai/best-pace?level=1")).body.cached, true);
    assert.equal(loads, 1);
    await s.get("/api/players/Kai/best-pace?level=2"); // another level: its own entry
    await s.get("/api/players/Sam/best-pace?level=1"); // another player: its own entry
    assert.equal(loads, 3);
    clock += 2_000; // 61 s after the first load
    const again = await s.get("/api/players/Kai/best-pace?level=1");
    assert.equal(again.body.cached, false);
    assert.equal(loads, 4);
  } finally {
    await s.close();
  }
});

test("bad input is a 400, not a database call", async () => {
  let loads = 0;
  const s = await serve(async () => (loads++, BEST), pace.createCache());
  try {
    for (const q of ["", "?level=", "?level=all", "?level=9", "?level=Impossible", "?level=0"]) {
      const r = await s.get(`/api/players/Kai/best-pace${q}`);
      assert.equal(r.status, 400, q);
      assert.equal(r.body.ok, false);
    }
    assert.equal((await s.get(`/api/players/${"x".repeat(80)}/best-pace?level=1`)).status, 400); // name too long
    assert.equal(loads, 0);
  } finally {
    await s.close();
  }
});

test("a loader failure is a JSON 500 and nothing is cached", async () => {
  let fail = true;
  const cache = pace.createCache();
  const app = express();
  app.get("/api/players/:name/best-pace", pace.createHandler({ loadBest: async () => { if (fail) throw new Error("db down"); return BEST; }, cache }));
  app.use((err, _req, res, _next) => res.status(500).json({ ok: false, error: "Server error" }));
  const server = http.createServer(app);
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const url = `http://127.0.0.1:${server.address().port}/api/players/Kai/best-pace?level=1`;
  try {
    assert.equal((await fetch(url)).status, 500);
    fail = false;
    const ok = await fetch(url);
    assert.equal(ok.status, 200);
    assert.equal((await ok.json()).cached, false);
  } finally {
    await new Promise((r) => server.close(r));
  }
});

test("parseLevelParam", () => {
  assert.equal(pace.parseLevelParam("1"), "Easy");
  assert.equal(pace.parseLevelParam("l3"), "Hard");
  assert.equal(pace.parseLevelParam("hard"), "Hard");
  assert.equal(pace.parseLevelParam("all"), null);
  assert.equal(pace.parseLevelParam(undefined), null);
});

test("cumulativeDamage: a step curve from zero, ignoring hits with no time or no damage, and bounded in size", () => {
  const pts = effort.cumulativeDamage([{ t: 5, damage: 10 }, { t: 2, damage: 5 }, { t: 9, damage: 0 }, { damage: 4 }, { t: 12, damage: 20.55 }]);
  assert.deepEqual(pts, [{ t: 0, damage: 0 }, { t: 2, damage: 5 }, { t: 5, damage: 15 }, { t: 12, damage: 35.6 }]);
  assert.equal(effort.damageAt(pts, 1), 0);
  assert.equal(effort.damageAt(pts, 5), 15);
  assert.equal(effort.damageAt(pts, 99), 35.6);
  const many = effort.cumulativeDamage(Array.from({ length: 2000 }, (_, i) => ({ t: i, damage: 1 })), 240);
  assert.equal(many.length, 240);
  assert.deepEqual(many.at(-1), { t: 1999, damage: 2000 });
  assert.deepEqual(effort.cumulativeDamage([]), [{ t: 0, damage: 0 }]);
});

test("a cache can drop one player's entries and keep everyone else's", () => {
  const cache = pace.createCache();
  cache.set("Ava\u0000Easy", 1);
  cache.set("Ava\u0000Hard", 2);
  cache.set("Ben\u0000Easy", 3);
  cache.deleteWhere("Ava\u0000");
  assert.equal(cache.get("Ava\u0000Easy"), null);
  assert.equal(cache.get("Ava\u0000Hard"), null);
  assert.ok(cache.get("Ben\u0000Easy"));
});
