const fs = require("fs");
const os = require("os");
const path = require("path");
const http = require("http");
const express = require("express");
const cors = require("cors");
const { Server } = require("socket.io");
const { v4: uuidv4 } = require("uuid");

const PORT = Number(process.env.PORT) || 4000;
const DB_PATH = process.env.DB_PATH ? path.resolve(process.env.DB_PATH) : path.join(__dirname, "db.json");
const FRONTEND_DIST = path.join(__dirname, "..", "frontend", "dist");
const MAX_EVENTS = 1000;
const LEVEL_ORDER = ["Easy", "Medium", "Hard"];
const STARTED_AT = Date.now();

// ---------------------------------------------------------------------------
// Storage — in-memory copy of db.json, written atomically (tmp file + rename)
// so a crash mid-write can never leave a half-written database behind.
// ---------------------------------------------------------------------------

function emptyDb() {
  return { sessions: [], events: [], meta: { version: 2, createdAt: new Date().toISOString() } };
}

function loadDb() {
  try {
    const data = JSON.parse(fs.readFileSync(DB_PATH, "utf8"));
    if (!Array.isArray(data.sessions)) data.sessions = [];
    if (!Array.isArray(data.events)) data.events = [];
    if (!data.meta) data.meta = { version: 2 };
    return data;
  } catch (err) {
    if (err.code !== "ENOENT") console.warn(`[db] could not read ${DB_PATH}: ${err.message} — starting empty`);
    return emptyDb();
  }
}

let db = loadDb();

function saveDb() {
  const tmp = `${DB_PATH}.tmp`;
  fs.writeFileSync(tmp, JSON.stringify(db, null, 2));
  fs.renameSync(tmp, DB_PATH);
}

// ---------------------------------------------------------------------------
// Normalisation & stats
// ---------------------------------------------------------------------------

const num = (v) => {
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
};
const round1 = (n) => Math.round(n * 10) / 10;
const endTime = (s) => new Date(s.endedAt || s.startedAt || 0).getTime();

function normalizeOutcome(value) {
  const v = String(value || "unknown").trim().toLowerCase();
  if (["victory", "win", "won"].includes(v)) return "victory";
  if (["defeat", "loss", "lost", "lose"].includes(v)) return "defeat";
  return v;
}

function normalizeSession(body, existing) {
  const now = new Date().toISOString();
  const base = existing || {};
  return {
    id: String(body.id || base.id || uuidv4()),
    playerName: String(body.playerName || base.playerName || "Player1").trim() || "Player1",
    level: String(body.level || base.level || "Easy"),
    outcome: normalizeOutcome(body.outcome ?? base.outcome),
    score: num(body.score ?? base.score),
    damageDealt: num(body.damageDealt ?? base.damageDealt),
    hitCount: num(body.hitCount ?? base.hitCount),
    tapCount: num(body.tapCount ?? base.tapCount),
    durationSec: num(body.durationSec ?? base.durationSec),
    playerHpRemaining: num(body.playerHpRemaining ?? base.playerHpRemaining),
    monsterHpRemaining: num(body.monsterHpRemaining ?? base.monsterHpRemaining),
    pointsEarned: num(body.pointsEarned ?? body.score ?? base.pointsEarned),
    startedAt: body.startedAt || base.startedAt || now,
    endedAt: body.endedAt || base.endedAt || now,
    source: String(body.source || base.source || "unity"),
    notes: String(body.notes ?? base.notes ?? ""),
  };
}

function sortedSessions() {
  return [...db.sessions].sort((a, b) => endTime(b) - endTime(a));
}

function summarize(sessions) {
  const n = sessions.length;
  const victories = sessions.filter((s) => s.outcome === "victory").length;
  const sum = (key) => sessions.reduce((a, s) => a + num(s[key]), 0);
  return {
    sessions: n,
    victories,
    defeats: sessions.filter((s) => s.outcome === "defeat").length,
    winRate: n ? round1((victories / n) * 100) : 0,
    bestScore: sessions.reduce((m, s) => Math.max(m, num(s.score)), 0),
    avgScore: n ? Math.round(sum("score") / n) : 0,
    avgDurationSec: n ? round1(sum("durationSec") / n) : 0,
  };
}

function computeStats(sessions) {
  const totalSessions = sessions.length;
  const victories = sessions.filter((s) => s.outcome === "victory").length;
  const defeats = sessions.filter((s) => s.outcome === "defeat").length;
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
    avgScore: totalSessions ? Math.round(sum("score") / totalSessions) : 0,
    avgDurationSec: totalSessions ? round1(sum("durationSec") / totalSessions) : 0,
    winRate: totalSessions ? round1((victories / totalSessions) * 100) : 0,
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

function pushEvent(event) {
  db.events.push(event);
  if (db.events.length > MAX_EVENTS) db.events = db.events.slice(-MAX_EVENTS);
}

// ---------------------------------------------------------------------------
// HTTP + realtime
// ---------------------------------------------------------------------------

const app = express();
const server = http.createServer(app);
const io = new Server(server, {
  cors: { origin: "*", methods: ["GET", "POST", "DELETE"] },
});

app.use(cors());
app.use(express.json({ limit: "1mb" }));

app.get("/api/health", (_req, res) => {
  res.json({
    ok: true,
    service: "vr-fitness-backend",
    version: 2,
    port: PORT,
    time: new Date().toISOString(),
    uptimeSec: Math.round((Date.now() - STARTED_AT) / 1000),
    sessions: db.sessions.length,
    events: db.events.length,
    clients: io.engine.clientsCount,
    lanUrls: lanUrls(),
  });
});

app.get("/api/sessions", (req, res) => {
  const { player, level, outcome } = req.query;
  let sessions = sortedSessions();
  if (player) sessions = sessions.filter((s) => s.playerName === player);
  if (level) sessions = sessions.filter((s) => s.level === level);
  if (outcome) sessions = sessions.filter((s) => s.outcome === normalizeOutcome(outcome));
  const limit = Number(req.query.limit);
  if (limit > 0) sessions = sessions.slice(0, limit);
  res.json({ sessions, count: sessions.length });
});

app.get("/api/sessions/:id", (req, res) => {
  const session = db.sessions.find((s) => s.id === req.params.id);
  if (!session) return res.status(404).json({ ok: false, error: "Session not found" });
  const events = db.events.filter((e) => e.sessionId === session.id).sort((a, b) => new Date(a.at) - new Date(b.at));
  res.json({ session, events });
});

app.get("/api/stats", (_req, res) => {
  res.json(computeStats(db.sessions));
});

app.get("/api/players", (_req, res) => {
  const players = computePlayers(db.sessions);
  res.json({ players, count: players.length });
});

app.get("/api/events", (req, res) => {
  const limit = Math.min(Number(req.query.limit) || 100, MAX_EVENTS);
  let events = db.events;
  if (req.query.type) events = events.filter((e) => e.type === req.query.type);
  if (req.query.sessionId) events = events.filter((e) => e.sessionId === req.query.sessionId);
  events = events.slice(-limit).reverse();
  res.json({ events, count: events.length });
});

// Unity posts here when a battle ends. Re-posting the same id updates that
// session instead of creating a duplicate.
app.post("/api/sessions", (req, res) => {
  const body = req.body || {};
  const index = body.id ? db.sessions.findIndex((s) => s.id === String(body.id)) : -1;
  const session = normalizeSession(body, index >= 0 ? db.sessions[index] : null);
  if (index >= 0) db.sessions[index] = session;
  else db.sessions.push(session);

  const event = {
    id: uuidv4(),
    type: "session_complete",
    sessionId: session.id,
    playerName: session.playerName,
    message: `${session.playerName} finished ${session.level}: ${session.outcome} — score ${session.score}`,
    payload: session,
    at: new Date().toISOString(),
  };
  pushEvent(event);
  saveDb();

  const stats = computeStats(db.sessions);
  io.emit("session", session);
  io.emit("event", event);
  io.emit("stats", stats);

  console.log(`[session] ${session.playerName} ${session.level} ${session.outcome} score=${session.score}`);
  res.status(index >= 0 ? 200 : 201).json({ ok: true, updated: index >= 0, session, stats });
});

app.post("/api/events", (req, res) => {
  const body = req.body || {};
  const event = {
    id: uuidv4(),
    type: String(body.type || "hit"),
    sessionId: body.sessionId ? String(body.sessionId) : null,
    playerName: String(body.playerName || "Player1"),
    message: String(body.message || body.type || "event"),
    payload: body.payload ?? null,
    at: new Date().toISOString(),
  };

  // "ping" events are connection checks from the dashboard — broadcast, never stored.
  if (event.type !== "ping") {
    pushEvent(event);
    saveDb();
  }

  io.emit("event", event);
  res.status(201).json({ ok: true, event });
});

app.delete("/api/sessions/:id", (req, res) => {
  const before = db.sessions.length;
  db.sessions = db.sessions.filter((s) => s.id !== req.params.id);
  if (db.sessions.length === before) return res.status(404).json({ ok: false, error: "Session not found" });
  db.events = db.events.filter((e) => e.sessionId !== req.params.id);
  saveDb();
  io.emit("session_deleted", { id: req.params.id });
  io.emit("stats", computeStats(db.sessions));
  res.json({ ok: true });
});

app.delete("/api/sessions", (_req, res) => {
  db.sessions = [];
  db.events = [];
  saveDb();
  io.emit("stats", computeStats(db.sessions));
  io.emit("cleared", { at: new Date().toISOString() });
  res.json({ ok: true, message: "Cleared sessions and events" });
});

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
  res.status(status).json({ ok: false, error: status === 400 ? "Invalid JSON body" : "Server error" });
});

io.on("connection", (socket) => {
  socket.emit("stats", computeStats(db.sessions));
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

server.listen(PORT, "0.0.0.0", () => {
  console.log(`VR Fitness backend on http://localhost:${PORT}  (db: ${DB_PATH})`);
  for (const url of lanUrls()) console.log(`  LAN / headset: ${url}`);
  console.log("  GET    /api/health | /api/stats | /api/players");
  console.log("  GET    /api/sessions[?player&level&outcome&limit] | /api/sessions/:id");
  console.log("  GET    /api/events[?limit&type&sessionId]");
  console.log("  POST   /api/sessions | /api/events");
  console.log("  DELETE /api/sessions | /api/sessions/:id");
  if (fs.existsSync(FRONTEND_DIST)) console.log(`  Dashboard: http://localhost:${PORT}/`);
});
