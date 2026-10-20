const test = require("node:test");
const assert = require("node:assert/strict");
const levels = require("../levels");
const fitness = require("../fitness");

test("level counts come from each level's status", () => {
  assert.equal(levels.levelsPlayable(), 3);
  assert.equal(levels.levelsPlanned(), 0);
  assert.equal(levels.levelsPlayable() + levels.levelsPlanned(), levels.LEVELS.length);
});

test("a session's peak swing speed can never read above the game's cap", () => {
  const hits = [{ tier: "full", speed: 90.78, t: 5 }, { tier: "light", speed: 3.1, t: 9 }, { tier: "full", speed: 11.6, t: 12 }];
  const s = fitness.sessionSummary({ id: "x", level: "Hard", status: "finished", startedAt: "2026-10-01T00:00:00Z", score: 1, durationSec: 30, hitCount: 3 }, hits);
  assert.equal(s.maxSpeed, levels.SWING_SPEED_CAP);
  const low = fitness.sessionSummary({ id: "y", level: "Easy", status: "finished", startedAt: "2026-10-01T00:00:00Z", score: 1, durationSec: 30, hitCount: 1 }, [{ tier: "light", speed: 3.1, t: 1 }]);
  assert.equal(low.maxSpeed, 3.1);
});
