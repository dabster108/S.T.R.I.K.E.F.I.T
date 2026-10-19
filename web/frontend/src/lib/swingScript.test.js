import test from "node:test";
import assert from "node:assert/strict";
import { LOOP_SEC, STILL_FRAME_SPEED, SWING, swingSpeedAt } from "./swingScript.js";
import { SWING_SPEED_CAP, tierFor } from "./game.js";

const samples = (n = 600) => Array.from({ length: n }, (_, i) => ({ t: (i / n) * LOOP_SEC, v: swingSpeedAt((i / n) * LOOP_SEC) }));

test("the swing starts and ends at rest and reaches exactly the 8 m/s cap", () => {
  assert.equal(swingSpeedAt(0), 0);
  assert.equal(swingSpeedAt(LOOP_SEC - 0.01), 0);
  const peak = Math.max(...samples().map((s) => s.v));
  assert.equal(peak, SWING_SPEED_CAP);
  assert.equal(swingSpeedAt(SWING.backswing + SWING.accel + SWING.hold / 2), SWING_SPEED_CAP);
});

test("it never leaves 0..8 m/s", () => {
  for (const { v } of samples(2000)) assert.ok(v >= 0 && v <= SWING_SPEED_CAP + 1e-9, String(v));
  for (const t of [-5, -0.3, 1e6]) assert.ok(swingSpeedAt(t) >= 0 && swingSpeedAt(t) <= SWING_SPEED_CAP);
});

test("the loop repeats exactly", () => {
  for (const t of [0.1, 0.9, 1.4, 2.2, 3.1]) assert.equal(swingSpeedAt(t), swingSpeedAt(t + LOOP_SEC));
});

test("the back-swing stays under the 1.2 m/s floor (no hit), then speed only rises until the cap", () => {
  const back = samples(4000).filter((s) => s.t < SWING.backswing);
  assert.ok(Math.max(...back.map((s) => s.v)) < 1.2);
  const accel = samples(4000).filter((s) => s.t >= SWING.backswing && s.t <= SWING.backswing + SWING.accel);
  for (let i = 1; i < accel.length; i++) assert.ok(accel[i].v >= accel[i - 1].v - 1e-9, "monotone while accelerating");
});

test("on the way up it passes through every strength tier in order, and the still frame is full power", () => {
  const order = [];
  for (const { v } of samples(4000)) {
    const tier = tierFor(v, "Easy");
    if (order[order.length - 1] !== tier) order.push(tier);
  }
  // none (rest / back-swing) -> tap -> light -> medium -> full, then back down the same way
  assert.deepEqual(order.slice(0, 6), ["none", "tap", "light", "medium", "full", "medium"]);
  assert.equal(tierFor(STILL_FRAME_SPEED, "Easy"), "full");
});
