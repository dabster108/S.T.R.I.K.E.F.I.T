// Run: npm test   (node --test, plain ES modules, no browser)
import test from "node:test";
import assert from "node:assert/strict";
import { bestPerPlayer, boardPool, levelMedians, mostImproved, weekStart, youVsLastWeek } from "./leaderboard.js";

const S = (playerName, level, score, startedAt, extra = {}) => ({ id: `${playerName}-${startedAt}`, playerName, level, score, startedAt, status: "finished", outcome: "victory", ...extra });

test("weekStart is the Monday at 00:00", () => {
  const w = weekStart(new Date(2026, 9, 9, 15, 30)); // Fri 9 Oct 2026
  assert.equal(w.getDay(), 1);
  assert.equal(w.getDate(), 5);
  assert.equal(w.getHours(), 0);
});

test("weekly board keeps this week only; best per player picks the top run", () => {
  const now = new Date(2026, 9, 9, 12);
  const list = [S("A", "Easy", 900, new Date(2026, 9, 6).toISOString()), S("A", "Easy", 1200, new Date(2026, 9, 8).toISOString()), S("B", "Easy", 1500, new Date(2026, 9, 2).toISOString())];
  const pool = boardPool(list, "weekly", { now });
  assert.deepEqual(pool.map((s) => s.score), [900, 1200]);
  assert.deepEqual(bestPerPlayer(pool).map((s) => [s.playerName, s.score]), [["A", 1200]]);
  assert.equal(bestPerPlayer(list)[0].playerName, "B");
});

test("live sessions never rank", () => {
  assert.equal(bestPerPlayer([S("A", "Easy", 5000, "2026-10-01T00:00:00Z", { status: "live" })]).length, 0);
});

test("level medians ignore zero scores and live sessions", () => {
  const m = levelMedians([S("A", "Easy", 100, "2026-10-01"), S("B", "Easy", 300, "2026-10-02"), S("C", "Easy", 0, "2026-10-03"), S("D", "Easy", 900, "2026-10-04", { status: "live" })]);
  assert.equal(m.get("Easy"), 200);
});

test("most improved (score): needs 3 sessions; latest third against the earlier ones, in points of the level median", () => {
  const list = [
    // the level median is 1000 (A 500, 1000, 1500; B 1000, 1000)
    S("A", "Easy", 500, "2026-10-01"), S("A", "Easy", 1000, "2026-10-02"), S("A", "Easy", 1500, "2026-10-03"),
    S("B", "Easy", 1000, "2026-10-01"), S("B", "Easy", 1000, "2026-10-02"),
  ];
  const r = mostImproved(list, "score");
  assert.equal(r.length, 1); // B has only 2 sessions
  assert.equal(r[0].playerName, "A");
  assert.equal(r[0].earlier, 75); // (50 + 100) / 2
  assert.equal(r[0].recent, 150);
  assert.equal(r[0].change, 75);
});

test("most improved (score): levels compare through their own medians", () => {
  const list = [
    S("A", "Easy", 1000, "2026-10-01"), S("A", "Hard", 200, "2026-10-02"), S("A", "Hard", 400, "2026-10-03"),
    S("Z", "Easy", 1000, "2026-10-01"), S("Z", "Hard", 400, "2026-10-02"),
  ];
  // Easy median 1000, Hard median 400: A goes 100 -> 50 -> 100, latest third (1 session) = 100, earlier = 75
  const [a] = mostImproved(list, "score");
  assert.equal(a.change, 25);
});

test("most improved (effort): full-power share in percentage points, sessions under 5 hits skipped", () => {
  const list = [
    S("A", "Easy", 1, "2026-10-01", { hitCount: 10, fullSwings: 1 }),
    S("A", "Easy", 1, "2026-10-02", { hitCount: 10, fullSwings: 3 }),
    S("A", "Easy", 1, "2026-10-03", { hitCount: 4, fullSwings: 4 }),
    S("A", "Easy", 1, "2026-10-04", { hitCount: 10, fullSwings: 6 }),
  ];
  const [a] = mostImproved(list, "effort");
  assert.equal(a.sessions, 3);
  assert.equal(a.earlier, 20);
  assert.equal(a.recent, 60);
  assert.equal(a.change, 40);
});

test("you vs last week", () => {
  const now = new Date(2026, 9, 9, 12);
  const list = [
    S("A", "Easy", 800, new Date(2026, 9, 1).toISOString()),
    S("A", "Easy", 1200, new Date(2026, 9, 6).toISOString()),
    S("A", "Easy", 1000, new Date(2026, 9, 7).toISOString()),
    S("B", "Easy", 9999, new Date(2026, 9, 7).toISOString()),
  ];
  const r = youVsLastWeek(list, "A", now);
  assert.deepEqual(r.thisWeek, { fights: 2, best: 1200, avg: 1100 });
  assert.deepEqual(r.lastWeek, { fights: 1, best: 800, avg: 800 });
});

test("most improved leaves abandoned fights out (no result, mostly a 0 score)", () => {
  const list = [
    S("A", "Easy", 500, "2026-10-01"), S("A", "Easy", 1000, "2026-10-02"), S("A", "Easy", 1500, "2026-10-03"),
    S("A", "Easy", 0, "2026-10-04", { outcome: "abandoned" }),
  ];
  const [a] = mostImproved(list, "score");
  assert.equal(a.sessions, 3);
  assert.equal(a.recent, 150);
  assert.equal(levelMedians(list).get("Easy"), 1000);
});
