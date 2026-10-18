import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { countOutcomes, formatWinRate, isDecided, outcomeSummary, winRate } from "./outcomes.js";

const backend = createRequire(import.meta.url)("../../../backend/outcomes.js");

test("rate = victories / (victories + defeats); abandoned excluded", () => {
  assert.equal(winRate(10, 2), 83.3);
  assert.equal(winRate(0, 0), null);
  const s = countOutcomes(["victory", "defeat", "abandoned", "abandoned", "victory"]);
  assert.equal(s.winRate, 66.7);
  assert.equal(s.abandoned, 2);
  assert.equal(s.decided, 3);
});

test("the web mirror gives the same answer as the backend module on many inputs", () => {
  const pool = ["victory", "defeat", "abandoned", "timeout", "in_progress", "unknown"];
  let seed = 7;
  const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  for (let i = 0; i < 200; i++) {
    const list = Array.from({ length: Math.floor(rnd() * 30) }, () => pool[Math.floor(rnd() * pool.length)]);
    assert.deepEqual(countOutcomes(list), backend.countOutcomes(list));
  }
  for (const [v, d] of [[0, 0], [1, 0], [0, 3], [24, 12], [3, 5]]) assert.equal(winRate(v, d), backend.winRate(v, d));
  assert.deepEqual(outcomeSummary({ victories: 3, defeats: 5, abandoned: 11, total: 19 }), backend.outcomeSummary({ victories: 3, defeats: 5, abandoned: 11, total: 19 }));
  for (const o of pool) assert.equal(isDecided(o), backend.isDecided(o));
});

test("formatWinRate shows a dash instead of a made-up 0%", () => {
  assert.equal(formatWinRate(null), "—");
  assert.equal(formatWinRate(0), "0%");
  assert.equal(formatWinRate(66.66), "66.7%");
});
