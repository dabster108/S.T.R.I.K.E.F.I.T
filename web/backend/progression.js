// Streaks, weekly goal, level/titles and badges. Pure functions only (no database access), so
// test/progression.test.js can run them against fixed fixtures. analytics.js loads the rows.
//
// Nothing here is invented: every badge is computed from stored sessions/events. A badge whose
// data the game doesn't send yet (combo before the Part 2C game update) reports `tracking: true`
// instead of pretending to be locked or unlocked.

const fitness = require("./fitness");

// --- Tunables -------------------------------------------------------------------------------
const DEFAULT_TZ = "Asia/Kathmandu"; // day and week boundaries when the caller sends no ?tz=
const WEEKLY_GOAL_TARGET = 3; // sessions per week
const FREEZE_WINDOW_DAYS = 7;

// XP curve: total XP needed to REACH level L is XP_BASE * (L - 1)^2.
//   L2 = 250, L3 = 1000, L4 = 2250, L5 = 4000, L8 = 12250, L10 = 20250.
// Sessions in the live data store up to ~6200 XP each, so a few fights reach Brawler and a
// long habit is needed for Warlord. Retune XP_BASE after playtesting.
const XP_BASE = 250;

// Highest `minLevel` that is <= the player's level wins.
const TITLES = [
  { minLevel: 1, title: "Rookie" },
  { minLevel: 3, title: "Brawler" },
  { minLevel: 5, title: "Gladiator" },
  { minLevel: 8, title: "Warlord" },
  { minLevel: 12, title: "Legend" },
];

const COMBO_TRACKING_NOTE = "Tracking starts after the game update: combo is not recorded in older sessions.";

const { dayIn, dayNum, dayFromNum, safeZone } = fitness;
const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const weekStartOf = (day) => dayFromNum(dayNum(day) - ((new Date(`${day}T00:00:00Z`).getUTCDay() + 6) % 7)); // Monday

// ---------------------------------------------------------------------------
// Streak with freeze
// ---------------------------------------------------------------------------
// days: YYYY-MM-DD strings (one per session, in the player's time zone). today: YYYY-MM-DD.
//
// RULE. The streak counts calendar days with at least one session. A day with no session is a
// "missed day". One missed day, sitting between two played days, is forgiven (a "freeze"): the
// streak carries on and the frozen day itself adds nothing to the count. A freeze can only be
// used if no other freeze fell within the previous 7 days (at most one freeze day in any 7
// consecutive days, counted across the player's whole history). Two or more missed days in a
// row always break the streak.
//
// "Alive today": the streak is current if the last session was today or yesterday. If it was two
// days ago (yesterday was missed) it is still current when a freeze is available for yesterday;
// that is reported as freezePending (it becomes "freeze used" once they play today).
function streakWithFreeze(days, today) {
  const nums = [...new Set(days)].map(dayNum).sort((a, b) => a - b);
  const todayN = dayNum(today);
  const freezeDays = [];
  const canFreeze = (missed) => !freezeDays.length || missed - freezeDays[freezeDays.length - 1] >= FREEZE_WINDOW_DAYS;

  let run = 0, longest = 0, prev = null, runFreezes = [];
  const reached = new Map(); // streak length -> first day number it was reached
  for (const n of nums) {
    if (prev !== null && n === prev + 1) run++;
    else if (prev !== null && n === prev + 2 && canFreeze(prev + 1)) {
      freezeDays.push(prev + 1);
      runFreezes.push(prev + 1);
      run++;
    } else {
      run = 1;
      runFreezes = [];
    }
    longest = Math.max(longest, run);
    if (!reached.has(run)) reached.set(run, n);
    prev = n;
  }

  const gap = prev === null ? null : todayN - prev;
  let current = 0;
  let freezePending = false;
  if (prev !== null) {
    if (gap <= 1) current = run;
    else if (gap === 2 && canFreeze(prev + 1)) {
      current = run;
      freezePending = true;
    }
  }
  return {
    current,
    longest,
    freezeUsed: current > 0 && runFreezes.length > 0,
    freezeDays: (current > 0 ? runFreezes : []).map(dayFromNum),
    freezePending,
    reachedOn: (length) => (reached.has(length) ? dayFromNum(reached.get(length)) : null),
  };
}

// ---------------------------------------------------------------------------
// Weekly goal
// ---------------------------------------------------------------------------
// The week runs Monday to Sunday in the player's time zone. Counts every recorded session,
// same definition as the engagement endpoint.
function weeklyGoal(days, today, target = WEEKLY_GOAL_TARGET) {
  const weekStart = weekStartOf(today);
  const sessions = days.filter((d) => weekStartOf(d) === weekStart).length;
  return {
    target,
    weekStart,
    weekEnd: dayFromNum(dayNum(weekStart) + 6),
    sessionsThisWeek: sessions,
    met: sessions >= target,
    definition: "Sessions from Monday to Sunday (player's time zone). Every recorded session counts, as on the engagement page.",
  };
}

// ---------------------------------------------------------------------------
// Level and title
// ---------------------------------------------------------------------------
const xpToReach = (level) => XP_BASE * (level - 1) ** 2;
const titleFor = (level) => [...TITLES].reverse().find((t) => level >= t.minLevel).title;

function levelFromXp(totalXp) {
  const xp = Math.max(0, Number(totalXp) || 0);
  const level = Math.floor(Math.sqrt(xp / XP_BASE)) + 1;
  const floor = xpToReach(level);
  const next = xpToReach(level + 1);
  return {
    level,
    title: titleFor(level),
    totalXp: Math.round(xp),
    xpIntoLevel: Math.round(xp - floor),
    xpForLevel: next - floor,
    xpToNext: Math.max(0, Math.ceil(next - xp)),
    nextLevel: level + 1,
    nextTitle: titleFor(level + 1),
    curve: `XP to reach level L = ${XP_BASE} x (L-1)^2`,
  };
}

// ---------------------------------------------------------------------------
// Badges
// ---------------------------------------------------------------------------
// ctx.sessions: chronological, each { id, startedAt, endedAt, level, outcome, hitCount, fullSwings,
//   playerHpRemaining, liveState }. ctx.summaryById: id -> fitness.sessionSummary. ctx.comboById /
//   ctx.hurtById / ctx.maxHpById: id -> number (only present when the events carried it). ctx.eventSessions: Set of
//   session ids that have any stored events. ctx.days: session id -> local day.
const when = (s) => s.endedAt || s.startedAt;
const hit = (s, extra = {}) => ({ unlocked: true, unlockedAt: when(s), sessionId: s.id, ...extra });
const locked = (extra = {}) => ({ unlocked: false, unlockedAt: null, sessionId: null, ...extra });

// Damage the player took in a session, or null when it can't be told.
function damageTaken(s, ctx) {
  // Max HP: the live state saved with the session, else the largest playerMaxHp any of its events reported.
  const max = (s.liveState && s.liveState.playerMaxHp) ?? (ctx.maxHpById && ctx.maxHpById.get(s.id));
  if (isNum(max) && isNum(s.playerHpRemaining)) return Math.max(0, max - s.playerHpRemaining);
  if (ctx.eventSessions.has(s.id) && ctx.hurtById.has(s.id)) return ctx.hurtById.get(s.id);
  return null;
}

// Walks sessions in order adding `valueOf`, returns the one where the running total reaches target.
function cumulative(ctx, valueOf, target) {
  let total = 0;
  let at = null;
  for (const s of ctx.sessions) {
    total += valueOf(s) || 0;
    if (!at && total >= target) at = s;
  }
  return { total, at };
}

const BADGES = [
  {
    id: "first_blood",
    name: "First Blood",
    description: "Land your first hit.",
    check(ctx) {
      const s = ctx.sessions.find((x) => x.hitCount > 0 || (ctx.summaryById.get(x.id)?.tiers.total || 0) > 0);
      return s ? hit(s) : locked();
    },
  },
  {
    id: "full_power_50",
    name: "Full Power x50",
    description: "Throw 50 full-power swings in total.",
    target: 50,
    check(ctx) {
      const { total, at } = cumulative(ctx, (s) => Math.max(ctx.summaryById.get(s.id)?.tiers.counts.full || 0, s.fullSwings || 0), this.target);
      return at ? hit(at, { current: total }) : locked({ current: total });
    },
  },
  {
    id: "combo_10",
    name: "10-hit combo",
    description: "Chain 10 hits in a row.",
    target: 10,
    check(ctx) {
      if (!ctx.comboById.size) return locked({ tracking: true, trackingNote: COMBO_TRACKING_NOTE, current: 0 });
      const best = Math.max(...ctx.comboById.values());
      const s = ctx.sessions.find((x) => (ctx.comboById.get(x.id) || 0) >= this.target);
      return s ? hit(s, { current: best }) : locked({ current: best });
    },
  },
  {
    id: "flawless_medium",
    name: "Untouched",
    description: "Beat Medium without taking any damage.",
    check(ctx) {
      const s = ctx.sessions.find((x) => x.level === "Medium" && x.outcome === "victory" && damageTaken(x, ctx) === 0);
      return s ? hit(s) : locked();
    },
  },
  {
    id: "burn_100",
    name: "Burned 100 kcal",
    description: "Burn 100 kcal in total (estimated).",
    target: 100,
    check(ctx) {
      const { total, at } = cumulative(ctx, (s) => ctx.summaryById.get(s.id)?.estimatedCalories, this.target);
      return at ? hit(at, { current: Math.round(total * 10) / 10 }) : locked({ current: Math.round(total * 10) / 10 });
    },
  },
  {
    id: "streak_3",
    name: "3-day streak",
    description: "Play on 3 days in a row (one missed day is forgiven, once a week).",
    target: 3,
    check(ctx) {
      const day = ctx.streak.reachedOn(3);
      const s = day && ctx.sessions.find((x) => ctx.days.get(x.id) === day);
      return s ? hit(s, { current: ctx.streak.longest }) : locked({ current: ctx.streak.longest });
    },
  },
  {
    id: "weekly_goal",
    name: "Weekly goal met",
    description: "Hit your weekly session goal.",
    check(ctx) {
      const target = ctx.weeklyTarget;
      const byWeek = new Map();
      for (const s of ctx.sessions) {
        const wk = weekStartOf(ctx.days.get(s.id));
        if (!byWeek.has(wk)) byWeek.set(wk, []);
        byWeek.get(wk).push(s);
      }
      for (const list of byWeek.values()) if (list.length >= target) return hit(list[target - 1], { current: ctx.thisWeekSessions, target });
      return locked({ current: ctx.thisWeekSessions, target });
    },
  },
];

// -> [{ id, name, description, unlocked, unlockedAt, sessionId, progress: {current,target}|null, tracking, trackingNote }]
function evaluateBadges(ctx) {
  return BADGES.map((b) => {
    const r = b.check.call(b, ctx);
    const target = r.target ?? b.target;
    return {
      id: b.id,
      name: b.name,
      description: b.description,
      unlocked: r.unlocked,
      unlockedAt: r.unlockedAt,
      sessionId: r.sessionId,
      progress: target && isNum(r.current) ? { current: r.current, target } : null,
      tracking: Boolean(r.tracking),
      trackingNote: r.trackingNote || null,
    };
  });
}

// ---------------------------------------------------------------------------
// Personal bests held by one session
// ---------------------------------------------------------------------------
const PB_LABELS = {
  highestSwingVelocity: "Fastest swing",
  highestFullSwingPercent: "Most full-power swings",
  highestEstimatedCalories: "Most calories (estimate)",
  longestSession: "Longest session",
  highestScore: "Highest score",
};

// summaries: fitness.sessionSummary for each of the player's finished sessions. A record only means
// something with something to beat, so a player's first session holds none.
function personalBestsSet(sessionId, summaries) {
  if (summaries.length < 2) return [];
  const pbs = fitness.personalBests(summaries);
  return Object.entries(pbs)
    .filter(([, v]) => v && v.sessionId === sessionId)
    .map(([key, v]) => ({ key, label: PB_LABELS[key] || key, value: v.value, unit: v.unit }));
}

module.exports = {
  DEFAULT_TZ,
  WEEKLY_GOAL_TARGET,
  FREEZE_WINDOW_DAYS,
  XP_BASE,
  TITLES,
  BADGES,
  safeZone,
  dayIn,
  weekStartOf,
  streakWithFreeze,
  weeklyGoal,
  levelFromXp,
  xpToReach,
  evaluateBadges,
  personalBestsSet,
  damageTaken,
};
