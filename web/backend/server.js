const fs = require("fs");
const path = require("path");
const http = require("http");
const express = require("express");
const cors = require("cors");
const { Server } = require("socket.io");
const { v4: uuidv4 } = require("uuid");

const PORT = Number(process.env.PORT) || 4000;
const DB_PATH = path.join(__dirname, "db.json");
const MAX_EVENTS = 500;

function loadDb() {
  try {
    const raw = fs.readFileSync(DB_PATH, "utf8");
    const data = JSON.parse(raw);
    if (!Array.isArray(data.sessions)) data.sessions = [];
    if (!Array.isArray(data.events)) data.events = [];
    if (!data.meta) data.meta = { version: 1 };
    return data;
  } catch {
    return { sessions: [], events: [], meta: { version: 1, createdAt: new Date().toISOString() } };
  }
}

function saveDb(db) {
  fs.writeFileSync(DB_PATH, JSON.stringify(db, null, 2));
}

function computeStats(sessions) {
  const totalSessions = sessions.length;
  const victories = sessions.filter((s) => s.outcome === "victory").length;
  const defeats = sessions.filter((s) => s.outcome === "defeat").length;
  const totalScore = sessions.reduce((a, s) => a + (Number(s.score) || 0), 0);
  const totalDamage = sessions.reduce((a, s) => a + (Number(s.damageDealt) || 0), 0);
  const totalHits = sessions.reduce((a, s) => a + (Number(s.hitCount) || 0), 0);
  const totalTaps = sessions.reduce((a, s) => a + (Number(s.tapCount) || 0), 0);
  const bestScore = sessions.reduce((m, s) => Math.max(m, Number(s.score) || 0), 0);
  const leaderboard = [...sessions]
    .sort((a, b) => (Number(b.score) || 0) - (Number(a.score) || 0))
    .slice(0, 20)
    .map((s) => ({
      id: s.id,
      playerName: s.playerName,
      score: s.score,
      outcome: s.outcome,
      level: s.level,
      endedAt: s.endedAt,
    }));

  return {
    totalSessions,
    victories,
    defeats,
    totalScore,
    totalDamage: Math.round(totalDamage * 10) / 10,
    totalHits,
    totalTaps,
    bestScore,
    winRate: totalSessions ? Math.round((victories / totalSessions) * 1000) / 10 : 0,
    leaderboard,
  };
}

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
    port: PORT,
    time: new Date().toISOString(),
  });
});

app.get("/api/sessions", (_req, res) => {
  const db = loadDb();
  const sessions = [...db.sessions].sort(
    (a, b) => new Date(b.endedAt || b.startedAt || 0) - new Date(a.endedAt || a.startedAt || 0)
  );
  res.json({ sessions, count: sessions.length });
});

app.get("/api/stats", (_req, res) => {
  const db = loadDb();
  res.json(computeStats(db.sessions));
});

app.get("/api/events", (req, res) => {
  const db = loadDb();
  const limit = Math.min(Number(req.query.limit) || 100, 500);
  const events = [...db.events].slice(-limit).reverse();
  res.json({ events, count: events.length });
});

app.post("/api/sessions", (req, res) => {
  const body = req.body || {};
  const db = loadDb();
  const now = new Date().toISOString();

  const session = {
    id: body.id || uuidv4(),
    playerName: String(body.playerName || "Player1"),
    level: String(body.level || "Easy"),
    outcome: String(body.outcome || "unknown"),
    score: Number(body.score) || 0,
    damageDealt: Number(body.damageDealt) || 0,
    hitCount: Number(body.hitCount) || 0,
    tapCount: Number(body.tapCount) || 0,
    durationSec: Number(body.durationSec) || 0,
    playerHpRemaining: Number(body.playerHpRemaining) || 0,
    monsterHpRemaining: Number(body.monsterHpRemaining) || 0,
    pointsEarned: Number(body.pointsEarned ?? body.score) || 0,
    startedAt: body.startedAt || now,
    endedAt: body.endedAt || now,
    source: body.source || "unity",
    notes: body.notes || "",
  };

  db.sessions.push(session);

  const event = {
    id: uuidv4(),
    type: "session_complete",
    sessionId: session.id,
    playerName: session.playerName,
    message: `${session.playerName} finished ${session.level}: ${session.outcome} — score ${session.score}`,
    payload: session,
    at: now,
  };
  db.events.push(event);
  if (db.events.length > MAX_EVENTS) db.events = db.events.slice(-MAX_EVENTS);

  saveDb(db);

  const stats = computeStats(db.sessions);
  io.emit("session", session);
  io.emit("event", event);
  io.emit("stats", stats);

  console.log(`[session] ${session.playerName} ${session.outcome} score=${session.score}`);
  res.status(201).json({ ok: true, session, stats });
});

app.post("/api/events", (req, res) => {
  const body = req.body || {};
  const db = loadDb();
  const now = new Date().toISOString();

  const event = {
    id: uuidv4(),
    type: String(body.type || "hit"),
    sessionId: body.sessionId || null,
    playerName: String(body.playerName || "Player1"),
    message: String(body.message || body.type || "event"),
    payload: body.payload || body,
    at: now,
  };

  db.events.push(event);
  if (db.events.length > MAX_EVENTS) db.events = db.events.slice(-MAX_EVENTS);
  saveDb(db);

  io.emit("event", event);
  res.status(201).json({ ok: true, event });
});

app.delete("/api/sessions", (_req, res) => {
  const db = loadDb();
  db.sessions = [];
  db.events = [];
  saveDb(db);
  const stats = computeStats(db.sessions);
  io.emit("stats", stats);
  io.emit("cleared", { at: new Date().toISOString() });
  res.json({ ok: true, message: "Cleared sessions and events" });
});

io.on("connection", (socket) => {
  const db = loadDb();
  socket.emit("stats", computeStats(db.sessions));
  socket.emit("sessions", db.sessions.slice(-50).reverse());
  socket.emit("events", db.events.slice(-50).reverse());
  console.log(`[socket] connected ${socket.id}`);
});

server.listen(PORT, "0.0.0.0", () => {
  console.log(`VR Fitness backend on http://localhost:${PORT}`);
  console.log(`  GET  /api/health`);
  console.log(`  GET  /api/sessions`);
  console.log(`  GET  /api/stats`);
  console.log(`  POST /api/sessions`);
  console.log(`  POST /api/events`);
  console.log(`  Socket.io realtime enabled`);
});
