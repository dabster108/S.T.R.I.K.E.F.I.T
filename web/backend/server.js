const fs = require("fs");
const os = require("os");
const path = require("path");
const http = require("http");
const express = require("express");
const cors = require("cors");
const { Server } = require("socket.io");
const { v4: uuidv4 } = require("uuid");
const { query, withTransaction } = require("./db");
const { SESSION_API_FIELDS, SESSION_API_SELECT, EVENT_SELECT, fromRow } = require("./columns");
const stream = require("./stream");
const broadcast = require("./broadcast");
const analytics = require("./analytics");
const feedback = require("./feedback");
const feedbackCore = require("./feedbackCore");
const progression = require("./progression");
const goals = require("./goals");
const goalsStore = require("./goalsStore");
const engage = require("./engage");
const engageStore = require("./engageStore");
const activityFeed = require("./activityFeed");
const outcomes = require("./outcomes");
const featureRoutes = require("./featureRoutes");
const sessionEnd = require("./sessionEnd");

const PORT = Number(process.env.PORT) || 4000;
const FRONTEND_DIST = path.join(__dirname, "..", "frontend", "dist");
const SCHEMA_SQL = path.join(__dirname, "schema.sql");
const MAX_EVENTS = 1000;
const MAX_BATCH = 200;
const LEVEL_ORDER = ["Easy", "Medium", "Hard"];
const STARTED_AT = Date.now();

// A live session with no activity for this long is finished automatically.
const LIVE_TIMEOUT_MS = 60_000;
const SWEEP_INTERVAL_MS = 10_000;
const FINAL_OUTCOMES = new Set(["victory", "defeat", "timeout"]);

// Numeric payload keys that make up a live session's latest state. Unity's hp
// snapshots and hit events carry these; the newest value wins.
const LIVE_KEYS = [
  "playerHp", "playerMaxHp", "monsterHp", "monsterMaxHp", "xp", "score", "hits",
  "damageTotal", "fullSwings", "t", "timeLeft", "stamina", "maxStamina",
  // Sent by the game since the feel-and-fitness update; absent from older builds (the live view falls back).
  "combo", "effort", "kcal",
];

// ---------------------------------------------------------------------------
// Normalisation & stats
// ---------------------------------------------------------------------------

const num = (v) => {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
};
const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round1 = (n) => Math.round(n * 10) / 10;
const endTime = (s) => new Date(s.endedAt || s.startedAt || 0).getTime();

// Postgres stores timestamptz, so an unparseable value would fail the insert.
// ISO strings pass through untouched (keeps Unity's sub-millisecond digits);
// anything else Date understands is converted; garbage falls back to now.
const ISO_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})$/;
function toTimestamp(value, fallback) {
  if (value === undefined || value === null || value === "") return fallback;
  if (typeof value === "string" && ISO_RE.test(value) && !Number.isNaN(Date.parse(value))) return value;
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? fallback : d.toISOString();
}

function normalizeOutcome(value) {
  const v = String(value || "unknown").trim().toLowerCase();
  if (["victory", "win", "won"].includes(v)) return "victory";
  if (["defeat", "loss", "lost", "lose"].includes(v)) return "defeat";
  if (["timeout", "timed_out", "time_up"].includes(v)) return "timeout";
  return v;
}

// An explicit final outcome always finishes the session; otherwise an explicit
// status wins; otherwise the stored status; a brand-new row without a status is
// a finished battle, which is what every client posted before live sessions.
function resolveStatus(body, base, outcome) {
  if (body.outcome !== undefined && FINAL_OUTCOMES.has(outcome)) return "finished";
  const requested = String(body.status || "").trim().toLowerCase();
  if (requested === "live" || requested === "finished") return requested;
  return base.status || "finished";
}

function normalizeSession(body, existing) {
  const now = new Date().toISOString();
  const base = existing || {};
  const liveRequested = String(body.status || "").trim().toLowerCase() === "live";
  const outcome = normalizeOutcome(body.outcome ?? base.outcome ?? (liveRequested ? "in_progress" : undefined));
  const status = resolveStatus(body, base, outcome);
  return {
    id: String(body.id || base.id || uuidv4()),
    playerName: String(body.playerName || base.playerName || "Player1").trim() || "Player1",
    level: String(body.level || base.level || "Easy"),
    outcome,
    score: num(body.score ?? base.score),
    damageDealt: num(body.damageDealt ?? base.damageDealt),
    hitCount: num(body.hitCount ?? base.hitCount),
    tapCount: num(body.tapCount ?? base.tapCount),
    durationSec: num(body.durationSec ?? base.durationSec),
    playerHpRemaining: num(body.playerHpRemaining ?? base.playerHpRemaining),
    monsterHpRemaining: num(body.monsterHpRemaining ?? base.monsterHpRemaining),
    pointsEarned: num(body.pointsEarned ?? body.score ?? base.pointsEarned),
    startedAt: toTimestamp(body.startedAt || base.startedAt, now),
    endedAt: toTimestamp(status === "live" ? null : body.endedAt || (base.status === "live" ? null : base.endedAt), now),
    source: String(body.source || base.source || "unity"),
    notes: String(body.notes ?? base.notes ?? ""),
    status,
    xp: num(body.xp ?? base.xp),
    fullSwings: num(body.fullSwings ?? base.fullSwings),
    lastEventAt: now,
  };
}

function normalizeEvent(body) {
  return {
    id: uuidv4(),
    type: String(body.type || "hit"),
    sessionId: body.sessionId ? String(body.sessionId) : null,
    playerName: String(body.playerName || "Player1"),
    message: String(body.message || body.type || "event"),
    payload: body.payload ?? null,
    at: new Date().toISOString(),
  };
}

// ---------------------------------------------------------------------------
// Data access (Postgres via db.js — parameterised queries only)
// ---------------------------------------------------------------------------

// Finished sessions in insertion order — the list db.sessions used to hold, so
// computeStats / computePlayers (the legacy /api/stats and /api/players) keep
// describing completed battles only.
async function allSessions(client = { query }) {
  const { rows } = await client.query(`SELECT ${SESSION_API_SELECT} FROM sessions WHERE status <> 'live' ORDER BY seq`);
  return rows.map(fromRow);
}

// Splits the live_state column off a row so fromRow sees only API fields.
function sessionFromRow(row) {
  const { liveState, ...rest } = row;
  return { session: fromRow(rest), liveState: liveState || null };
}

const SESSION_COLUMNS = SESSION_API_FIELDS.map(([, col]) => col);
const UPSERT_SESSION_SQL = `
  INSERT INTO sessions (${SESSION_COLUMNS.join(", ")})
  VALUES (${SESSION_COLUMNS.map((_, i) => `$${i + 1}`).join(", ")})
  ON CONFLICT (id) DO UPDATE SET
    ${SESSION_COLUMNS.filter((c) => c !== "id").map((c) => `${c} = EXCLUDED.${c}`).join(",\n    ")}
  RETURNING ${SESSION_API_SELECT}, live_state AS "liveState", (xmax = 0) AS inserted`;

// session_id (the FK) is only set when the session already exists; older Unity
// builds send session_start before the session row is created. session_ref keeps the raw id.
async function insertEvents(client, events) {
  if (!events.length) return;
  const params = [];
  const values = events.map((e) => {
    const p = (v) => `$${params.push(v)}`;
    const ref = p(e.sessionId);
    return `(${p(e.id)}, (SELECT id FROM sessions WHERE id = ${ref}), ${ref}, ${p(e.type)}, ${p(e.playerName)}, ${p(e.message)}, ${p(
      e.payload == null ? null : JSON.stringify(e.payload)
    )}::json, ${p(e.at)})`;
  });
  await client.query(
    `INSERT INTO events (id, session_id, session_ref, type, player_name, message, payload, occurred_at) VALUES ${values.join(", ")}`,
    params
  );
}

// Per-group summary. winRate follows the one shared rule (outcomes.js): victories / (victories + defeats), abandoned fights
// left out and reported as `abandoned`. This legacy shape has always carried a number, so "nothing decided" stays 0 here
// (check `decided`); avgScore is the mean over decided fights, so abandoned zero-score fights don't drag it down.
function summarize(sessions) {
  const n = sessions.length;
  const o = outcomes.countOutcomes(sessions.map((s) => s.outcome));
  const decided = sessions.filter((s) => outcomes.isDecided(s.outcome));
  const sum = (key, list = sessions) => list.reduce((a, s) => a + num(s[key]), 0);
  return {
    sessions: n,
    victories: o.victories,
    defeats: o.defeats,
    abandoned: o.abandoned,
    decided: o.decided,
    winRate: o.winRate ?? 0,
    bestScore: sessions.reduce((m, s) => Math.max(m, num(s.score)), 0),
    avgScore: decided.length ? Math.round(sum("score", decided) / decided.length) : 0,
    avgDurationSec: n ? round1(sum("durationSec") / n) : 0,
  };
}

function computeStats(sessions) {
  const totalSessions = sessions.length;
  const overall = summarize(sessions);
  const { victories, defeats } = overall;
  const sum = (key) => sessions.reduce((a, s) => a + num(s[key]), 0);

  // Streaks, oldest -> newest.
  const chrono = [...sessions].sort((a, b) => endTime(a) - endTime(b));
  let bestWinStreak = 0;
  let run = 0;
  for (const s of chrono) {
    run = s.outcome === "victory" ? run + 1 : 0;
    bestWinStreak = Math.max(bestWinStreak, run);
  }
  let currentWinStreak = 0;
  for (let i = chrono.length - 1; i >= 0 && chrono[i].outcome === "victory"; i--) currentWinStreak++;

  const levels = [...new Set(sessions.map((s) => s.level))].sort(
    (a, b) => (LEVEL_ORDER.indexOf(a) + 1 || 99) - (LEVEL_ORDER.indexOf(b) + 1 || 99)
  );
  const byLevel = levels.map((level) => ({ level, ...summarize(sessions.filter((s) => s.level === level)) }));

  const leaderboard = [...sessions]
    .sort((a, b) => num(b.score) - num(a.score))
    .slice(0, 20)
    .map((s) => ({
      id: s.id,
      playerName: s.playerName,
      score: s.score,
      outcome: s.outcome,
      level: s.level,
      durationSec: s.durationSec,
      endedAt: s.endedAt,
    }));

  return {
    totalSessions,
    victories,
    defeats,
    totalScore: sum("score"),
    totalDamage: round1(sum("damageDealt")),
    totalHits: sum("hitCount"),
    totalTaps: sum("tapCount"),
    totalDurationSec: round1(sum("durationSec")),
    bestScore: sessions.reduce((m, s) => Math.max(m, num(s.score)), 0),
    avgScore: overall.avgScore,
    avgDurationSec: totalSessions ? round1(sum("durationSec") / totalSessions) : 0,
    winRate: overall.winRate,
    abandoned: overall.abandoned,
    decided: overall.decided,
    currentWinStreak,
    bestWinStreak,
    playerCount: new Set(sessions.map((s) => s.playerName)).size,
    lastSessionAt: chrono.length ? chrono[chrono.length - 1].endedAt : null,
    byLevel,
    leaderboard,
  };
}

function computePlayers(sessions) {
  const groups = new Map();
  for (const s of sessions) {
    if (!groups.has(s.playerName)) groups.set(s.playerName, []);
    groups.get(s.playerName).push(s);
  }
  return [...groups.entries()]
    .map(([playerName, list]) => ({
      playerName,
      ...summarize(list),
      totalHits: list.reduce((a, s) => a + num(s.hitCount), 0),
      totalDamage: round1(list.reduce((a, s) => a + num(s.damageDealt), 0)),
      lastPlayedAt: list.reduce((m, s) => (endTime(s) > new Date(m || 0).getTime() ? s.endedAt : m), null),
    }))
    .sort((a, b) => b.bestScore - a.bestScore);
}

function lanUrls() {
  const urls = [];
  for (const addrs of Object.values(os.networkInterfaces())) {
    for (const a of addrs || []) {
      if (a.family === "IPv4" && !a.internal) urls.push(`http://${a.address}:${PORT}`);
    }
  }
  return urls;
}

// Express 4 doesn't catch rejected promises; forward them to the error handler.
const route = (fn) => (req, res, next) => fn(req, res, next).catch(next);

// ---------------------------------------------------------------------------
// Live sessions
// ---------------------------------------------------------------------------

// id -> last activity (ms). Only a hint for the sweeper, so it never has to
// query Postgres (and wake Neon) while nobody is playing; the database stays
// the source of truth.
const liveActivity = new Map();

function trackLive(session) {
  if (session.status === "live") liveActivity.set(session.id, Date.now());
  else liveActivity.delete(session.id);
}

async function reloadLiveActivity() {
  const { rows } = await query("SELECT id, last_event_at FROM sessions WHERE status = 'live'");
  liveActivity.clear();
  for (const r of rows) liveActivity.set(r.id, r.last_event_at ? new Date(r.last_event_at).getTime() : Date.now());
}

// extra: fields added to a finished fight's message (sessionEnd.js: rankUp, newRecords). The original { session, liveState } stay as they were.
function broadcastSession(kind, session, liveState, extra = null) {
  const type = { started: "session_started", updated: "session_updated", finished: "session_finished" }[kind];
  if (!type) return;
  stream.broadcast(type, extra ? { session, liveState, ...extra } : { session, liveState }, session.playerName);
}

// Folds a batch of events into the session's latest state, oldest first.
function mergeLiveState(previous, events) {
  const state = { ...(previous || {}) };
  for (const e of events) {
    const p = e.payload && typeof e.payload === "object" && !Array.isArray(e.payload) ? e.payload : {};
    for (const key of LIVE_KEYS) if (isNum(p[key])) state[key] = p[key];
    // Older builds' hit payload: { damage, score, hitIndex }.
    if (e.type === "hit") {
      if (!isNum(p.hits) && isNum(p.hitIndex)) state.hits = p.hitIndex;
      if (!isNum(p.damageTotal) && isNum(p.damage)) state.damageTotal = round1((state.damageTotal || 0) + p.damage);
    }
    if (e.type !== "hp") state.lastEvent = { type: e.type, message: e.message, at: e.at };
    state.updatedAt = e.at;
  }
  return state;
}

function finalOutcomeOf(event) {
  const byType = normalizeOutcome(event.type);
  if (FINAL_OUTCOMES.has(byType)) return byType;
  if (event.type === "session_end" && event.payload && typeof event.payload === "object") {
    const o = normalizeOutcome(event.payload.outcome);
    if (FINAL_OUTCOMES.has(o)) return o;
  }
  return null;
}

// Applies stored events to their sessions inside one transaction. A session is
// live from its first event until an outcome arrives; events for an unknown
// session id create it. Returns the session changes to broadcast after commit.
async function applyEvents(client, events) {
  const changes = [];
  const bySession = new Map();
  for (const e of events) {
    if (!e.sessionId) continue;
    if (!bySession.has(e.sessionId)) bySession.set(e.sessionId, []);
    bySession.get(e.sessionId).push(e);
  }

  for (const [id, list] of bySession) {
    const found = await client.query(
      `SELECT ${SESSION_API_SELECT}, live_state AS "liveState" FROM sessions WHERE id = $1 FOR UPDATE`,
      [id]
    );
    const before = found.rows.length ? sessionFromRow(found.rows[0]) : null;
    const outcome = list.map(finalOutcomeOf).find(Boolean) || null;
    const now = new Date().toISOString();
    let kind;

    if (!before) {
      const firstPayload = list.find((e) => e.payload && typeof e.payload.level === "string")?.payload;
      await client.query(
        `INSERT INTO sessions (id, player_name, level, outcome, status, started_at, ended_at, last_event_at, source)
         VALUES ($1, $2, $3, 'in_progress', 'live', $4, $4, $4, 'unity')`,
        [id, list[0].playerName, firstPayload ? firstPayload.level : "Unknown", now]
      );
      kind = "started";
    } else if (before.session.status === "finished") {
      // Headset taken off (the app pauses) and the sweeper gave up on it: resume.
      if (before.session.outcome === "abandoned" && !outcome) kind = "started";
      else continue; // late events for a finished fight are stored, the session is left alone
    } else {
      kind = "updated";
    }

    const state = mergeLiveState(before && before.liveState, list);
    const finished = Boolean(outcome);
    const levelFromEvents = list.map((e) => e.payload && e.payload.level).find((l) => typeof l === "string");
    const { rows } = await client.query(
      `UPDATE sessions SET
         status = $2,
         outcome = CASE WHEN $3::text IS NOT NULL THEN $3 WHEN $2 = 'live' THEN 'in_progress' ELSE outcome END,
         live_state = $4::jsonb,
         last_event_at = $5, ended_at = $5,
         level = CASE WHEN level = 'Unknown' AND $6::text IS NOT NULL THEN $6 ELSE level END,
         score = COALESCE($7, score), points_earned = COALESCE($7, points_earned),
         hit_count = COALESCE($8, hit_count), damage_dealt = COALESCE($9, damage_dealt),
         xp = COALESCE($10, xp), duration_sec = COALESCE($11, duration_sec),
         player_hp_remaining = COALESCE($12, player_hp_remaining),
         monster_hp_remaining = COALESCE($13, monster_hp_remaining),
         full_swings = COALESCE($14, full_swings)
       WHERE id = $1
       RETURNING ${SESSION_API_SELECT}, live_state AS "liveState"`,
      [id, finished ? "finished" : "live", outcome, JSON.stringify(state), now, levelFromEvents || null,
        state.score ?? null, state.hits ?? null, state.damageTotal ?? null, state.xp ?? null, state.t ?? null,
        state.playerHp ?? null, state.monsterHp ?? null, state.fullSwings ?? null]
    );
    const after = sessionFromRow(rows[0]);
    changes.push({ kind: finished ? "finished" : kind, ...after });
  }

  await insertEvents(client, events);
  return changes;
}

async function sweepStaleSessions() {
  const cutoff = Date.now() - LIVE_TIMEOUT_MS;
  const stale = [...liveActivity].filter(([, t]) => t < cutoff).map(([id]) => id);
  if (!stale.length) return;
  const { rows } = await query(
    `UPDATE sessions SET status = 'finished',
       outcome = CASE WHEN outcome IN ('in_progress', 'unknown') THEN 'abandoned' ELSE outcome END,
       ended_at = COALESCE(last_event_at, ended_at)
     WHERE id = ANY($1) AND status = 'live' AND (last_event_at IS NULL OR last_event_at < $2)
     RETURNING ${SESSION_API_SELECT}, live_state AS "liveState"`,
    [stale, new Date(cutoff).toISOString()]
  );
  await reloadLiveActivity();
  for (const row of rows) {
    const { session, liveState } = sessionFromRow(row);
    console.log(`[live] ${session.playerName} ${session.id} finished after ${LIVE_TIMEOUT_MS / 1000}s without events (${session.outcome})`);
    broadcastSession("finished", session, liveState, await sessionEnd.extras(session));
    io.emit("session", session);
    await evaluateGoalAfter(session);
    await evaluateEngagementAfter(session);
  }
}

// ---------------------------------------------------------------------------
// HTTP + realtime
// ---------------------------------------------------------------------------

// Browsers only: requests without an Origin (Unity, curl, same-origin GETs) are
// unaffected. ALLOWED_ORIGINS is comma separated; "https://*.vercel.app" style
// wildcards match one or more subdomain labels.
const DEV_ORIGIN = /^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/;
const ALLOWED_ORIGINS = (process.env.ALLOWED_ORIGINS || "")
  .split(",")
  .map((o) => o.trim().replace(/\/+$/, ""))
  .filter(Boolean)
  .map((o) => (o.includes("*") ? new RegExp(`^${o.split("*").map((p) => p.replace(/[.+?^${}()|[\]\\]/g, "\\$&")).join("[a-z0-9-.]+")}$`, "i") : o));

function originAllowed(origin) {
  if (!origin || DEV_ORIGIN.test(origin)) return true;
  return ALLOWED_ORIGINS.some((o) => (typeof o === "string" ? o === origin : o.test(origin)));
}
const corsOptions = { origin: (origin, cb) => cb(null, originAllowed(origin)), methods: ["GET", "POST", "PATCH", "PUT", "DELETE"] };

const app = express();
app.set("trust proxy", 1); // Render sits behind one proxy; makes req.ip the client (used by the survey rate limit)
const server = http.createServer(app);
const io = new Server(server, { cors: corsOptions });
broadcast.attach(server);

app.use(cors(corsOptions));
app.use(express.json({ limit: "1mb" }));

app.get("/api/health", route(async (_req, res) => {
  const database = { ok: false, latencyMs: null };
  let counts = { sessions: null, events: null, live: null };
  const t0 = Date.now();
  try {
    await query("SELECT 1");
    database.latencyMs = Date.now() - t0;
    database.ok = true;
    const { rows } = await query(
      `SELECT (SELECT count(*) FROM sessions)::int AS sessions, (SELECT count(*) FROM events)::int AS events,
              (SELECT count(*) FROM sessions WHERE status = 'live')::int AS live`
    );
    counts = rows[0];
  } catch (err) {
    console.error("[health] database check failed:", err.message);
    database.error = "Database unavailable";
  }
  // 503 when the database is down: the server is up but can't serve data.
  res.status(database.ok ? 200 : 503).json({
    ok: database.ok,
    service: "vr-fitness-backend",
    version: 3,
    port: PORT,
    time: new Date().toISOString(),
    uptimeSec: Math.round((Date.now() - STARTED_AT) / 1000),
    server: "up",
    database,
    sessions: counts.sessions,
    events: counts.events,
    clients: io.engine.clientsCount,
    lanUrls: lanUrls(),
    liveSessions: counts.live,
    streamClients: stream.clientCount(),
  });
}));

// Real-time feed for the dashboard (Server-Sent Events). ?player=NAME scopes it.
app.get("/api/stream", stream.open);

// Headset livestream: who is online per room (the WebRTC signaling itself is WS /api/broadcast/ws).
app.get("/api/broadcast/status", (_req, res) => res.json({ ok: true, rooms: broadcast.status() }));

app.get("/api/sessions", route(async (req, res) => {
  const { player, level, outcome, status } = req.query;
  const where = [];
  const params = [];
  if (player) where.push(`player_name = $${params.push(String(player))}`);
  if (level) where.push(`level = $${params.push(String(level))}`);
  if (outcome) where.push(`outcome = $${params.push(normalizeOutcome(outcome))}`);
  if (status) where.push(`status = $${params.push(String(status))}`);
  // Old behaviour: any positive limit (fractions truncate); non-numbers mean "all".
  const limit = Number(req.query.limit);
  const limitSql = limit > 0 && Number.isFinite(limit) ? `LIMIT $${params.push(Math.trunc(limit))}` : "";
  // Newest first by end time; ties keep insertion order, as the old stable sort did.
  const { rows } = await query(
    `SELECT ${SESSION_API_SELECT} FROM sessions
     ${where.length ? `WHERE ${where.join(" AND ")}` : ""}
     ORDER BY ended_at DESC, seq ASC ${limitSql}`,
    params
  );
  const sessions = rows.map(fromRow);
  res.json({ sessions, count: sessions.length });
}));

app.get("/api/sessions/:id", route(async (req, res) => {
  const found = await query(`SELECT ${SESSION_API_SELECT}, live_state AS "liveState" FROM sessions WHERE id = $1`, [req.params.id]);
  if (!found.rows.length) return res.status(404).json({ ok: false, error: "Session not found" });
  const { session, liveState } = sessionFromRow(found.rows[0]);
  const { rows } = await query(
    `SELECT ${EVENT_SELECT} FROM events WHERE session_ref = $1 ORDER BY occurred_at ASC, seq ASC`,
    [session.id]
  );
  const events = rows.map(fromRow);
  // Added: swing-tier breakdown, exertion trend and calorie estimate (see fitness.js), the
  // post-session rating (null when unrated) and the personal bests this session holds.
  const [rating, personalBestsSet, newRecords] = await Promise.all([feedback.getFeedback(session.id), analytics.sessionPersonalBests(session), featureRoutes.newRecordsFor(session)]);
  // newRecords (added): what this fight beat on its level, [{ metric, value, previous }]; empty for a first fight, an abandoned one or a live one.
  res.json({ session, events, liveState, fitness: analytics.sessionFitness(session, events), feedback: rating, personalBestsSet, newRecords });
}));

app.get("/api/stats", route(async (_req, res) => {
  res.json(computeStats(await allSessions()));
}));

// Players are derived from sessions — there is no players table.
app.get("/api/players", route(async (_req, res) => {
  const players = computePlayers(await allSessions());
  res.json({ players, count: players.length });
}));

app.get("/api/players/:name", route(async (req, res) => {
  const profile = await analytics.playerProfile(String(req.params.name));
  if (!profile) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(profile);
}));

// Sessions over time, streaks and recency for one player. Day boundaries follow ?tz= (IANA name), default UTC.
app.get("/api/players/:name/engagement", route(async (req, res) => {
  const engagement = await analytics.playerEngagement(String(req.params.name), { tz: req.query.tz });
  if (!engagement) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(engagement);
}));

// Level, title, streak (with freeze), weekly goal and badges. Day boundaries follow ?tz= (default Asia/Kathmandu).
// ?goal= overrides the weekly session target.
app.get("/api/players/:name/achievements", route(async (req, res) => {
  const goal = Number(req.query.goal);
  const result = await analytics.playerAchievements(String(req.params.name), { tz: req.query.tz, goal: Number.isInteger(goal) && goal > 0 && goal <= 50 ? goal : undefined });
  if (!result) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(result);
}));

// Daily goal (one metric, one target, for the player's local day). No login: anyone who knows a name can set that player's goal.
// PUT validates server side and is rate limited per IP; goal/today is the lightweight read the headset and the web both use.
const goalLimiter = feedbackCore.createRateLimiter({ max: 30, windowMs: 10 * 60_000 });
async function broadcastGoal(type, goal, extra = {}) {
  if (!goal) return;
  stream.broadcast(type, { playerName: goal.playerName, date: goal.date, metric: goal.metric, target: goal.target, completedAt: goal.completedAt, bonusXp: goal.bonusXp, ...extra }, goal.playerName);
}
// After a session finished: evaluate that day's goal once and announce goal_met. Never throws (a goal problem must not break saving).
async function evaluateGoalAfter(session) {
  try {
    const r = await goalsStore.evaluateForSession(session);
    if (r.completedNow) await broadcastGoal("goal_met", r.goal);
  } catch (err) {
    console.error("[goals] evaluation failed:", err.message);
  }
}
// Challenges (added with the engagement pass): after a session finished, record any challenge it completed (idempotent)
// and announce challenge_done; the activity feed is rebuilt. Never throws, like the goal evaluation above.
function broadcastChallenges(list) {
  for (const c of list) {
    stream.broadcast("challenge_done", { playerName: c.playerName, key: c.key, id: c.id, title: c.title, period: c.period, periodStart: c.periodStart, completedAt: c.completedAt, bonusXp: c.bonusXp }, c.playerName);
  }
  if (list.length) activityFeed.invalidate();
}
async function evaluateEngagementAfter(session) {
  try {
    await engageStore.evaluateForSession(session, broadcastChallenges);
  } catch (err) {
    console.error("[challenges] evaluation failed:", err.message);
  }
  activityFeed.invalidate();
}
app.put("/api/players/:name/goal", route(async (req, res) => {
  if (!goalLimiter.allow(req.ip)) return res.status(429).json({ ok: false, error: "Too many goal changes from this connection. Try again in a few minutes." });
  const out = await goalsStore.setGoal(String(req.params.name), req.body, { tz: req.query.tz });
  if (out.status >= 400) return res.status(out.status).json(out.body);
  await broadcastGoal("goal_set", out.goal);
  if (out.completedNow) await broadcastGoal("goal_met", out.goal);
  if (out.completedNow) activityFeed.invalidate();
  res.status(out.status).json(out.body);
}));

app.get("/api/players/:name/goal/today", route(async (req, res) => {
  const n = goals.validateName(req.params.name);
  if (!n.ok) return res.status(400).json({ ok: false, error: n.error });
  res.json(await goalsStore.todayState(n.value, { tz: req.query.tz }));
}));

app.get("/api/players/:name/goals", route(async (req, res) => {
  const n = goals.validateName(req.params.name);
  if (!n.ok) return res.status(400).json({ ok: false, error: n.error });
  res.json(await goalsStore.history(n.value, req.query.days, { tz: req.query.tz }));
}));

// ---------------------------------------------------------------------------
// Engagement (added): challenges, calendar, weekly recap, best run, public challenge page, activity feed, live reactions.
// Additions only; every route validates the name and uses parameterised queries (engageStore.js).
// ---------------------------------------------------------------------------
function nameOr400(req, res) {
  const n = goals.validateName(req.params.name);
  if (!n.ok) res.status(400).json({ ok: false, error: n.error });
  return n.ok ? n.value : null;
}

// Today's 3 daily challenges and this week's 1, progress computed on read; a met challenge is recorded here too (defensive).
app.get("/api/players/:name/challenges", route(async (req, res) => {
  const name = nameOr400(req, res);
  if (!name) return;
  res.json(await engageStore.challengeState(name, { onCompleted: broadcastChallenges }));
}));

app.get("/api/players/:name/challenges/history", route(async (req, res) => {
  const name = nameOr400(req, res);
  if (!name) return;
  res.json(await engageStore.challengeHistory(name, req.query.days));
}));

// GitHub-style calendar: sessions and estimated kcal per local day, last ?weeks= (default 26, max 53).
app.get("/api/players/:name/calendar", route(async (req, res) => {
  const name = nameOr400(req, res);
  if (!name) return;
  res.json(await engageStore.calendar(name, req.query.weeks));
}));
app.get("/api/calendar", route(async (req, res) => {
  res.json(await engageStore.calendar(null, req.query.weeks));
}));

// "Your week": ?week=last for last week, anything else this week so far. Monday to Sunday, local time zone constant.
app.get("/api/players/:name/week", route(async (req, res) => {
  const name = nameOr400(req, res);
  if (!name) return;
  res.json(await engageStore.week(name, req.query.week === "last" ? 1 : 0));
}));

// The best run on a level (highest score; ?before=<sessionId> = only runs that started before that one), with its swing
// speed and monster HP samples for the replay ghost.
app.get("/api/players/:name/best", route(async (req, res) => {
  const name = nameOr400(req, res);
  if (!name) return;
  const level = typeof req.query.level === "string" ? req.query.level.trim() : "";
  if (!level || level.length > 20 || /[\u0000-\u001f\u007f]/.test(level)) return res.status(400).json({ ok: false, error: "level is required" });
  const before = typeof req.query.before === "string" && req.query.before.length <= 100 ? req.query.before : null;
  res.json(await engageStore.best(name, level, before));
}));

// Public "Challenge a friend" page: one stored session (no events), and whether a typed name has beaten its score.
app.get("/api/challenge/:sessionId", route(async (req, res) => {
  const out = await engageStore.challengeCard(String(req.params.sessionId));
  res.status(out.status).json(out.body);
}));
app.get("/api/challenge/:sessionId/check", route(async (req, res) => {
  const out = await engageStore.challengeCheck(String(req.params.sessionId), req.query.name);
  res.status(out.status).json(out.body);
}));

// Activity feed: names and event types only, newest first, at most 20.
app.get("/api/activity", route(async (req, res) => {
  res.json(await activityFeed.feed(req.query.limit));
}));

// Audience cheers on a live fight: validated, rate limited (per IP 1 per 2 s with a burst of 5, and 10 per second in
// total), never stored, broadcast as an SSE "reaction". REACTIONS_ENABLED=false switches it off. The headset does not
// receive them yet.
const REACTIONS_ON = engage.reactionsEnabled();
const reactPerIp = engage.createBucket(engage.REACTION_LIMITS.perIp);
const reactGlobal = engage.createBucket(engage.REACTION_LIMITS.global);
app.get("/api/live/reactions", (_req, res) => {
  res.json({ ok: true, enabled: REACTIONS_ON, kinds: engage.REACTION_KINDS, limits: engage.REACTION_LIMITS, stored: false });
});
app.post("/api/live/react", route(async (req, res) => {
  if (!REACTIONS_ON) return res.status(503).json({ ok: false, error: "Reactions are switched off." });
  const v = engage.validateReaction(req.body);
  if (!v.ok) return res.status(400).json({ ok: false, error: v.error });
  if (!reactPerIp.take(req.ip)) return res.status(429).json({ ok: false, error: "Easy there: one cheer every 2 seconds." });
  if (!reactGlobal.take()) return res.status(429).json({ ok: false, error: "Lots of cheers right now. Try again in a moment." });
  const { rows } = await query("SELECT player_name, status FROM sessions WHERE id = $1", [v.value.sessionId]);
  if (!rows.length) return res.status(404).json({ ok: false, error: "Session not found" });
  if (rows[0].status !== "live") return res.status(409).json({ ok: false, error: "That fight is not live." });
  const payload = { sessionId: v.value.sessionId, kind: v.value.kind, playerName: rows[0].player_name, at: new Date().toISOString() };
  stream.broadcast("reaction", payload, rows[0].player_name);
  res.status(202).json({ ok: true, ...payload });
}));

// Effort fade: first third vs last third of a fight, from the stored hit events (effort.js). Read-only.
app.get("/api/sessions/:id/fade", route(async (req, res) => {
  const out = await analytics.sessionFade(String(req.params.id));
  if (!out) return res.status(404).json({ ok: false, error: "Session not found" });
  res.json(out);
}));
app.get("/api/players/:name/fade", route(async (req, res) => {
  const out = await analytics.playerFade(String(req.params.name), req.query.last);
  if (!out) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(out);
}));

// Effort fingerprint: five 0-100 axes from the player's recent fights (effort.js). Read-only.
app.get("/api/players/:name/fingerprint", route(async (req, res) => {
  const out = await analytics.playerFingerprint(String(req.params.name));
  if (!out) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(out);
}));

// Plain-language insights (insights.js): only sentences with enough data behind them; the rest listed with their minimum.
app.get("/api/players/:name/insights", route(async (req, res) => {
  const result = await analytics.playerInsights(String(req.params.name));
  if (!result) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(result);
}));

// First-run checklist (first fight, a chosen name, a rated session, a badge), computed from the database.
app.get("/api/players/:name/checklist", route(async (req, res) => {
  const result = await analytics.playerChecklist(String(req.params.name));
  if (!result) return res.status(404).json({ ok: false, error: "Player not found" });
  res.json(result);
}));

const MAX_PLAYER_NAME = 40;

// Renames a player everywhere the name is stored: sessions.player_name and
// events.player_name, in one transaction. Players are derived from sessions, so
// renaming onto an existing name would silently merge two people's histories
// with no way to split them again; that is refused with 409 instead. A player
// in a live fight is refused too: the headset keeps posting under the old name.
// Event payloads and messages are left as recorded (they are historical logs).
app.patch("/api/players/:name", route(async (req, res) => {
  const oldName = String(req.params.name);
  const raw = req.body && req.body.newName;
  const newName = typeof raw === "string" ? raw.trim() : "";
  if (!newName) return res.status(400).json({ ok: false, error: "New name is required" });
  if (newName.length > MAX_PLAYER_NAME) return res.status(400).json({ ok: false, error: `New name must be ${MAX_PLAYER_NAME} characters or fewer` });
  if (/[\u0000-\u001f\u007f]/.test(newName)) return res.status(400).json({ ok: false, error: "New name can't contain control characters" });
  if (newName === oldName) return res.status(400).json({ ok: false, error: "That is already this player's name" });

  const result = await withTransaction(async (client) => {
    // Locks this player's sessions so a live update can't interleave with the rename.
    const own = await client.query("SELECT id, status FROM sessions WHERE player_name = $1 FOR UPDATE", [oldName]);
    if (!own.rowCount) return { status: 404, error: "Player not found" };
    if (own.rows.some((r) => r.status === "live")) {
      return { status: 409, error: `${oldName} is in a live fight right now. Rename after it finishes.` };
    }
    const taken = await client.query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [newName]);
    if (taken.rowCount) {
      return { status: 409, error: `A player named "${newName}" already exists. Pick another name; merging two players isn't supported.` };
    }
    const sessions = await client.query("UPDATE sessions SET player_name = $2 WHERE player_name = $1", [oldName, newName]);
    const events = await client.query("UPDATE events SET player_name = $2 WHERE player_name = $1", [oldName, newName]);
    // Daily goals follow the name (a day that already has a goal under the new name keeps that one).
    await client.query(
      `UPDATE daily_goals g SET player_name = $2 WHERE g.player_name = $1
       AND NOT EXISTS (SELECT 1 FROM daily_goals x WHERE x.player_name = $2 AND x.goal_date = g.goal_date)`,
      [oldName, newName]
    );
    // Challenge completions follow the name the same way (a key the new name already holds keeps that row).
    await client.query(
      `UPDATE challenge_completions c SET player_name = $2 WHERE c.player_name = $1
       AND NOT EXISTS (SELECT 1 FROM challenge_completions x WHERE x.player_name = $2 AND x.challenge_key = c.challenge_key)`,
      [oldName, newName]
    );
    return { sessions: sessions.rowCount, events: events.rowCount };
  });
  if (result.error) return res.status(result.status).json({ ok: false, error: result.error });

  console.log(`[player] renamed "${oldName}" -> "${newName}" (${result.sessions} session rows, ${result.events} event rows)`);
  const change = { oldName, newName, sessions: result.sessions, events: result.events };
  // Sent to every client (no player scope), including ones watching the old name.
  featureRoutes.invalidateAll();
  activityFeed.invalidate();
  stream.broadcast("player_renamed", change);
  io.emit("player_renamed", change);
  io.emit("stats", computeStats(await allSessions()));

  res.set({
    "X-Renamed-Sessions": String(result.sessions),
    "X-Renamed-Events": String(result.events),
    "Access-Control-Expose-Headers": "X-Renamed-Sessions, X-Renamed-Events",
  });
  res.json(await analytics.playerProfile(newName));
}));

app.get("/api/events", route(async (req, res) => {
  const requested = Math.trunc(Math.min(Number(req.query.limit) || 100, MAX_EVENTS));
  const params = [];
  const where = [];
  if (req.query.type) where.push(`type = $${params.push(String(req.query.type))}`);
  if (req.query.sessionId) where.push(`session_ref = $${params.push(String(req.query.sessionId))}`);
  if (req.query.player) where.push(`player_name = $${params.push(String(req.query.player))}`);
  params.push(requested > 0 ? requested : 100);
  // Most recent first (the old slice(-limit).reverse()).
  const { rows } = await query(
    `SELECT ${EVENT_SELECT} FROM events
     ${where.length ? `WHERE ${where.join(" AND ")}` : ""}
     ORDER BY seq DESC LIMIT $${params.length}`,
    params
  );
  const events = rows.map(fromRow);
  res.json({ events, count: events.length });
}));

// Sessions being played right now, each with its latest HP / XP / score state.
app.get("/api/live", route(async (req, res) => {
  await sweepStaleSessions();
  const params = [];
  const where = ["status = 'live'"];
  if (req.query.player) where.push(`player_name = $${params.push(String(req.query.player))}`);
  const { rows } = await query(
    `SELECT ${SESSION_API_SELECT}, live_state AS "liveState" FROM sessions WHERE ${where.join(" AND ")} ORDER BY started_at`,
    params
  );
  const sessions = rows.map((r) => {
    const { session, liveState } = sessionFromRow(r);
    return { ...session, liveState };
  });
  res.json({ sessions, count: sessions.length, liveTimeoutSec: LIVE_TIMEOUT_MS / 1000, serverTime: new Date().toISOString() });
}));

app.get("/api/overview", route(async (req, res) => {
  res.json(await analytics.overview({ tz: req.query.tz }));
}));

// Landing page numbers: aggregates only, no player names or per-person rows.
app.get("/api/public-stats", route(async (_req, res) => {
  res.set("Cache-Control", "public, max-age=15");
  res.json(await analytics.publicStats());
}));

// Site-wide estimated calorie burn: total, per day (30 days), per level, top 5 sessions. Day boundaries follow ?tz=.
app.get("/api/insights/calories", route(async (req, res) => {
  res.json(await analytics.calorieInsights({ tz: req.query.tz }));
}));

// Perceived vs measured effort. Aggregates and anonymous points only.
app.get("/api/analytics/feedback", route(async (_req, res) => {
  res.json(await feedback.feedbackAnalytics());
}));

app.get("/api/analytics", route(async (req, res) => {
  const { player, level, from, to } = req.query;
  res.json(await analytics.researchAnalytics({ player, level, from, to }));
}));

app.get("/api/export/:kind.csv", route(async (req, res) => {
  const { player, level, from, to, type } = req.query;
  const filters = { player, level, from, to, type };
  const make = { sessions: analytics.exportSessionsCsv, events: analytics.exportEventsCsv, goals: goalsStore.exportCsv }[req.params.kind];
  if (!make) return res.status(404).json({ ok: false, error: "Unknown export" });
  const { csv, count } = await make(filters);
  const stamp = new Date().toISOString().slice(0, 10);
  const who = player ? `-${String(player).replace(/[^a-z0-9_-]+/gi, "_")}` : "";
  res.set({
    "Content-Type": "text/csv; charset=utf-8",
    "Content-Disposition": `attachment; filename="strikefit-${req.params.kind}${who}-${stamp}.csv"`,
    "X-Row-Count": String(count),
    "Access-Control-Expose-Headers": "X-Row-Count, Content-Disposition",
  });
  res.send(csv);
}));

// Unity posts here at battle start (status "live") and when a battle ends.
// Re-posting the same id updates that session instead of creating a duplicate.
app.post("/api/sessions", route(async (req, res) => {
  const body = req.body || {};
  const { session, liveState, updated, event, before } = await withTransaction(async (client) => {
    let existing = null;
    if (body.id) {
      const { rows } = await client.query(`SELECT ${SESSION_API_SELECT} FROM sessions WHERE id = $1 FOR UPDATE`, [String(body.id)]);
      if (rows.length) existing = fromRow(rows[0]);
    }
    const normalized = normalizeSession(body, existing);
    const { rows } = await client.query(UPSERT_SESSION_SQL, SESSION_API_FIELDS.map(([key]) => normalized[key]));
    let { session: saved, liveState: state } = sessionFromRow(rows[0]);

    // Starting state sent with the live session (HP, max HP) seeds the live view.
    if (saved.status === "live") {
      const seed = Object.fromEntries(LIVE_KEYS.filter((k) => isNum(body[k])).map((k) => [k, body[k]]));
      if (Object.keys(seed).length) {
        const r = await client.query(
          `UPDATE sessions SET live_state = COALESCE(live_state, '{}'::jsonb) || $2::jsonb WHERE id = $1 RETURNING live_state`,
          [saved.id, JSON.stringify(seed)]
        );
        state = r.rows[0].live_state;
      }
    }

    // Link events posted before the session existed (older builds' session_start).
    await client.query("UPDATE events SET session_id = $1 WHERE session_ref = $1 AND session_id IS NULL", [saved.id]);

    let completeEvent = null;
    if (saved.status === "finished") {
      completeEvent = {
        id: uuidv4(),
        type: "session_complete",
        sessionId: saved.id,
        playerName: saved.playerName,
        message: `${saved.playerName} finished ${saved.level}: ${saved.outcome} — score ${saved.score}`,
        payload: saved,
        at: new Date().toISOString(),
      };
      await insertEvents(client, [completeEvent]);
    }
    return { session: saved, liveState: state, updated: !rows[0].inserted, event: completeEvent, before: existing };
  });

  trackLive(session);
  const stats = computeStats(await allSessions());
  io.emit("session", session);
  if (event) io.emit("event", event);
  io.emit("stats", stats);

  const wasLive = before && before.status === "live";
  if (session.status === "live") broadcastSession(wasLive ? "updated" : "started", session, liveState);
  else {
    const kind = wasLive || !before ? "finished" : "updated";
    broadcastSession(kind, session, liveState, kind === "finished" ? await sessionEnd.extras(session) : null);
  }
  if (event) stream.broadcast("event_created", event, event.playerName);
  if (session.status === "finished") await evaluateGoalAfter(session);
  if (session.status === "finished") await evaluateEngagementAfter(session);

  console.log(`[session] ${session.playerName} ${session.level} ${session.status}/${session.outcome} score=${session.score}`);
  res.status(updated ? 200 : 201).json({ ok: true, updated, session, stats });
}));

// One event (the original contract, answered with { ok, event }), or a batch:
// { events: [...] } or a bare array, answered with { ok, count, events }.
app.post("/api/events", route(async (req, res) => {
  const body = req.body;
  const isBatch = Array.isArray(body) || Array.isArray(body && body.events);
  const raw = Array.isArray(body) ? body : isBatch ? body.events : [body || {}];
  if (raw.length > MAX_BATCH) return res.status(413).json({ ok: false, error: `At most ${MAX_BATCH} events per request` });

  const events = raw.map((b) => normalizeEvent(b && typeof b === "object" ? b : {}));
  // "ping" events are connection checks from the dashboard — broadcast, never stored.
  const stored = events.filter((e) => e.type !== "ping");
  const changes = stored.length ? await withTransaction((client) => applyEvents(client, stored)) : [];

  for (const e of events) io.emit("event", e);
  // Pings are echoed too (never stored) so the dashboard can time a full round trip.
  for (const e of events) stream.broadcast("event_created", e, e.playerName);
  for (const c of changes) {
    trackLive(c.session);
    broadcastSession(c.kind, c.session, c.liveState, c.kind === "finished" ? await sessionEnd.extras(c.session) : null);
    if (c.kind === "finished") io.emit("session", c.session);
  }
  for (const c of changes) if (c.kind === "finished") await evaluateGoalAfter(c.session);
  for (const c of changes) if (c.kind === "finished") await evaluateEngagementAfter(c.session);

  if (isBatch) return res.status(201).json({ ok: true, count: events.length, events });
  res.status(201).json({ ok: true, event: events[0] });
}));

// Post-session survey: enjoyment (1-5) and/or Borg RPE (6-20), one rating per finished session.
// Public and unauthenticated by design (players rate from their phone), so it is rate limited per IP.
const feedbackLimiter = feedbackCore.createRateLimiter({ max: 12, windowMs: 10 * 60_000 });
app.post("/api/sessions/:id/feedback", route(async (req, res) => {
  if (!feedbackLimiter.allow(req.ip)) return res.status(429).json({ ok: false, error: "Too many ratings from this connection. Try again in a few minutes." });
  const checked = feedbackCore.validateFeedback(req.body);
  if (!checked.ok) return res.status(400).json({ ok: false, error: checked.error });
  const found = await query("SELECT id, player_name, level, status FROM sessions WHERE id = $1", [req.params.id]);
  if (!found.rows.length) return res.status(404).json({ ok: false, error: "Session not found" });
  const s = found.rows[0];
  if (s.status === "live") return res.status(409).json({ ok: false, error: "That session is still being played. Rate it when it finishes." });
  const saved = await feedback.saveFeedback(s.id, checked.value);
  if (!saved) return res.status(409).json({ ok: false, error: "That session has already been rated." });
  // The comment stays out of the live stream.
  stream.broadcast("feedback_saved", { sessionId: s.id, playerName: s.player_name, level: s.level, enjoyment: saved.enjoyment, rpe: saved.rpe }, s.player_name);
  res.status(201).json({ ok: true, feedback: saved });
}));

app.delete("/api/sessions/:id", route(async (req, res) => {
  const deleted = await withTransaction(async (client) => {
    const r = await client.query("DELETE FROM sessions WHERE id = $1 RETURNING player_name", [req.params.id]);
    if (!r.rowCount) return null;
    // Linked events went with the cascade; this catches any still unlinked.
    await client.query("DELETE FROM events WHERE session_ref = $1", [req.params.id]);
    return { playerName: r.rows[0].player_name };
  });
  if (!deleted) return res.status(404).json({ ok: false, error: "Session not found" });
  liveActivity.delete(req.params.id);
  featureRoutes.invalidatePlayer(deleted.playerName);
  activityFeed.invalidate();
  io.emit("session_deleted", { id: req.params.id });
  io.emit("stats", computeStats(await allSessions()));
  stream.broadcast("session_deleted", { id: req.params.id, playerName: deleted.playerName }, deleted.playerName);
  res.json({ ok: true });
}));

app.delete("/api/sessions", route(async (_req, res) => {
  await withTransaction(async (client) => {
    await client.query("DELETE FROM events");
    await client.query("DELETE FROM sessions");
  });
  liveActivity.clear();
  featureRoutes.invalidateAll();
  activityFeed.invalidate();
  io.emit("stats", computeStats([]));
  io.emit("cleared", { at: new Date().toISOString() });
  stream.broadcast("session_deleted", { all: true });
  res.json({ ok: true, message: "Cleared sessions and events" });
}));

// Public scoreboard shared by the headset menu and the web Leaderboard page (added; rule in leaderboard.js).
require("./leaderboardRoute").mount(app, { query, route });
featureRoutes.mount(app, { route }); // rank (and the other read-only engagement endpoints): featureRoutes.js
require("./commentaryAi").mount(app, { route }); // AI match commentary (Groq) and its voice (Google TTS): commentaryAi.js

// Ghost pace for the Live Arena: a player's personal-best fight on a level as cumulative damage over time (pace.js, cached 60 s).
require("./pace").mount(app, { query });

// Playtest study (/study): consent, participant codes, pre/post forms, research CSV (study.js).
require("./study").mount(app, { route });

// Thesis Lab: return rate, RPE vs swing speed, level table, research CSV (lab.js). Read-only.
require("./lab").mount(app, { route });

app.use("/api", (_req, res) => {
  res.status(404).json({ ok: false, error: "Unknown API route" });
});

// Serve the built dashboard (npm run build in web/frontend) so a headset or
// another device on the LAN can open http://<mac-ip>:4000 directly.
if (fs.existsSync(FRONTEND_DIST)) {
  app.use(express.static(FRONTEND_DIST));
  app.get("*", (_req, res) => res.sendFile(path.join(FRONTEND_DIST, "index.html")));
}

// Malformed JSON from a client gets a JSON 400 instead of an HTML stack trace.
app.use((err, _req, res, _next) => {
  const status = err.status || err.statusCode || 500;
  if (status >= 500) console.error("[error]", err);
  if (res.headersSent) return res.end();
  res.status(status).json({ ok: false, error: status === 400 ? "Invalid JSON body" : status === 413 ? "Request too large" : "Server error" });
});

io.on("connection", (socket) => {
  allSessions()
    .then((sessions) => socket.emit("stats", computeStats(sessions)))
    .catch((err) => console.error("[socket] could not load stats:", err.message));
  console.log(`[socket] connected ${socket.id} (${io.engine.clientsCount} online)`);
  socket.on("disconnect", () => console.log(`[socket] disconnected ${socket.id}`));
});

server.on("error", (err) => {
  if (err.code === "EADDRINUSE") {
    console.error(`Port ${PORT} is already in use — is the backend already running? (PORT=xxxx npm start to change)`);
    process.exit(1);
  }
  throw err;
});

// Idempotent (IF NOT EXISTS throughout), so every boot — including a Render
// deploy — brings the database up to date before live tracking starts.
async function prepareDatabase() {
  await query(fs.readFileSync(SCHEMA_SQL, "utf8"));
  await reloadLiveActivity();
  console.log(`  Database: Postgres connected, schema up to date (${liveActivity.size} live session(s))`);
  goalsStore.refreshPresets(goals.safeZone(goals.DEFAULT_TZ)).catch(() => {});   // warm the goal preset cache (silent failure)
  engageStore.refreshMedianRows().then(() => activityFeed.warm()).catch(() => {}); // warm the challenge targets and the feed
}

// Migrate first so no request can see the old schema; if Postgres is down the
// server still starts (health reports 503) and the next boot migrates.
const ready = prepareDatabase().catch((err) => console.error("  Database: Postgres UNREACHABLE or migration failed —", err.message));

ready.then(() => server.listen(PORT, "0.0.0.0", () => {
  console.log(`VR Fitness backend on http://localhost:${PORT}`);
  setInterval(() => sweepStaleSessions().catch((err) => console.error("[live] sweep failed:", err.message)), SWEEP_INTERVAL_MS).unref();
  for (const url of lanUrls()) console.log(`  LAN / headset: ${url}`);
  console.log(`  CORS: localhost + ${ALLOWED_ORIGINS.length ? `${ALLOWED_ORIGINS.length} origin(s) from ALLOWED_ORIGINS` : "no ALLOWED_ORIGINS set"}`);
  console.log("  GET    /api/health | /api/stats | /api/players | /api/players/:name");
  console.log("  GET    /api/sessions[?player&level&outcome&status&limit] | /api/sessions/:id");
  console.log("  GET    /api/events[?limit&type&sessionId&player] | /api/live[?player]");
  console.log("  GET    /api/overview | /api/public-stats | /api/analytics | /api/export/{sessions,events}.csv");
  console.log("  GET    /api/stream[?player]  (Server-Sent Events)");
  console.log(`  WS     ${broadcast.PATH}?role=streamer|viewer[&room]  (WebRTC signaling) | GET /api/broadcast/status`);
  console.log("  GET    /api/players/:name/achievements | /api/analytics/feedback");
  console.log("  POST   /api/sessions | /api/events (single or batch) | /api/sessions/:id/feedback");
  console.log("  PATCH  /api/players/:name  { newName }");
  console.log("  PUT    /api/players/:name/goal  { metric, target } | GET /api/players/:name/goal/today | /api/players/:name/goals?days=30");
  console.log("  GET    /api/players/:name/{challenges,challenges/history,calendar,week,best} | /api/calendar | /api/activity");
  console.log("  GET    /api/challenge/:sessionId[/check?name=] | /api/live/reactions | POST /api/live/react { sessionId, kind }");
  console.log("  DELETE /api/sessions | /api/sessions/:id");
  if (fs.existsSync(FRONTEND_DIST)) console.log(`  Dashboard: http://localhost:${PORT}/`);
}));
