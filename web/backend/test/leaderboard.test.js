// Leaderboard rule against fixed fixtures (no database, no clock), plus the route on a stub query. Run: npm test
const test = require("node:test");
const assert = require("node:assert/strict");
const express = require("express");
const lb = require("../leaderboard");
const lbRoute = require("../leaderboardRoute");

let n = 0;
const run = (playerName, level, score, extra = {}) => ({
  id: `s${++n}`,
  playerName,
  level,
  outcome: "victory",
  score,
  durationSec: 120,
  endedAt: `2026-10-0${1 + (n % 9)}T10:00:00.000Z`,
  status: "finished",
  ...extra,
});

const FIXTURE = [
  run("Ana", "Easy", 400),
  run("Ana", "Hard", 900),
  run("Ben", "Easy", 650),
  run("Ben", "Medium", 300),
  run("Cara", "Medium", 500),
  run("Cara", "Medium", 520, { outcome: "defeat" }),
  run("Dev", "Hard", 900, { durationSec: 95 }), // same score as Ana's Hard run, faster
  run("Eli", "Easy", 650, { durationSec: 120, endedAt: "2026-09-01T10:00:00.000Z" }), // ties Ben's 650 Easy, earlier
  run("Live", "Easy", 9999, { status: "live" }), // a fight in progress never ranks
  run("Zero", "Easy", 0), // no score, no row
];

test("rankBest: one row per player, their best run, score high to low", () => {
  const all = lb.rankBest(FIXTURE);
  assert.deepEqual(all.map((r) => [r.rank, r.playerName, r.score, r.level]), [
    [1, "Dev", 900, "Hard"],
    [2, "Ana", 900, "Hard"],
    [3, "Eli", 650, "Easy"],
    [4, "Ben", 650, "Easy"],
    [5, "Cara", 520, "Medium"],
  ]);
  assert.equal(all.find((r) => r.playerName === "Cara").outcome, "defeat"); // the best run keeps its own outcome
});

test("ties: the faster run first, then the earlier one, then the name", () => {
  const all = lb.rankBest(FIXTURE);
  assert.ok(all.findIndex((r) => r.playerName === "Dev") < all.findIndex((r) => r.playerName === "Ana"), "faster run wins");
  assert.ok(all.findIndex((r) => r.playerName === "Eli") < all.findIndex((r) => r.playerName === "Ben"), "same duration: earlier wins");
  const same = [run("Zed", "Easy", 100, { endedAt: "2026-10-01T00:00:00Z" }), run("Amy", "Easy", 100, { endedAt: "2026-10-01T00:00:00Z" })];
  assert.deepEqual(lb.rankBest(same).map((r) => r.playerName), ["Amy", "Zed"]);
  const noDuration = [run("Slow", "Easy", 100, { durationSec: 0 }), run("Timed", "Easy", 100, { durationSec: 300 })];
  assert.deepEqual(lb.rankBest(noDuration).map((r) => r.playerName), ["Timed", "Slow"], "a missing duration counts as slowest");
});

test("level filter: only that level's runs, each player's best there", () => {
  assert.deepEqual(lb.rankBest(FIXTURE, { level: "Easy" }).map((r) => [r.playerName, r.score]), [["Eli", 650], ["Ben", 650], ["Ana", 400]]);
  assert.deepEqual(lb.rankBest(FIXTURE, { level: "Medium" }).map((r) => [r.playerName, r.score]), [["Cara", 520], ["Ben", 300]]);
  assert.deepEqual(lb.rankBest(FIXTURE, { level: "Hard" }).map((r) => r.playerName), ["Dev", "Ana"]);
  assert.deepEqual(lb.rankBest([], { level: "Easy" }), []);
});

test("board: top N, the player's own rank, and the you flag", () => {
  const all = lb.rankBest(FIXTURE);
  const top2 = lb.board(all, { limit: 2, player: "Cara" });
  assert.equal(top2.rows.length, 2);
  assert.equal(top2.total, 5);
  assert.deepEqual(top2.you, { rank: 5, score: 520, level: "Medium", date: all[4].date, inTop: false });
  assert.ok(top2.rows.every((r) => !("playerName" in r)), "raw names never leave the server");
  const inTop = lb.board(all, { limit: 10, player: "Ana" });
  assert.equal(inTop.you.rank, 2);
  assert.equal(inTop.you.inTop, true);
  assert.equal(inTop.rows.find((r) => r.you).name, "Ana");
  assert.equal(lb.board(all, { player: "Nobody" }).you, null);
  assert.equal(lb.board(all, {}).you, undefined);
});

test("displayName: trim, collapse, cap, blocklist (tunable)", () => {
  assert.equal(lb.displayName("  Ana   Maria  "), "Ana Maria");
  assert.equal(lb.displayName("A".repeat(40)).length, lb.DISPLAY_NAME_MAX);
  assert.ok(lb.displayName("A".repeat(40)).endsWith("…"));
  assert.equal(lb.displayName("big SH1T energy"), lb.HIDDEN_NAME);
  assert.equal(lb.displayName("f.u.c.k"), lb.HIDDEN_NAME);
  assert.equal(lb.displayName("Dikshanta"), "Dikshanta");
  assert.equal(lb.displayName("Hancock"), "Hancock");
  assert.equal(lb.displayName(""), "Player");
  const words = lb.blocklist("bananas, xx");
  assert.ok(words.includes("bananas") && !words.includes("xx"));
  assert.equal(lb.displayName("BaNaNaS99", words), lb.HIDDEN_NAME);
});

test("parseLevel / parseLimit", () => {
  assert.equal(lb.parseLevel(undefined), "all");
  assert.equal(lb.parseLevel("2"), "Medium");
  assert.equal(lb.parseLevel("L3"), "Hard");
  assert.equal(lb.parseLevel("easy"), "Easy");
  assert.equal(lb.parseLevel("Extreme"), null);
  assert.equal(lb.parseLimit("abc"), lb.DEFAULT_LIMIT);
  assert.equal(lb.parseLimit(500), lb.MAX_LIMIT);
  assert.equal(lb.parseLimit(3), 3);
});

test("route: parameterised level filter, cache, 400 on a bad level", async () => {
  lbRoute.clearCache();
  const calls = [];
  const query = async (sql, params) => {
    calls.push({ sql, params });
    const level = params ? params[0] : "all";
    return { rows: FIXTURE.filter((s) => s.status !== "live" && s.score > 0 && (level === "all" || s.level === level)) };
  };
  const app = express();
  lbRoute.mount(app, { query, route: (fn) => (req, res, next) => fn(req, res, next).catch(next) });
  const server = app.listen(0);
  const base = `http://127.0.0.1:${server.address().port}`;
  try {
    const a = await (await fetch(`${base}/api/leaderboard?level=1&player=Ana`)).json();
    assert.equal(a.level, "Easy");
    assert.deepEqual(a.rows.map((r) => r.name), ["Eli", "Ben", "Ana"]);
    assert.equal(a.you.rank, 3);
    assert.equal(a.cached, false);
    assert.deepEqual(calls[0].params, ["Easy"]);
    assert.ok(!calls[0].sql.includes("Easy"), "the level is a parameter, never spliced into SQL");
    const b = await (await fetch(`${base}/api/leaderboard?level=Easy`)).json();
    assert.equal(b.cached, true);
    assert.equal(calls.length, 1, "second call within the cache window does not query");
    const bad = await fetch(`${base}/api/leaderboard?level=9`);
    assert.equal(bad.status, 400);
    const all = await (await fetch(`${base}/api/leaderboard`)).json();
    assert.equal(all.rows[0].name, "Dev");
    assert.equal(calls[1].params, undefined);
  } finally {
    server.close();
  }
});

test("the web dashboard's copy of the rule (frontend/src/lib/leaderboard.js) ranks the fixture exactly like the server", async (t) => {
  const path = require("path");
  const file = path.join(__dirname, "..", "..", "frontend", "src", "lib", "leaderboard.js");
  if (!require("fs").existsSync(file)) return t.skip("frontend not checked out next to the backend");
  const web = await import(require("url").pathToFileURL(file).href);
  for (const level of ["all", "Easy", "Medium", "Hard"]) {
    const pool = level === "all" ? FIXTURE : FIXTURE.filter((s) => s.level === level);
    assert.deepEqual(
      web.bestPerPlayer(pool).map((s) => s.id),
      lb.rankBest(FIXTURE, { level }).map((r) => r.sessionId),
      `level ${level}`
    );
  }
});

test("board: a just-finished run ranks the player while the board is still cached", () => {
  const easy = lb.rankBest(FIXTURE, { level: "Easy" }); // Eli 650, Ben 650, Ana 400
  assert.equal(lb.board(easy, { player: "Ana", run: { score: 700, durationSec: 90, level: "Easy" } }).you.rank, 1);
  assert.equal(lb.board(easy, { player: "Ana", run: { score: 650, durationSec: 60, level: "Easy" } }).you.rank, 1, "faster run wins the tie");
  assert.equal(lb.board(easy, { player: "Ana", run: { score: 650, durationSec: 500, level: "Easy" } }).you.rank, 3);
  assert.equal(lb.board(easy, { player: "Ana", run: { score: 100, level: "Easy" } }).you.rank, 3, "a worse run keeps the stored best");
  assert.equal(lb.board(easy, { player: "New", run: { score: 500, level: "Easy" } }).you.rank, 3, "first run of a new player");
  assert.equal(lb.board(easy, { player: "New", run: { score: 500 } }).rows.length, 3, "public rows unchanged");
});
