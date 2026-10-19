import test from "node:test";
import assert from "node:assert/strict";
import { hasTimeline, milestoneDate, milestoneStatus, progressPercent } from "./milestones.js";

test("a calendar day is written as day, month name and year", () => {
  assert.equal(milestoneDate("2026-10-15"), "15 Oct 2026");
  assert.equal(milestoneDate("2026-01-05"), "5 Jan 2026");
  assert.equal(milestoneDate("2026-12-31"), "31 Dec 2026");
  for (const bad of [undefined, null, "", "15/10/2026", "2026-10"]) assert.equal(milestoneDate(bad), "");
});

test("progress is a whole percent between 0 and 100", () => {
  assert.deepEqual([0, 0.5, 0.667, 1, 1.4, -0.2].map(progressPercent), [0, 50, 67, 100, 100, 0]);
  assert.deepEqual([NaN, undefined, null, "x"].map(progressPercent), [0, 0, 0, 0]);
});

test("the status sentence says when a milestone was reached or how far along it is", () => {
  assert.equal(milestoneStatus({ reachedAt: "2026-10-15T08:00:00.000Z", day: "2026-10-15", progress: 1 }), "Reached 15 Oct 2026");
  assert.equal(milestoneStatus({ reachedAt: null, progress: 0.62 }), "62% of the way");
});

test("there is a timeline when something is reached or upcoming", () => {
  assert.equal(hasTimeline({ achieved: [{}], upcoming: [] }), true);
  assert.equal(hasTimeline({ achieved: [], upcoming: [{}] }), true);
  assert.equal(hasTimeline({ achieved: [], upcoming: [] }), false);
  assert.equal(hasTimeline(null), false);
});
