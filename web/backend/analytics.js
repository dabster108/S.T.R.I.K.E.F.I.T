// Read-only aggregates for the dashboard, the landing page and CSV export.
// Every number comes straight from the sessions/events tables; nothing is
// estimated. Swing-speed figures only exist for hits recorded by the live
// telemetry build (hit events carrying payload.swingSpeed), so each result
// reports its sample size and uses null when there is no data.

const { query } = require("./db");
const { SESSION_API_SELECT, EVENT_SELECT, fromRow } = require("./columns");
const fitness = require("./fitness");

const LEVEL_ORDER = ["Easy", "Medium", "Hard"];
const TIERS = ["tap", "light", "medium", "full"];
const LEVELS_PLAYABLE = 2; // Level 1 (Easy) and Level 2 (Medium); Level 3 is planned
const LEVELS_PLANNED = 1;
const HIST_STEP = 0.5; // m/s per histogram bucket

const num = (v) => (v === null || v === undefined || !Number.isFinite(Number(v)) ? null : Number(v));
const round = (v, d = 2) => (v === null ? null : Math.round(v * 10 ** d) / 10 ** d);
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

// One row per landed hit that carries a measured swing speed.
function hitsCte(filters, params) {
  const where = sessionWhere(filters, params);
  return `hits AS (
    SELECT e.session_ref AS session_id, s.player_name, s.level, s.status, s.duration_sec,
           CASE WHEN json_typeof(e.payload->'swingSpeed') = 'number' THEN (e.payload->>'swingSpeed')::float8 END AS speed,
           lower(e.payload->>'tier') AS tier,
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
            count(*) FILTER (WHERE tier = 'full')::int AS full, count(*) FILTER (WHERE tier IS NOT NULL)::int AS tiered
     FROM hits GROUP BY level`,
    params
  );
  return new Map(rows.map((r) => [r.level, {
    swingSamples: r.n,
    avgSwingSpeed: round(num(r.avg_speed)),
    peakSwingSpeed: round(num(r.peak_speed)),
    fullPowerRatio: r.tiered ? round(r.full / r.tiered, 4) : null,
  }]));
}

const emptyHitStats = { swingSamples: 0, avgSwingSpeed: null, peakSwingSpeed: null, fullPowerRatio: null };

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
           avg(score) FILTER (WHERE status = 'finished') AS avg_score,
           max(score) AS best_score,
           COALESCE(sum(hit_count), 0)::float8 AS swings,
           COALESCE(sum(damage_dealt), 0)::float8 AS damage,
           avg(duration_sec) FILTER (WHERE status = 'finished' AND duration_sec > 0) AS avg_duration
    FROM sessions GROUP BY level`)).rows;
  const hitStats = await levelHitStats();

  const recent = (await query(`SELECT ${SESSION_API_SELECT} FROM sessions ORDER BY started_at DESC, seq DESC LIMIT 5`)).rows.map(fromRow);
  const decided = totals.wins + totals.losses;

  return {
    totalSessions: totals.sessions,
    liveSessions: totals.live,
    totalPlayers: totals.players,
    totalSwings: totals.swings,
    totalDamage: round(totals.damage, 1),
    wins: totals.wins,
    losses: totals.losses,
    // Share of fights that ended in victory or defeat (abandoned / in-progress excluded).
    winRate: decided ? round((totals.wins / decided) * 100, 1) : null,
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
        winRate: l.wins + l.losses ? round((l.wins / (l.wins + l.losses)) * 100, 1) : null,
        avgScore: round(num(l.avg_score), 0),
        bestScore: num(l.best_score),
        swings: l.swings,
        damage: round(l.damage, 1),
        avgSessionSec: round(num(l.avg_duration), 1),
        ...(hitStats.get(l.level) || emptyHitStats),
      }))
      .sort(levelSort),
    recentSessions: recent,
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
    levelsAvailable: LEVELS_PLAYABLE,
    levelsPlanned: LEVELS_PLANNED,
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
  const sum = (list, k) => list.reduce((a, s) => a + (Number(s[k]) || 0), 0);

  const levels = [...new Set(sessions.map((s) => s.level))];
  const levelStats = await levelHitStats({ player: name });
  const byLevel = levels
    .map((level) => {
      const list = sessions.filter((s) => s.level === level);
      const done = list.filter((s) => s.status !== "live");
      const w = list.filter((s) => s.outcome === "victory").length;
      const l = list.filter((s) => s.outcome === "defeat").length;
      return {
        level,
        sessions: list.length,
        wins: w,
        losses: l,
        winRate: w + l ? round((w / (w + l)) * 100, 1) : null,
        bestScore: Math.max(0, ...list.map((s) => Number(s.score) || 0)),
        avgScore: done.length ? round(sum(done, "score") / done.length, 0) : null,
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
      winRate: wins + losses ? round((wins / (wins + losses)) * 100, 1) : null,
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
    .map((e) => ({ tier: String(e.payload.tier || "").toLowerCase(), speed: num(e.payload.swingSpeed), t: num(e.payload.t) }))
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
            avg(s.score) AS avg_score, avg(s.damage_dealt) AS avg_damage, avg(s.hit_count) AS avg_hits,
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
      const decided = r ? r.wins + r.losses : 0;
      return {
        level,
        sessions: r ? r.sessions : 0,
        winRate: decided ? round((r.wins / decided) * 100, 1) : null,
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
  return { csv: toCsv(SESSION_CSV_COLS, rows.map(fromRow)), count: rows.length };
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
  overview,
  publicStats,
  playerProfile,
  playerEngagement,
  researchAnalytics,
  sessionFitness,
  exportSessionsCsv,
  exportEventsCsv,
  TIERS,
};
