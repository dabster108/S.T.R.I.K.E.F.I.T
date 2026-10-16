// Daily and weekly challenges: pure rules (no database, no clock reads except where a `now` is passed), so
// test/challenges.test.js can run them against fixed fixtures. engageStore.js does the SQL.
//
// Days and weeks use the same local time zone constant as the streak and the daily goal
// (progression.DEFAULT_TZ, Asia/Kathmandu). A session counts for the local day it STARTED on (the streak's rule).
// Weeks run Monday to Sunday in that zone.
//
// Honesty: kcal is the project's ESTIMATE (fitness.js MET method), never a measurement. Progress is computed on read
// from sessions and events; only a completion is stored (table challenge_completions), because the bonus XP must be
// awarded exactly once.

const fitness = require("./fitness");
const progression = require("./progression");

// ---------------------------------------------------------------------------------------------------------------
// The challenge list.
//
// HOW TO ADD A CHALLENGE: append one object below. Fields:
//   id        unique, stable, lower-case (it is stored in challenge_completions.challenge_key; never rename one)
//   title     short name shown on the card
//   metric    one of METRICS (below)
//   target    the FIXED FALLBACK target, used when there is not enough real data to scale from
//   scale     optional: multiply the real median (daily or weekly total of players with 3+ active days) by this
//             to get the target. Leave it out for yes/no metrics (clearLevel, clearDamage).
//   period    "daily" or "weekly"
//   tier      daily only: "easy", "medium" or "stretch" (one of each is offered per day)
//   bonusXp   XP added once when it is completed
//   level     clearLevel / clearDamage only: "Easy" | "Medium" | "Hard" (clearDamage: optional, any level if omitted)
//   maxDamagePercent  clearDamage only: the most damage (% of max health) the win may cost
// Every target is clamped to SAFETY_CAPS whatever the data says. Rotation is deterministic (seeded by the date), so
// adding an entry changes which challenge some future days get, never a past day's completions.
// ---------------------------------------------------------------------------------------------------------------
const CHALLENGES = [
  // daily, easy
  { id: "d-show-up", title: "Show up", metric: "sessions", target: 1, period: "daily", tier: "easy", bonusXp: 25 },
  { id: "d-full-warmup", title: "Full-power warm-up", metric: "fullSwings", target: 10, scale: 0.5, period: "daily", tier: "easy", bonusXp: 30 },
  { id: "d-clear-l1", title: "Clear Level 1", metric: "clearLevel", level: "Easy", target: 1, period: "daily", tier: "easy", bonusXp: 30 },
  { id: "d-move-a-bit", title: "Keep moving", metric: "activeMinutes", target: 3, scale: 0.5, period: "daily", tier: "easy", bonusXp: 25 },
  // daily, medium
  { id: "d-burn", title: "Steady effort", metric: "kcal", target: 10, scale: 1, period: "daily", tier: "medium", bonusXp: 50 },
  { id: "d-full-power", title: "Full power", metric: "fullSwings", target: 20, scale: 1, period: "daily", tier: "medium", bonusXp: 50 },
  { id: "d-combo", title: "Chain it", metric: "bestCombo", target: 8, scale: 1, period: "daily", tier: "medium", bonusXp: 50 },
  { id: "d-clear-l2", title: "Clear Level 2", metric: "clearLevel", level: "Medium", target: 1, period: "daily", tier: "medium", bonusXp: 60 },
  // daily, stretch
  { id: "d-guarded-win", title: "Guarded win", metric: "clearDamage", maxDamagePercent: 50, target: 1, period: "daily", tier: "stretch", bonusXp: 80 },
  { id: "d-active-stretch", title: "Longer session", metric: "activeMinutes", target: 6, scale: 1.5, period: "daily", tier: "stretch", bonusXp: 70 },
  { id: "d-full-stretch", title: "Power stretch", metric: "fullSwings", target: 30, scale: 1.5, period: "daily", tier: "stretch", bonusXp: 70 },
  { id: "d-clear-l3", title: "Clear Level 3", metric: "clearLevel", level: "Hard", target: 1, period: "daily", tier: "stretch", bonusXp: 100 },
  // weekly
  { id: "w-three-fights", title: "Fights this week", metric: "sessions", target: 3, scale: 1, period: "weekly", bonusXp: 120 },
  { id: "w-effort", title: "Weekly effort", metric: "kcal", target: 30, scale: 1, period: "weekly", bonusXp: 120 },
  { id: "w-full-power", title: "Weekly full power", metric: "fullSwings", target: 60, scale: 1, period: "weekly", bonusXp: 120 },
  { id: "w-careful-win", title: "Careful win", metric: "clearDamage", maxDamagePercent: 40, target: 1, period: "weekly", bonusXp: 150 },
];

const METRICS = ["fullSwings", "kcal", "sessions", "activeMinutes", "bestCombo", "clearLevel", "clearDamage"];
const DAILY_TIERS = ["easy", "medium", "stretch"];

// --- Safety caps (tunable, named). No target can leave these ranges, whatever the data says. -----------------------
// Daily caps keep a "stretch" day short (at most 3 fights, 20 active minutes); weekly caps stay under one fight a day.
const SAFETY_CAPS = {
  daily: {
    fullSwings: { min: 5, max: 150, step: 5 },
    kcal: { min: 5, max: 60, step: 1 },
    sessions: { min: 1, max: 3, step: 1 },
    activeMinutes: { min: 2, max: 20, step: 1 },
    bestCombo: { min: 3, max: 25, step: 1 },
  },
  weekly: {
    fullSwings: { min: 20, max: 500, step: 10 },
    kcal: { min: 10, max: 200, step: 5 },
    sessions: { min: 2, max: 5, step: 1 },
    activeMinutes: { min: 5, max: 75, step: 5 },
    bestCombo: { min: 3, max: 25, step: 1 },
  },
};
// The damage limit of a "win without taking more than X% damage" challenge: never stricter than MIN (no near-flawless demands).
const DAMAGE_PERCENT_CAP = { min: 25, max: 100 };

// Targets scale from players with at least this many active days; with fewer such players the fixed targets are used.
const MEDIAN_MIN_ACTIVE_DAYS = 3;
const MEDIAN_MIN_PLAYERS = 2;
const MEDIAN_MIN_VALUES = 3; // a metric needs at least this many day (or week) values before its median is trusted

// After this many consecutive active days the card shows a rest-day note and nothing suggests another session.
const REST_NOTE_AFTER_DAYS = 5;
const SAFETY_LINE = "Stop if you feel dizzy or in pain.";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round1 = (n) => Math.round(n * 10) / 10;
const LEVEL_NUMBER = { Easy: 1, Medium: 2, Hard: 3 };

// ---------------------------------------------------------------------------
// Periods
// ---------------------------------------------------------------------------
const weekStartOf = progression.weekStartOf;
// -> { day, weekStart, weekEnd } in `zone` for the instant `now`.
function periodsAt(now, zone) {
  const day = fitness.dayIn(now, zone);
  const weekStart = weekStartOf(day);
  return { day, weekStart, weekEnd: fitness.dayFromNum(fitness.dayNum(weekStart) + 6) };
}
const periodStartFor = (challenge, periods) => (challenge.period === "weekly" ? periods.weekStart : periods.day);
const challengeKey = (id, periodStart) => `${id}@${periodStart}`;

// ---------------------------------------------------------------------------
// Rotation: deterministic, seeded by the local date (FNV-1a, 32 bit)
// ---------------------------------------------------------------------------
function hash32(text) {
  let h = 0x811c9dc5;
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i);
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h >>> 0;
}

// The day's three (easy, medium, stretch) and the week's one. A metric already picked for the day is skipped by the
// next tier when another candidate exists, so the three daily cards ask for different things.
function rotationFor(day, list = CHALLENGES) {
  const weekStart = weekStartOf(day);
  const used = new Set();
  const daily = [];
  for (const tier of DAILY_TIERS) {
    const pool = list.filter((c) => c.period === "daily" && c.tier === tier);
    if (!pool.length) continue;
    const fresh = pool.filter((c) => !used.has(c.metric));
    const from = fresh.length ? fresh : pool;
    const pick = from[hash32(`${day}|${tier}`) % from.length];
    used.add(pick.metric);
    daily.push(pick);
  }
  const weeklyPool = list.filter((c) => c.period === "weekly");
  const weekly = weeklyPool.length ? [weeklyPool[hash32(`${weekStart}|weekly`) % weeklyPool.length]] : [];
  return { day, weekStart, daily, weekly };
}

// ---------------------------------------------------------------------------
// Targets: scaled from real medians, fixed fallbacks, always clamped to the caps
// ---------------------------------------------------------------------------
function median(values) {
  const v = values.filter(isNum).sort((a, b) => a - b);
  if (!v.length) return null;
  const m = v.length >> 1;
  return v.length % 2 ? v[m] : (v[m - 1] + v[m]) / 2;
}

function clampToCaps(metric, period, value) {
  if (metric === "clearLevel" || metric === "clearDamage") return 1;
  const cap = SAFETY_CAPS[period] && SAFETY_CAPS[period][metric];
  if (!cap) return value;
  const stepped = Math.round(value / cap.step) * cap.step;
  return Math.min(cap.max, Math.max(cap.min, stepped));
}
const clampDamagePercent = (p) => Math.min(DAMAGE_PERCENT_CAP.max, Math.max(DAMAGE_PERCENT_CAP.min, Math.round(p)));

// medians: { daily: { source, values: { metric: number|null } }, weekly: {...} } (mediansFor below).
// -> { target, targetSource: "data" | "fallback" | "fixed", median }. "fixed" = a challenge with no scale (e.g. 1 fight).
function targetFor(challenge, medians) {
  const fixed = clampToCaps(challenge.metric, challenge.period, challenge.target);
  if (!isNum(challenge.scale)) return { target: fixed, targetSource: "fixed", median: null };
  const basis = medians && medians[challenge.period];
  const m = basis && basis.source === "data" && basis.values ? basis.values[challenge.metric] : null;
  if (!isNum(m) || m <= 0) return { target: fixed, targetSource: "fallback", median: null };
  return { target: clampToCaps(challenge.metric, challenge.period, m * challenge.scale), targetSource: "data", median: m };
}

// rows: [{ playerName, day, fullSwings, kcal, sessions, activeMinutes, bestCombo }] one per player per active local day
// (kcal / bestCombo null when unknown). cutoffDay: only days BEFORE it count for daily medians, only weeks before its
// week count for weekly medians, so a period's targets are fixed for the whole period.
function medianBasis(rows, cutoffDay) {
  const cutoffWeek = weekStartOf(cutoffDay);
  const before = rows.filter((r) => r.day < cutoffDay);
  const daysByPlayer = new Map();
  for (const r of before) {
    if (!daysByPlayer.has(r.playerName)) daysByPlayer.set(r.playerName, []);
    daysByPlayer.get(r.playerName).push(r);
  }
  const eligible = [...daysByPlayer.entries()].filter(([, days]) => days.length >= MEDIAN_MIN_ACTIVE_DAYS);
  const scaled = ["fullSwings", "kcal", "sessions", "activeMinutes", "bestCombo"];
  if (eligible.length < MEDIAN_MIN_PLAYERS) {
    return {
      source: "fallback",
      playersUsed: eligible.length,
      note: `Fewer than ${MEDIAN_MIN_PLAYERS} players with ${MEDIAN_MIN_ACTIVE_DAYS}+ active days before this period: fixed starting targets (labelled as fallbacks).`,
      daily: {},
      weekly: {},
    };
  }
  const daily = {};
  const weekly = {};
  for (const metric of scaled) {
    const dayValues = eligible.flatMap(([, days]) => days.map((d) => d[metric])).filter(isNum);
    daily[metric] = dayValues.length >= MEDIAN_MIN_VALUES ? round1(median(dayValues)) : null;
    const weekValues = [];
    for (const [, days] of eligible) {
      const byWeek = new Map();
      for (const d of days) {
        const wk = weekStartOf(d.day);
        if (wk >= cutoffWeek) continue;
        if (!isNum(d[metric])) continue;
        byWeek.set(wk, metric === "bestCombo" ? Math.max(byWeek.get(wk) || 0, d[metric]) : (byWeek.get(wk) || 0) + d[metric]);
      }
      weekValues.push(...byWeek.values());
    }
    weekly[metric] = weekValues.length >= MEDIAN_MIN_VALUES ? round1(median(weekValues)) : null;
  }
  return {
    source: "data",
    playersUsed: eligible.length,
    note: `Scaled from the median daily (or weekly) totals of ${eligible.length} players with ${MEDIAN_MIN_ACTIVE_DAYS}+ active days, counting days before this period only, then clamped to the safety caps.`,
    daily,
    weekly,
  };
}

// Daily targets use days before today; weekly targets use weeks before this one (and players eligible before it).
function mediansFor(rows, periods) {
  const d = medianBasis(rows, periods.day);
  const w = medianBasis(rows, periods.weekStart);
  return {
    daily: { source: d.source, playersUsed: d.playersUsed, note: d.note, values: d.daily },
    weekly: { source: w.source, playersUsed: w.playersUsed, note: w.note, values: w.weekly },
  };
}

// ---------------------------------------------------------------------------
// Progress (computed on read)
// ---------------------------------------------------------------------------
// facts: the player's finished sessions in the period, each
//   { level, outcome, fullSwings, kcal|null, activeMinutes, bestCombo|null, damagePercent|null }
function valueOf(challenge, facts) {
  switch (challenge.metric) {
    case "fullSwings": return { value: facts.reduce((a, f) => a + (f.fullSwings || 0), 0) };
    case "kcal": return { value: round1(facts.reduce((a, f) => a + (isNum(f.kcal) ? f.kcal : 0), 0)) };
    case "sessions": return { value: facts.length };
    case "activeMinutes": return { value: facts.reduce((a, f) => a + (f.activeMinutes || 0), 0) };
    case "bestCombo": {
      const known = facts.filter((f) => isNum(f.bestCombo));
      // No combo recorded in any of these sessions (older game builds): say so instead of showing 0 as a result.
      return { value: known.length ? Math.max(...known.map((f) => f.bestCombo)) : 0, tracking: facts.length > 0 && !known.length };
    }
    case "clearLevel":
      return { value: facts.some((f) => f.outcome === "victory" && f.level === challenge.level) ? 1 : 0 };
    case "clearDamage": {
      const limit = clampDamagePercent(challenge.maxDamagePercent ?? DAMAGE_PERCENT_CAP.max);
      const wins = facts.filter((f) => f.outcome === "victory" && (!challenge.level || f.level === challenge.level));
      const measured = wins.filter((f) => isNum(f.damagePercent));
      return { value: measured.some((f) => f.damagePercent <= limit) ? 1 : 0, unmeasured: wins.length > measured.length && !measured.length };
    }
    default: return { value: 0 };
  }
}

function describe(challenge, target) {
  const n = (v) => (Number.isInteger(v) ? String(v) : String(round1(v)));
  switch (challenge.metric) {
    case "fullSwings": return `Land ${n(target)} full-power swings`;
    case "kcal": return `Reach ${n(target)} kcal (estimated)`;
    case "sessions": return `Finish ${n(target)} fight${target === 1 ? "" : "s"}`;
    case "activeMinutes": return `${n(target)} active minute${target === 1 ? "" : "s"} (minutes with at least one swing)`;
    case "bestCombo": return `Reach a ${n(target)}-hit combo`;
    case "clearLevel": return `Win a fight on Level ${LEVEL_NUMBER[challenge.level] || challenge.level}`;
    case "clearDamage": {
      const where = challenge.level ? ` on Level ${LEVEL_NUMBER[challenge.level]}` : "";
      return `Win a fight${where} losing at most ${clampDamagePercent(challenge.maxDamagePercent ?? 100)}% of your health`;
    }
    default: return challenge.title;
  }
}

// One card's worth: the challenge with its target, progress and completion state.
// completions: Map challenge_key -> { completedAt, bonusXp }.
function evaluate(challenge, facts, { periods, medians, completions = new Map() }) {
  const periodStart = periodStartFor(challenge, periods);
  const key = challengeKey(challenge.id, periodStart);
  const t = targetFor(challenge, medians);
  const v = valueOf(challenge, facts);
  const target = t.target;
  const percent = target > 0 ? Math.min(100, Math.round((v.value / target) * 100)) : 0;
  const done = completions.get(key) || null;
  return {
    id: challenge.id,
    key,
    title: challenge.title,
    description: describe(challenge, target),
    metric: challenge.metric,
    period: challenge.period,
    tier: challenge.tier || null,
    level: challenge.level || null,
    maxDamagePercent: challenge.metric === "clearDamage" ? clampDamagePercent(challenge.maxDamagePercent ?? 100) : null,
    periodStart,
    periodEnd: challenge.period === "weekly" ? periods.weekEnd : periods.day,
    resets: challenge.period === "weekly" ? "Resets on Monday" : "Resets tomorrow",
    target,
    targetSource: t.targetSource,
    median: t.median,
    value: v.value,
    percent,
    met: v.value >= target,
    tracking: Boolean(v.tracking),
    unmeasured: Boolean(v.unmeasured),
    bonusXp: challenge.bonusXp,
    completed: Boolean(done),
    completedAt: done ? done.completedAt : null,
  };
}

// The cards whose progress is met but that have no completion row yet: these are recorded (idempotently) and announced.
function newlyMet(cards) {
  return cards.filter((c) => c.met && !c.completed);
}

// ---------------------------------------------------------------------------
// Rest day
// ---------------------------------------------------------------------------
// days: YYYY-MM-DD local days with a session. Consecutive active days ending today (or yesterday, before today's first
// session). No freeze: this is about rest, not about keeping a streak alive.
function consecutiveActiveDays(days, today) {
  const set = new Set(days);
  const todayN = fitness.dayNum(today);
  let n = set.has(today) ? todayN : todayN - 1;
  let run = 0;
  while (set.has(fitness.dayFromNum(n))) {
    run++;
    n--;
  }
  return run;
}

function restNote(days, today) {
  const run = consecutiveActiveDays(days, today);
  const show = run >= REST_NOTE_AFTER_DAYS;
  return {
    consecutiveActiveDays: run,
    show,
    afterDays: REST_NOTE_AFTER_DAYS,
    text: show ? `${run} active days in a row. A rest day is part of training too; your challenges will still be here.` : null,
  };
}

module.exports = {
  CHALLENGES, METRICS, DAILY_TIERS, SAFETY_CAPS, DAMAGE_PERCENT_CAP, MEDIAN_MIN_ACTIVE_DAYS, MEDIAN_MIN_PLAYERS, MEDIAN_MIN_VALUES,
  REST_NOTE_AFTER_DAYS, SAFETY_LINE,
  periodsAt, periodStartFor, challengeKey, hash32, rotationFor,
  median, clampToCaps, clampDamagePercent, targetFor, medianBasis, mediansFor,
  valueOf, describe, evaluate, newlyMet,
  consecutiveActiveDays, restNote,
  DEFAULT_TZ: progression.DEFAULT_TZ, safeZone: progression.safeZone, dayIn: progression.dayIn,
};
