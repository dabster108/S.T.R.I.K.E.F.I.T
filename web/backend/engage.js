// Engagement rules: mastery stars, the activity calendar, the weekly recap, the best-run pick and the live reaction
// limits. Pure functions only (no database, no clock reads except a passed `now`), so test/engage.test.js runs them on
// fixed fixtures. engageStore.js loads the rows.
//
// Nothing here is invented: every figure is computed from stored sessions and their events. kcal is the project's
// ESTIMATE (fitness.js), never a measurement.

const fitness = require("./fitness");
const progression = require("./progression");

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round1 = (n) => Math.round(n * 10) / 10;
const LEVELS = ["Easy", "Medium", "Hard"];

// ---------------------------------------------------------------------------
// Session facts: one row per finished session, from the aggregate query in engageStore.js
// ---------------------------------------------------------------------------
// row: session API fields + liveState + aggregates { tap, light, medium, full, minutes, peak, combo, maxhp, hurt, evented }
function sessionFacts(row, zone) {
  const counts = { tap: row.tap || 0, light: row.light || 0, medium: row.medium || 0, full: row.full || 0 };
  const tiered = counts.tap + counts.light + counts.medium + counts.full;
  const avgTierWeight = tiered ? (counts.tap * 1 + counts.light * 2 + counts.medium * 3 + counts.full * 4) / tiered : null;
  const effort = fitness.estimateEffort(row.durationSec, avgTierWeight, tiered > 0 || Number(row.hitCount) > 0);
  // Damage taken: the same evidence progression.damageTaken uses (max HP from the saved live state or any event).
  const maxHp = (row.liveState && isNum(row.liveState.playerMaxHp) ? row.liveState.playerMaxHp : null) ?? (isNum(row.maxhp) ? row.maxhp : null);
  let damagePercent = null;
  if (isNum(maxHp) && maxHp > 0 && isNum(Number(row.playerHpRemaining))) {
    damagePercent = round1(Math.max(0, Math.min(100, ((maxHp - Number(row.playerHpRemaining)) / maxHp) * 100)));
  } else if (row.evented && row.hurt === 0) damagePercent = 0;
  return {
    id: row.id,
    playerName: row.playerName,
    level: row.level,
    outcome: row.outcome,
    score: Number(row.score) || 0,
    startedAt: row.startedAt,
    endedAt: row.endedAt,
    durationSec: Number(row.durationSec) || 0,
    // Added for ranks, records, milestones and Wrapped: the counted swings (the same "counted" the player page shows), XP and damage.
    swings: Number(row.hitCount) || 0,
    xp: Number(row.xp) || 0,
    damageDealt: Number(row.damageDealt) || 0,
    day: fitness.dayIn(new Date(row.startedAt), zone),
    kcal: effort.estimatedCalories,
    walkingMinutes: effort.estimatedWalkingMinutes,
    fullSwings: Math.max(counts.full, Number(row.fullSwings) || 0),
    tieredSwings: tiered,
    fullRatio: tiered >= MASTERY_MIN_TIERED ? round1((counts.full / tiered) * 1000) / 1000 : null,
    activeMinutes: row.minutes || 0,
    peakSpeed: isNum(row.peak) ? Math.round(row.peak * 100) / 100 : null,
    bestCombo: isNum(row.combo) ? row.combo : null,
    damagePercent,
  };
}

// Per player per local day totals (the challenge medians use these).
function dailyRows(facts) {
  const map = new Map();
  for (const f of facts) {
    const k = `${f.playerName}\u0000${f.day}`;
    const cur = map.get(k) || { playerName: f.playerName, day: f.day, fullSwings: 0, kcal: null, sessions: 0, activeMinutes: 0, bestCombo: null };
    cur.fullSwings += f.fullSwings;
    if (isNum(f.kcal)) cur.kcal = round1((cur.kcal || 0) + f.kcal);
    cur.sessions += 1;
    cur.activeMinutes += f.activeMinutes;
    if (isNum(f.bestCombo)) cur.bestCombo = Math.max(cur.bestCombo || 0, f.bestCombo);
    map.set(k, cur);
  }
  return [...map.values()];
}

// ---------------------------------------------------------------------------
// Mastery stars
// ---------------------------------------------------------------------------
// tuning in progress, set from playtest data.
// Star 1: win the level. Star 2: win it losing at most maxDamagePercent of your health. Star 3: win it within
// clearWithinSec seconds OR with at least fullPowerRatio of the fight's tiered swings at full power.
// Damage and time limits are the "silver" result-panel limits of each scene (project_progress.md, 2026-10-07: Level 1
// 50 s / 40%, Level 2 55 s / 55%, Level 3 80 s / 50%), themselves development values from few victories (n = 8, 4, 2).
// The full-power ratios have no source yet; they are starting values.
const MASTERY = {
  Easy: { maxDamagePercent: 40, clearWithinSec: 50, fullPowerRatio: 0.35 },
  Medium: { maxDamagePercent: 55, clearWithinSec: 55, fullPowerRatio: 0.3 },
  Hard: { maxDamagePercent: 50, clearWithinSec: 80, fullPowerRatio: 0.25 },
};
const MASTERY_MIN_TIERED = 10; // a full-power ratio needs at least this many tiered swings in the fight
const MASTERY_NOTE = "Thresholds: tuning in progress, set from playtest data.";

// facts: one player's finished sessions (sessionFacts). -> one entry per level.
function mastery(facts) {
  return LEVELS.map((level) => {
    const cfg = MASTERY[level];
    const played = facts.filter((f) => f.level === level);
    const wins = played.filter((f) => f.outcome === "victory");
    const measuredWins = wins.filter((f) => isNum(f.damagePercent));
    const lowest = measuredWins.length ? Math.min(...measuredWins.map((f) => f.damagePercent)) : null;
    const timedWins = wins.filter((f) => f.durationSec > 0);
    const fastest = timedWins.length ? Math.min(...timedWins.map((f) => f.durationSec)) : null;
    const ratioWins = wins.filter((f) => isNum(f.fullRatio));
    const bestRatio = ratioWins.length ? Math.max(...ratioWins.map((f) => f.fullRatio)) : null;
    const star1 = wins.length > 0;
    const star2 = measuredWins.some((f) => f.damagePercent <= cfg.maxDamagePercent);
    const star3 = wins.some((f) => (f.durationSec > 0 && f.durationSec <= cfg.clearWithinSec) || (isNum(f.fullRatio) && f.fullRatio >= cfg.fullPowerRatio));
    const stars = [
      { id: "cleared", label: "Cleared", earned: star1 },
      { id: "low_damage", label: `Cleared losing at most ${cfg.maxDamagePercent}% health`, earned: star2, measurable: wins.length === 0 || measuredWins.length > 0 },
      { id: "fast_or_power", label: `Cleared within ${cfg.clearWithinSec} s or with ${Math.round(cfg.fullPowerRatio * 100)}% full-power swings`, earned: star3 },
    ];
    return {
      level,
      played: played.length,
      wins: wins.length,
      starsEarned: stars.filter((s) => s.earned).length,
      stars,
      best: {
        score: played.length ? Math.max(...played.map((f) => f.score)) : null,
        lowestDamagePercent: lowest,
        fastestClearSec: fastest === null ? null : round1(fastest),
        bestFullPowerRatio: bestRatio,
      },
      thresholds: cfg,
    };
  });
}

// ---------------------------------------------------------------------------
// Activity calendar (GitHub style): weeks x 7 days, Monday first, ending with today's week
// ---------------------------------------------------------------------------
const CALENDAR_MAX_WEEKS = 53;
const clampWeeks = (w) => Math.min(CALENDAR_MAX_WEEKS, Math.max(1, Math.trunc(Number(w)) || 26));

// byDay: Map day -> { sessions, kcal|null, players? }. -> { from, to, weeks, days: [...] } (days after today omitted).
function calendarGrid(byDay, today, weeks) {
  const w = clampWeeks(weeks);
  const thisWeek = progression.weekStartOf(today);
  const fromN = fitness.dayNum(thisWeek) - (w - 1) * 7;
  const todayN = fitness.dayNum(today);
  const days = [];
  let sessions = 0;
  let kcal = 0;
  let kcalKnown = false;
  let activeDays = 0;
  for (let n = fromN; n <= todayN; n++) {
    const day = fitness.dayFromNum(n);
    const d = byDay.get(day);
    const cell = { date: day, sessions: d ? d.sessions : 0, kcal: d && isNum(d.kcal) ? round1(d.kcal) : null };
    if (d && d.players !== undefined) cell.players = d.players;
    if (cell.sessions) activeDays++;
    sessions += cell.sessions;
    if (isNum(cell.kcal)) { kcal += cell.kcal; kcalKnown = true; }
    days.push(cell);
  }
  return {
    from: fitness.dayFromNum(fromN),
    to: today,
    weeks: w,
    days,
    totals: { sessions, activeDays, estimatedCalories: kcalKnown ? round1(kcal) : null },
    max: { sessions: Math.max(0, ...days.map((d) => d.sessions)), kcal: Math.max(0, ...days.map((d) => d.kcal || 0)) },
  };
}

// facts -> Map day -> { sessions, kcal, players }
function byDayOf(facts, { countPlayers = false } = {}) {
  const map = new Map();
  for (const f of facts) {
    const cur = map.get(f.day) || { sessions: 0, kcal: null, names: new Set() };
    cur.sessions++;
    if (isNum(f.kcal)) cur.kcal = (cur.kcal || 0) + f.kcal;
    cur.names.add(f.playerName);
    map.set(f.day, cur);
  }
  const out = new Map();
  for (const [day, v] of map) out.set(day, { sessions: v.sessions, kcal: v.kcal, ...(countPlayers ? { players: v.names.size } : {}) });
  return out;
}

// ---------------------------------------------------------------------------
// "Your week" (Monday to Sunday, local)
// ---------------------------------------------------------------------------
function weekSummary(facts, weekStart) {
  const weekEnd = fitness.dayFromNum(fitness.dayNum(weekStart) + 6);
  const list = facts.filter((f) => f.day >= weekStart && f.day <= weekEnd);
  const kcalList = list.filter((f) => isNum(f.kcal));
  const speeds = list.map((f) => f.peakSpeed).filter(isNum);
  const cleared = [...new Set(list.filter((f) => f.outcome === "victory").map((f) => f.level))].sort((a, b) => LEVELS.indexOf(a) - LEVELS.indexOf(b));
  return {
    weekStart,
    weekEnd,
    sessions: list.length,
    activeDays: new Set(list.map((f) => f.day)).size,
    activeMinutes: list.reduce((a, f) => a + f.activeMinutes, 0),
    fightSeconds: Math.round(list.reduce((a, f) => a + f.durationSec, 0)),
    estimatedCalories: kcalList.length ? round1(kcalList.reduce((a, f) => a + f.kcal, 0)) : null,
    estimatedWalkingMinutes: kcalList.length ? round1(kcalList.reduce((a, f) => a + (f.walkingMinutes || 0), 0)) : null,
    sessionsEstimated: kcalList.length,
    fullSwings: list.reduce((a, f) => a + f.fullSwings, 0),
    bestSwingSpeed: speeds.length ? Math.max(...speeds) : null,
    levelsCleared: cleared,
    wins: list.filter((f) => f.outcome === "victory").length,
  };
}

// "vs previous week": only when that week has at least one session.
function weekCompare(current, previous) {
  if (!previous || !previous.sessions) return null;
  const diff = (k) => (isNum(current[k]) && isNum(previous[k]) ? round1(current[k] - previous[k]) : null);
  return {
    sessions: diff("sessions"),
    activeMinutes: diff("activeMinutes"),
    estimatedCalories: diff("estimatedCalories"),
    fullSwings: diff("fullSwings"),
    bestSwingSpeed: diff("bestSwingSpeed"),
  };
}

// ---------------------------------------------------------------------------
// Best run on a level (the replay ghost)
// ---------------------------------------------------------------------------
// sessions: one player's finished sessions on one level. before: optional { id, startedAt } -> only runs that started
// earlier than it (and not it). Only decided fights with a score count (an abandoned or 0-point run is no "best run").
// Highest score wins; ties go to the shorter fight, then the earlier one.
const DECIDED = new Set(["victory", "defeat", "timeout"]);
function pickBest(sessions, before = null) {
  const pool = sessions.filter((s) => {
    if (s.status === "live" || !DECIDED.has(s.outcome) || !(Number(s.score) > 0)) return false;
    if (!before) return true;
    return s.id !== before.id && new Date(s.startedAt) < new Date(before.startedAt);
  });
  if (!pool.length) return null;
  return [...pool].sort((a, b) => (Number(b.score) || 0) - (Number(a.score) || 0) || (Number(a.durationSec) || 1e9) - (Number(b.durationSec) || 1e9) || new Date(a.startedAt) - new Date(b.startedAt))[0];
}

// ---------------------------------------------------------------------------
// Live reactions: kinds and rate limits (nothing is stored)
// ---------------------------------------------------------------------------
const REACTION_KINDS = ["fire", "power", "respect"];
const REACTION_LIMITS = { perIp: { capacity: 5, refillPerSec: 0.5 }, global: { capacity: 10, refillPerSec: 10 } }; // 1 per 2 s burst 5; 10 per s

// Token bucket. take(key) -> true when allowed. Buckets idle for a minute are dropped.
function createBucket({ capacity, refillPerSec, now = Date.now }) {
  const buckets = new Map(); // key -> { tokens, at }
  const sweep = setInterval(() => {
    const t = now();
    for (const [k, b] of buckets) if (t - b.at > 60_000) buckets.delete(k);
  }, 60_000);
  sweep.unref();
  return {
    take(key = "*") {
      const t = now();
      const b = buckets.get(key) || { tokens: capacity, at: t };
      b.tokens = Math.min(capacity, b.tokens + ((t - b.at) / 1000) * refillPerSec);
      b.at = t;
      buckets.set(key, b);
      if (b.tokens < 1) return false;
      b.tokens -= 1;
      return true;
    },
  };
}

function validateReaction(body) {
  const b = body && typeof body === "object" && !Array.isArray(body) ? body : {};
  const sessionId = typeof b.sessionId === "string" ? b.sessionId.trim() : "";
  if (!sessionId || sessionId.length > 100 || /[\u0000-\u001f\u007f]/.test(sessionId)) return { ok: false, error: "sessionId is required" };
  const kind = typeof b.kind === "string" ? b.kind.trim().toLowerCase() : "";
  if (!REACTION_KINDS.includes(kind)) return { ok: false, error: `kind must be one of: ${REACTION_KINDS.join(", ")}` };
  return { ok: true, value: { sessionId, kind } };
}

const reactionsEnabled = (env = process.env) => !/^(0|false|off|no)$/i.test(String(env.REACTIONS_ENABLED ?? "true").trim());

module.exports = {
  LEVELS, MASTERY, MASTERY_MIN_TIERED, MASTERY_NOTE, CALENDAR_MAX_WEEKS,
  sessionFacts, dailyRows, mastery, clampWeeks, calendarGrid, byDayOf, weekSummary, weekCompare, pickBest,
  REACTION_KINDS, REACTION_LIMITS, createBucket, validateReaction, reactionsEnabled,
};
