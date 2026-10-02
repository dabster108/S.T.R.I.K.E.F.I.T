// Fitness-outcome calculations built from data the game already records: session rows
// and `hit` events (payload.tier, payload.swingSpeed, payload.t). Everything in here is
// pure (no database access) so it can be checked against real rows from the console.
//
// HONESTY NOTE: swing counts, tier shares, speeds, scores and dates are measured. Calories
// and the walking / jogging equivalents are ESTIMATES built from the assumptions below,
// not measurements: the headset reports no heart rate or oxygen use. Every API field that
// carries an estimate sits next to `estimate: { ... }` metadata saying so.

const TIERS = ["tap", "light", "medium", "full"];

// Weight of one swing by tier, used as a simple "how hard was this swing" score (1 to 4).
const TIER_WEIGHT = { tap: 1, light: 2, medium: 3, full: 4 };

// --- Tunable constants for the calorie estimate and workout equivalents ------------------
// Energy cost uses the standard MET method: kcal = MET x body mass (kg) x hours.
const ASSUMED_BODY_MASS_KG = 70; // ASSUMPTION: no body weight is collected; a round adult figure.
// ASSUMPTION (not taken from a source): how demanding the game is, as a MET range. A session of
// all-tap swings is scored at MET_MIN and a session of all-full-power swings at MET_MAX, linearly
// in between. These two numbers are the least well supported part of the estimate and are meant
// to be replaced with a cited value for active VR boxing / sword games before the thesis cites them.
const MET_MIN = 3.0;
const MET_MAX = 6.0;
// Sessions that landed swings but have no recorded tiers (older builds) are priced at MET_MIN.
const MET_NO_SWING_DATA = MET_MIN;

// Reference activities, from the 2011 Compendium of Physical Activities
// (Ainsworth BE et al., Med Sci Sports Exerc 2011;43(8):1575-1581; compendium list at
// pacompendium.com). Check the code numbers against the Compendium before citing them.
const MET_BRISK_WALKING = 4.3; // Compendium 17190, "walking, 3.5 mph, level, firm surface, brisk pace"
const MET_JOGGING = 7.0; // Compendium 12020, "running/jogging, general"

const SWING_FLOOR_FOR_RECORDS = 10; // a "highest full-swing %" needs at least this many tiered swings
const EXERTION_BUCKETS = 5;
const DIRECTION_MIN_SWINGS = 10; // total swings before a rose/level/dropped label is shown
const DIRECTION_MIN_PER_HALF = 3;
const DIRECTION_DELTA = 0.25; // difference in average tier weight (scale 1-4) that counts as a change

const round = (v, d = 2) => (v === null || v === undefined || !Number.isFinite(v) ? null : Math.round(v * 10 ** d) / 10 ** d);
const isNum = (v) => typeof v === "number" && Number.isFinite(v);

const ESTIMATE_NOTE =
  "Estimate, not a measurement: MET method with an assumed 70 kg body mass and an assumed MET range scaled by swing intensity. " +
  "Walking and jogging figures are a rough equivalent at reference MET values, not specific to this movement.";

// ---------------------------------------------------------------------------
// Swing tiers
// ---------------------------------------------------------------------------

// tiers: array of tier strings, one per swing.
function tierBreakdown(tiers) {
  const counts = Object.fromEntries(TIERS.map((t) => [t, 0]));
  for (const t of tiers) if (t in counts) counts[t]++;
  const total = TIERS.reduce((a, t) => a + counts[t], 0);
  const percent = Object.fromEntries(TIERS.map((t) => [t, total ? round((counts[t] / total) * 100, 1) : null]));
  const weighted = TIERS.reduce((a, t) => a + counts[t] * TIER_WEIGHT[t], 0);
  return { counts, percent, total, avgTierWeight: total ? round(weighted / total, 3) : null };
}

// hits: [{ tier, t }]. Splits the fight into equal time slices and averages tier weight in each.
function exertionTrend(hits, durationSec, buckets = EXERTION_BUCKETS) {
  const usable = hits.filter((h) => h.tier in TIER_WEIGHT && isNum(h.t) && h.t >= 0);
  const maxT = usable.reduce((m, h) => Math.max(m, h.t), 0);
  const length = durationSec > 0 ? Math.max(durationSec, maxT) : maxT;
  if (!usable.length || !(length > 0)) {
    return { bucketCount: buckets, swingsUsed: 0, buckets: [], direction: null, directionNote: "No timed swings recorded for this session." };
  }
  const slots = Array.from({ length: buckets }, (_, i) => ({
    index: i,
    fromPct: round((i / buckets) * 100, 1),
    toPct: round(((i + 1) / buckets) * 100, 1),
    fromSec: round((i / buckets) * length, 1),
    toSec: round(((i + 1) / buckets) * length, 1),
    swings: 0,
    sum: 0,
  }));
  let firstSum = 0, firstN = 0, secondSum = 0, secondN = 0;
  for (const h of usable) {
    const idx = Math.min(buckets - 1, Math.floor((h.t / length) * buckets));
    slots[idx].swings++;
    slots[idx].sum += TIER_WEIGHT[h.tier];
    if (h.t / length < 0.5) { firstSum += TIER_WEIGHT[h.tier]; firstN++; } else { secondSum += TIER_WEIGHT[h.tier]; secondN++; }
  }
  let direction = null;
  let directionNote;
  if (usable.length < DIRECTION_MIN_SWINGS || firstN < DIRECTION_MIN_PER_HALF || secondN < DIRECTION_MIN_PER_HALF) {
    directionNote = `Too few swings to call a trend (needs ${DIRECTION_MIN_SWINGS}+ with ${DIRECTION_MIN_PER_HALF}+ in each half).`;
  } else {
    const diff = secondSum / secondN - firstSum / firstN;
    direction = diff >= DIRECTION_DELTA ? "rose" : diff <= -DIRECTION_DELTA ? "dropped" : "level";
    directionNote = `Average swing intensity in the second half of the fight minus the first half: ${round(diff, 2)} (on a 1-4 scale; changes under ${DIRECTION_DELTA} count as level).`;
  }
  return {
    bucketCount: buckets,
    swingsUsed: usable.length,
    buckets: slots.map(({ sum, ...b }) => ({ ...b, avgTierWeight: b.swings ? round(sum / b.swings, 3) : null })),
    direction,
    directionNote,
  };
}

// Average the per-session bucket curves (each session is already scaled to its own length).
function averageExertionTrend(trends) {
  const used = trends.filter((t) => t.swingsUsed >= DIRECTION_MIN_SWINGS);
  if (used.length < 2) return null;
  return {
    sessionsUsed: used.length,
    bucketCount: EXERTION_BUCKETS,
    buckets: Array.from({ length: EXERTION_BUCKETS }, (_, i) => {
      const vals = used.map((t) => t.buckets[i]?.avgTierWeight).filter(isNum);
      return {
        index: i,
        fromPct: round((i / EXERTION_BUCKETS) * 100, 1),
        toPct: round(((i + 1) / EXERTION_BUCKETS) * 100, 1),
        sessions: vals.length,
        avgTierWeight: vals.length ? round(vals.reduce((a, v) => a + v, 0) / vals.length, 3) : null,
      };
    }),
  };
}

// ---------------------------------------------------------------------------
// Calories and workout equivalents (all estimates)
// ---------------------------------------------------------------------------

// swingsRecorded: false when the session has no landed swings at all. Then there is no evidence of
// physical effort, so no figure is produced (null) rather than pricing idle time.
function estimateEffort(durationSec, avgTierWeight, swingsRecorded = true) {
  const secs = Number(durationSec);
  if (!swingsRecorded) return { estimatedCalories: null, estimatedWalkingMinutes: null, estimatedJoggingMinutes: null, estimate: { note: ESTIMATE_NOTE, basis: "no swings recorded in this session, so no estimate" } };
  if (!(secs > 0)) return { estimatedCalories: null, estimatedWalkingMinutes: null, estimatedJoggingMinutes: null, estimate: { note: ESTIMATE_NOTE, basis: "no duration recorded" } };
  const hasSwings = isNum(avgTierWeight);
  const met = hasSwings ? MET_MIN + (MET_MAX - MET_MIN) * ((avgTierWeight - 1) / (TIER_WEIGHT.full - 1)) : MET_NO_SWING_DATA;
  const kcal = met * ASSUMED_BODY_MASS_KG * (secs / 3600);
  const minutesAt = (refMet) => kcal / ((refMet * ASSUMED_BODY_MASS_KG) / 60);
  return {
    estimatedCalories: round(kcal, 1),
    estimatedWalkingMinutes: round(minutesAt(MET_BRISK_WALKING), 1),
    estimatedJoggingMinutes: round(minutesAt(MET_JOGGING), 1),
    estimate: {
      note: ESTIMATE_NOTE,
      basis: hasSwings ? "duration x MET scaled by measured swing intensity" : "duration only (no swing tiers recorded): lowest assumed MET",
      metUsed: round(met, 2),
      assumedBodyMassKg: ASSUMED_BODY_MASS_KG,
      metRange: [MET_MIN, MET_MAX],
      referenceMet: { briskWalking: MET_BRISK_WALKING, jogging: MET_JOGGING },
    },
  };
}

// ---------------------------------------------------------------------------
// Per-session summaries, personal bests, progress
// ---------------------------------------------------------------------------

// session: API session object. hits: [{ tier, speed, t }] for that session.
function sessionSummary(session, hits) {
  const tiers = tierBreakdown(hits.map((h) => h.tier));
  const effort = estimateEffort(session.durationSec, tiers.avgTierWeight, tiers.total > 0 || Number(session.hitCount) > 0);
  const speeds = hits.map((h) => h.speed).filter(isNum);
  return {
    id: session.id,
    level: session.level,
    status: session.status,
    startedAt: session.startedAt,
    score: Number(session.score) || 0,
    durationSec: Number(session.durationSec) || 0,
    tiers,
    maxSpeed: speeds.length ? Math.max(...speeds) : null,
    trend: exertionTrend(hits, Number(session.durationSec) || 0),
    ...effort,
  };
}

const ref = (s, value, extra = {}) => ({ value, sessionId: s.id, date: s.startedAt, level: s.level, ...extra });
const best = (list, pick) => list.reduce((m, s) => (pick(s) !== null && (!m || pick(s) > pick(m)) ? s : m), null);

// summaries: output of sessionSummary for every non-live session.
function personalBests(summaries) {
  const speed = best(summaries, (s) => s.maxSpeed);
  const fullPct = best(summaries.filter((s) => s.tiers.total >= SWING_FLOOR_FOR_RECORDS), (s) => s.tiers.percent.full);
  const kcal = best(summaries, (s) => s.estimatedCalories);
  const longest = best(summaries.filter((s) => s.durationSec > 0), (s) => s.durationSec);
  const score = best(summaries.filter((s) => s.score > 0), (s) => s.score);
  return {
    highestSwingVelocity: speed && ref(speed, round(speed.maxSpeed, 2), { unit: "m/s" }),
    highestFullSwingPercent: fullPct && ref(fullPct, fullPct.tiers.percent.full, { unit: "%", swingsInSession: fullPct.tiers.total, minSwingsRequired: SWING_FLOOR_FOR_RECORDS }),
    highestEstimatedCalories:
      kcal && ref(kcal, kcal.estimatedCalories, { unit: "kcal (estimate)", estimatedWalkingMinutes: kcal.estimatedWalkingMinutes, estimatedJoggingMinutes: kcal.estimatedJoggingMinutes }),
    longestSession: longest && ref(longest, round(longest.durationSec, 1), { unit: "seconds" }),
    highestScore: score && ref(score, score.score, { unit: "points" }),
  };
}

// Most recent session against the average of all earlier ones. Needs at least 2 sessions.
function progress(summaries) {
  if (summaries.length < 2) return null;
  const sorted = [...summaries].sort((a, b) => new Date(b.startedAt) - new Date(a.startedAt));
  const [latest, ...rest] = sorted;
  const compare = (key, pick, digits) => {
    const earlier = rest.map(pick).filter(isNum);
    const now = pick(latest);
    if (!isNum(now) || !earlier.length) return null;
    const average = earlier.reduce((a, v) => a + v, 0) / earlier.length;
    return { latest: round(now, digits), average: round(average, digits), difference: round(now - average, digits), percentDifference: average ? round(((now - average) / average) * 100, 1) : null, sessionsInAverage: earlier.length };
  };
  return {
    latestSessionId: latest.id,
    latestSessionDate: latest.startedAt,
    latestLevel: latest.level,
    comparedAgainstSessions: rest.length,
    note: "Latest session against the average of all earlier sessions, across every level played. Swing intensity is average tier weight (1 = tap, 4 = full power).",
    swingIntensity: compare("swingIntensity", (s) => s.tiers.avgTierWeight, 2),
    estimatedCalories: compare("estimatedCalories", (s) => s.estimatedCalories, 1),
    score: compare("score", (s) => s.score, 0),
  };
}

// Pooled swing tiers per level: did harder levels produce more intense swings?
// levels: level names to report; summaries: output of sessionSummary.
function intensityByLevel(levels, summaries) {
  return levels.map((level) => {
    const list = summaries.filter((s) => s.level === level);
    const withSwings = list.filter((s) => s.tiers.total > 0);
    const pooled = { counts: Object.fromEntries(TIERS.map((t) => [t, withSwings.reduce((a, s) => a + s.tiers.counts[t], 0)])) };
    const total = TIERS.reduce((a, t) => a + pooled.counts[t], 0);
    return {
      level,
      sessions: list.length,
      sessionsWithSwingData: withSwings.length,
      swings: total,
      counts: pooled.counts,
      percent: Object.fromEntries(TIERS.map((t) => [t, total ? round((pooled.counts[t] / total) * 100, 1) : null])),
      avgTierWeight: total ? round(TIERS.reduce((a, t) => a + pooled.counts[t] * TIER_WEIGHT[t], 0) / total, 3) : null,
      // Mean of each session's own average, so one long fight can't dominate.
      avgTierWeightPerSession: withSwings.length ? round(withSwings.reduce((a, s) => a + s.tiers.avgTierWeight, 0) / withSwings.length, 3) : null,
    };
  });
}

// ---------------------------------------------------------------------------
// Engagement
// ---------------------------------------------------------------------------

function safeZone(tz) {
  try {
    new Intl.DateTimeFormat("en-CA", { timeZone: tz });
    return typeof tz === "string" && tz ? tz : "UTC";
  } catch {
    return "UTC";
  }
}

// Calendar day (YYYY-MM-DD) of an instant in a time zone.
function dayIn(date, zone) {
  return new Intl.DateTimeFormat("en-CA", { timeZone: zone, year: "numeric", month: "2-digit", day: "2-digit" }).format(date);
}

const dayNum = (day) => Date.parse(`${day}T00:00:00Z`) / 864e5; // whole days since epoch, no DST issues
const dayFromNum = (n) => new Date(n * 864e5).toISOString().slice(0, 10);
const weekStartOf = (day) => dayFromNum(dayNum(day) - ((new Date(`${day}T00:00:00Z`).getUTCDay() + 6) % 7)); // Monday

// Consecutive calendar days with at least one session. The current streak is still alive if the
// last session was today or yesterday; once a whole day passes with no session it is 0.
function streaks(days, today) {
  const nums = [...new Set(days)].map(dayNum).sort((a, b) => a - b);
  let longest = 0, run = 0, prev = null;
  for (const n of nums) {
    run = prev !== null && n === prev + 1 ? run + 1 : 1;
    longest = Math.max(longest, run);
    prev = n;
  }
  const last = nums[nums.length - 1];
  const current = last !== undefined && dayNum(today) - last <= 1 ? run : 0;
  return { current, longest };
}

// startedAts: ISO strings of one player's sessions.
function playerEngagement(playerName, startedAts, tz, now = new Date()) {
  const zone = safeZone(tz);
  const today = dayIn(now, zone);
  const stamps = startedAts.map((s) => new Date(s)).filter((d) => !Number.isNaN(d.getTime())).sort((a, b) => a - b);
  const days = stamps.map((d) => dayIn(d, zone));
  const byDay = new Map();
  for (const d of days) byDay.set(d, (byDay.get(d) || 0) + 1);
  const byWeek = new Map();
  for (const d of days) byWeek.set(weekStartOf(d), (byWeek.get(weekStartOf(d)) || 0) + 1);
  const st = streaks(days, today);
  const first = stamps[0];
  const last = stamps[stamps.length - 1];
  return {
    playerName,
    timeZone: zone,
    definitions: {
      session: "Every recorded session, finished or abandoned (same count as the rest of the dashboard).",
      streak: "Consecutive calendar days (in the time zone above) with at least one session. The current streak stays alive until a full day passes with no session.",
    },
    streakUnit: "day",
    totalSessions: stamps.length,
    daysPlayed: byDay.size,
    firstSessionAt: first ? first.toISOString() : null,
    lastSessionAt: last ? last.toISOString() : null,
    daysSinceLastSession: last ? Math.max(0, dayNum(today) - dayNum(dayIn(last, zone))) : null,
    currentStreak: st.current,
    longestStreak: st.longest,
    sessionsByDay: [...byDay].map(([day, sessions]) => ({ day, sessions })),
    sessionsByWeek: [...byWeek].map(([weekStart, sessions]) => ({ weekStart, sessions })),
  };
}

// rows: [{ player, startedAt }] for every session, all players.
function siteEngagement(rows, tz, now = new Date()) {
  const zone = safeZone(tz);
  const today = dayIn(now, zone);
  const todayN = dayNum(today);
  const perDay = new Map();
  const perPlayer = new Map();
  const week = new Set();
  const month = new Set();
  for (const r of rows) {
    const d = new Date(r.startedAt);
    if (Number.isNaN(d.getTime())) continue;
    const day = dayIn(d, zone);
    perDay.set(day, (perDay.get(day) || 0) + 1);
    perPlayer.set(r.player, (perPlayer.get(r.player) || 0) + 1);
    const age = todayN - dayNum(day);
    if (age >= 0 && age < 7) week.add(r.player);
    if (age >= 0 && age < 30) month.add(r.player);
  }
  const players = perPlayer.size;
  const returning = [...perPlayer.values()].filter((n) => n > 1).length;
  return {
    timeZone: zone,
    definitions: {
      activePlayers: "Players with at least one session in the last 7 days (week) or 30 days (month), counting today.",
      returnRate: "Share of all players who have more than one recorded session (any day, any outcome).",
    },
    sessionsPerDay: Array.from({ length: 30 }, (_, i) => {
      const day = dayFromNum(todayN - 29 + i);
      return { day, sessions: perDay.get(day) || 0 };
    }),
    activePlayersThisWeek: week.size,
    activePlayersThisMonth: month.size,
    totalPlayers: players,
    returningPlayers: returning,
    returnRatePercent: players ? round((returning / players) * 100, 1) : null,
  };
}

module.exports = {
  TIERS,
  TIER_WEIGHT,
  ASSUMED_BODY_MASS_KG,
  MET_MIN,
  MET_MAX,
  MET_BRISK_WALKING,
  MET_JOGGING,
  tierBreakdown,
  exertionTrend,
  averageExertionTrend,
  estimateEffort,
  sessionSummary,
  personalBests,
  progress,
  intensityByLevel,
  playerEngagement,
  siteEngagement,
};
