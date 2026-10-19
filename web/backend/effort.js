// Effort maths for the effort-fade, fight-fingerprint, ghost-pace and Thesis Lab features. Pure functions: no database,
// no clock (tests: test/effort.test.js). Everything is computed from events the game already stores:
//   hit            payload { t, swingSpeed, tier, damage, defended }      t = the fight clock in seconds
//   monster_attack payload { t, attack }        one per attack the monster starts
//   player_hurt    payload { t, amount, attack } one per damage tick the player takes (amount > 0)
// Swing speeds are clamped to the game's cap (levels.js SWING_SPEED_CAP) before any average is taken.

const { SWING_SPEED_CAP } = require("./levels");

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round = (v, d = 1) => (isNum(v) ? Math.round(v * 10 ** d) / 10 ** d : null);
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const mean = (xs) => xs.reduce((a, v) => a + v, 0) / xs.length;

// ---------------------------------------------------------------------------
// B1  Effort fade
// ---------------------------------------------------------------------------
// fade% = (average swing speed in the LAST third of the fight - average in the FIRST third) / average in the first third.
// "Thirds" are thirds of fight time (the fight's length is its duration, or its last swing if that is later).

const FADE = {
  minSwings: 6, // fewer landed swings than this and there is no fade, only "Not enough swings"
  minPerEnd: 2, // and each end third needs this many, or its average is one lucky swing
  steadyBandPct: 5, // |fade| under this reads as "Held steady"
};

const FADE_LABELS = {
  steady: "Held steady",
  slowed: "Slowed down",
  stronger: "Got stronger",
};

function fadeLabel(fadePct) {
  if (fadePct <= -FADE.steadyBandPct) return "slowed";
  if (fadePct >= FADE.steadyBandPct) return "stronger";
  return "steady";
}

/** hits: [{ t, speed }]. -> { status: "ok", firstAvg, lastAvg, fadePct, label, labelText, ... } | { status: "insufficient", reason, ... } */
function fade(hits, durationSec) {
  const pts = (hits || [])
    .filter((h) => h && isNum(h.t) && h.t >= 0 && isNum(h.speed) && h.speed >= 0)
    .map((h) => ({ t: h.t, speed: Math.min(h.speed, SWING_SPEED_CAP) }));
  const base = { swings: pts.length, needed: FADE.minSwings };
  if (pts.length < FADE.minSwings) {
    return { status: "insufficient", reason: `Not enough swings (${pts.length} of the ${FADE.minSwings} needed)`, ...base };
  }
  const length = Math.max(isNum(durationSec) ? durationSec : 0, ...pts.map((p) => p.t));
  if (!(length > 0)) return { status: "insufficient", reason: "Not enough swings (no fight time recorded)", ...base };
  const firstEnd = length / 3;
  const lastStart = (2 * length) / 3;
  const first = pts.filter((p) => p.t < firstEnd);
  const last = pts.filter((p) => p.t >= lastStart);
  if (first.length < FADE.minPerEnd || last.length < FADE.minPerEnd) {
    const which = first.length < FADE.minPerEnd ? "first" : "last";
    return { status: "insufficient", reason: `Not enough swings in the ${which} third of the fight`, ...base, firstSwings: first.length, lastSwings: last.length };
  }
  const firstAvg = mean(first.map((p) => p.speed));
  const lastAvg = mean(last.map((p) => p.speed));
  if (!(firstAvg > 0)) return { status: "insufficient", reason: "Not enough swings (the first third has no measurable speed)", ...base };
  const fadePct = ((lastAvg - firstAvg) / firstAvg) * 100;
  const label = fadeLabel(fadePct);
  return {
    status: "ok",
    swings: pts.length,
    firstSwings: first.length,
    lastSwings: last.length,
    firstAvg: round(firstAvg, 2),
    lastAvg: round(lastAvg, 2),
    fadePct: round(fadePct, 1),
    label,
    labelText: FADE_LABELS[label],
    lengthSec: round(length, 1),
    firstEndSec: round(firstEnd, 1),
    lastStartSec: round(lastStart, 1),
  };
}

/** Mean fade over the sessions that had enough swings. results: fade() outputs. */
function averageFade(results) {
  const ok = (results || []).filter((r) => r && r.status === "ok");
  if (!ok.length) return { status: "insufficient", sessionsUsed: 0, sessionsConsidered: (results || []).length, reason: "Not enough swings in any of these fights" };
  const fadePct = round(mean(ok.map((r) => r.fadePct)), 1);
  const label = fadeLabel(fadePct);
  return { status: "ok", fadePct, label, labelText: FADE_LABELS[label], sessionsUsed: ok.length, sessionsConsidered: results.length };
}

// ---------------------------------------------------------------------------
// B3  Effort fingerprint (five axes, each 0-100)
// ---------------------------------------------------------------------------
//   Power         = average swing speed / speed cap (8 m/s)                                   x 100
//   Consistency   = 100 - (standard deviation of swing speed / mean swing speed x 100)         clamped 0-100
//   Stamina       = 100 + average fade % (the last-10-fights average from B1)                  clamped 0-100
//   Dodge         = share of monster attacks that did NOT hurt the player within 1.5 s of the attack starting   x 100
//                   (monster_attack.t against player_hurt.t on the fight clock; an attack or hurt tick without a time is left out)
//   Guard timing  = share of landed hits that came while the monster was NOT guarding          x 100
//                   (hit.payload.defended is stored, so this axis exists; it is dropped when no hit carries the flag)
// Notes: the old Dodge (1 - hurt ticks / attacks) was 0 for every player: almost every attack lands and some hurt twice.
// The time window counts each attack once. An attack whose wind-up is longer than the window (Level 1's 1.6 s Leaping Slam)
// can land after it and still count as avoided; dragon fire is a hazard, not a monster attack, and is excluded by the caller.
// An axis with no data behind it is null and left off the chart; it is never shown as 0.

const FINGERPRINT = { minSwings: 10, minAttacks: 5, dodgeWindowSec: 1.5 };

const AXES = [
  { key: "power", label: "Power", formula: "Average swing speed ÷ the game's 8 m/s cap" },
  { key: "consistency", label: "Consistency", formula: "100 − (standard deviation of swing speed ÷ mean swing speed × 100), kept between 0 and 100" },
  { key: "stamina", label: "Stamina", formula: "100 + average fade % over the last 10 fights, kept between 0 and 100 (no fade = 100)" },
  { key: "dodge", label: "Dodge", formula: "Share of the monster's attacks that did not hurt you within 1.5 s of starting" },
  { key: "guard", label: "Guard timing", formula: "Share of your hits that landed while the monster was not guarding" },
];

function stdev(xs) {
  const m = mean(xs);
  return Math.sqrt(mean(xs.map((v) => (v - m) ** 2)));
}

/**
 * Dodge evidence. fights: [{ attacks: [t, ...], hurts: [t, ...] }] in fight-clock seconds (monster_attack and damaging player_hurt).
 * An attack is avoided when no hurt tick falls in [t, t + windowSec]. -> { attacks, avoided, untimed, share | null }
 */
function dodgeShare(fights, windowSec = FINGERPRINT.dodgeWindowSec) {
  let attacks = 0;
  let avoided = 0;
  let untimed = 0;
  for (const f of fights || []) {
    const hurts = (f.hurts || []).filter(isNum).sort((a, b) => a - b);
    for (const t of f.attacks || []) {
      if (!isNum(t)) {
        untimed++;
        continue;
      }
      attacks++;
      if (!hurts.some((h) => h >= t && h <= t + windowSec)) avoided++;
    }
  }
  return { attacks, avoided, untimed, share: attacks >= FINGERPRINT.minAttacks ? avoided / attacks : null };
}

/**
 * hits: [{ speed, defended? }]; fades: fade() outputs for the fights to average (the last 10);
 * attackFights: per fight, the times of monster attacks and of damaging hurt ticks (see dodgeShare).
 */
function fingerprint({ hits, fades, attackFights }) {
  const speeds = (hits || []).filter((h) => isNum(h.speed)).map((h) => Math.min(h.speed, SWING_SPEED_CAP));
  const enough = speeds.length >= FINGERPRINT.minSwings;
  const values = {};

  if (enough) {
    const m = mean(speeds);
    values.power = clamp((m / SWING_SPEED_CAP) * 100, 0, 100);
    values.consistency = m > 0 ? clamp(100 - (stdev(speeds) / m) * 100, 0, 100) : null;
    const avg = averageFade(fades);
    values.stamina = avg.status === "ok" ? clamp(100 + avg.fadePct, 0, 100) : null;
    const flagged = (hits || []).filter((h) => typeof h.defended === "boolean");
    values.guard = flagged.length >= FINGERPRINT.minSwings ? (flagged.filter((h) => !h.defended).length / flagged.length) * 100 : null;
  }
  const dodge = dodgeShare(attackFights);
  values.dodge = dodge.share === null ? null : clamp(dodge.share, 0, 1) * 100;

  const axes = AXES.map((a) => ({ ...a, score: isNum(values[a.key]) ? Math.round(values[a.key]) : null }));
  const have = axes.filter((a) => a.score !== null);
  return {
    status: have.length >= 3 ? "ok" : "insufficient",
    reason: have.length >= 3 ? null : `Not enough data yet (needs ${FINGERPRINT.minSwings}+ landed swings and ${FINGERPRINT.minAttacks}+ monster attacks)`,
    swings: speeds.length,
    attacks: dodge.attacks,
    attacksAvoided: dodge.avoided,
    hitsTaken: (attackFights || []).reduce((a, f) => a + (f.hurts || []).length, 0),
    dodgeWindowSec: FINGERPRINT.dodgeWindowSec,
    axes,
    omitted: axes.filter((a) => a.score === null).map((a) => a.key),
  };
}

// ---------------------------------------------------------------------------
// B4  Ghost pace: cumulative damage against fight time
// ---------------------------------------------------------------------------

/** hits: [{ t, damage }]. -> [{ t, damage }] cumulative, starting at { 0, 0 }, at most maxPoints long (always keeps the last). */
function cumulativeDamage(hits, maxPoints = 240) {
  const sorted = (hits || []).filter((h) => isNum(h.t) && h.t >= 0 && isNum(h.damage) && h.damage > 0).sort((a, b) => a.t - b.t);
  let total = 0;
  const points = [{ t: 0, damage: 0 }];
  for (const h of sorted) {
    total += h.damage;
    points.push({ t: round(h.t, 2), damage: round(total, 1) });
  }
  if (points.length <= maxPoints) return points;
  const step = (points.length - 1) / (maxPoints - 1);
  const out = [];
  for (let i = 0; i < maxPoints - 1; i++) out.push(points[Math.round(i * step)]);
  out.push(points[points.length - 1]);
  return out;
}

/** Cumulative damage at time t on a curve from cumulativeDamage (step curve: a hit counts from its own moment on). */
function damageAt(points, t) {
  let v = 0;
  for (const p of points) {
    if (p.t <= t) v = p.damage;
    else break;
  }
  return v;
}

// ---------------------------------------------------------------------------
// B5  Thesis Lab statistics
// ---------------------------------------------------------------------------

const RPE_MIN_PAIRS = 8;
const RETURN_WINDOWS_DAYS = [1, 3, 7];
const SMALL_SAMPLE = 10;

/** Pearson r over paired points; null with fewer than 2 pairs or no spread in either variable. */
function pearson(xs, ys) {
  const n = Math.min(xs.length, ys.length);
  if (n < 2) return null;
  const mx = mean(xs.slice(0, n));
  const my = mean(ys.slice(0, n));
  let sxy = 0, sxx = 0, syy = 0;
  for (let i = 0; i < n; i++) {
    sxy += (xs[i] - mx) * (ys[i] - my);
    sxx += (xs[i] - mx) ** 2;
    syy += (ys[i] - my) ** 2;
  }
  if (!(sxx > 0) || !(syy > 0)) return null;
  return clamp(sxy / Math.sqrt(sxx * syy), -1, 1);
}

/** points: [{ rpe, avgSpeed }]. r only with at least RPE_MIN_PAIRS pairs. */
function rpeCorrelation(points) {
  const pairs = (points || []).filter((p) => isNum(p.rpe) && isNum(p.avgSpeed));
  if (pairs.length < RPE_MIN_PAIRS) return { status: "insufficient", n: pairs.length, minPairs: RPE_MIN_PAIRS, r: null };
  const r = pearson(pairs.map((p) => p.avgSpeed), pairs.map((p) => p.rpe));
  return { status: r === null ? "insufficient" : "ok", n: pairs.length, minPairs: RPE_MIN_PAIRS, r: round(r, 3) };
}

/**
 * Share of players who played again within 1, 3 and 7 days of their first session.
 * sessions: [{ player, startedAt }] (every recorded session, any outcome, live ones left out by the caller); nowMs: the clock.
 * A window only counts players whose first session is at least that many days old (n), otherwise "not yet" would read as "no".
 */
function returnRates(sessions, nowMs) {
  const byPlayer = new Map();
  for (const s of sessions || []) {
    const t = new Date(s.startedAt).getTime();
    if (!Number.isFinite(t)) continue;
    if (!byPlayer.has(s.player)) byPlayer.set(s.player, []);
    byPlayer.get(s.player).push(t);
  }
  const players = [...byPlayer.values()].map((ts) => ts.sort((a, b) => a - b));
  return {
    players: players.length,
    windows: RETURN_WINDOWS_DAYS.map((days) => {
      const span = days * 864e5;
      const eligible = players.filter((ts) => ts[0] + span <= nowMs);
      const returned = eligible.filter((ts) => ts.some((t) => t > ts[0] && t - ts[0] <= span));
      return {
        days,
        n: eligible.length,
        returned: returned.length,
        ratePct: eligible.length ? round((returned.length / eligible.length) * 100, 1) : null,
        smallSample: eligible.length < SMALL_SAMPLE,
      };
    }),
  };
}

module.exports = {
  FADE, FADE_LABELS, FINGERPRINT, AXES, RPE_MIN_PAIRS, RETURN_WINDOWS_DAYS, SMALL_SAMPLE,
  fade, fadeLabel, averageFade, fingerprint, dodgeShare, cumulativeDamage, damageAt, pearson, rpeCorrelation, returnRates,
  clamp, mean, stdev, round,
};
