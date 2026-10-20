const test = require("node:test");
const assert = require("node:assert/strict");
const { winRate, countOutcomes, outcomeSummary, isDecided } = require("../outcomes");

test("win rate = victories / (victories + defeats)", () => {
  assert.equal(winRate(10, 2), 83.3);
  assert.equal(winRate(11, 5), 68.8);
  assert.equal(winRate(3, 5), 37.5);
  assert.equal(winRate(1, 0), 100);
  assert.equal(winRate(0, 4), 0);
});

test("no decided fights means no rate, not 0%", () => {
  assert.equal(winRate(0, 0), null);
  assert.equal(countOutcomes([]).winRate, null);
  assert.equal(countOutcomes(["abandoned", "abandoned"]).winRate, null);
});

test("abandoned fights are excluded from the rate and counted separately", () => {
  const s = countOutcomes(["victory", "defeat", "abandoned", "abandoned", "abandoned", "victory"]);
  assert.equal(s.winRate, 66.7); // 2 / (2 + 1), not 2 / 6
  assert.deepEqual({ v: s.victories, d: s.defeats, a: s.abandoned, o: s.other, t: s.total, dec: s.decided }, { v: 2, d: 1, a: 3, o: 0, t: 6, dec: 3 });
});

test("timeouts and in-progress fights are 'other': not decided, not abandoned", () => {
  const s = countOutcomes(["victory", "timeout", "in_progress", "unknown", "defeat"]);
  assert.equal(s.other, 3);
  assert.equal(s.decided, 2);
  assert.equal(s.winRate, 50);
});

test("the real production counts give one number per level", () => {
  // Easy 10W 2L 4A, Medium 11W 5L 11A, Hard 3W 5L 11A (62 fights, 24W 12L 26A on 2026-10-18)
  assert.equal(outcomeSummary({ victories: 10, defeats: 2, abandoned: 4, total: 16 }).winRate, 83.3);
  assert.equal(outcomeSummary({ victories: 11, defeats: 5, abandoned: 11, total: 27 }).winRate, 68.8);
  assert.equal(outcomeSummary({ victories: 3, defeats: 5, abandoned: 11, total: 19 }).winRate, 37.5);
  const all = outcomeSummary({ victories: 24, defeats: 12, abandoned: 26, total: 62 });
  assert.equal(all.winRate, 66.7);
  assert.equal(all.other, 0);
});

test("isDecided", () => {
  assert.ok(isDecided("victory") && isDecided("defeat"));
  assert.ok(!isDecided("abandoned") && !isDecided("timeout") && !isDecided("in_progress"));
});
