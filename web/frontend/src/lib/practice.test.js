import test from "node:test";
import assert from "node:assert/strict";
import { LEVELS, simulateHit, tierFor } from "./game.js";
import {
  COMBO,
  GUARD,
  GUARD_DAMAGE_MULTIPLIER,
  applySwing,
  createDragTracker,
  createGravityFilter,
  createMotionEstimator,
  demoSpeed,
  guardAt,
  hitXp,
  liveCombo,
  loadPrefs,
  newSession,
  resolveSwing,
  savePrefs,
  summary,
} from "./practice.js";

// A swing: acceleration a(t) = A sin(2 pi t / T) along x for one period (speed up, then slow down), sampled every 16 ms.
// Its true peak speed is A T / pi (at mid-swing).
function swing(est, { A = 30, T = 0.3, t0 = 0, dt = 16, tail = 300 } = {}) {
  let out = null;
  for (let ms = 0; ms <= T * 1000 + tail; ms += dt) {
    const s = ms / 1000;
    const x = s <= T ? A * Math.sin((2 * Math.PI * s) / T) : 0;
    const r = est.push({ t: t0 + ms, x, y: 0.1, z: -0.1 });
    if (r) out = r;
  }
  return out;
}

test("motion: one swing window estimates the peak speed (within 12 %), a second swing starts from zero velocity", () => {
  const est = createMotionEstimator();
  const truth = (30 * 0.3) / Math.PI; // 2.86 m/s
  const a = swing(est);
  assert.ok(a, "a swing was reported");
  assert.ok(Math.abs(a.speed - truth) / truth < 0.12, `estimate ${a.speed.toFixed(2)} vs ${truth.toFixed(2)}`);
  const b = swing(est, { t0: 2000 });
  assert.ok(Math.abs(b.speed - a.speed) < 0.05, "same swing, same estimate: nothing carried over");
});

test("motion: faster swings read faster; the scale factor scales; the 8 m/s clamp holds", () => {
  const slow = swing(createMotionEstimator(), { A: 20 });
  const fast = swing(createMotionEstimator(), { A: 50 });
  assert.ok(fast.speed > slow.speed * 2);
  const scaled = swing(createMotionEstimator({ scale: 1.5 }), { A: 20 });
  assert.ok(Math.abs(scaled.speed - slow.speed * 1.5) < 0.02);
  const wild = swing(createMotionEstimator(), { A: 200 });
  assert.equal(wild.speed, 8);
  assert.ok(wild.raw > 8);
});

test("motion: drift reset - a constant sensor bias under the start threshold never makes a swing; noise does not either", () => {
  const est = createMotionEstimator();
  let hits = 0;
  for (let ms = 0; ms < 5000; ms += 16) if (est.push({ t: ms, x: 1.2, y: -0.8 + Math.sin(ms) * 0.5, z: 0.4 })) hits++;
  assert.equal(hits, 0);
  assert.equal(est.state.mode, "idle");
});

test("motion: a bias inside a window is bled off by the leak (a long window stays bounded)", () => {
  const est = createMotionEstimator({ maxWindowMs: 700 });
  est.push({ t: 0, x: 7 }); // opens the window
  let r = null;
  for (let ms = 16; ms <= 800 && !r; ms += 16) r = est.push({ t: ms, x: 3 }); // stuck at 3 m/s² (a tilted phone)
  assert.ok(r, "the window closes at maxWindowMs");
  assert.ok(r.raw < 3 * 0.7, `leaked ${r.raw.toFixed(2)} < naive ${(3 * 0.7).toFixed(2)}`);
});

test("gravity filter: a still phone reads ~0 after settling; a jolt shows through", () => {
  const f = createGravityFilter(0.1);
  let out;
  for (let i = 0; i < 200; i++) out = f({ x: 0, y: 9.81, z: 0 });
  assert.ok(Math.hypot(out.x, out.y, out.z) < 0.01);
  out = f({ x: 12, y: 9.81, z: 0 });
  assert.ok(out.x > 10);
});

test("drag: px/s to m/s at the calibration; sensitivity scales; taps do not count; clamp", () => {
  const d = createDragTracker();
  d.start({ t: 0, x: 0, y: 0 });
  for (let ms = 10; ms <= 200; ms += 10) d.move({ t: ms, x: ms * 4.5, y: 0 }); // 4500 px/s
  const r = d.end({ t: 210, x: 945, y: 0 });
  assert.ok(Math.abs(r.speed - 5) < 0.1, `5 m/s expected, got ${r.speed}`);
  const s = createDragTracker({ sensitivity: 0.5 });
  s.start({ t: 0, x: 0, y: 0 });
  for (let ms = 10; ms <= 200; ms += 10) s.move({ t: ms, x: ms * 4.5, y: 0 });
  assert.ok(Math.abs(s.end({ t: 210, x: 945, y: 0 }).speed - 2.5) < 0.1);
  const tap = createDragTracker();
  tap.start({ t: 0, x: 0, y: 0 });
  assert.equal(tap.end({ t: 30, x: 5, y: 2 }), null);
  const huge = createDragTracker();
  huge.start({ t: 0, x: 0, y: 0 });
  for (let ms = 10; ms <= 100; ms += 10) huge.move({ t: ms, x: ms * 40, y: 0 });
  assert.equal(huge.end({ t: 110, x: 4400, y: 0 }).speed, 8);
});

test("tier and damage mapping is the game's (simulateHit / tierFor) at every speed, both levels", () => {
  for (const level of ["Easy", "Medium"]) {
    for (let v = 0; v <= 8.0001; v += 0.1) {
      const r = resolveSwing({ speed: v, level });
      const g = simulateHit(Math.min(v, 8), level);
      assert.equal(r.tier, g.tier === "none" ? "none" : tierFor(Math.min(v, 8), level));
      assert.ok(Math.abs(r.damage - g.damage) < 1e-9, `${level} ${v.toFixed(1)}: ${r.damage} vs ${g.damage}`);
      assert.equal(r.xp, g.xp, `${level} ${v.toFixed(1)} xp`);
    }
  }
  assert.equal(resolveSwing({ speed: 1.19, level: "Easy" }).tier, "none");
  assert.equal(resolveSwing({ speed: 1.2, level: "Easy" }).tier, "tap");
  assert.equal(resolveSwing({ speed: 5.0, level: "Easy" }).tier, "full");
  assert.equal(resolveSwing({ speed: 4.6, level: "Medium" }).tier, "full");
  // above the cap the damage is the cap's
  assert.equal(resolveSwing({ speed: 11, level: "Easy" }).damage, simulateHit(8, "Easy").damage);
  assert.equal(resolveSwing({ speed: 11, level: "Easy" }).speed, 8);
});

test("guard: 70 % less damage to HP, XP from the full damage (as the game awards it)", () => {
  assert.equal(GUARD_DAMAGE_MULTIPLIER, 0.3);
  const open = resolveSwing({ speed: 4, level: "Medium" });
  const guarded = resolveSwing({ speed: 4, level: "Medium", guarding: true });
  assert.ok(Math.abs(guarded.damage - open.damage * 0.3) < 1e-9);
  assert.equal(guarded.fullDamage, open.damage);
  assert.equal(guarded.xp, open.xp);
  assert.equal(guarded.guarded, true);
  assert.equal(resolveSwing({ speed: 0.5, level: "Medium", guarding: true }).guarded, false);
});

test("guard timing: 1.5 s every 8 s, with a warning before it", () => {
  assert.equal(guardAt(0).guarding, false);
  assert.equal(guardAt(GUARD.cooldown - 0.3).warning, true);
  assert.equal(guardAt(GUARD.cooldown + 0.1).guarding, true);
  assert.equal(guardAt(GUARD.cooldown + GUARD.duration + 0.1).guarding, false);
  assert.ok(Math.abs(guardAt(1).nextChangeIn - (GUARD.cooldown - 1)) < 1e-9);
});

test("combo: hits inside 2 s chain; the step bonus is capped at 10; a slow swing keeps the combo but does not add", () => {
  let s = newSession("Easy");
  let t = 0;
  for (let i = 0; i < 12; i++) ({ session: s } = applySwing(s, 3.5, { now: (t += 1) }));
  assert.equal(s.combo, 12);
  assert.equal(s.bestCombo, 12);
  const capped = hitXp(20, "medium", 15, "Easy"), atCap = hitXp(20, "medium", COMBO.cap, "Easy");
  assert.equal(capped, atCap);
  ({ session: s } = applySwing(s, 0.6, { now: (t += 0.5) }));
  assert.equal(s.combo, 12);
  ({ session: s } = applySwing(s, 3.5, { now: (t += 3) }));
  assert.equal(s.combo, 1);
  assert.equal(liveCombo(s, t + 2.5), 0);
});

test("session: totals, summary, the monster goes down at 0 HP and later swings are ignored", () => {
  let s = newSession("Easy");
  let t = 0, guards = 0;
  while (!s.defeated && t < 1000) {
    const guarding = guardAt(t).guarding;
    const r = applySwing(s, 6.5, { now: t, guarding });
    if (r.hit.guarded) guards++;
    s = r.session;
    t += 0.7;
  }
  assert.equal(s.defeated, true);
  assert.equal(s.hpLeft, 0);
  assert.ok(s.damage >= LEVELS.Easy.hp);
  assert.equal(s.guardedHits, guards);
  const sum = summary(s);
  assert.equal(sum.defeated, true);
  assert.equal(sum.hits, s.swings);
  assert.equal(sum.fullPower, s.swings);
  const after = applySwing(s, 6.5, { now: t });
  assert.equal(after.hit, null);
  assert.equal(after.session, s);
});

test("demo swings cover every band, inside the level's bands", () => {
  for (const level of ["Easy", "Medium"]) {
    const tiers = new Set();
    for (let n = 0; n < 6; n++) tiers.add(tierFor(demoSpeed(n, level), level));
    assert.deepEqual([...tiers].sort(), ["full", "light", "medium", "none", "tap"]);
  }
});

test("prefs: stored when allowed, an empty object when storage throws or holds junk", () => {
  const mem = new Map();
  const ok = { getItem: (k) => mem.get(k) ?? null, setItem: (k, v) => mem.set(k, v) };
  assert.equal(savePrefs({ best: { Easy: 5.1 } }, ok), true);
  assert.deepEqual(loadPrefs(ok), { best: { Easy: 5.1 } });
  const bad = { getItem: () => { throw new Error("blocked"); }, setItem: () => { throw new Error("blocked"); } };
  assert.deepEqual(loadPrefs(bad), {});
  assert.equal(savePrefs({}, bad), false);
  assert.deepEqual(loadPrefs({ getItem: () => "{nope" }), {});
});
