// A scripted swing for the landing hero's ticker: one loop is a back-swing, a hard acceleration up to the game's 8 m/s cap, a
// moment at the cap, the follow-through, and a rest. swingSpeedAt(t) is a pure function of time, so the ticker, its tests and
// the reduced-motion still frame all agree. Speeds are the same m/s the game measures at impact.

import { SWING_SPEED_CAP } from "./game.js";

export const SWING = Object.freeze({ backswing: 0.55, accel: 0.7, hold: 0.22, decay: 0.75, rest: 1.0 });
export const LOOP_SEC = SWING.backswing + SWING.accel + SWING.hold + SWING.decay + SWING.rest;
export const STILL_FRAME_SPEED = 6.4; // what the ticker shows when nothing may move: a full-power Level 1 swing

const easeInOut = (x) => (x < 0.5 ? 4 * x * x * x : 1 - (-2 * x + 2) ** 3 / 2);
const easeOut = (x) => 1 - (1 - x) ** 3;

/** The controller's speed in m/s, t seconds into the loop (t may be any number: it wraps). */
export function swingSpeedAt(t) {
  const { backswing, accel, hold, decay } = SWING;
  const x = ((t % LOOP_SEC) + LOOP_SEC) % LOOP_SEC;
  if (x < backswing) return 0.9 * Math.sin((x / backswing) * Math.PI); // a small pull-back first: well under the 1.2 m/s floor
  if (x < backswing + accel) return 0.15 + (SWING_SPEED_CAP - 0.15) * easeInOut((x - backswing) / accel);
  if (x < backswing + accel + hold) return SWING_SPEED_CAP;
  if (x < backswing + accel + hold + decay) return SWING_SPEED_CAP * (1 - easeOut((x - backswing - accel - hold) / decay));
  return 0;
}
