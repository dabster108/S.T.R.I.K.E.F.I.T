import test from "node:test";
import assert from "node:assert/strict";
import { plainSummary, spokenDuration, summaryFacts } from "./summary.js";
import { createState, step } from "./commentary.js";

const SESSION = { outcome: "victory", level: "Hard", durationSec: 102, score: 5639, damageDealt: 5600, hitCount: 87, fullSwings: 12 };
const NAMES = { playerName: "Ava", monsterName: "The Crimson Warden", peakCombo: 24 };

test("a victory report names both sides and gives the totals in plain digits", () => {
  assert.equal(plainSummary(summaryFacts(SESSION, NAMES)), "Full time: Ava beat The Crimson Warden in 1m 42s. 87 hits landed (12 at full power), best combo 24, score 5639.");
});

test("a defeat and an abandoned fight read correctly", () => {
  assert.equal(plainSummary(summaryFacts({ ...SESSION, outcome: "defeat", fullSwings: 0 }, NAMES)), "Full time: The Crimson Warden wins this round after 1m 42s. 87 hits landed, best combo 24, score 5639.");
  assert.equal(plainSummary(summaryFacts({ outcome: "abandoned", durationSec: 20 }, NAMES)), "Fight abandoned after 20s. Best combo 24.");
  assert.equal(summaryFacts({ outcome: "in_progress" }, NAMES).outcome, "abandoned"); // anything that is not a result is not claimed as one
});

test("missing numbers are left out, never printed as 0 or NaN", () => {
  const text = plainSummary(summaryFacts({ outcome: "victory", durationSec: "x", score: null }, { playerName: "Ava", monsterName: "The Gatekeeper" }));
  assert.equal(text, "Full time: Ava beat The Gatekeeper.");
  assert.equal(spokenDuration(0), "");
  assert.equal(spokenDuration(59.6), "1m 00s");
});

test("the commentary remembers the best combo of the fight", () => {
  let s = createState({ levelKey: "Easy", playerName: "Ava", monsterName: "The Gatekeeper" });
  for (const [i, combo] of [3, 12, 4, 0].entries()) s = step({ type: "hit", payload: { combo }, at: i * 100 }, s).state;
  assert.equal(s.peakCombo, 12);
});
