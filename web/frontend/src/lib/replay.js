// The fight replay model (components/effort/FightReplay.jsx): the four kinds of events the game already stores, split into lanes,
// and a readout for any moment of the fight. Pure functions (tested in replay.test.js).
//
//   hit            payload { t, swingSpeed, tier, damage }   -> a swing bar (height = speed, colour = tier)
//   monster_attack payload { t, attack, category }           -> a marker on the monster lane
//   clash          payload { t, swingSpeed }                 -> a diamond
//   player_hurt    payload { t, amount, attack }             -> a red tick below the line (amount > 0 only)
// Other event types (hp snapshots, guards, dodges, blocks, pauses) are not drawn here; the Timeline replay above already has them.

import { clampSpeed } from "./game.js";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const P = (e) => (e && e.payload && typeof e.payload === "object" && !Array.isArray(e.payload) ? e.payload : {});

// What the game does not log yet, so the replay cannot show it (listed in the panel and in the report).
export const NOT_LOGGED = [
  "Swings that missed or never reached the monster (only too-slow touches are logged)",
  "The controller's speed between hits",
  "When a monster attack's wind-up begins (only its start and whether it landed)",
  "Where the player was standing, and which way they dodged",
];

/** events: [{ type, t, payload }] with t in fight seconds (the detail page's timed()). -> lanes sorted by time. */
export function buildReplay(events, durationSec = 0) {
  const swings = [];
  const attacks = [];
  const clashes = [];
  const hurts = [];
  for (const e of events || []) {
    const p = P(e);
    const t = isNum(e.t) ? e.t : p.t;
    if (!isNum(t) || t < 0) continue;
    if (e.type === "hit" && isNum(p.swingSpeed)) swings.push({ t, speed: clampSpeed(p.swingSpeed), rawSpeed: p.swingSpeed, tier: String(p.tier || "").toLowerCase() || null, damage: isNum(p.damage) ? p.damage : null });
    else if (e.type === "monster_attack") attacks.push({ t, attack: typeof p.attack === "string" ? p.attack : "Attack", category: typeof p.category === "string" ? p.category : null });
    else if (e.type === "clash") clashes.push({ t, speed: isNum(p.swingSpeed) ? clampSpeed(p.swingSpeed) : null });
    else if (e.type === "player_hurt" && isNum(p.amount) && p.amount > 0) hurts.push({ t, amount: p.amount, attack: typeof p.attack === "string" ? p.attack : null });
  }
  const byT = (a, b) => a.t - b.t;
  [swings, attacks, clashes, hurts].forEach((l) => l.sort(byT));
  const last = Math.max(0, ...[swings, attacks, clashes, hurts].map((l) => (l.length ? l[l.length - 1].t : 0)));
  const duration = Math.max(isNum(durationSec) ? durationSec : 0, last);
  return {
    duration,
    swings,
    attacks,
    clashes,
    hurts,
    maxHurt: hurts.reduce((m, h) => Math.max(m, h.amount), 0),
    isEmpty: !swings.length && !attacks.length && !clashes.length && !hurts.length,
  };
}

const lastAtOrBefore = (list, t) => {
  let found = null;
  for (const x of list) {
    if (x.t <= t + 1e-9) found = x;
    else break;
  }
  return found;
};

// An attack counts as "current" this long after it started (the monster's moves last a second or two).
export const ATTACK_WINDOW_SEC = 3;

/** What the readout under the scrubber says at time t. */
export function readoutAt(model, t) {
  const swing = lastAtOrBefore(model.swings, t);
  const attack = lastAtOrBefore(model.attacks, t);
  const hurtsSoFar = model.hurts.filter((h) => h.t <= t + 1e-9);
  const lastHurt = hurtsSoFar[hurtsSoFar.length - 1] || null;
  return {
    t,
    swing, // the latest swing at or before t, or null
    swingAgeSec: swing ? t - swing.t : null,
    swingsSoFar: model.swings.filter((s) => s.t <= t + 1e-9).length,
    damageTaken: hurtsSoFar.reduce((a, h) => a + h.amount, 0),
    ticksTaken: hurtsSoFar.length,
    lastHurt,
    attack: attack && t - attack.t <= ATTACK_WINDOW_SEC ? attack : null,
    lastAttack: attack,
    clashesSoFar: model.clashes.filter((c) => c.t <= t + 1e-9).length,
  };
}

/** The next / previous event time around t (any lane), for "jump to" buttons. null when there is none. */
export function neighbourTime(model, t, dir) {
  const all = [...model.swings, ...model.attacks, ...model.clashes, ...model.hurts].map((e) => e.t);
  if (dir > 0) {
    const next = all.filter((x) => x > t + 0.01);
    return next.length ? Math.min(...next) : null;
  }
  const prev = all.filter((x) => x < t - 0.01);
  return prev.length ? Math.max(...prev) : null;
}

/** One sentence that stands in for the picture (screen readers, and the no-JS-chart fallback). */
export function replaySummary(model) {
  if (model.isEmpty) return "No swings, attacks, clashes or damage were recorded for this fight.";
  const parts = [`${model.swings.length} landed swing${model.swings.length === 1 ? "" : "s"}`, `${model.attacks.length} monster attack${model.attacks.length === 1 ? "" : "s"}`, `${model.clashes.length} weapon clash${model.clashes.length === 1 ? "" : "es"}`, `${model.hurts.length} time${model.hurts.length === 1 ? "" : "s"} the player took damage`];
  return `Over ${Math.round(model.duration)} seconds: ${parts.join(", ")}.`;
}
