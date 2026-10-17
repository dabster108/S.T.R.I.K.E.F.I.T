// Overview highlights. Run: npm test (node --test src/lib/*.test.js)
import test from "node:test";
import assert from "node:assert/strict";
import { bestRun, longestStreak, mostPlayedLevel, highlights } from "./highlights.js";

const S = (id, player, outcome, score, level = "Easy", t = id) => ({ id, playerName: player, outcome, score, level, startedAt: new Date(2026, 9, 1, 12, t).toISOString() });

test("no sessions: nothing to highlight", () => {
  assert.deepEqual(highlights([]), { best: null, streak: null, level: null });
});

test("best run is the highest score; a score of 0 never counts", () => {
  assert.equal(bestRun([S(1, "A", "victory", 800), S(2, "B", "victory", 1200), S(3, "A", "defeat", 300)]).id, 2);
  assert.equal(bestRun([S(1, "A", "abandoned", 0)]), null);
});

test("longest streak is per player, over decided fights, in time order", () => {
  const list = [
    S(1, "A", "victory", 1), S(2, "A", "victory", 1), S(3, "A", "defeat", 1), S(4, "A", "victory", 1),
    S(5, "B", "victory", 1), S(6, "B", "victory", 1), S(7, "B", "victory", 1),
  ];
  assert.deepEqual(longestStreak(list), { length: 3, player: "B" });
});

test("an abandoned fight neither extends nor breaks a streak; a defeat breaks it", () => {
  const list = [S(1, "A", "victory", 1), S(2, "A", "abandoned", 0), S(3, "A", "victory", 1), S(4, "A", "defeat", 1), S(5, "A", "victory", 1)];
  assert.deepEqual(longestStreak(list), { length: 2, player: "A" });
  assert.equal(longestStreak([S(1, "A", "defeat", 5)]), null);
});

test("most played level counts every recorded fight of the three levels", () => {
  const list = [S(1, "A", "victory", 5, "Medium"), S(2, "A", "victory", 5, "Medium"), S(3, "A", "victory", 5, "Easy"), S(4, "A", "victory", 5, "Unknown")];
  assert.deepEqual(mostPlayedLevel(list), { level: "Medium", count: 2 });
});
