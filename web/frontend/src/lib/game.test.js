import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { clampSpeed, LEVELS, levelCounts, levelCountsText, SWING_SPEED_CAP } from "./game.js";

const backend = createRequire(import.meta.url)("../../../backend/levels.js");

test("level counts are computed from each level's status", () => {
  assert.deepEqual(levelCounts(), { playable: 3, planned: 0, total: 3 });
  assert.equal(levelCountsText(), "3 levels playable.");
  assert.equal(levelCountsText({ a: { status: "playable" }, b: { status: "planned" } }), "1 level playable, 1 planned.");
  assert.equal(levelCountsText({ a: { status: "playable" }, b: { status: "playable" }, c: { status: "planned" } }), "2 levels playable, 1 planned.");
});

test("the web and backend level lists agree (status and swing cap)", () => {
  for (const l of backend.LEVELS) assert.equal(LEVELS[l.key].status, l.status, `${l.key} status`);
  assert.equal(backend.levelsPlayable(), levelCounts().playable);
  assert.equal(backend.levelsPlanned(), levelCounts().planned);
  assert.equal(SWING_SPEED_CAP, backend.SWING_SPEED_CAP);
  assert.equal(LEVELS.Easy.swing.cap, SWING_SPEED_CAP);
});

test("clampSpeed never exceeds the cap", () => {
  assert.equal(clampSpeed(11.56), 8);
  assert.equal(clampSpeed(3.2), 3.2);
  assert.ok(Number.isNaN(clampSpeed(NaN)));
});
