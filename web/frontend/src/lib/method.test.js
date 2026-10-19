import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { CALORIE, estimateKcal } from "../content/method.js";
import { LEVELS } from "./game.js";

const backend = createRequire(import.meta.url)("../../../backend/fitness.js");

test("the Method page's calorie constants are the backend's", () => {
  assert.equal(CALORIE.bodyMassKg, backend.ASSUMED_BODY_MASS_KG);
  assert.equal(CALORIE.metMin, backend.MET_MIN);
  assert.equal(CALORIE.metMax, backend.MET_MAX);
  assert.equal(CALORIE.metWalking, backend.MET_BRISK_WALKING);
  assert.equal(CALORIE.metJogging, backend.MET_JOGGING);
  assert.equal(CALORIE.tierWeightFull, backend.TIER_WEIGHT.full);
});

test("estimateKcal gives exactly what the backend estimate gives", () => {
  for (const [w, secs] of [[1, 60], [2.5, 90], [3, 120], [4, 600]]) {
    const mine = estimateKcal(w, secs);
    const theirs = backend.estimateEffort(secs, w, true);
    assert.equal(Math.round(mine.kcal * 10) / 10, theirs.estimatedCalories);
    assert.equal(Math.round(mine.met * 100) / 100, theirs.estimate.metUsed);
  }
  assert.equal(estimateKcal(1, 3600).kcal, 3 * 70); // all taps for an hour: MET 3, 70 kg
  assert.equal(estimateKcal(4, 3600).kcal, 6 * 70); // all full power for an hour: MET 6
});

test("every playable level has the thresholds the page prints", () => {
  for (const l of Object.values(LEVELS)) if (l.swing) assert.ok(l.swing.min < l.swing.tiers.light + 1 && l.swing.tiers.medium < l.swing.tiers.full && l.swing.cap === 8);
});
