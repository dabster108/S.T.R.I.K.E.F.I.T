// STRIKEFIT Wrapped: one player's week in numbers (Monday to Sunday, Asia/Kathmandu). Pure functions (no database, no clock reads
// except a passed `now`), tested in test/wrapped.test.js; wrappedStore.js loads the rows.
//
// Every figure follows a rule the site already has: wins and the win rate come from outcomes.js (abandoned fights are not decided),
// swing speeds are clamped to the 8 m/s cap (levels.js), the effort fade is effort.fade averaged (effort.js, only with enough swings),
// the personality comes from the Fingerprint axes (effort.js), and the day of a fight is its LOCAL start day (fitness.dayIn).
// The result holds the pseudonym and numbers only: no other personal data exists in it.

const fitness = require("./fitness");
const progression = require("./progression");
const outcomes = require("./outcomes");
const effort = require("./effort");
const { SWING_SPEED_CAP } = require("./levels");
const cfg = require("./config/wrapped");

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const sum = (list, pick) => list.reduce((a, x) => a + (Number(pick(x)) || 0), 0);
const addDays = (day, n) => fitness.dayFromNum(fitness.dayNum(day) + n);
const LEVEL_NUMBER = { Easy: 1, Medium: 2, Hard: 3 };

// ---------------------------------------------------------------------------------------------------------------- the week

/** The Monday of the week that contains `now`, in the given time zone. */
const currentWeekStart = (now, zone) => progression.weekStartOf(fitness.dayIn(now, zone));

/** ?weekStart= -> { ok, value }: null for "not given" (the caller uses the current week), else the Monday of that date's week. */
function parseWeekStart(raw) {
  if (raw === undefined || raw === null || raw === "") return { ok: true, value: null };
  if (typeof raw !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(raw)) return { ok: false, error: "weekStart must be a date like 2026-10-19" };
  const d = new Date(`${raw}T00:00:00Z`);
  if (Number.isNaN(d.getTime()) || d.toISOString().slice(0, 10) !== raw) return { ok: false, error: "weekStart is not a real date" };
  if (raw < cfg.EARLIEST_WEEK) return { ok: false, error: `weekStart is before ${cfg.EARLIEST_WEEK}` };
  return { ok: true, value: progression.weekStartOf(raw) };
}

// ---------------------------------------------------------------------------------------------------------------- the numbers

/** Longest run of victories in play order. A defeat ends a run; an abandoned or timed-out fight is not decided and changes nothing. */
function longestWinStreak(facts) {
  let run = 0;
  let best = 0;
  for (const f of [...facts].sort((a, b) => new Date(a.startedAt) - new Date(b.startedAt) || String(a.id).localeCompare(String(b.id)))) {
    if (f.outcome === "victory") best = Math.max(best, ++run);
    else if (f.outcome === "defeat") run = 0;
  }
  return best;
}

/** The day with the most counted swings: { day, swings }. Ties go to the earlier day. null when there were no swings. */
function bestDay(facts) {
  const byDay = new Map();
  for (const f of facts) byDay.set(f.day, (byDay.get(f.day) || 0) + (Number(f.swings) || 0));
  let best = null;
  for (const [day, swings] of [...byDay].sort((a, b) => (a[0] < b[0] ? -1 : 1))) if (swings > 0 && (!best || swings > best.swings)) best = { day, swings };
  return best;
}

/** The level fought most: { level, levelNumber, fights, damageDealt }. Ties go to the one with more damage dealt, then the lower level. */
function favouriteMonster(facts) {
  const byLevel = new Map();
  for (const f of facts) {
    const cur = byLevel.get(f.level) || { level: f.level, levelNumber: LEVEL_NUMBER[f.level] ?? null, fights: 0, damageDealt: 0 };
    cur.fights++;
    cur.damageDealt += Number(f.damageDealt) || 0;
    byLevel.set(f.level, cur);
  }
  const list = [...byLevel.values()].sort((a, b) => b.fights - a.fights || b.damageDealt - a.damageDealt || (a.levelNumber ?? 9) - (b.levelNumber ?? 9));
  return list.length ? { ...list[0], damageDealt: Math.round(list[0].damageDealt * 10) / 10 } : null;
}

/**
 * The effort personality from the fingerprint of the week's fights (analytics.fingerprintOfRows).
 * Highest axis names it; the top two within ALL_ROUNDER_WITHIN points: The All-Rounder; not enough data: Warming Up.
 */
function personalityFor(fingerprint) {
  const axes = (fingerprint?.axes || []).map((a) => ({ key: a.key, label: a.label, score: a.score }));
  if (!fingerprint || fingerprint.status !== "ok") {
    return { status: "insufficient", key: "warming_up", name: cfg.WARMING_UP, topAxis: null, axes, reason: fingerprint?.reason || "Not enough fights with swing data this week yet." };
  }
  const scored = axes.filter((a) => isNum(a.score)).sort((a, b) => b.score - a.score);
  const [top, second] = scored;
  if (second && top.score - second.score <= cfg.ALL_ROUNDER_WITHIN) {
    return { status: "ok", key: "all_rounder", name: cfg.ALL_ROUNDER, topAxis: null, axes, reason: `${top.label} (${top.score}) and ${second.label} (${second.score}) are within ${cfg.ALL_ROUNDER_WITHIN} points of each other.` };
  }
  return { status: "ok", key: top.key, name: cfg.PERSONALITY_BY_AXIS[top.key], topAxis: top.key, axes, reason: `${top.label} is the highest of your axes (${top.score}).` };
}

// ---------------------------------------------------------------------------------------------------------------- the whole answer

/**
 * facts: ALL the player's finished sessions (engage.sessionFacts). weekStart: a Monday. fades: effort.fade results for the week's
 * sessions; fingerprint: analytics.fingerprintOfRows for the week's sessions (or null when the week is empty).
 */
function buildWrapped({ playerName, facts, weekStart, fades = [], fingerprint = null, now = new Date(), zone }) {
  const weekEnd = addDays(weekStart, 6);
  const thisWeek = currentWeekStart(now, zone);
  const week = facts.filter((f) => f.day >= weekStart && f.day <= weekEnd);

  const counts = outcomes.countOutcomes(week.map((f) => f.outcome));
  const tiered = sum(week, (f) => f.tieredSwings);
  const full = sum(week, (f) => f.fullSwings);
  const peaks = week.map((f) => f.peakSpeed).filter(isNum);
  const average = fades.length ? effort.averageFade(fades) : null;

  // the weeks the picker offers: the newest weeks that have fights
  const perWeek = new Map();
  for (const f of facts) {
    const w = progression.weekStartOf(f.day);
    perWeek.set(w, (perWeek.get(w) || 0) + 1);
  }
  const weeks = [...perWeek].sort((a, b) => (a[0] < b[0] ? 1 : -1)).slice(0, cfg.WEEKS_LISTED).map(([w, fights]) => ({ weekStart: w, weekEnd: addDays(w, 6), fights }));

  return {
    ok: true,
    playerName,
    timeZone: zone,
    weekStart,
    weekEnd,
    isCurrentWeek: weekStart === thisWeek,
    previousWeek: addDays(weekStart, -7),
    nextWeek: weekStart < thisWeek ? addDays(weekStart, 7) : null,
    weeks,
    fights: week.length,
    decidedFights: counts.decided,
    wins: counts.victories,
    defeats: counts.defeats,
    abandoned: counts.abandoned,
    winRate: counts.winRate,
    swings: sum(week, (f) => f.swings),
    fastestSwing: peaks.length ? Math.round(Math.min(Math.max(...peaks), SWING_SPEED_CAP) * 100) / 100 : null,
    speedCap: SWING_SPEED_CAP,
    fullPowerSwings: full,
    fullPowerShare: tiered > 0 ? Math.round(Math.min(1, full / tiered) * 1000) / 1000 : null,
    activeMinutes: sum(week, (f) => f.activeMinutes),
    averageFade: average && average.status === "ok" ? { fadePct: average.fadePct, label: average.label, labelText: average.labelText, sessionsUsed: average.sessionsUsed } : null,
    bestDay: bestDay(week),
    favouriteMonster: favouriteMonster(week),
    xpGained: Math.round(sum(week, (f) => f.xp)),
    longestWinStreak: longestWinStreak(week),
    personality: week.length ? personalityFor(fingerprint) : personalityFor(null),
    rules: {
      winRate: outcomes.RULE,
      fastestSwing: `Clamped to the game's ${SWING_SPEED_CAP} m/s cap.`,
      week: "Monday to Sunday, by the day each fight started (Asia/Kathmandu).",
      fade: "Average effort fade of the week's fights that had enough swings.",
    },
  };
}

module.exports = { currentWeekStart, parseWeekStart, longestWinStreak, bestDay, favouriteMonster, personalityFor, buildWrapped };
