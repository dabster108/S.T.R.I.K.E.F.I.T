import test from "node:test";
import assert from "node:assert/strict";
import { describeRecords, metricLabels, recordsHeadline, recordsSentence } from "./records.js";

const sample = [
  { metric: "score", value: 1400, previous: 900 },
  { metric: "peakSwingSpeed", value: 7.94, previous: 6.2 },
  { metric: "fade", value: -3, previous: -10 },
  { metric: "damageDealt", value: 503.1, previous: 333.1 },
];

test("records are listed in a fixed order with their numbers and units written out", () => {
  assert.deepEqual(describeRecords(sample).map((r) => [r.label, r.value, r.previous]), [
    ["Fastest swing", "7.94 m/s", "6.2 m/s"],
    ["Damage dealt", "503.1", "333.1"],
    ["Score", "1,400 pts", "900 pts"],
    ["Effort fade", "-3%", "-10%"],
  ]);
  assert.equal(describeRecords([{ metric: "fade", value: 4.5, previous: -2 }])[0].value, "+4.5%");
});

test("a metric the page does not know, or a broken number, is left out", () => {
  assert.deepEqual(describeRecords([{ metric: "mystery", value: 1, previous: 0 }, { metric: "score", value: NaN, previous: 1 }, null]), []);
  assert.deepEqual(describeRecords(undefined), []);
});

test("headline and sentence", () => {
  assert.equal(recordsHeadline([1]), "New personal best");
  assert.equal(recordsHeadline([1, 2, 3]), "3 new personal bests");
  assert.equal(recordsSentence([sample[0]]), "Score 1,400 pts (was 900 pts)");
  assert.deepEqual(metricLabels(["score", "peakSwingSpeed", "nope"]), ["Fastest swing", "Score"]);
});
