import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const TIMELINE = JSON.parse(readFileSync(new URL("../content/level2DemoTimeline.json", import.meta.url), "utf8"));
const TYPES = new Set(["start", "warning", "dodge", "hit", "guardedHit", "clash", "guard", "low"]);

test("demo timeline: markers in order, inside the clip, known types, chips are text", () => {
  assert.equal(TIMELINE.durationSeconds, 21);
  let last = -1;
  for (const m of TIMELINE.markers) {
    assert.ok(m.t >= 0 && m.t < TIMELINE.durationSeconds, `${m.label} at ${m.t}`);
    assert.ok(m.t > last, `${m.label} is after the previous marker`);
    assert.ok(TYPES.has(m.type), m.type);
    assert.ok(m.label && typeof m.label === "string");
    if ("chip" in m) assert.ok(typeof m.chip === "string" && m.chip.length > 0);
    last = m.t;
  }
  assert.ok(TIMELINE.markers.some((m) => m.type === "hit"));
  assert.ok(TIMELINE.markers.some((m) => m.type === "guardedHit"));
  assert.ok(TIMELINE.markers.some((m) => m.type === "clash"));
});
