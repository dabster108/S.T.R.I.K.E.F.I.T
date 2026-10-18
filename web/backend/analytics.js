// Read-only aggregates for the dashboard, the landing page and CSV export.
// Every number comes straight from the sessions/events tables; nothing is
// estimated. Swing-speed figures only exist for hits recorded by the live
// telemetry build (hit events carrying payload.swingSpeed), so each result
// reports its sample size and uses null when there is no data.

const { query } = require("./db");
const { SESSION_API_SELECT, EVENT_SELECT, fromRow } = require("./columns");
const fitness = require("./fitness");
const progression = require("./progression");
const feedback = require("./feedback");
const insightsRules = require("./insights");
const goalsStore = require("./goalsStore");
const goalsRules = require("./goals");
const engage = require("./engage");
const engageStore = require("./engageStore");
const outcomes = require("./outcomes");
const levelsInfo = require("./levels");
const effort = require("./effort");

const LEVEL_ORDER = ["Easy", "Medium", "Hard"];
const TIERS = ["tap", "light", "medium", "full"];
const SWING_CAP = levelsInfo.SWING_SPEED_CAP; // m/s; see levels.js
const HIST_STEP = 0.5; // m/s per histogram bucket

const num = (v) => (v === null || v === undefined || !Number.isFinite(Number(v)) ? null : Number(v));
const round = (v, d = 2) => (v === null ? null : Math.round(v * 10 ** d) / 10 ** d);
const capSpeed = (v) => (v === null ? null : Math.min(v, SWING_CAP));
const levelSort = (a, b) => (LEVEL_ORDER.indexOf(a.level) + 1 || 99) - (LEVEL_ORDER.indexOf(b.level) + 1 || 99);

// Session filters shared by every query: player, level, date range on started_at.
function sessionWhere(filters, params, alias = "s") {
  const where = [];
  if (filters.player) where.push(`${alias}.player_name = $${params.push(String(filters.player))}`);
  if (filters.level) where.push(`${alias}.level = $${params.push(String(filters.level))}`);
  if (filters.from && !Number.isNaN(Date.parse(filters.from))) where.push(`${alias}.started_at >= $${params.push(new Date(filters.from).toISOString())}`);
  if (filters.to && !Number.isNaN(Date.parse(filters.to))) {
    // A bare date ("2026-09-28") means the whole of that day.
    const to = /^\d{4}-\d{2}-\d{2}$/.test(filters.to) ? new Date(Date.parse(filters.to) + 864e5) : new Date(filters.to);
    where.push(`${alias}.started_at < $${params.push(to.toISOString())}`);
  }
  return where;
}

// One row per landed hit that carries a measured swing speed. `speed` is clamped to the game's cap (levels.js
// SWING_SPEED_CAP): the headset stores the raw controller speed, and a raw average of 11.6 m/s on a game that caps at 8 is
// a number the game never used. `raw_speed` keeps the stored value, for counting how many swings were over the cap.
function hitsCte(filters, params) {
  const where = sessionWhere(filters, params);
  return `hits AS (
    SELECT e.session_ref AS session_id, s.player_name, s.level, s.status, s.duration_sec,
           CASE WHEN json_typeof(e.payload->'swingSpeed') = 'number' THEN LEAST((e.payload->>'swingSpeed')::float8, ${SWING_CAP}) END AS speed,
           CASE WHEN json_typeof(e.payload->'swingSpeed') = 'number' THEN (e.payload->>'swingSpeed')::float8 END AS raw_speed,
           lower(e.payload->>'tier') AS tier,
           CASE WHEN json_typeof(e.payload->'defended') = 'boolean' THEN (e.payload->>'defended')::boolean END AS defended,
           CASE WHEN json_typeof(e.payload->'damage') = 'number' THEN (e.payload->>'damage')::float8 END AS damage,
           CASE WHEN json_typeof(e.payload->'t') = 'number' THEN (e.payload->>'t')::float8 END AS t
    FROM events e JOIN sessions s ON s.id = e.session_ref
    WHERE e.type = 'hit' AND json_typeof(e.payload->'swingSpeed') = 'number'
    ${where.length ? `AND ${where.join(" AND ")}` : ""}
  )`;
}

function tierCounts(rows) {
  const out = Object.fromEntries(TIERS.map((t) => [t, 0]));
  for (const r of rows) if (r.tier in out) out[r.tier] += Number(r.n);
  return out;
}

// Runs a query with an IANA time zone parameter, falling back to UTC if Postgres rejects it.
async function withZone(tz, fn) {
  const zone = typeof tz === "string" && /^[A-Za-z]+(?:[/_+-][A-Za-z0-9]+)*$/.test(tz) ? tz : "UTC";
  try {
    return { zone, result: await fn(zone) };
  } catch (err) {
    if (zone === "UTC" || !/time zone/i.test(err.message)) throw err;
    return { zone: "UTC", result: await fn("UTC") };
  }
}

async function levelHitStats(filters = {}) {
  const params = [];
  const { rows } = await query(
    `WITH ${hitsCte(filters, params)}
     SELECT level, count(*)::int AS n, avg(speed) AS avg_speed, max(speed) AS peak_speed,
            max(raw_speed) AS raw_peak, count(*) FILTER (WHERE raw_speed > ${SWING_CAP})::int AS over_cap,
            count(*) FILTER (WHERE tier = 'full')::int AS full, count(*) FILTER (WHERE tier IS NOT NULL)::int AS tiered
     FROM hits GROUP BY level`,
    params
  );
  return new Map(rows.map((r) => [r.level, {
    swingSamples: r.n,
    avgSwingSpeed: round(num(r.avg_speed)),
    peakSwingSpeed: round(num(r.peak_speed)),
    fullPowerRatio: r.tiered ? round(r.full / r.tiered, 4) : null,
    // Added with the cap: speeds above SWING_SPEED_CAP are counted here and clamped in the two figures above.
    swingsOverCap: r.over_cap,
    rawPeakSwingSpeed: round(num(r.raw_peak)),
  }]));
}

const emptyHitStats = { swingSamples: 0, avgSwingSpeed: null, peakSwingSpeed: null, fullPowerRatio: null, swingsOverCap: 0, rawPeakSwingSpeed: null };

async function overview({ tz } = {}) {
  const engagementRows = (await query(`SELECT player_name AS player, started_at AS "startedAt" FROM sessions`)).rows;
  const totals = (await query(`
    SELECT count(*)::int AS sessions,
           count(*) FILTER (WHERE status = 'live')::int AS live,
           count(DISTINCT player_name)::int AS players,
           COALESCE(sum(hit_count), 0)::float8 AS swings,
           COALESCE(sum(damage_dealt), 0)::float8 AS damage,
           count(*) FILTER (WHERE outcome = 'victory')::int AS wins,
           count(*) FILTER (WHERE outcome = 'defeat')::int AS losses,
           count(*) FILTER (WHERE outcome = 'abandoned')::int AS abandoned,
           avg(duration_sec) FILTER (WHERE status = 'finished' AND duration_sec > 0) AS avg_duration
    FROM sessions`)).rows[0];

  const { zone, result: perDay } = await withZone(tz, async (z) => (await query(
    `SELECT to_char(d, 'YYYY-MM-DD') AS day, count(s.id)::int AS sessions,
            count(s.id) FILTER (WHERE s.outcome = 'victory')::int AS wins
     FROM generate_series((now() AT TIME ZONE $1)::date - 13, (now() AT TIME ZONE $1)::date, interval '1 day') AS d
     LEFT JOIN sessions s ON (s.started_at AT TIME ZONE $1)::date = d::date
     GROUP BY d ORDER BY d`, [z])).rows);

  const levels = (await query(`
    SELECT level, count(*)::int AS sessions,
           count(*) FILTER (WHERE outcome = 'victory')::int AS wins,
           count(*) FILTER (WHERE outcome = 'defeat')::int AS losses,
           count(*) FILTER (WHERE outcome = 'abandoned')::int AS abandoned,
           avg(score) FILTER (WHERE status = 'finished' AND outcome IN ('victory', 'defeat')) AS avg_score,
           max(score) AS best_score,
           COALESCE(sum(hit_count), 0)::float8 AS swings,
           COALESCE(sum(damage_dealt), 0)::float8 AS damage,
           avg(duration_sec) FILTER (WHERE status = 'finished' AND duration_sec > 0) AS avg_duration
    FROM sessions GROUP BY level`)).rows;
  const hitStats = await levelHitStats();

  const recent = (await query(`SELECT ${SESSION_API_SELECT} FROM sessions ORDER BY started_at DESC, seq DESC LIMIT 5`)).rows.map(fromRow);

  return {
    totalSessions: totals.sessions,
    liveSessions: totals.live,
    totalPlayers: totals.players,
    totalSwings: totals.swings,
    totalDamage: round(totals.damage, 1),
    wins: totals.wins,
    losses: totals.losses,
    abandoned: totals.abandoned,
    // The one shared rule (outcomes.js): victories / (victories + defeats), abandoned fights excluded and counted above.
    winRate: outcomes.winRate(totals.wins, totals.losses),
    avgSessionSec: round(num(totals.avg_duration), 1),
    timeZone: zone,
    sessionsPerDay: perDay,
    // Added: site-wide engagement (last 30 days, active players, return rate).
    engagement: fitness.siteEngagement(engagementRows, tz),
    byLevel: levels
      .map((l) => ({
        level: l.level,
        sessions: l.sessions,
        wins: l.wins,
        losses: l.losses,
        abandoned: l.abandoned,
        winRate: outcomes.winRate(l.wins, l.losses),
        avgScore: round(num(l.avg_score), 0), // mean over decided fights (victory + defeat), like the win rate
        bestScore: num(l.best_score),
        swings: l.swings,
        damage: round(l.damage, 1),
        avgSessionSec: round(num(l.avg_duration), 1),
        ...(hitStats.get(l.level) || emptyHitStats),
      }))
      .sort(levelSort),
    recentSessions: recent,
    // Added with daily goals (aggregates only): how many players set a goal today and how many met it.
    goalsToday: await goalsStore.overviewToday({ tz }).catch(() => null),
  };
}

// Aggregates only: no names, no per-person rows.
async function publicStats() {
  const r = (await query(`
    SELECT count(*) FILTER (WHERE status = 'finished')::int AS sessions,
           count(*) FILTER (WHERE status = 'live')::int AS live,
           COALESCE(sum(hit_count) FILTER (WHERE status = 'finished'), 0)::float8 AS swings,
           count(DISTINCT player_name) FILTER (WHERE status = 'finished')::int AS players,
           max(ended_at) FILTER (WHERE status = 'finished') AS last_session
    FROM sessions`)).rows[0];
  return {
    totalSessions: r.sessions,
    liveSessions: r.live,
    totalSwings: r.swings,
    totalPlayers: r.players,
    // Computed from levels.js (status "playable" / "planned"), not typed in: Level 3 shipped and this still said 2 + 1.
    levelsAvailable: levelsInfo.levelsPlayable(),
    levelsPlanned: levelsInfo.levelsPlanned(),
    lastSessionAt: r.last_session ? new Date(r.last_session).toISOString() : null,
  };
}

async function playerProfile(name) {
  const { rows } = await query(
    `SELECT ${SESSION_API_SELECT}, live_state AS "liveState" FROM sessions WHERE player_name = $1 ORDER BY started_at DESC, seq DESC`,
    [name]
  );
  if (!rows.length) return null;
  const sessions = rows.map((r) => {
    const { liveState, ...rest } = r;
    return { ...fromRow(rest), liveState: liveState || null };
  });

  const hitParams = [];
  const cte = hitsCte({ player: name }, hitParams);
  const [tierRows, perSession, overall] = await Promise.all([
    query(`WITH ${cte} SELECT level, tier, count(*)::int AS n, avg(damage) AS avg_damage FROM hits GROUP BY level, tier`, hitParams),
    query(`WITH ${cte} SELECT session_id, count(*)::int AS n, avg(speed) AS avg_speed, max(speed) AS peak_speed,
                  count(*) FILTER (WHERE tier = 'full')::int AS full FROM hits GROUP BY session_id`, hitParams),
    query(`WITH ${cte} SELECT count(*)::int AS n, avg(speed) AS avg_speed, max(speed) AS peak_speed,
                  count(*) FILTER (WHERE tier = 'full')::int AS full, count(*) FILTER (WHERE tier IS NOT NULL)::int AS tiered FROM hits`, hitParams),
  ]);
  const speedBySession = new Map(perSession.rows.map((r) => [r.session_id, r]));
  const o = overall.rows[0];

  // Fitness outcomes: one summary per non-live session, built from that session's hit events.
  const hitParams2 = [];
  const hitRows = (await query(`WITH ${hitsCte({ player: name }, hitParams2)} SELECT session_id, tier, speed, t FROM hits`, hitParams2)).rows;
  const hitsBySession = new Map();
  for (const h of hitRows) {
    if (!hitsBySession.has(h.session_id)) hitsBySession.set(h.session_id, []);
    hitsBySession.get(h.session_id).push({ tier: h.tier, speed: num(h.speed), t: num(h.t) });
  }
  const summaries = sessions.filter((s) => s.status !== "live").map((s) => fitness.sessionSummary(s, hitsBySession.get(s.id) || []));
  const summaryById = new Map(summaries.map((s) => [s.id, s]));
  const allTiers = fitness.tierBreakdown(hitRows.map((h) => h.tier));
  const estimated = summaries.filter((s) => s.estimatedCalories !== null);
  const sumOf = (key) => (estimated.length ? estimated.reduce((a, s) => a + s[key], 0) : null);
  const kcalTotal = sumOf("estimatedCalories");
  const walkTotal = sumOf("estimatedWalkingMinutes");
  const jogTotal = sumOf("estimatedJoggingMinutes");

  const finished = sessions.filter((s) => s.status !== "live");
  const wins = sessions.filter((s) => s.outcome === "victory").length;
  const losses = sessions.filter((s) => s.outcome === "defeat").length;
  const abandonedTotal = sessions.filter((s) => s.outcome === "abandoned").length;
  const sum = (list, k) => list.reduce((a, s) => a + (Number(s[k]) || 0), 0);

  const levels = [...new Set(sessions.map((s) => s.level))];
  const levelStats = await levelHitStats({ player: name });
  const byLevel = levels
    .map((level) => {
      const list = sessions.filter((s) => s.level === level);
      const done = list.filter((s) => s.status !== "live");
      const decidedList = done.filter((s) => outcomes.isDecided(s.outcome));
      const w = list.filter((s) => s.outcome === "victory").length;
      const l = list.filter((s) => s.outcome === "defeat").length;
      return {
        level,
        sessions: list.length,
        wins: w,
        losses: l,
        abandoned: list.filter((s) => s.outcome === "abandoned").length,
        winRate: outcomes.winRate(w, l),
        bestScore: Math.max(0, ...list.map((s) => Number(s.score) || 0)),
        avgScore: decidedList.length ? round(sum(decidedList, "score") / decidedList.length, 0) : null, // decided fights only
        avgDamage: done.length ? round(sum(done, "damageDealt") / done.length, 1) : null,
        avgHits: done.length ? round(sum(done, "hitCount") / done.length, 1) : null,
        avgSessionSec: done.length ? round(sum(done, "durationSec") / done.length, 1) : null,
        tiers: tierCounts(tierRows.rows.filter((r) => r.level === level)),
        ...(levelStats.get(level) || emptyHitStats),
      };
    })
    .sort(levelSort);

  const series = [...sessions].reverse().map((s) => {
    const sp = speedBySession.get(s.id);
    return {
      id: s.id,
      level: s.level,
      outcome: s.outcome,
      status: s.status,
      startedAt: s.startedAt,
      score: s.score,
      damageDealt: s.damageDealt,
      hitCount: s.hitCount,
      xp: s.xp,
      durationSec: s.durationSec,
      swingSamples: sp ? sp.n : 0,
      avgSwingSpeed: sp ? round(num(sp.avg_speed)) : null,
      peakSwingSpeed: sp ? round(num(sp.peak_speed)) : null,
      fullSwings: sp ? sp.full : null,
      // Added: estimates (see fitness.js); null for live sessions.
      estimatedCalories: summaryById.get(s.id)?.estimatedCalories ?? null,
      estimatedWalkingMinutes: summaryById.get(s.id)?.estimatedWalkingMinutes ?? null,
      estimatedJoggingMinutes: summaryById.get(s.id)?.estimatedJoggingMinutes ?? null,
    };
  });

  return {
    playerName: sessions[0].playerName,
    totals: {
      sessions: sessions.length,
      finished: finished.length,
      wins,
      losses,
      abandoned: abandonedTotal,
      winRate: outcomes.winRate(wins, losses),
      bestScore: Math.max(0, ...sessions.map((s) => Number(s.score) || 0)),
      totalDamage: round(sum(sessions, "damageDealt"), 1),
      totalSwings: sum(sessions, "hitCount"),
      totalXp: sum(sessions, "xp"),
      totalDurationSec: round(sum(finished, "durationSec"), 1),
      swingSamples: o.n,
      avgSwingSpeed: round(num(o.avg_speed)),
      peakSwingSpeed: round(num(o.peak_speed)),
      fullPowerRatio: o.tiered ? round(o.full / o.tiered, 4) : null,
    },
    tiers: tierCounts(tierRows.rows),
    tierDamage: Object.fromEntries(TIERS.map((t) => {
      const list = tierRows.rows.filter((r) => r.tier === t && r.avg_damage !== null);
      const n = list.reduce((a, r) => a + r.n, 0);
      return [t, n ? round(list.reduce((a, r) => a + r.avg_damage * r.n, 0) / n, 1) : null];
    })),
    byLevel,
    series,
    recentSessions: sessions.slice(0, 25),
    sessions, // newest first, every session

    live: sessions.find((s) => s.status === "live") || null,
    firstPlayedAt: series[0]?.startedAt ?? null,
    lastPlayedAt: sessions[0].endedAt,

    // Added for the fitness-outcome features.
    swingIntensity: allTiers, // measured: tier counts, percentages and average tier weight across every session
    exertionTrend: fitness.averageExertionTrend(summaries.map((s) => s.trend)), // null until 2+ sessions have enough swings
    intensityByLevel: fitness.intensityByLevel([...levels].sort((a, b) => levelSort({ level: a }, { level: b })), summaries),
    personalBests: fitness.personalBests(summaries),
    progress: fitness.progress(summaries),
    fitnessEstimate: {
      estimatedCalories: round(kcalTotal, 1),
      estimatedWalkingMinutes: round(walkTotal, 1),
      estimatedJoggingMinutes: round(jogTotal, 1),
      sessionsEstimated: estimated.length,
      note: "Sum of per-session estimates (sessions with no recorded swings are left out); an estimate, not a measurement.",
    },
  };
}

// Fitness block for GET /api/sessions/:id, built from the events already loaded for that route.
function sessionFitness(session, events) {
  const hits = events
    .filter((e) => e.type === "hit" && e.payload && typeof e.payload === "object")
    .map((e) => ({ tier: String(e.payload.tier || "").toLowerCase(), speed: capSpeed(num(e.payload.swingSpeed)), t: num(e.payload.t) }))
    .filter((h) => h.speed !== null);
  const s = fitness.sessionSummary(session, hits);
  return {
    swingIntensity: s.tiers,
    exertionTrend: s.trend,
    estimatedCalories: s.estimatedCalories,
    estimatedWalkingMinutes: s.estimatedWalkingMinutes,
    estimatedJoggingMinutes: s.estimatedJoggingMinutes,
    estimate: s.estimate,
  };
}

// ---------------------------------------------------------------------------
// Achievements (streak, weekly goal, level, badges) and per-session personal bests
// ---------------------------------------------------------------------------

// Everything the progression logic needs for one player: finished sessions oldest first, a
// fitness summary for each, and per-session combo / damage evidence from the stored events.
async function playerProgressData(name) {
  const { rows } = await query(
    `SELECT ${SESSION_API_SELECT}, live_state AS "liveState" FROM sessions
     WHERE player_name = $1 AND status <> 'live' ORDER BY started_at, seq`,
    [name]
  );
  const sessions = rows.map((r) => {
    const { liveState, ...rest } = r;
    return { ...fromRow(rest), liveState: liveState || null };
  });
  const hitParams = [];
  const hitRows = (await query(`WITH ${hitsCte({ player: name }, hitParams)} SELECT session_id, tier, speed, t FROM hits`, hitParams)).rows;
  const hitsBySession = new Map();
  for (const h of hitRows) {
    if (!hitsBySession.has(h.session_id)) hitsBySession.set(h.session_id, []);
    hitsBySession.get(h.session_id).push({ tier: h.tier, speed: num(h.speed), t: num(h.t) });
  }
  const summaries = sessions.map((s) => fitness.sessionSummary(s, hitsBySession.get(s.id) || []));
  return { sessions, summaries, summaryById: new Map(summaries.map((s) => [s.id, s])) };
}

async function playerAchievements(name, { tz, goal } = {}) {
  const [{ sessions, summaryById }, evidence] = await Promise.all([
    playerProgressData(name),
    query(
      `SELECT e.session_ref AS id,
              max(CASE WHEN json_typeof(e.payload->'combo') = 'number' THEN (e.payload->>'combo')::float8 END) AS combo,
              max(CASE WHEN json_typeof(e.payload->'playerMaxHp') = 'number' THEN (e.payload->>'playerMaxHp')::float8 END) AS maxhp,
              count(*) FILTER (WHERE e.type = 'player_hurt')::int AS hurt
       FROM events e JOIN sessions s ON s.id = e.session_ref
       WHERE s.player_name = $1 AND e.type IN ('hit', 'hp', 'player_hurt') GROUP BY e.session_ref`,
      [name]
    ).then((r) => r.rows),
  ]);
  if (!sessions.length) {
    // Either an unknown name (404) or a player whose only session is still live (empty progress).
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  const comboById = new Map(evidence.filter((r) => r.combo !== null).map((r) => [r.id, Number(r.combo)]));
  const hurtById = new Map(evidence.map((r) => [r.id, r.hurt]));
  const maxHpById = new Map(evidence.filter((r) => r.maxhp !== null).map((r) => [r.id, Number(r.maxhp)]));
  const eventSessions = new Set(evidence.map((r) => r.id));

  const zone = progression.safeZone(tz || progression.DEFAULT_TZ);
  const today = progression.dayIn(new Date(), zone);
  const days = new Map(sessions.map((s) => [s.id, progression.dayIn(new Date(s.startedAt), zone)]));
  const dayList = [...days.values()];
  const target = Number.isInteger(goal) && goal > 0 ? goal : progression.WEEKLY_GOAL_TARGET;
  const streak = progression.streakWithFreeze(dayList, today);
  const weekly = progression.weeklyGoal(dayList, today, target);
  const totalXp = sessions.reduce((a, s) => a + (Number(s.xp) || 0), 0);

  // Daily goals: the days this player met their goal, and the bonus XP those earned (NEW fields below; level / streak unchanged).
  // Challenges and mastery (added with the engagement pass): completion bonuses and per-level stars, also NEW fields only.
  const [goalMet, challengeDone, masteryFacts] = await Promise.all([
    goalsStore.metSummary(name).catch(() => ({ days: [], bonusXp: 0 })),
    engageStore.completionSummary(name).catch(() => ({ count: 0, bonusXp: 0 })),
    engageStore.loadFacts({ player: name }).catch(() => null),
  ]);
  const badges = progression.evaluateBadges({
    sessions, summaryById, comboById, hurtById, maxHpById, eventSessions, days, streak,
    weeklyTarget: target, thisWeekSessions: weekly.sessionsThisWeek, goalDays: goalMet.days,
  });
  const { reachedOn, ...streakOut } = streak;
  return {
    playerName: name,
    timeZone: zone,
    level: progression.levelFromXp(totalXp),
    streak: {
      ...streakOut,
      rule: `Consecutive days with a session. One missed day is forgiven once per rolling ${progression.FREEZE_WINDOW_DAYS} days (a "freeze"); two missed days in a row end the streak.`,
    },
    weeklyGoal: weekly,
    badges,
    badgesUnlocked: badges.filter((b) => b.unlocked).length,
    badgesTotal: badges.length,
    definitions: { session: "Finished or abandoned sessions; a session still being played is not counted yet." },
    // Added with daily goals: goal bonuses counted into XP / level, and the goal streak. `level` above is session XP only.
    goalBonusXp: goalMet.bonusXp,
    totalXpWithGoals: Math.round(totalXp + goalMet.bonusXp),
    levelWithGoals: progression.levelFromXp(totalXp + goalMet.bonusXp),
    goalStreak: { ...goalsRules.goalStreak(goalMet.days, today), rule: "Consecutive days on which the daily goal was met (no freeze)." },
    goalsMet: goalMet.days.length,
    // Added with daily / weekly challenges: their one-off bonus XP, and XP / level with every bonus (sessions + goals + challenges).
    challengeBonusXp: challengeDone.bonusXp,
    challengesCompleted: challengeDone.count,
    totalXpWithBonuses: Math.round(totalXp + goalMet.bonusXp + challengeDone.bonusXp),
    levelWithBonuses: progression.levelFromXp(totalXp + goalMet.bonusXp + challengeDone.bonusXp),
    // Added: three stars per level (engage.MASTERY). null if the facts query failed.
    mastery: masteryFacts
      ? (() => {
          const levels = engage.mastery(masteryFacts);
          return { levels, starsEarned: levels.reduce((a, l) => a + l.starsEarned, 0), starsTotal: levels.length * 3, note: engage.MASTERY_NOTE };
        })()
      : null,
  };
}

// Plain-language insights for one player (insights.js rules): each sentence only with enough data behind it, the rest
// listed as pending with the stated minimum. Guard sentences use the monster_guard / opening_hit telemetry.
async function playerInsights(name) {
  const [{ sessions }, hitRows, guardRows] = await Promise.all([
    playerProgressData(name),
    (async () => {
      const params = [];
      return (await query(`WITH ${hitsCte({ player: name }, params)} SELECT session_id, tier, speed, t FROM hits WHERE status <> 'live'`, params)).rows;
    })(),
    query(
      `SELECT e.session_ref AS id,
              count(*) FILTER (WHERE e.type = 'hit')::int AS hits,
              coalesce(sum(CASE WHEN e.type = 'monster_guard' AND json_typeof(e.payload->'blockedHits') = 'number'
                                THEN (e.payload->>'blockedHits')::float8 END), 0)::int AS blocked,
              count(*) FILTER (WHERE e.type = 'opening_hit')::int AS openings,
              count(*) FILTER (WHERE e.type IN ('monster_guard', 'opening_hit'))::int AS guard_events
       FROM events e JOIN sessions s ON s.id = e.session_ref
       WHERE s.player_name = $1 AND s.status <> 'live' GROUP BY e.session_ref`,
      [name]
    ).then((r) => r.rows),
  ]);
  if (!sessions.length) {
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  const hitsBy = new Map();
  for (const h of hitRows) {
    if (!hitsBy.has(h.session_id)) hitsBy.set(h.session_id, []);
    hitsBy.get(h.session_id).push({ t: num(h.t), speed: num(h.speed), tier: h.tier });
  }
  const result = insightsRules.playerInsights({
    trendSessions: sessions.map((s) => ({ durationSec: num(s.durationSec), hits: hitsBy.get(s.id) || [] })),
    tiers: hitRows.map((h) => h.tier),
    outcomes: sessions.map((s) => ({ level: s.level, outcome: s.outcome })),
    guardSessions: guardRows.filter((r) => r.guard_events > 0).map((r) => ({ hits: r.hits, guardsBlocked: r.blocked, openings: r.openings })),
  });
  return { playerName: name, sessions: sessions.length, ...result };
}

// First-run checklist for one player, computed from the database (the dashboard only stores its dismissal).
const DEFAULT_NAME = /^player\s*\d*$/i; // the game's "Player 1" and the API's "Player1"
async function playerChecklist(name) {
  const [{ rows }, rated] = await Promise.all([
    query("SELECT count(*) FILTER (WHERE status <> 'live')::int AS finished, count(*)::int AS total FROM sessions WHERE player_name = $1", [name]),
    query("SELECT count(*)::int AS n FROM session_feedback f JOIN sessions s ON s.id = f.session_id WHERE s.player_name = $1", [name]),
  ]);
  if (!rows[0].total) return null;
  const ach = await playerAchievements(name, {});
  const steps = [
    { id: "first_fight", label: "Play a first fight", done: rows[0].finished > 0, detail: `${rows[0].finished} finished` },
    { id: "pick_name", label: "Pick a name", done: !DEFAULT_NAME.test(name.trim()), detail: name },
    { id: "rate_session", label: "Rate a session", done: rated.rows[0].n > 0, detail: `${rated.rows[0].n} rated` },
    { id: "unlock_badge", label: "Unlock a badge", done: (ach?.badgesUnlocked || 0) > 0, detail: `${ach?.badgesUnlocked || 0} of ${ach?.badgesTotal || 0}` },
  ];
  return { playerName: name, steps, done: steps.filter((s) => s.done).length, total: steps.length };
}

// Records this session holds against the player's other finished sessions (see progression.personalBestsSet).
async function sessionPersonalBests(session) {
  if (session.status === "live") return [];
  const { summaries } = await playerProgressData(session.playerName);
  return progression.personalBestsSet(session.id, summaries);
}

// One player's engagement over time (sessions grouped by day and week, streaks, recency).
async function playerEngagement(name, { tz } = {}) {
  const { rows } = await query(`SELECT started_at AS "startedAt" FROM sessions WHERE player_name = $1`, [name]);
  if (!rows.length) return null;
  return fitness.playerEngagement(name, rows.map((r) => r.startedAt), tz);
}

// Session summaries (tiers, no estimates needed) for every session matching the filters.
async function siteIntensityByLevel(filters, levels) {
  const sParams = [];
  const sWhere = sessionWhere(filters, sParams);
  sWhere.push("s.status = 'finished'");
  const sessions = (await query(`SELECT s.id, s.level, s.status, s.started_at AS "startedAt", s.score, s.duration_sec AS "durationSec" FROM sessions s WHERE ${sWhere.join(" AND ")}`, sParams)).rows;
  const hParams = [];
  const hits = (await query(`WITH ${hitsCte(filters, hParams)} SELECT session_id, tier, speed, t FROM hits`, hParams)).rows;
  const bySession = new Map();
  for (const h of hits) {
    if (!bySession.has(h.session_id)) bySession.set(h.session_id, []);
    bySession.get(h.session_id).push({ tier: h.tier, speed: num(h.speed), t: num(h.t) });
  }
  const summaries = sessions.map((s) => fitness.sessionSummary(s, bySession.get(s.id) || []));
  const all = [...new Set([...levels, ...sessions.map((s) => s.level)])].sort((a, b) => levelSort({ level: a }, { level: b }));
  return fitness.intensityByLevel(all, summaries);
}

// Site-wide calorie insights. Every figure is a sum or average of fitness.sessionSummary()
// estimates (same per-session formula as the player pages); nothing is re-derived here.
// Sessions with no recorded swings come back from sessionSummary with estimatedCalories null and
// are excluded from every total and average (never counted as 0).
const INSIGHT_DAYS = 30;
const INSIGHT_TOP = 5;
const INSIGHT_LEVELS = ["Easy", "Medium", "Hard"]; // always reported, so a level with no data says so

async function calorieInsights({ tz } = {}) {
  const sessions = (await query(
    `SELECT s.id, s.player_name AS player, s.level, s.status, s.started_at AS "startedAt", s.score,
            s.duration_sec AS "durationSec", s.hit_count AS "hitCount"
     FROM sessions s WHERE s.status <> 'live'`
  )).rows;
  const hParams = [];
  const hits = (await query(`WITH ${hitsCte({}, hParams)} SELECT session_id, tier, speed, t FROM hits`, hParams)).rows;
  const bySession = new Map();
  for (const h of hits) {
    if (!bySession.has(h.session_id)) bySession.set(h.session_id, []);
    bySession.get(h.session_id).push({ tier: h.tier, speed: num(h.speed), t: num(h.t) });
  }
  const rows = sessions.map((s) => ({ session: s, summary: fitness.sessionSummary(s, bySession.get(s.id) || []) }));
  const estimated = rows.filter((r) => r.summary.estimatedCalories !== null);
  const sumOf = (list, key) => (list.length ? list.reduce((a, r) => a + r.summary[key], 0) : null);

  const zone = fitness.safeZone(tz);
  const todayN = fitness.dayNum(fitness.dayIn(new Date(), zone));
  const perDay = new Map();
  for (const r of estimated) {
    const d = new Date(r.session.startedAt);
    if (Number.isNaN(d.getTime())) continue;
    const day = fitness.dayIn(d, zone);
    const cur = perDay.get(day) || { kcal: 0, sessions: 0 };
    cur.kcal += r.summary.estimatedCalories;
    cur.sessions++;
    perDay.set(day, cur);
  }

  const levels = [...new Set([...INSIGHT_LEVELS, ...sessions.map((s) => s.level)])].sort((a, b) => levelSort({ level: a }, { level: b }));
  const intensity = fitness.intensityByLevel(levels, rows.map((r) => r.summary));
  const total = sumOf(estimated, "estimatedCalories");

  return {
    estimate: { note: estimated[0]?.summary.estimate.note || null, ...(estimated[0] ? { assumedBodyMassKg: estimated[0].summary.estimate.assumedBodyMassKg, metRange: estimated[0].summary.estimate.metRange, referenceMet: estimated[0].summary.estimate.referenceMet } : {}) },
    timeZone: zone,
    sessionsConsidered: sessions.length,
    sessionsEstimated: estimated.length,
    sessionsExcluded: sessions.length - estimated.length,
    excludedNote: "Sessions with no recorded swings are left out of every total and average below, not counted as 0.",
    totalEstimatedCalories: round(total, 1),
    estimatedWalkingMinutes: round(sumOf(estimated, "estimatedWalkingMinutes"), 1),
    estimatedJoggingMinutes: round(sumOf(estimated, "estimatedJoggingMinutes"), 1),
    avgCaloriesPerSession: estimated.length ? round(total / estimated.length, 1) : null,
    caloriesPerDay: Array.from({ length: INSIGHT_DAYS }, (_, i) => {
      const day = fitness.dayFromNum(todayN - (INSIGHT_DAYS - 1) + i);
      const d = perDay.get(day);
      return { day, estimatedCalories: d ? round(d.kcal, 1) : null, sessions: d ? d.sessions : 0 };
    }),
    byLevel: levels.map((level) => {
      const list = estimated.filter((r) => r.session.level === level);
      const i = intensity.find((x) => x.level === level);
      // Same honesty check as intensity-by-level: no session with swing tiers means no figure.
      const insufficient = !i || i.sessionsWithSwingData === 0;
      const kcal = insufficient ? null : sumOf(list, "estimatedCalories");
      return {
        level,
        sessions: i ? i.sessions : 0,
        sessionsWithSwingData: i ? i.sessionsWithSwingData : 0,
        sessionsEstimated: insufficient ? 0 : list.length,
        insufficientData: insufficient,
        totalEstimatedCalories: round(kcal, 1),
        avgCaloriesPerSession: kcal !== null && list.length ? round(kcal / list.length, 1) : null,
      };
    }),
    topBurns: [...estimated]
      .sort((a, b) => b.summary.estimatedCalories - a.summary.estimatedCalories)
      .slice(0, INSIGHT_TOP)
      .map((r) => ({ sessionId: r.session.id, playerName: r.session.player, level: r.session.level, date: r.session.startedAt, estimatedCalories: r.summary.estimatedCalories, estimatedWalkingMinutes: r.summary.estimatedWalkingMinutes, estimatedJoggingMinutes: r.summary.estimatedJoggingMinutes })),
  };
}

async function researchAnalytics(filters = {}) {
  const run = async (sql) => {
    const params = [];
    const cte = hitsCte(filters, params);
    return (await query(`WITH ${cte} ${sql}`, params)).rows;
  };

  const [hist, tiers, effort, sample] = await Promise.all([
    run(`SELECT level, (floor(speed / ${HIST_STEP}) * ${HIST_STEP})::float8 AS bucket, count(*)::int AS n
         FROM hits GROUP BY level, bucket ORDER BY bucket`),
    run(`SELECT level, tier, count(*)::int AS n, avg(damage) AS avg_damage FROM hits GROUP BY level, tier`),
    // Each finished fight is split into five equal time slices; slice = when the hit landed.
    run(`SELECT level, LEAST(4, floor(t / duration_sec * 5))::int AS phase,
                count(*)::int AS n, avg(speed) AS avg_speed, count(DISTINCT session_id)::int AS sessions,
                count(*) FILTER (WHERE tier = 'full')::int AS full
         FROM hits WHERE status = 'finished' AND duration_sec > 0 AND t IS NOT NULL AND t >= 0
         GROUP BY level, phase ORDER BY phase`),
    run(`SELECT count(*)::int AS hits, count(DISTINCT session_id)::int AS sessions FROM hits`),
  ]);

  const sParams = [];
  const sWhere = sessionWhere(filters, sParams);
  sWhere.push("s.status = 'finished'");
  const levelRows = (await query(
    `SELECT s.level, count(*)::int AS sessions,
            count(*) FILTER (WHERE s.outcome = 'victory')::int AS wins,
            count(*) FILTER (WHERE s.outcome = 'defeat')::int AS losses,
            count(*) FILTER (WHERE s.outcome = 'abandoned')::int AS abandoned,
            avg(s.score) FILTER (WHERE s.outcome IN ('victory', 'defeat')) AS avg_score, avg(s.damage_dealt) AS avg_damage, avg(s.hit_count) AS avg_hits,
            avg(s.duration_sec) FILTER (WHERE s.duration_sec > 0) AS avg_duration,
            sum(s.hit_count) AS hits, sum(s.damage_dealt) AS damage,
            sum(s.duration_sec) FILTER (WHERE s.duration_sec > 0) AS seconds
     FROM sessions s WHERE ${sWhere.join(" AND ")} GROUP BY s.level`,
    sParams
  )).rows;
  const hitStats = await levelHitStats(filters);

  const levels = [...new Set([...levelRows.map((r) => r.level), ...hist.map((r) => r.level)])]
    .map((level) => ({ level }))
    .sort(levelSort)
    .map((l) => l.level);

  return {
    filters: { player: filters.player || null, level: filters.level || null },
    sample: { hitsWithSpeed: sample[0].hits, sessionsWithSpeed: sample[0].sessions, finishedSessions: levelRows.reduce((a, r) => a + r.sessions, 0) },
    speedCap: { value: SWING_CAP, unit: "m/s", note: "Stored swing speeds are the raw controller speed. Every speed figure here is clamped to the game's cap; swingsOverCap per level counts the swings that were above it." },
    histogramStep: HIST_STEP,
    speedHistogram: hist.map((r) => ({ level: r.level, from: r.bucket, to: r.bucket + HIST_STEP, count: r.n })),
    tiers: levels.map((level) => ({ level, counts: tierCounts(tiers.filter((r) => r.level === level)) })),
    damagePerSwing: TIERS.map((tier) => {
      const list = tiers.filter((r) => r.tier === tier && r.avg_damage !== null);
      const n = list.reduce((a, r) => a + r.n, 0);
      return { tier, hits: n, avgDamage: n ? round(list.reduce((a, r) => a + r.avg_damage * r.n, 0) / n, 1) : null };
    }),
    effortOverSession: levels.map((level) => ({
      level,
      phases: [0, 1, 2, 3, 4].map((phase) => {
        const r = effort.find((e) => e.level === level && e.phase === phase);
        return {
          phase,
          label: `${phase * 20}–${phase * 20 + 20}%`,
          hits: r ? r.n : 0,
          sessions: r ? r.sessions : 0,
          avgSwingSpeed: r ? round(num(r.avg_speed)) : null,
          fullPowerRatio: r && r.n ? round(r.full / r.n, 4) : null,
        };
      }),
    })),
    // Added: do harder levels produce more intense swings? Pooled across all matching sessions.
    intensityByLevel: await siteIntensityByLevel(filters, levels),
    levelComparison: levels.map((level) => {
      const r = levelRows.find((x) => x.level === level);
      return {
        level,
        sessions: r ? r.sessions : 0,
        wins: r ? r.wins : 0,
        losses: r ? r.losses : 0,
        abandoned: r ? r.abandoned : 0,
        winRate: r ? outcomes.winRate(r.wins, r.losses) : null,
        avgScore: r ? round(num(r.avg_score), 0) : null,
        avgDamage: r ? round(num(r.avg_damage), 1) : null,
        avgHits: r ? round(num(r.avg_hits), 1) : null,
        avgSessionSec: r ? round(num(r.avg_duration), 1) : null,
        hitsPerMinute: r && num(r.seconds) ? round((num(r.hits) / num(r.seconds)) * 60, 1) : null,
        damagePerHit: r && num(r.hits) ? round(num(r.damage) / num(r.hits), 1) : null,
        ...(hitStats.get(level) || emptyHitStats),
      };
    }),
  };
}

// ---------------------------------------------------------------------------
// Effort fade (effort.js): first third vs last third of a fight, from the stored hit events
// ---------------------------------------------------------------------------

// session id -> [{ t, speed, tier, damage, defended }] for the given sessions (any status), swing speed clamped to the cap.
async function hitsForSessions(ids) {
  const map = new Map(ids.map((id) => [id, []]));
  if (!ids.length) return map;
  const { rows } = await query(
    `SELECT e.session_ref AS id,
            CASE WHEN json_typeof(e.payload->'t') = 'number' THEN (e.payload->>'t')::float8 END AS t,
            LEAST((e.payload->>'swingSpeed')::float8, ${SWING_CAP}) AS speed,
            lower(e.payload->>'tier') AS tier,
            CASE WHEN json_typeof(e.payload->'damage') = 'number' THEN (e.payload->>'damage')::float8 END AS damage,
            CASE WHEN json_typeof(e.payload->'defended') = 'boolean' THEN (e.payload->>'defended')::boolean END AS defended
     FROM events e
     WHERE e.type = 'hit' AND e.session_ref = ANY($1) AND json_typeof(e.payload->'swingSpeed') = 'number'
     ORDER BY e.occurred_at, e.seq`,
    [ids]
  );
  for (const r of rows) map.get(r.id).push({ t: num(r.t), speed: num(r.speed), tier: r.tier, damage: num(r.damage), defended: r.defended });
  return map;
}

// GET /api/sessions/:id/fade -> the fade summary plus the swing series the chart draws. null when the session is unknown.
async function sessionFade(id) {
  const { rows } = await query(`SELECT id, player_name AS "playerName", level, status, outcome, duration_sec AS "durationSec" FROM sessions WHERE id = $1`, [id]);
  if (!rows.length) return null;
  const s = rows[0];
  const hits = (await hitsForSessions([id])).get(id);
  return {
    sessionId: s.id,
    playerName: s.playerName,
    level: s.level,
    status: s.status,
    outcome: s.outcome,
    durationSec: num(s.durationSec),
    fade: effort.fade(hits, num(s.durationSec)),
    // Every landed swing with a fight-clock time, speeds clamped to the cap (see speedCap).
    points: hits.filter((h) => h.t !== null).map((h) => ({ t: round(h.t, 2), speed: round(h.speed, 2), tier: h.tier })),
    fullPowerSpeed: levelsInfo.fullPowerSpeed(s.level),
    speedCap: SWING_CAP,
    rule: "Fade % = (average swing speed in the last third of the fight - average in the first third) / average in the first third. Needs at least 6 landed swings, 2 in each end third.",
  };
}

// GET /api/players/:name/fade?last=10 -> the player's most recent finished fights, each with its fade, and their average.
async function playerFade(name, last = 10) {
  const n = Math.min(30, Math.max(1, Math.trunc(Number(last)) || 10));
  const { rows } = await query(
    `SELECT id, level, outcome, duration_sec AS "durationSec", started_at AS "startedAt"
     FROM sessions WHERE player_name = $1 AND status = 'finished' ORDER BY started_at DESC, seq DESC LIMIT $2`,
    [name, n]
  );
  if (!rows.length) {
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  const hits = await hitsForSessions(rows.map((r) => r.id));
  const fights = rows.map((r) => ({
    sessionId: r.id,
    level: r.level,
    outcome: r.outcome,
    startedAt: new Date(r.startedAt).toISOString(),
    fade: effort.fade(hits.get(r.id), num(r.durationSec)),
  }));
  return { playerName: name, last: n, average: effort.averageFade(fights.map((f) => f.fade)), fights };
}

// GET /api/players/:name/fingerprint -> five 0-100 axes (formulas in effort.js) over the player's last 10 finished fights that
// have swing telemetry. Hits taken counts player_hurt ticks with damage, minus dragon fire (a hazard, not a monster attack).
const FINGERPRINT_FIGHTS = 10;
async function playerFingerprint(name) {
  const { rows } = await query(
    `SELECT s.id, s.level, s.outcome, s.duration_sec AS "durationSec", s.started_at AS "startedAt"
     FROM sessions s
     WHERE s.player_name = $1 AND s.status = 'finished'
       AND EXISTS (SELECT 1 FROM events e WHERE e.session_ref = s.id AND e.type = 'hit' AND json_typeof(e.payload->'swingSpeed') = 'number')
     ORDER BY s.started_at DESC, s.seq DESC LIMIT $2`,
    [name, FINGERPRINT_FIGHTS]
  );
  if (!rows.length) {
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  const ids = rows.map((r) => r.id);
  const hitsBySession = await hitsForSessions(ids);
  const counts = ids.length
    ? (await query(
        `SELECT session_ref AS id,
                count(*) FILTER (WHERE type = 'monster_attack')::int AS attacks,
                count(*) FILTER (WHERE type = 'player_hurt' AND json_typeof(payload->'amount') = 'number' AND (payload->>'amount')::float8 > 0
                                   AND COALESCE(payload->>'attack', '') <> 'Dragon fire')::int AS hurt
         FROM events WHERE session_ref = ANY($1) GROUP BY session_ref`,
        [ids]
      )).rows
    : [];
  const fades = rows.map((r) => effort.fade(hitsBySession.get(r.id), num(r.durationSec)));
  const result = effort.fingerprint({
    hits: ids.flatMap((id) => hitsBySession.get(id)),
    fades,
    hurt: counts.reduce((a, c) => a + c.hurt, 0),
    attacks: counts.reduce((a, c) => a + c.attacks, 0),
  });
  return {
    playerName: name,
    fights: rows.length,
    window: `The last ${FINGERPRINT_FIGHTS} finished fights with swing data${rows.length ? ` (${rows.length} found)` : ""}.`,
    ...result,
    averageFade: effort.averageFade(fades),
    speedCap: SWING_CAP,
  };
}

// ---------------------------------------------------------------------------
// CSV export
// ---------------------------------------------------------------------------

const csvCell = (v) => {
  if (v === null || v === undefined) return "";
  const s = typeof v === "object" ? JSON.stringify(v) : String(v);
  return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
};
const toCsv = (cols, rows) => [cols.join(","), ...rows.map((r) => cols.map((c) => csvCell(r[c])).join(","))].join("\n") + "\n";

const SESSION_CSV_COLS = [
  "id", "playerName", "level", "status", "outcome", "score", "xp", "damageDealt", "hitCount", "fullSwings",
  "tapCount", "durationSec", "playerHpRemaining", "monsterHpRemaining", "startedAt", "endedAt", "source", "notes",
  "enjoyment", "rpe", "feedbackComment", // post-session survey, blank when unrated
];
const EVENT_PAYLOAD_COLS = ["t", "damage", "swingSpeed", "tier", "hand", "defended", "score", "xp", "hits",
  "playerHp", "playerMaxHp", "monsterHp", "monsterMaxHp", "attack", "category", "amount", "blocked", "timeLeft"];
const EVENT_CSV_COLS = ["id", "at", "sessionId", "playerName", "level", "type", "message", ...EVENT_PAYLOAD_COLS, "payload"];

async function exportSessionsCsv(filters) {
  const params = [];
  const where = sessionWhere(filters, params);
  const { rows } = await query(
    `SELECT ${SESSION_API_SELECT} FROM sessions s ${where.length ? `WHERE ${where.join(" AND ")}` : ""} ORDER BY s.started_at, s.seq`,
    params
  );
  const ratings = await feedback.feedbackBySession();
  const flat = rows.map(fromRow).map((s) => {
    const f = ratings.get(s.id);
    return { ...s, enjoyment: f ? f.enjoyment : null, rpe: f ? f.rpe : null, feedbackComment: f ? f.comment : null };
  });
  return { csv: toCsv(SESSION_CSV_COLS, flat), count: rows.length };
}

async function exportEventsCsv(filters) {
  const params = [];
  const where = sessionWhere(filters, params);
  if (filters.type) where.push(`ev."type" = $${params.push(String(filters.type))}`);
  // Subquery keeps EVENT_SELECT's unqualified column names unambiguous next to sessions.
  const { rows } = await query(
    `SELECT ev.*, s.level AS level
     FROM (SELECT ${EVENT_SELECT}, session_ref AS _ref, occurred_at AS _occ, seq AS _seq FROM events) ev
     JOIN sessions s ON s.id = ev._ref
     ${where.length ? `WHERE ${where.join(" AND ")}` : ""}
     ORDER BY ev._occ, ev._seq LIMIT 200000`,
    params
  );
  const flat = rows.map((row) => {
    const { _ref, _occ, _seq, ...rest } = row;
    const e = fromRow(rest);
    const p = e.payload && typeof e.payload === "object" ? e.payload : {};
    return { ...e, ...Object.fromEntries(EVENT_PAYLOAD_COLS.map((k) => [k, p[k]])), payload: e.payload };
  });
  return { csv: toCsv(EVENT_CSV_COLS, flat), count: rows.length };
}

module.exports = {
  playerInsights,
  playerChecklist,
  overview,
  publicStats,
  playerProfile,
  playerEngagement,
  playerAchievements,
  sessionPersonalBests,
  researchAnalytics,
  calorieInsights,
  sessionFitness,
  sessionFade,
  playerFade,
  playerFingerprint,
  hitsForSessions,
  exportSessionsCsv,
  exportEventsCsv,
  TIERS,
};
