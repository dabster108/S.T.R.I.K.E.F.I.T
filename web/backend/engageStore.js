// Engagement data access: challenges (table challenge_completions), calendar, weekly recap, best run, public challenge
// page. Parameterised queries only. The rules live in challenges.js and engage.js.
//
// Every query is one round trip on existing indexes: sessions by player_name / started_at / id, events by session_ref.
// The heavy all-players aggregate (challenge target medians) is cached and refreshed in the background.

const { query } = require("./db");
const { SESSION_API_SELECT, fromRow } = require("./columns");
const fitness = require("./fitness");
const goals = require("./goals");
const challenges = require("./challenges");
const engage = require("./engage");

const ZONE = challenges.safeZone(challenges.DEFAULT_TZ); // day and week boundaries: the streak / goal constant
const HISTORY_MAX_DAYS = 90;
const MEDIAN_CACHE_MS = 10 * 60_000;

const isoDayStart = (day, shiftDays = 0) => new Date(Date.parse(`${day}T00:00:00Z`) + shiftDays * 864e5).toISOString();

// ---------------------------------------------------------------------------
// Session facts: finished sessions with their hit / evidence aggregates (one query)
// ---------------------------------------------------------------------------
// filters: { player, level, ids, fromIso, toIso } (started_at in [fromIso, toIso)). -> engage.sessionFacts rows
async function loadFacts({ player, level, ids, fromIso, toIso } = {}, zone = ZONE) {
  const params = [];
  const where = ["status <> 'live'"];
  if (player) where.push(`player_name = $${params.push(String(player))}`);
  if (level) where.push(`level = $${params.push(String(level))}`);
  if (ids) where.push(`id = ANY($${params.push(ids.map(String))})`);
  if (fromIso) where.push(`started_at >= $${params.push(fromIso)}`);
  if (toIso) where.push(`started_at < $${params.push(toIso)}`);
  const { rows } = await query(
    `WITH s AS (
       SELECT ${SESSION_API_SELECT}, live_state AS "liveState" FROM sessions WHERE ${where.join(" AND ")}
     ), h AS (
       SELECT e.session_ref AS id,
              count(*) FILTER (WHERE lower(e.payload->>'tier') = 'tap')::int AS tap,
              count(*) FILTER (WHERE lower(e.payload->>'tier') = 'light')::int AS light,
              count(*) FILTER (WHERE lower(e.payload->>'tier') = 'medium')::int AS medium,
              count(*) FILTER (WHERE lower(e.payload->>'tier') = 'full')::int AS full,
              count(DISTINCT CASE WHEN json_typeof(e.payload->'t') = 'number' AND (e.payload->>'t')::float8 >= 0
                                  THEN floor((e.payload->>'t')::float8 / 60) END)::int AS minutes,
              max((e.payload->>'swingSpeed')::float8) AS peak
       FROM events e
       WHERE e.type = 'hit' AND json_typeof(e.payload->'swingSpeed') = 'number' AND e.session_ref IN (SELECT id FROM s)
       GROUP BY e.session_ref
     ), x AS (
       SELECT e.session_ref AS id,
              max(CASE WHEN json_typeof(e.payload->'combo') = 'number' THEN (e.payload->>'combo')::float8 END) AS combo,
              max(CASE WHEN json_typeof(e.payload->'playerMaxHp') = 'number' THEN (e.payload->>'playerMaxHp')::float8 END) AS maxhp,
              count(*) FILTER (WHERE e.type = 'player_hurt')::int AS hurt
       FROM events e
       WHERE e.type IN ('hit', 'hp', 'player_hurt') AND e.session_ref IN (SELECT id FROM s)
       GROUP BY e.session_ref
     )
     SELECT s.*, h.tap, h.light, h.medium, h.full, h.minutes, h.peak, x.combo, x.maxhp, x.hurt, (x.id IS NOT NULL) AS evented
     FROM s LEFT JOIN h ON h.id = s.id LEFT JOIN x ON x.id = s.id
     ORDER BY s."startedAt"`,
    params
  );
  return rows.map((r) => {
    const { tap, light, medium, full, minutes, peak, combo, maxhp, hurt, evented, liveState, ...rest } = r;
    const session = fromRow(rest);
    return engage.sessionFacts({ ...session, liveState, tap, light, medium, full, minutes, peak: peak === null ? null : Number(peak), combo: combo === null ? null : Number(combo), maxhp: maxhp === null ? null : Number(maxhp), hurt, evented }, zone);
  });
}

// Facts whose LOCAL start day is within [fromDay, toDay]. UTC bounds are widened by a day each side, then the exact
// local day is checked (the goal code's approach).
async function factsForDays(filters, fromDay, toDay, zone = ZONE) {
  const facts = await loadFacts({ ...filters, fromIso: isoDayStart(fromDay, -1), toIso: isoDayStart(toDay, 2) }, zone);
  return facts.filter((f) => f.day >= fromDay && f.day <= toDay);
}

// ---------------------------------------------------------------------------
// Challenge target medians: every player's daily totals, cached (stale-while-revalidate, never throws)
// ---------------------------------------------------------------------------
let medianCache = null;
let medianRefreshing = null;
function refreshMedianRows() {
  if (!medianRefreshing) {
    medianRefreshing = loadFacts({})
      .then((facts) => { medianCache = { at: Date.now(), rows: engage.dailyRows(facts) }; })
      .catch((err) => { console.error("[challenges] median rows failed, using fixed targets:", err.message); if (!medianCache) medianCache = { at: Date.now(), rows: [] }; })
      .finally(() => { medianRefreshing = null; });
  }
  return medianRefreshing;
}
async function medianRows() {
  if (!medianCache) await refreshMedianRows();
  else if (Date.now() - medianCache.at > MEDIAN_CACHE_MS) refreshMedianRows();
  return medianCache.rows;
}
function clearMedianCache() { medianCache = null; }

// ---------------------------------------------------------------------------
// Challenges
// ---------------------------------------------------------------------------
const COMPLETION_SELECT = `player_name, challenge_key, challenge_id, period, period_start::text AS period_start, title, description, target, completed_at, bonus_xp`;
const completionToApi = (r) => ({
  playerName: r.player_name,
  key: r.challenge_key,
  id: r.challenge_id,
  period: r.period,
  periodStart: r.period_start,
  title: r.title,
  description: r.description,
  target: r.target === null ? null : Number(r.target),
  completedAt: new Date(r.completed_at).toISOString(),
  bonusXp: Number(r.bonus_xp) || 0,
});

async function activeDaysSince(name, sinceDay) {
  const { rows } = await query(`SELECT started_at FROM sessions WHERE player_name = $1 AND status <> 'live' AND started_at >= $2`, [name, isoDayStart(sinceDay, -1)]);
  return [...new Set(rows.map((r) => fitness.dayIn(new Date(r.started_at), ZONE)))];
}

// Records the met-but-unrecorded cards. INSERT ... ON CONFLICT DO NOTHING is the idempotency guard: however many
// requests race here, each (player, challenge_key) row is created once and only that request gets it back.
async function recordCompletions(name, cards) {
  const fresh = challenges.newlyMet(cards);
  if (!fresh.length) return [];
  const params = [];
  const values = fresh.map((c) => {
    const p = (v) => `$${params.push(v)}`;
    return `(${p(name)}, ${p(c.key)}, ${p(c.id)}, ${p(c.period)}, ${p(c.periodStart)}::date, ${p(c.title)}, ${p(c.description)}, ${p(c.target)}, ${p(c.bonusXp)})`;
  });
  const { rows } = await query(
    `INSERT INTO challenge_completions (player_name, challenge_key, challenge_id, period, period_start, title, description, target, bonus_xp)
     VALUES ${values.join(", ")}
     ON CONFLICT (player_name, challenge_key) DO NOTHING
     RETURNING ${COMPLETION_SELECT}`,
    params
  );
  return rows.map(completionToApi);
}

// The challenge cards for the periods containing `at`, with progress computed on read. Met cards are recorded
// (idempotently); `onCompleted(list)` gets only the completions this call created.
async function challengeState(name, { at = new Date(), onCompleted } = {}) {
  const periods = challenges.periodsAt(at, ZONE);
  const rot = challenges.rotationFor(periods.day);
  const list = [...rot.daily, ...rot.weekly];
  const keys = list.map((c) => challenges.challengeKey(c.id, challenges.periodStartFor(c, periods)));
  const restFrom = fitness.dayFromNum(fitness.dayNum(periods.day) - 30);
  const [facts, done, rows, restDays] = await Promise.all([
    factsForDays({ player: name }, periods.weekStart, periods.weekEnd),
    query(`SELECT ${COMPLETION_SELECT} FROM challenge_completions WHERE player_name = $1 AND challenge_key = ANY($2)`, [name, keys]).then((r) => r.rows.map(completionToApi)),
    medianRows(),
    activeDaysSince(name, restFrom),
  ]);
  const medians = challenges.mediansFor(rows, periods);
  const completions = new Map(done.map((c) => [c.key, c]));
  const factsFor = (c) => (c.period === "weekly" ? facts : facts.filter((f) => f.day === periods.day));
  let cards = list.map((c) => challenges.evaluate(c, factsFor(c), { periods, medians, completions }));
  const created = await recordCompletions(name, cards);
  if (created.length) {
    const byKey = new Map(created.map((c) => [c.key, c]));
    cards = cards.map((c) => (byKey.has(c.key) ? { ...c, completed: true, completedAt: byKey.get(c.key).completedAt, completedNow: true } : c));
    if (onCompleted) onCompleted(created);
  }
  const today = fitness.dayIn(new Date(), ZONE);
  return {
    ok: true,
    playerName: name,
    timeZone: ZONE,
    date: periods.day,
    weekStart: periods.weekStart,
    weekEnd: periods.weekEnd,
    daily: cards.filter((c) => c.period === "daily"),
    weekly: cards.filter((c) => c.period === "weekly"),
    targets: {
      daily: { source: medians.daily.source, note: medians.daily.note, playersUsed: medians.daily.playersUsed },
      weekly: { source: medians.weekly.source, note: medians.weekly.note, playersUsed: medians.weekly.playersUsed },
      caps: challenges.SAFETY_CAPS,
    },
    rest: challenges.restNote(restDays, today),
    safety: challenges.SAFETY_LINE,
    estimateNote: "kcal is an estimate (MET method), not a measurement. Progress is computed from finished sessions that started in the period.",
  };
}

// After a session finished: evaluate the periods it STARTED in (late finishes after midnight still count for that day).
async function evaluateForSession(session, onCompleted) {
  if (!session || session.status === "live") return [];
  let created = [];
  await challengeState(session.playerName, { at: new Date(session.startedAt || Date.now()), onCompleted: (list) => { created = list; } });
  if (created.length && onCompleted) onCompleted(created);
  return created;
}

async function challengeHistory(name, days) {
  const n = Math.min(HISTORY_MAX_DAYS, Math.max(1, Math.trunc(Number(days)) || 30));
  const today = fitness.dayIn(new Date(), ZONE);
  const fromDay = fitness.dayFromNum(fitness.dayNum(today) - (n - 1));
  const [{ rows }, totals] = await Promise.all([
    query(`SELECT ${COMPLETION_SELECT} FROM challenge_completions WHERE player_name = $1 AND completed_at >= $2 ORDER BY completed_at DESC LIMIT 200`, [name, isoDayStart(fromDay, -1)]),
    completionSummary(name),
  ]);
  const list = rows.map(completionToApi).filter((c) => fitness.dayIn(new Date(c.completedAt), ZONE) >= fromDay);
  return { ok: true, playerName: name, timeZone: ZONE, days: n, from: fromDay, to: today, completions: list, completedInRange: list.length, bonusXpInRange: list.reduce((a, c) => a + c.bonusXp, 0), totalCompleted: totals.count, totalBonusXp: totals.bonusXp };
}

async function completionSummary(name) {
  const { rows } = await query(`SELECT count(*)::int AS n, COALESCE(sum(bonus_xp), 0)::int AS xp FROM challenge_completions WHERE player_name = $1`, [name]);
  return { count: rows[0].n, bonusXp: rows[0].xp };
}

// ---------------------------------------------------------------------------
// Calendar and weekly recap
// ---------------------------------------------------------------------------
async function calendar(name, weeks) {
  const w = engage.clampWeeks(weeks);
  const today = fitness.dayIn(new Date(), ZONE);
  const from = fitness.dayFromNum(fitness.dayNum(challenges.periodsAt(new Date(), ZONE).weekStart) - (w - 1) * 7);
  const facts = await factsForDays(name ? { player: name } : {}, from, today);
  return { ok: true, scope: name ? "player" : "site", playerName: name || null, timeZone: ZONE, ...engage.calendarGrid(engage.byDayOf(facts, { countPlayers: !name }), today, w), estimateNote: "kcal is an estimate (MET method), not a measurement; days with no recorded swings have no kcal." };
}

// offset 0 = this week (so far), 1 = last week. previous = the week before the chosen one; compare only when it has sessions.
async function week(name, offset = 0) {
  const off = Math.min(1, Math.max(0, Math.trunc(Number(offset)) || 0));
  const thisWeek = challenges.periodsAt(new Date(), ZONE).weekStart;
  const start = fitness.dayFromNum(fitness.dayNum(thisWeek) - off * 7);
  const prevStart = fitness.dayFromNum(fitness.dayNum(start) - 7);
  const facts = await factsForDays({ player: name }, prevStart, fitness.dayFromNum(fitness.dayNum(start) + 6));
  const current = engage.weekSummary(facts, start);
  const previous = engage.weekSummary(facts, prevStart);
  return { ok: true, playerName: name, timeZone: ZONE, offset: off, inProgress: off === 0, week: current, previous: previous.sessions ? previous : null, vsPrevious: engage.weekCompare(current, previous), estimateNote: "kcal is an estimate (MET method), not a measurement." };
}

// ---------------------------------------------------------------------------
// Best run on a level (replay ghost) and the public challenge page
// ---------------------------------------------------------------------------
// One round trip: the candidate runs, the best of them (highest score, then the shorter fight, then the earlier one; the
// same rule as engage.pickBest) and its hit / hp samples.
async function best(name, level, beforeId) {
  const { rows } = await query(
    `WITH target AS (
       SELECT id, started_at FROM sessions WHERE id = $3
     ), cand AS (
       SELECT id, outcome, score, duration_sec, started_at FROM sessions
       WHERE player_name = $1 AND level = $2 AND status <> 'live' AND outcome IN ('victory', 'defeat', 'timeout') AND score > 0
         AND ($3::text IS NULL OR (id <> $3 AND started_at < (SELECT started_at FROM target)))
     ), b AS (
       SELECT * FROM cand ORDER BY score DESC, CASE WHEN duration_sec > 0 THEN duration_sec ELSE 1e9 END, started_at LIMIT 1
     ), ev AS (
       SELECT e.type,
              CASE WHEN json_typeof(e.payload->'t') = 'number' THEN (e.payload->>'t')::float8 END AS t,
              e.occurred_at AS at,
              CASE WHEN json_typeof(e.payload->'swingSpeed') = 'number' THEN (e.payload->>'swingSpeed')::float8 END AS speed,
              lower(e.payload->>'tier') AS tier,
              CASE WHEN json_typeof(e.payload->'monsterHp') = 'number' THEN (e.payload->>'monsterHp')::float8 END AS mhp,
              CASE WHEN json_typeof(e.payload->'monsterMaxHp') = 'number' THEN (e.payload->>'monsterMaxHp')::float8 END AS mmax,
              e.seq
       FROM events e WHERE e.session_ref = (SELECT id FROM b) AND e.type IN ('hit', 'hp')
     )
     SELECT (SELECT row_to_json(b) FROM b) AS best,
            (SELECT count(*)::int FROM target) AS target_found,
            COALESCE((SELECT json_agg(ev ORDER BY ev.at, ev.seq) FROM ev), '[]'::json) AS events`,
    [name, level, beforeId || null]
  );
  const r = rows[0];
  if (beforeId && !r.target_found) return { ok: true, playerName: name, level, best: null, reason: "unknown_session" };
  if (!r.best) return { ok: true, playerName: name, level, best: null, reason: beforeId ? "no_earlier_session" : "no_session" };
  const pick = r.best;
  const t0 = new Date(pick.started_at).getTime();
  const tOf = (e) => (e.t !== null ? Number(e.t) : Math.max(0, (new Date(e.at).getTime() - t0) / 1000));
  const hits = r.events.filter((e) => e.type === "hit" && e.speed !== null).map((e) => ({ t: Math.round(tOf(e) * 100) / 100, speed: Number(e.speed), tier: e.tier }));
  const hp = r.events.filter((e) => e.mhp !== null && e.mmax > 0).map((e) => ({ t: Math.round(tOf(e) * 100) / 100, f: Math.max(0, Math.min(1, e.mhp / e.mmax)) })).sort((a, b) => a.t - b.t);
  return {
    ok: true,
    playerName: name,
    level,
    best: { id: pick.id, outcome: pick.outcome, score: Number(pick.score) || 0, durationSec: Number(pick.duration_sec) || 0, startedAt: new Date(pick.started_at).toISOString() },
    samples: { hits, hp },
    reason: hits.length || hp.length > 1 ? null : "no_events",
    rule: beforeId ? "Highest score among this player's earlier finished runs on this level." : "Highest score among this player's finished runs on this level.",
  };
}

async function challengeCard(sessionId) {
  // Both in parallel: the status (a live fight has no final score yet) and the finished-session facts.
  const [{ rows }, facts] = await Promise.all([query(`SELECT status FROM sessions WHERE id = $1`, [sessionId]), loadFacts({ ids: [sessionId] })]);
  if (!rows.length) return { status: 404, body: { ok: false, error: "Session not found" } };
  if (rows[0].status === "live" || !facts.length) return { status: 409, body: { ok: false, error: "That fight is still being played." } };
  const f = facts[0];
  return {
    status: 200,
    body: {
      ok: true,
      session: { id: f.id, playerName: f.playerName, level: f.level, outcome: f.outcome, score: f.score, durationSec: f.durationSec, startedAt: f.startedAt },
      estimatedCalories: f.kcal,
      estimatedWalkingMinutes: f.walkingMinutes,
      fullSwings: f.fullSwings,
      peakSwingSpeed: f.peakSpeed,
      damagePercent: f.damagePercent,
      estimateNote: "kcal is an estimate (MET method), not a measurement.",
    },
  };
}

// Did `name` ever beat this session's score on the same level? Strictly greater; the challenged session itself never
// counts. One round trip.
async function challengeCheck(sessionId, rawName) {
  const n = goals.validateName(rawName);
  if (!n.ok) return { status: 400, body: { ok: false, error: n.error } };
  const { rows } = await query(
    `WITH t AS (SELECT id, level, score FROM sessions WHERE id = $2),
     mine AS (
       SELECT s.id, s.score, s.started_at FROM sessions s, t
       WHERE s.player_name = $1 AND s.level = t.level AND s.status <> 'live' AND s.id <> t.id
     )
     SELECT t.level, t.score AS target,
            (SELECT count(*)::int FROM mine) AS n,
            (SELECT max(score) FROM mine) AS best,
            (SELECT id FROM mine ORDER BY score DESC, started_at LIMIT 1) AS best_id,
            (SELECT count(*)::int FROM sessions WHERE player_name = $1) AS any_sessions
     FROM t`,
    [n.value, sessionId]
  );
  if (!rows.length) return { status: 404, body: { ok: false, error: "Session not found" } };
  const r = rows[0];
  const targetScore = Number(r.target) || 0;
  const bestScore = r.best === null ? null : Number(r.best);
  return {
    status: 200,
    body: {
      ok: true,
      name: n.value,
      level: r.level,
      targetScore,
      found: r.any_sessions > 0,
      sessionsOnLevel: r.n,
      bestScore,
      bestSessionId: r.best_id,
      beaten: bestScore !== null && bestScore > targetScore,
      gap: bestScore === null ? null : Math.round(bestScore - targetScore),
    },
  };
}

module.exports = {
  ZONE, loadFacts, factsForDays, medianRows, refreshMedianRows, clearMedianCache,
  challengeState, evaluateForSession, challengeHistory, completionSummary, recordCompletions,
  calendar, week, best, challengeCard, challengeCheck,
};
