// Daily goals: database access (table daily_goals, see schema.sql). Parameterised queries only.
// The pure rules live in goals.js. Nothing here waits on the network and every function is safe to call
// defensively: server.js wraps the "after a session" evaluation in a catch so a goal problem can never
// break session saving.

const { query } = require("./db");
const { SESSION_API_SELECT, fromRow } = require("./columns");
const goals = require("./goals");
const fitness = require("./fitness");

const HISTORY_MAX_DAYS = 90;
const PRESET_CACHE_MS = 10 * 60_000;

const toApi = (r) =>
  r && {
    playerName: r.player_name,
    date: typeof r.goal_date === "string" ? r.goal_date : fitness.dayFromNum(Math.round(new Date(r.goal_date).getTime() / 864e5)),
    metric: r.metric,
    target: Number(r.target),
    createdAt: new Date(r.created_at).toISOString(),
    updatedAt: new Date(r.updated_at).toISOString(),
    completedAt: r.completed_at ? new Date(r.completed_at).toISOString() : null,
    bonusXp: Number(r.bonus_xp) || 0,
  };

// goal_date is read as text so no time zone can shift it.
const GOAL_SELECT = `player_name, goal_date::text AS goal_date, metric, target, created_at, updated_at, completed_at, bonus_xp`;

const zoneOf = (tz) => goals.safeZone(tz || goals.DEFAULT_TZ);
const todayIn = (zone, now = new Date()) => goals.dayIn(now, zone);

async function getGoal(name, day) {
  const { rows } = await query(`SELECT ${GOAL_SELECT} FROM daily_goals WHERE player_name = $1 AND goal_date = $2::date`, [name, day]);
  return rows.length ? toApi(rows[0]) : null;
}

// One round trip: the goal rows, the finished / abandoned sessions that STARTED in [fromDay, toDay] (local days), and their hit
// events, as JSON. UTC bounds are widened by a day each side, then the exact local day is checked in JS (dayIn uses the same
// zone code the streak uses).
async function fetchRange(name, fromDay, toDay, zone) {
  const lo = new Date(new Date(`${fromDay}T00:00:00Z`).getTime() - 864e5).toISOString();
  const hi = new Date(new Date(`${toDay}T00:00:00Z`).getTime() + 2 * 864e5).toISOString();
  const { rows } = await query(
    `WITH g AS (
       SELECT ${GOAL_SELECT} FROM daily_goals WHERE player_name = $1 AND goal_date >= $2::date AND goal_date <= $3::date
     ), s AS (
       SELECT ${SESSION_API_SELECT} FROM sessions
       WHERE player_name = $1 AND status <> 'live' AND started_at >= $4 AND started_at < $5
     ), h AS (
       SELECT e.session_ref AS id, lower(e.payload->>'tier') AS tier,
              (e.payload->>'swingSpeed')::float8 AS speed,
              CASE WHEN json_typeof(e.payload->'t') = 'number' THEN (e.payload->>'t')::float8 END AS t
       FROM events e
       WHERE e.type = 'hit' AND json_typeof(e.payload->'swingSpeed') = 'number'
         AND e.session_ref IN (SELECT id FROM s)
     )
     SELECT COALESCE((SELECT json_agg(row_to_json(g)) FROM g), '[]'::json) AS goals,
            COALESCE((SELECT json_agg(row_to_json(s)) FROM s), '[]'::json) AS sessions,
            COALESCE((SELECT json_agg(row_to_json(h)) FROM h), '[]'::json) AS hits`,
    [name, fromDay, toDay, lo, hi]
  );
  const sessions = rows[0].sessions.map(fromRow).filter((x) => {
    const d = goals.dayIn(new Date(x.startedAt), zone);
    return d >= fromDay && d <= toDay;
  });
  const hitsBySession = new Map();
  for (const h of rows[0].hits) {
    if (!hitsBySession.has(h.id)) hitsBySession.set(h.id, []);
    hitsBySession.get(h.id).push({ tier: h.tier, speed: h.speed === null ? null : Number(h.speed), t: h.t === null ? null : Number(h.t) });
  }
  const byDay = new Map();
  for (const x of sessions) {
    const d = goals.dayIn(new Date(x.startedAt), zone);
    if (!byDay.has(d)) byDay.set(d, []);
    byDay.get(d).push(x);
  }
  return { goalRows: rows[0].goals, byDay, hitsBySession };
}

async function totalsForDay(name, day, zone) {
  const { byDay, hitsBySession } = await fetchRange(name, day, day, zone);
  return goals.dayTotals(byDay.get(day) || [], hitsBySession);
}

// Preset suggestions from every player's real daily totals. Stale-while-revalidate: a cached value (any age) is returned
// at once and refreshed in the background once it is older than PRESET_CACHE_MS, so goal/today never waits for the heavy
// aggregate except on the very first call after boot (which server.js pre-warms). Never throws: any failure returns the
// fixed fallbacks.
let presetCache = null;
let presetRefreshing = null;
async function computePresets(zone) {
  try {
    const { rows } = await query(`SELECT ${SESSION_API_SELECT} FROM sessions WHERE status <> 'live' ORDER BY started_at, seq`);
    const sessions = rows.map(fromRow);
    const hits = await query(
      `SELECT session_ref AS id, lower(payload->>'tier') AS tier,
              (payload->>'swingSpeed')::float8 AS speed,
              CASE WHEN json_typeof(payload->'t') = 'number' THEN (payload->>'t')::float8 END AS t
       FROM events WHERE type = 'hit' AND json_typeof(payload->'swingSpeed') = 'number'`
    );
    const hitsBySession = new Map();
    for (const h of hits.rows) {
      if (!hitsBySession.has(h.id)) hitsBySession.set(h.id, []);
      hitsBySession.get(h.id).push({ tier: h.tier, speed: h.speed === null ? null : Number(h.speed), t: h.t === null ? null : Number(h.t) });
    }
    const perPlayerDay = new Map();
    for (const s of sessions) {
      const d = goals.dayIn(new Date(s.startedAt), zone);
      if (!perPlayerDay.has(s.playerName)) perPlayerDay.set(s.playerName, new Map());
      const days = perPlayerDay.get(s.playerName);
      if (!days.has(d)) days.set(d, []);
      days.get(d).push(s);
    }
    const dailyByPlayer = new Map();
    for (const [player, days] of perPlayerDay) {
      dailyByPlayer.set(player, [...days.entries()].map(([day, list]) => ({ day, ...goals.dayTotals(list, hitsBySession) })));
    }
    return goals.suggestPresets(dailyByPlayer);
  } catch (err) {
    console.error("[goals] presets failed, using fixed fallbacks:", err.message);
    return goals.suggestPresets(new Map());
  }
}
function refreshPresets(zone) {
  if (!presetRefreshing) {
    presetRefreshing = computePresets(zone).then((value) => { presetCache = { zone, at: Date.now(), value }; }).finally(() => { presetRefreshing = null; });
  }
  return presetRefreshing;
}
async function presets(zone) {
  if (!presetCache || presetCache.zone !== zone) { await refreshPresets(zone); return presetCache.value; }
  if (Date.now() - presetCache.at > PRESET_CACHE_MS) refreshPresets(zone);   // background; this call keeps the cached value
  return presetCache.value;
}

const stepsFor = () => Object.fromEntries(goals.METRICS.map((m) => [m, goals.TARGET_RANGE[m]]));

// goal/today: cheap (one goal row, one sessions query, one hits query for today's sessions, cached presets).
async function todayState(name, { tz } = {}) {
  const zone = zoneOf(tz);
  const day = todayIn(zone);
  const [range, sug] = await Promise.all([fetchRange(name, day, day, zone), presets(zone)]);
  const goalRow = range.goalRows.find((r) => r.goal_date === day);
  const goal = goalRow ? toApi(goalRow) : null;
  const totals = goals.dayTotals(range.byDay.get(day) || [], range.hitsBySession);
  const progress = goal ? goals.progressFor(goal, totals) : null;
  return {
    ok: true,
    playerName: name,
    date: day,
    timeZone: zone,
    goal,
    progress: progress && { ...progress, metric: goal.metric },
    percent: progress ? progress.percent : 0,
    completed: Boolean(goal && goal.completedAt),
    bonusXp: goal ? goal.bonusXp : 0,
    totals,                                      // today's totals for every metric (the chips show these)
    suggestions: { source: sug.source, note: sug.note, playersUsed: sug.playersUsed, presets: sug.presets },
    ranges: stepsFor(),
    bonusXpOnCompletion: goals.BONUS_XP,
    estimateNote: "kcal is an estimate (MET method), not a measurement. Goals have no login: anyone who knows a player's name can set it.",
  };
}

// PUT: sets or updates today's goal. A goal that was already met today is not changed (that would let it be
// re-met and re-awarded). Returns { status, body }.
async function setGoal(name, body, { tz } = {}) {
  const n = goals.validateName(name);
  if (!n.ok) return { status: 400, body: { ok: false, error: n.error } };
  const g = goals.validateGoal(body);
  if (!g.ok) return { status: 400, body: { ok: false, error: g.error } };
  const zone = zoneOf(tz);
  const day = todayIn(zone);
  const existing = await getGoal(n.value, day);
  if (existing && existing.completedAt) {
    return { status: 409, body: { ok: false, error: "Today's goal is already reached. Set a new one tomorrow.", goal: existing } };
  }
  const { rows } = await query(
    `INSERT INTO daily_goals (player_name, goal_date, metric, target) VALUES ($1, $2::date, $3, $4)
     ON CONFLICT (player_name, goal_date) DO UPDATE SET metric = EXCLUDED.metric, target = EXCLUDED.target, updated_at = now()
     WHERE daily_goals.completed_at IS NULL
     RETURNING ${GOAL_SELECT}, (xmax = 0) AS inserted`,
    [n.value, day, g.value.metric, g.value.target]
  );
  if (!rows.length) return { status: 409, body: { ok: false, error: "Today's goal is already reached. Set a new one tomorrow." } };
  const goal = toApi(rows[0]);
  // The new target may already be met by what was played today: evaluate at once (idempotent).
  const done = await evaluate(n.value, day, zone);
  return { status: rows[0].inserted ? 201 : 200, body: { ok: true, created: rows[0].inserted, goal: done.goal || goal, completedNow: done.completedNow, bonusXp: done.completedNow ? goals.BONUS_XP : 0 }, goal: done.goal || goal, completedNow: done.completedNow };
}

// Evaluates the goal of (name, day) once. The conditional UPDATE (completed_at IS NULL) is the idempotency guard: however
// many times this runs, only one call ever flips it and awards the bonus.
async function evaluate(name, day, zone) {
  const goal = await getGoal(name, day);
  if (!goal) return { goal: null, completedNow: false };
  if (goal.completedAt) return { goal, completedNow: false };
  const totals = await totalsForDay(name, day, zone);
  const verdict = goals.evaluateCompletion(goal, totals);
  if (!verdict.complete) return { goal, completedNow: false, totals };
  const { rows } = await query(
    `UPDATE daily_goals SET completed_at = now(), bonus_xp = $3, updated_at = now()
     WHERE player_name = $1 AND goal_date = $2::date AND completed_at IS NULL
     RETURNING ${GOAL_SELECT}`,
    [name, day, verdict.bonusXp]
  );
  if (!rows.length) return { goal: await getGoal(name, day), completedNow: false, totals }; // another request won
  return { goal: toApi(rows[0]), completedNow: true, totals };
}

// Called after a session finished (and from goal/today reads is NOT needed: reads stay read-only; defensive
// evaluation happens in the achievements and the explicit evaluateToday below).
async function evaluateForSession(session, { tz } = {}) {
  if (!session || session.status === "live") return { goal: null, completedNow: false };
  const zone = zoneOf(tz);
  const day = goals.dayIn(new Date(session.startedAt || Date.now()), zone);
  return { ...(await evaluate(session.playerName, day, zone)), day };
}

async function evaluateToday(name, { tz } = {}) {
  const zone = zoneOf(tz);
  return evaluate(name, todayIn(zone), zone);
}

// goals?days=30: one row per local day from today back, with the goal (if any), its progress and whether it was met.
async function history(name, days, { tz } = {}) {
  const n = Math.min(HISTORY_MAX_DAYS, Math.max(1, Math.trunc(Number(days)) || 30));
  const zone = zoneOf(tz);
  const today = todayIn(zone);
  const fromDay = fitness.dayFromNum(fitness.dayNum(today) - (n - 1));
  const { goalRows, byDay, hitsBySession } = await fetchRange(name, fromDay, today, zone);
  const byDate = new Map(goalRows.map((r) => [r.goal_date, toApi(r)]));
  const out = [];
  for (let i = 0; i < n; i++) {
    const day = fitness.dayFromNum(fitness.dayNum(fromDay) + i);
    const goal = byDate.get(day) || null;
    const totals = goals.dayTotals(byDay.get(day) || [], hitsBySession);
    const progress = goal ? goals.progressFor(goal, totals) : null;
    out.push({
      date: day,
      goal: goal && { metric: goal.metric, target: goal.target },
      progress: progress && { value: progress.value, percent: progress.percent },
      met: Boolean(goal && goal.completedAt),
      completedAt: goal ? goal.completedAt : null,
      bonusXp: goal ? goal.bonusXp : 0,
      sessions: totals.sessions,
    });
  }
  const metDays = out.filter((d) => d.met).map((d) => d.date);
  return { ok: true, playerName: name, timeZone: zone, days: out, daysMet: metDays.length, goalStreak: goals.goalStreak(metDays, today), bonusXpTotal: out.reduce((a, d) => a + d.bonusXp, 0) };
}

// For the achievements response: every day this player met a goal, and the bonus XP so far.
async function metSummary(name) {
  const { rows } = await query(
    `SELECT goal_date::text AS day, bonus_xp FROM daily_goals WHERE player_name = $1 AND completed_at IS NOT NULL ORDER BY goal_date`,
    [name]
  );
  return { days: rows.map((r) => r.day), bonusXp: rows.reduce((a, r) => a + (Number(r.bonus_xp) || 0), 0) };
}

// Overview: how many players met today's goal (and how many set one).
async function overviewToday({ tz } = {}) {
  const zone = zoneOf(tz);
  const day = todayIn(zone);
  const { rows } = await query(
    `SELECT count(*)::int AS set_count, count(*) FILTER (WHERE completed_at IS NOT NULL)::int AS met_count
     FROM daily_goals WHERE goal_date = $1::date`,
    [day]
  );
  return { date: day, timeZone: zone, goalsSet: rows[0].set_count, goalsMet: rows[0].met_count };
}

async function exportCsv(filters = {}) {
  const params = [];
  const where = [];
  if (filters.player) where.push(`player_name = $${params.push(String(filters.player))}`);
  if (filters.from && /^\d{4}-\d{2}-\d{2}$/.test(filters.from)) where.push(`goal_date >= $${params.push(filters.from)}::date`);
  if (filters.to && /^\d{4}-\d{2}-\d{2}$/.test(filters.to)) where.push(`goal_date <= $${params.push(filters.to)}::date`);
  const { rows } = await query(
    `SELECT ${GOAL_SELECT} FROM daily_goals ${where.length ? `WHERE ${where.join(" AND ")}` : ""} ORDER BY goal_date DESC, player_name`,
    params
  );
  const esc = (v) => {
    const s = v === null || v === undefined ? "" : String(v);
    return /^[=+\-@]/.test(s) || /[",\n]/.test(s) ? `"${(/^[=+\-@]/.test(s) ? "'" + s : s).replace(/"/g, '""')}"` : s;
  };
  const head = ["player", "date", "metric", "target", "created_at", "updated_at", "completed_at", "bonus_xp"];
  const lines = [head.join(",")];
  for (const r of rows) {
    const g = toApi(r);
    lines.push([g.playerName, g.date, g.metric, g.target, g.createdAt, g.updatedAt, g.completedAt, g.bonusXp].map(esc).join(","));
  }
  return { csv: lines.join("\n") + "\n", count: rows.length };
}

function clearPresetCache() { presetCache = null; }

module.exports = { getGoal, todayState, setGoal, evaluate, evaluateForSession, evaluateToday, history, metSummary, overviewToday, exportCsv, presets, refreshPresets, clearPresetCache, totalsForDay, fetchRange };
