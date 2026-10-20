import test from "node:test";
import assert from "node:assert/strict";
import { deltaChip, lastN, percentChange } from "./delta.js";

test("percentChange rounds to a whole percent and has no baseline at zero", () => {
  assert.equal(percentChange(112, 100), 12);
  assert.equal(percentChange(50, 100), -50);
  assert.equal(percentChange(100, 100), 0);
  assert.equal(percentChange(5, 0), null);
  assert.equal(percentChange(NaN, 4), null);
  assert.equal(percentChange(3, undefined), null);
});

test("deltaChip: percent, points, and the zero-baseline fallback", () => {
  assert.deepEqual(deltaChip({ cur: 9, prev: 8, suffix: "vs previous 7 days" }), { value: 13, label: "13%", suffix: "vs previous 7 days" });
  assert.deepEqual(deltaChip({ cur: 40, prev: 50, mode: "points", suffix: "x" }), { value: -10, label: "10 pts", suffix: "x" });
  assert.deepEqual(deltaChip({ cur: 4, prev: 0, suffix: "x", fallbackLabel: (n) => `${n} more` }), { value: 4, label: "4 more", suffix: "x" });
  assert.deepEqual(deltaChip({ cur: 0, prev: 0, suffix: "x" }), { value: 0, label: "0", suffix: "x" });
  assert.equal(deltaChip({ cur: 1, prev: NaN, suffix: "x" }), null);
});

test("lastN keeps the newest n points", () => {
  assert.deepEqual(lastN([1, 2, 3, 4, 5, 6, 7, 8, 9]), [3, 4, 5, 6, 7, 8, 9]);
  assert.deepEqual(lastN([1, 2, 3]), [1, 2, 3]);
});
