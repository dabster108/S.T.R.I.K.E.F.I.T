import { useEffect, useState } from "react";
import { io } from "socket.io-client";

const API_BASE = import.meta.env.VITE_API_URL || "http://localhost:4000";

function formatTime(iso) {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export default function App() {
  const [stats, setStats] = useState(null);
  const [sessions, setSessions] = useState([]);
  const [events, setEvents] = useState([]);
  const [connected, setConnected] = useState(false);
  const [liveFlash, setLiveFlash] = useState(null);

  useEffect(() => {
    let cancelled = false;

    async function bootstrap() {
      try {
        const [sRes, sessRes, evRes] = await Promise.all([
          fetch(`${API_BASE}/api/stats`),
          fetch(`${API_BASE}/api/sessions`),
          fetch(`${API_BASE}/api/events?limit=40`),
        ]);
        if (cancelled) return;
        if (sRes.ok) setStats(await sRes.json());
        if (sessRes.ok) {
          const data = await sessRes.json();
          setSessions(data.sessions || []);
        }
        if (evRes.ok) {
          const data = await evRes.json();
          setEvents(data.events || []);
        }
      } catch (err) {
        console.warn("API bootstrap failed (is backend running?)", err);
      }
    }

    bootstrap();

    const socket = io(API_BASE || undefined, {
      transports: ["websocket", "polling"],
    });

    socket.on("connect", () => setConnected(true));
    socket.on("disconnect", () => setConnected(false));

    socket.on("stats", (payload) => setStats(payload));
    socket.on("sessions", (list) => setSessions(Array.isArray(list) ? list : []));
    socket.on("events", (list) => setEvents(Array.isArray(list) ? list : []));

    socket.on("session", (session) => {
      setSessions((prev) => [session, ...prev.filter((s) => s.id !== session.id)]);
      setLiveFlash({
        title: session.outcome === "victory" ? "Victory" : session.outcome === "defeat" ? "Defeat" : "Session",
        detail: `${session.playerName} · ${session.score} pts · ${session.hitCount || 0} hits`,
      });
      setTimeout(() => setLiveFlash(null), 3500);
    });

    socket.on("event", (event) => {
      setEvents((prev) => [event, ...prev].slice(0, 80));
    });

    socket.on("cleared", () => {
      setSessions([]);
      setEvents([]);
      setStats({
        totalSessions: 0,
        victories: 0,
        defeats: 0,
        totalScore: 0,
        totalDamage: 0,
        totalHits: 0,
        totalTaps: 0,
        bestScore: 0,
        winRate: 0,
        leaderboard: [],
      });
    });

    return () => {
      cancelled = true;
      socket.disconnect();
    };
  }, []);

  async function clearAll() {
    if (!confirm("Clear all tracked sessions from db.json?")) return;
    await fetch(`${API_BASE}/api/sessions`, { method: "DELETE" });
  }

  const s = stats || {};

  return (
    <div className="page">
      <header className="hero">
        <p className="eyebrow">VR Fitness Monster Battle</p>
        <h1>Live Stats Board</h1>
        <p className="sub">
          Sessions, score, hits, and points from Unity — stored in <code>db.json</code>
          {connected ? " · realtime connected" : " · connecting…"}
        </p>
        <div className={`pill ${connected ? "on" : "off"}`}>
          {connected ? "LIVE" : "OFFLINE"}
        </div>
      </header>

      {liveFlash && (
        <div className="flash" role="status">
          <strong>{liveFlash.title}</strong>
          <span>{liveFlash.detail}</span>
        </div>
      )}

      <section className="metrics">
        <Metric label="Sessions" value={s.totalSessions ?? 0} />
        <Metric label="Best Score" value={s.bestScore ?? 0} />
        <Metric label="Total Points" value={s.totalScore ?? 0} />
        <Metric label="Hits Landed" value={s.totalHits ?? 0} />
        <Metric label="Damage Dealt" value={s.totalDamage ?? 0} />
        <Metric label="Win Rate" value={`${s.winRate ?? 0}%`} />
        <Metric label="Victories" value={s.victories ?? 0} />
        <Metric label="Defeats" value={s.defeats ?? 0} />
      </section>

      <div className="grid">
        <section className="panel">
          <div className="panel-head">
            <h2>Recent sessions</h2>
            <button type="button" className="ghost" onClick={clearAll}>
              Clear db
            </button>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>When</th>
                  <th>Player</th>
                  <th>Level</th>
                  <th>Outcome</th>
                  <th>Score</th>
                  <th>Hits</th>
                  <th>Damage</th>
                  <th>Taps</th>
                </tr>
              </thead>
              <tbody>
                {sessions.length === 0 && (
                  <tr>
                    <td colSpan={8} className="empty">
                      No sessions yet. Play a round in Unity with the backend running.
                    </td>
                  </tr>
                )}
                {sessions.map((row) => (
                  <tr key={row.id}>
                    <td>{formatTime(row.endedAt || row.startedAt)}</td>
                    <td>{row.playerName}</td>
                    <td>{row.level}</td>
                    <td>
                      <span className={`tag ${row.outcome}`}>{row.outcome}</span>
                    </td>
                    <td>{row.score}</td>
                    <td>{row.hitCount ?? 0}</td>
                    <td>{Math.round(row.damageDealt || 0)}</td>
                    <td>{row.tapCount ?? 0}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section className="panel">
          <div className="panel-head">
            <h2>Live event feed</h2>
          </div>
          <ul className="feed">
            {events.length === 0 && <li className="empty">Waiting for hits / session events…</li>}
            {events.map((ev) => (
              <li key={ev.id}>
                <time>{formatTime(ev.at)}</time>
                <strong>{ev.type}</strong>
                <span>{ev.message}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      <section className="panel leaderboard">
        <div className="panel-head">
          <h2>Leaderboard</h2>
        </div>
        <ol>
          {(s.leaderboard || []).length === 0 && <li className="empty">No scores yet</li>}
          {(s.leaderboard || []).map((row, i) => (
            <li key={row.id}>
              <span className="rank">#{i + 1}</span>
              <span className="name">{row.playerName}</span>
              <span className="score">{row.score} pts</span>
              <span className={`tag ${row.outcome}`}>{row.outcome}</span>
            </li>
          ))}
        </ol>
      </section>

      <footer>
        Backend: <code>web/BACKEND_URL.json</code> · Unity posts to <code>/api/sessions</code>
      </footer>
    </div>
  );
}

function Metric({ label, value }) {
  return (
    <div className="metric">
      <span className="label">{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
