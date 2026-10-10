// Daily goals: pure helpers (no database, no clock reads except where a `now` is passed), so
// test/goals.test.js can run them against fixed fixtures. goalsStore.js does the SQL.
//
// A goal is one metric and one target for ONE local day of the player (same time zone constant as
// the streak and the achievements: progression.DEFAULT_TZ, Asia/Kathmandu). A session counts for
// the local day it STARTED on, the same rule the streak uses.
//
// Honesty: kcal is the project's ESTIMATE (fitness.js MET method), never a measurement. There is no
// login, so anyone who knows a player's name can set that player's goal.

const fitness = require("./fitness");
const progression = require("./progression");

// --- Tunables (named constants; stated assumptions) ---------------------------------------------
const METRICS = ["kcal", "fullSwings", "activeMinutes", "xp"];
// Accepted target range per metric (inclusive). Safety caps: no goal asks for more than is sensible in one day.
const TARGET_RANGE = {
  kcal: { min: 5, max: 500, step: 5 },
  fullSwings: { min: 10, max: 1000, step: 10 },
  activeMinutes: { min: 1, max: 120, step: 1 },
  xp: { min: 100, max: 20000, step: 100 },
};
// Used when fewer than PRESET_MIN_DAYS_PER_PLAYER days of data exist across players; labelled as fallbacks in the API.
const FALLBACK_PRESETS = {
  kcal: { easy: 20, standard: 40, hard: 70 },
  fullSwings: { easy: 30, standard: 60, hard: 120 },
  activeMinutes: { easy: 5, standard: 10, hard: 20 },
  xp: { easy: 500, standard: 1000, hard: 2000 },
};
const PRESET_MIN_DAYS_PER_PLAYER = 3; // a player's days count toward the medians only with at least this many active days
const PRESET_MIN_PLAYERS = 2;         // and at least this many such players must exist
const PRESET_MULTIPLIERS = { easy: 1, standard: 1.5, hard: 2.5 }; // x the median daily total
const BONUS_XP = 75;                  // one-off XP for meeting a day's goal (tunable)
const GOAL_STREAK_BADGES = { goalGetterDays: 3, goalWeekDays: 7 }; // distinct days met

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round1 = (n) => Math.round(n * 10) / 10;

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------
const NAME_MAX = 40;
function validateName(name) {
  const n = String(name === undefined || name === null ? "" : name).trim();
  if (!n || n.length > NAME_MAX || /[\u0000-\u001f\u007f]/.test(n)) return { ok: false, error: `Player name must be 1 to ${NAME_MAX} characters` };
  return { ok: true, value: n };
}

// body: { metric, target }. Returns { ok, value: { metric, target } } or { ok: false, error }.
function validateGoal(body) {
  const b = body && typeof body === "object" ? body : {};
  const metric = String(b.metric === undefined ? "" : b.metric).trim();
  if (!METRICS.includes(metric)) return { ok: false, error: `metric must be one of: ${METRICS.join(", ")}` };
  const target = typeof b.target === "string" && b.target.trim() !== "" ? Number(b.target) : b.target;
  if (!isNum(target)) return { ok: false, error: "target must be a number" };
  const r = TARGET_RANGE[metric];
  if (target < r.min || target > r.max) return { ok: false, error: `target for ${metric} must be between ${r.min} and ${r.max}` };
  return { ok: true, value: { metric, target: metric === "kcal" ? Math.round(target * 10) / 10 : Math.round(target) } };
}

// ---------------------------------------------------------------------------
// Progress
// ---------------------------------------------------------------------------
// Distinct 60 s buckets of fight time that contain at least one recorded swing (hit event). Hits carry
// payload.t (seconds into the fight); a hit without t is skipped (it cannot be placed in a minute).
function activeMinutesOf(hits) {
  const set = new Set();
  for (const h of hits || []) if (isNum(h.t) && h.t >= 0) set.add(Math.floor(h.t / 60));
  return set.size;
}

// sessions: API-shaped finished sessions; hitsBySession: Map id -> [{ tier, speed, t }].
// -> { kcal, fullSwings, activeMinutes, xp, sessions }
function dayTotals(sessions, hitsBySession) {
  let kcal = 0, fullSwings = 0, activeMinutes = 0, xp = 0, kcalKnown = false;
  for (const s of sessions) {
    const hits = (hitsBySession && hitsBySession.get(s.id)) || [];
    const summary = fitness.sessionSummary(s, hits);
    if (isNum(summary.estimatedCalories)) { kcal += summary.estimatedCalories; kcalKnown = true; }
    fullSwings += Math.max(summary.tiers.counts.full || 0, Number(s.fullSwings) || 0);
    activeMinutes += activeMinutesOf(hits);
    xp += Number(s.xp) || 0;
  }
  return { kcal: kcalKnown ? round1(kcal) : 0, fullSwings, activeMinutes, xp: Math.round(xp), sessions: sessions.length };
}

function progressFor(goal, totals) {
  if (!goal) return null;
  const value = totals ? totals[goal.metric] || 0 : 0;
  const percent = goal.target > 0 ? Math.min(100, Math.round((value / goal.target) * 100)) : 0;
  return { value, target: goal.target, percent, met: value >= goal.target };
}

// ---------------------------------------------------------------------------
// Completion (idempotent by construction: a goal is evaluated only while completedAt is null)
// ---------------------------------------------------------------------------
// -> { complete: bool, bonusXp }
function evaluateCompletion(goal, totals) {
  if (!goal) return { complete: false, bonusXp: 0 };
  if (goal.completedAt) return { complete: false, bonusXp: 0, already: true };
  const p = progressFor(goal, totals);
  return p && p.met ? { complete: true, bonusXp: BONUS_XP } : { complete: false, bonusXp: 0 };
}

// ---------------------------------------------------------------------------
// Presets from real data
// ---------------------------------------------------------------------------
function median(values) {
  const v = values.filter(isNum).sort((a, b) => a - b);
  if (!v.length) return null;
  const m = v.length >> 1;
  return v.length % 2 ? v[m] : (v[m - 1] + v[m]) / 2;
}

function roundToStep(value, metric) {
  const r = TARGET_RANGE[metric];
  const stepped = Math.round(value / r.step) * r.step;
  return Math.min(r.max, Math.max(r.min, stepped));
}

// dailyByPlayer: Map playerName -> [{ day, kcal, fullSwings, activeMinutes, xp }] (days with a session).
// -> { source: "data"|"fallback", basis: {...}, presets: { metric: { easy, standard, hard } } }
function suggestPresets(dailyByPlayer) {
  const eligible = [...dailyByPlayer.values()].filter((days) => days.length >= PRESET_MIN_DAYS_PER_PLAYER);
  const presets = {};
  const medians = {};
  if (eligible.length < PRESET_MIN_PLAYERS) {
    for (const m of METRICS) presets[m] = { ...FALLBACK_PRESETS[m] };
    return { source: "fallback", playersUsed: eligible.length, note: `Fewer than ${PRESET_MIN_PLAYERS} players with ${PRESET_MIN_DAYS_PER_PLAYER}+ active days: fixed starting suggestions.`, medians, presets };
  }
  for (const m of METRICS) {
    const all = eligible.flatMap((days) => days.map((d) => d[m]));
    const med = median(all);
    medians[m] = med === null ? null : round1(med);
    if (!med || med <= 0) { presets[m] = { ...FALLBACK_PRESETS[m] }; continue; }
    const easy = roundToStep(med * PRESET_MULTIPLIERS.easy, m);
    const standard = Math.max(easy + TARGET_RANGE[m].step, roundToStep(med * PRESET_MULTIPLIERS.standard, m));
    const hard = Math.max(standard + TARGET_RANGE[m].step, roundToStep(med * PRESET_MULTIPLIERS.hard, m));
    presets[m] = { easy, standard, hard: Math.min(TARGET_RANGE[m].max, hard) };
  }
  return {
    source: "data",
    playersUsed: eligible.length,
    note: `Easy = the median daily total of players with ${PRESET_MIN_DAYS_PER_PLAYER}+ active days, Standard x${PRESET_MULTIPLIERS.standard}, Hard x${PRESET_MULTIPLIERS.hard}; rounded to a sensible step and clamped to the safety caps.`,
    medians,
    presets,
  };
}

// ---------------------------------------------------------------------------
// Goal streak and badges
// ---------------------------------------------------------------------------
// metDays: YYYY-MM-DD strings (days the goal was met). today: YYYY-MM-DD. Consecutive days; no freeze.
// Alive when the last met day is today or yesterday (so it does not read 0 before today's goal is met).
function goalStreak(metDays, today) {
  const nums = [...new Set(metDays)].map(fitness.dayNum).sort((a, b) => a - b);
  let run = 0, longest = 0, prev = null;
  for (const n of nums) {
    run = prev !== null && n === prev + 1 ? run + 1 : 1;
    longest = Math.max(longest, run);
    prev = n;
  }
  const gap = prev === null ? null : fitness.dayNum(today) - prev;
  return { current: gap !== null && gap <= 1 ? run : 0, longest, daysMet: nums.length };
}

module.exports = {
  METRICS, TARGET_RANGE, FALLBACK_PRESETS, PRESET_MIN_DAYS_PER_PLAYER, PRESET_MIN_PLAYERS, PRESET_MULTIPLIERS, BONUS_XP, GOAL_STREAK_BADGES,
  NAME_MAX, validateName, validateGoal,
  activeMinutesOf, dayTotals, progressFor, evaluateCompletion,
  suggestPresets, goalStreak, median, roundToStep,
  dayIn: progression.dayIn, safeZone: progression.safeZone, DEFAULT_TZ: progression.DEFAULT_TZ,
};
