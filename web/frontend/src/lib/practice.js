// /practice: swing-to-damage practice without a headset. Pure (no DOM), so the estimators and the rules are unit tested
// (practice.test.js). Every game number comes from lib/game.js (the Unity scene values) or is cited here:
//   damage curve, tiers, XP           game.js simulateHit / tierFor (HitDetection, XPManager, PlayerCombat)
//   speed cap 8 m/s                   game.js SWING_SPEED_CAP
//   guard: hits deal 30 %             MonsterAI.defendDamageMultiplier = 0.3 (HP only; XP is awarded from the full damage,
//                                     as HitDetection calls AwardHitXP before the monster's guard reduces it)
//   guard timing 1.5 s every 8 s      MonsterAI.defendDuration / defendCooldown
//   combo: 2 s window, +8 XP a step,  XPManager.comboWindow / xpComboStepBonus / comboBonusCap (Level1_Easy, Level2_Medium)
//   capped at 10
// A phone or a mouse is not a VR controller: every speed here is an ESTIMATE, and nothing is sent anywhere.
import { LEVELS, SWING_SPEED_CAP, roundToInt, simulateHit, tierFor } from "./game.js";

export const PRACTICE_LEVELS = ["Easy", "Medium"]; // Level 3's weapons have their own values, not in game.js
export const GUARD_DAMAGE_MULTIPLIER = 0.3;
export const GUARD = { duration: 1.5, cooldown: 8, warn: 0.6 };
export const COMBO = { window: 2, stepXp: 8, cap: 10 };

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
export const clampSpeed = (v) => clamp(Number.isFinite(v) ? v : 0, 0, SWING_SPEED_CAP);

// ───────────────────────── phone motion ─────────────────────────
// DeviceMotion linear acceleration (m/s², gravity removed) is integrated over one swing window:
//   - a window opens when |a| passes startAccel, with the velocity reset to zero (drift cannot carry over between swings);
//   - inside it, v += a dt (trapezoid) with a small leak (v decays with time constant leakTau), so sensor bias does not pile up;
//   - it closes after |a| stays under quietAccel for quietMs, or after maxWindowMs;
//   - the swing's speed is the peak |v| in the window x scale, clamped to the 8 m/s cap; then a short refractory gap.
export const MOTION = { startAccel: 6, quietAccel: 2.5, quietMs: 120, minWindowMs: 60, maxWindowMs: 700, leakTau: 2, refractoryMs: 250, maxGapS: 0.05 };

export function createMotionEstimator(options = {}) {
  const o = { ...MOTION, scale: 1, ...options };
  let mode = "idle", t0 = 0, lastT = 0, quietSince = null, until = 0, peak = 0;
  let v = [0, 0, 0], prev = [0, 0, 0];
  const reset = () => {
    mode = "idle"; v = [0, 0, 0]; prev = [0, 0, 0]; peak = 0; quietSince = null;
  };
  return {
    get state() {
      return { mode, v: [...v], peak };
    },
    setScale(s) {
      o.scale = s;
    },
    reset,
    /** One sample { t (ms), x, y, z (m/s², no gravity) }. Returns a swing { speed, raw, ms } when a window closes, else null. */
    push({ t, x = 0, y = 0, z = 0 }) {
      const a = [x || 0, y || 0, z || 0];
      const mag = Math.hypot(a[0], a[1], a[2]);
      if (mode === "refractory") {
        if (t < until) return null;
        reset();
      }
      if (mode === "idle") {
        if (mag < o.startAccel) return null;
        mode = "active"; t0 = t; lastT = t; v = [0, 0, 0]; prev = a; peak = 0; quietSince = null;
        return null;
      }
      const dt = Math.min(o.maxGapS, Math.max(0, (t - lastT) / 1000));
      const leak = Math.exp(-dt / o.leakTau);
      for (let i = 0; i < 3; i++) v[i] = (v[i] + ((prev[i] + a[i]) / 2) * dt) * leak;
      prev = a; lastT = t;
      peak = Math.max(peak, Math.hypot(v[0], v[1], v[2]));
      quietSince = mag < o.quietAccel ? quietSince ?? t : null;
      const done = (quietSince !== null && t - quietSince >= o.quietMs) || t - t0 >= o.maxWindowMs;
      if (!done) return null;
      const ms = t - t0, raw = peak * o.scale;
      mode = "refractory"; until = t + o.refractoryMs;
      if (ms < o.minWindowMs) return null;
      return { speed: clampSpeed(raw), raw, ms };
    },
  };
}

/** For devices that only report accelerationIncludingGravity: a low-pass gravity estimate, subtracted (k = smoothing per sample). */
export function createGravityFilter(k = 0.1) {
  let g = null;
  return ({ x = 0, y = 0, z = 0 }) => {
    const a = [x || 0, y || 0, z || 0];
    g = g ? g.map((gi, i) => gi + (a[i] - gi) * k) : a;
    return { x: a[0] - g[0], y: a[1] - g[1], z: a[2] - g[2] };
  };
}

// ───────────────────────── mouse / touch drag ─────────────────────────
// Peak pointer speed over a drag, measured across ~50 ms so one jittery event cannot spike it, in m/s at a calibration of
// pxPerMetre (default 900 px = 1 m) x sensitivity. A press that barely moves (under 24 px or 40 ms) is a tap, not a swing.
export const DRAG = { pxPerMetre: 900, spanMs: 50, minPx: 24, minMs: 40 };

export function createDragTracker(options = {}) {
  const o = { ...DRAG, sensitivity: 1, ...options };
  let pts = [], peak = 0, travel = 0;
  return {
    setSensitivity(s) {
      o.sensitivity = s;
    },
    start({ t, x, y }) {
      pts = [{ t, x, y }]; peak = 0; travel = 0;
    },
    move({ t, x, y }) {
      if (!pts.length) return;
      const last = pts[pts.length - 1];
      travel += Math.hypot(x - last.x, y - last.y);
      pts.push({ t, x, y });
      let i = pts.length - 1;
      while (i > 0 && t - pts[i - 1].t <= o.spanMs) i--;
      const from = pts[Math.max(0, i - 1)];
      const span = (t - from.t) / 1000;
      if (span > 0) peak = Math.max(peak, Math.hypot(x - from.x, y - from.y) / span);
      if (pts.length > 64) pts = pts.slice(-32);
    },
    /** Ends the drag: a swing { speed, raw, pxPerSec } or null for a tap. */
    end({ t, x, y }) {
      if (!pts.length) return null;
      this.move({ t, x, y });
      const ms = t - pts[0].t;
      const res = travel < o.minPx || ms < o.minMs ? null : peak;
      pts = [];
      if (res === null) return null;
      const raw = (res / o.pxPerMetre) * o.sensitivity;
      return { speed: clampSpeed(raw), raw, pxPerSec: res };
    },
  };
}

// ───────────────────────── the demo swing ─────────────────────────
// The "Demo swing" button and the S key: one swing from each band in turn (below the gate, tap, light, medium, full), at a speed
// inside that band for the chosen level.
export function demoSpeed(n, level = "Easy") {
  const s = LEVELS[level].swing;
  const bands = [
    [0.6, s.min - 0.2],
    [s.min + 0.05, s.tiers.light - 0.05],
    [s.tiers.light + 0.2, s.tiers.medium - 0.2],
    [s.tiers.medium + 0.2, s.tiers.full - 0.2],
    [s.tiers.full + 0.3, SWING_SPEED_CAP - 0.5],
  ];
  const order = [3, 4, 2, 0, 1, 4];
  const [lo, hi] = bands[order[n % order.length]];
  const f = ((n * 0.618034) % 1 + 1) % 1; // spread inside the band, deterministic
  return +(lo + (hi - lo) * f).toFixed(2);
}

// ───────────────────────── the monster's guard ─────────────────────────
// The practice monster raises its guard for 1.5 s every 8 s (MonsterAI's defaults), with a short warning before it.
export function guardAt(seconds) {
  const cycle = GUARD.cooldown + GUARD.duration;
  const t = ((seconds % cycle) + cycle) % cycle;
  const guarding = t >= GUARD.cooldown;
  const warning = !guarding && t >= GUARD.cooldown - GUARD.warn;
  return { guarding, warning, nextChangeIn: guarding ? cycle - t : GUARD.cooldown - t };
}

// ───────────────────────── one hit, one session ─────────────────────────
/** XP for one counted hit, as XPManager.AwardHitXP: base + damage / 2, the combo step bonus, then the tier bonus or tap penalty. */
export function hitXp(damage, tier, combo, level = "Easy") {
  const s = LEVELS[level].swing;
  let xp = s.xpHit + roundToInt(damage * 0.5);
  if (combo > 1) xp += COMBO.stepXp * (Math.min(combo, COMBO.cap) - 1);
  if (tier === "full") xp += s.xpFull;
  else if (tier === "medium") xp += roundToInt(s.xpFull * 0.5);
  else if (tier === "tap") xp = Math.max(1, Math.trunc(xp / 2));
  return xp;
}

/**
 * One swing at `speed` (m/s, clamped to the cap) against the practice monster.
 *   combo / lastHitAt: the live combo before this swing and when the last counted hit landed (seconds); now: this swing's time.
 * Returns { speed, tier, counts, damage (to HP), fullDamage, guarded, xp, combo }.
 */
export function resolveSwing({ speed, level = "Easy", guarding = false, combo = 0, lastHitAt = -Infinity, now = 0 }) {
  const v = clampSpeed(speed);
  const base = simulateHit(v, level);
  if (!base.counts) return { speed: v, tier: "none", counts: false, damage: 0, fullDamage: 0, guarded: false, xp: 0, combo: now - lastHitAt <= COMBO.window ? combo : 0 };
  const nextCombo = now - lastHitAt <= COMBO.window ? combo + 1 : 1;
  const damage = guarding ? base.damage * GUARD_DAMAGE_MULTIPLIER : base.damage;
  return { speed: v, tier: tierFor(v, level), counts: true, damage, fullDamage: base.damage, guarded: guarding, xp: hitXp(base.damage, base.tier, nextCombo, level), combo: nextCombo };
}

export function newSession(level = "Easy") {
  return {
    level,
    hp: LEVELS[level].hp,
    hpLeft: LEVELS[level].hp,
    swings: 0,
    hits: 0,
    guardedHits: 0,
    tiers: { none: 0, tap: 0, light: 0, medium: 0, full: 0 },
    damage: 0,
    xp: 0,
    combo: 0,
    bestCombo: 0,
    lastHitAt: -Infinity,
    bestSpeed: 0,
    speedSum: 0,
    defeated: false,
  };
}

/** Applies one swing to a session (immutable). Swings after the monster is down are ignored. */
export function applySwing(session, speed, { now, guarding = false }) {
  if (session.defeated) return { session, hit: null };
  const hit = resolveSwing({ speed, level: session.level, guarding, combo: session.combo, lastHitAt: session.lastHitAt, now });
  const hpLeft = Math.max(0, session.hpLeft - hit.damage);
  const next = {
    ...session,
    swings: session.swings + 1,
    hits: session.hits + (hit.counts ? 1 : 0),
    guardedHits: session.guardedHits + (hit.counts && hit.guarded ? 1 : 0),
    tiers: { ...session.tiers, [hit.tier]: session.tiers[hit.tier] + 1 },
    damage: session.damage + hit.damage,
    xp: session.xp + hit.xp,
    combo: hit.combo,
    bestCombo: Math.max(session.bestCombo, hit.combo),
    lastHitAt: hit.counts ? now : session.lastHitAt,
    bestSpeed: Math.max(session.bestSpeed, hit.speed),
    speedSum: session.speedSum + hit.speed,
    hpLeft,
    defeated: hpLeft <= 0,
  };
  return { session: next, hit };
}

/** The combo as the HUD shows it: 0 once the window has passed. */
export const liveCombo = (session, now) => (now - session.lastHitAt <= COMBO.window ? session.combo : 0);

export function summary(session) {
  return {
    swings: session.swings,
    hits: session.hits,
    accuracy: session.swings ? session.hits / session.swings : 0,
    avgSpeed: session.swings ? session.speedSum / session.swings : 0,
    bestSpeed: session.bestSpeed,
    damage: session.damage,
    xp: session.xp,
    bestCombo: session.bestCombo,
    fullPower: session.tiers.full,
    guardedHits: session.guardedHits,
    hpLeftShare: session.hp ? session.hpLeft / session.hp : 0,
    defeated: session.defeated,
  };
}

// ───────────────────────── this browser only ─────────────────────────
// Per-visitor conveniences (best speed per level, the calibration), in localStorage when the browser allows it.
export const STORE_KEY = "strikefit.practice.v1";
export function loadPrefs(storage = globalThis.localStorage) {
  try {
    const v = JSON.parse(storage?.getItem(STORE_KEY) || "null");
    return v && typeof v === "object" ? v : {};
  } catch {
    return {};
  }
}
export function savePrefs(prefs, storage = globalThis.localStorage) {
  try {
    storage?.setItem(STORE_KEY, JSON.stringify(prefs));
    return true;
  } catch {
    return false;
  }
}
