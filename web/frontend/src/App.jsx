import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { useTelemetry } from "./hooks/useTelemetry.js";
import { filterByRange } from "./lib/derive.js";
import { formatNumber } from "./lib/format.js";
import { LiveProvider, useLiveIds } from "./live/LiveProvider.jsx";
import { MobileNav, Sidebar } from "./components/layout/Sidebar.jsx";
import Topbar from "./components/layout/Topbar.jsx";
import { Banner, DashboardSkeleton } from "./components/ui/Feedback.jsx";
import { ToastStack, useToasts } from "./components/ui/Toasts.jsx";
import { WakeNotice } from "./components/live/LiveFight.jsx";
import Landing from "./views/Landing.jsx";
import DashboardView from "./views/DashboardView.jsx";
import LiveView from "./views/LiveView.jsx";
import SessionsView from "./views/SessionsView.jsx";
import SessionDetailView from "./views/SessionDetailView.jsx";
import LeaderboardView from "./views/LeaderboardView.jsx";
import PlayersView from "./views/PlayersView.jsx";
import PlayerView from "./views/PlayerView.jsx";
import CompareView from "./views/CompareView.jsx";
import AnalyticsView from "./views/AnalyticsView.jsx";
import ExportView from "./views/ExportView.jsx";
import SettingsView from "./views/SettingsView.jsx";

const RANGE_KEY = "vrfitness.range";
const TITLES = {
  "": "Overview",
  live: "Live Arena",
  players: "Players",
  player: "Player",
  sessions: "Sessions",
  session: "Session",
  leaderboard: "Leaderboard",
  compare: "Compare",
  analytics: "Analytics",
  export: "Export",
  connection: "Connection",
};

function readRange() {
  try {
    return localStorage.getItem(RANGE_KEY) || "all";
  } catch {
    return "all";
  }
}

function Dashboard() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const section = pathname.replace(/^\/dashboard\/?/, "").split("/")[0];
  const [range, setRangeState] = useState(readRange);
  const [menuOpen, setMenuOpen] = useState(false);
  const { toasts, push, dismiss } = useToasts();
  const liveCount = useLiveIds().length;

  const onSession = useCallback(
    (s) =>
      push({
        tone: s.outcome === "victory" ? "good" : s.outcome === "defeat" ? "bad" : "info",
        title: `${s.playerName}: ${s.outcome === "victory" ? "Victory" : s.outcome === "defeat" ? "Defeat" : "Battle finished"}`,
        detail: `${s.level} · ${formatNumber(s.score)} pts · ${s.hitCount || 0} hits`,
      }),
    [push]
  );

  const onConnectionChange = useCallback(
    (next, prev) => {
      if (next === "offline" && prev === "live") push({ tone: "bad", title: "Backend disconnected", detail: "Retrying automatically…" });
      if (next === "live" && prev === "offline") push({ tone: "good", title: "Backend reconnected", detail: "Data refreshed." });
    },
    [push]
  );

  const t = useTelemetry({ onSession, onConnectionChange });

  const setRange = (r) => {
    setRangeState(r);
    try {
      localStorage.setItem(RANGE_KEY, r);
    } catch {
      /* private mode — the choice just won't persist */
    }
  };

  const filtered = useMemo(() => filterByRange(t.sessions, range), [t.sessions, range]);
  const openSession = useCallback((id) => navigate(`/dashboard/session/${encodeURIComponent(id)}`), [navigate]);

  useEffect(() => {
    document.title = `${TITLES[section] || "Dashboard"} · STRIKEFIT`;
    window.scrollTo({ top: 0 });
    setMenuOpen(false);
  }, [pathname, section]);

  const offline = !t.loading && t.connection === "offline" && !t.health?.ok;
  // Pages that load their own data don't need to wait for the telemetry snapshot.
  const needsTelemetry = ["", "sessions", "leaderboard", "players", "connection"].includes(section);

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:shadow-lg">
        Skip to content
      </a>
      <Sidebar connection={t.connection} liveCount={liveCount} health={t.health} />
      <MobileNav open={menuOpen} onClose={() => setMenuOpen(false)} liveCount={liveCount} />

      <div className="min-w-0 lg:pl-[var(--sidebar-w)]">
        <Topbar connection={t.connection} onMenu={() => setMenuOpen(true)} />
        <main id="main" className="mx-auto max-w-7xl space-y-6 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          <WakeNotice />
          {offline && section !== "connection" && (
            <Banner
              title="Can't reach the backend"
              action={
                <Link
                  to="/dashboard/connection"
                  className="rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 text-xs font-medium text-[var(--ink-secondary)] shadow-sm hover:text-[var(--ink)]"
                >
                  Connection help
                </Link>
              }
            >
              The dashboard reconnects on its own, checks for live fights every 3 seconds meanwhile, and shows the last data it loaded.
            </Banner>
          )}

          {t.loading && needsTelemetry ? (
            <DashboardSkeleton />
          ) : (
            <div key={section} className="view-in min-w-0">
              <Routes>
                <Route index element={<DashboardView sessions={filtered} allSessions={t.sessions} events={t.events} range={range} onRange={setRange} openSession={openSession} />} />
                <Route path="live" element={<LiveView />} />
                <Route path="players" element={<PlayersView sessions={t.sessions} />} />
                <Route path="player/:name" element={<PlayerView />} />
                <Route path="sessions" element={<SessionsView sessions={filtered} range={range} onRange={setRange} openSession={openSession} />} />
                <Route path="session/:id" element={<SessionDetailView onDeleted={(s) => push({ tone: "info", title: "Session deleted", detail: s ? `${s.playerName} · ${s.level}` : undefined })} />} />
                <Route path="leaderboard" element={<LeaderboardView sessions={t.sessions} openSession={openSession} />} />
                <Route path="compare" element={<CompareView />} />
                <Route path="analytics" element={<AnalyticsView />} />
                <Route path="export" element={<ExportView />} />
                <Route
                  path="connection"
                  element={<SettingsView connection={t.connection} health={t.health} checkHealth={t.checkHealth} pingRoundTrip={t.pingRoundTrip} refresh={t.refresh} toast={push} />}
                />
                <Route path="*" element={<DashboardView sessions={filtered} allSessions={t.sessions} events={t.events} range={range} onRange={setRange} openSession={openSession} />} />
              </Routes>
            </div>
          )}

          <footer className="pt-4 text-center text-xs text-[var(--muted)]">STRIKEFIT · VR combat telemetry</footer>
        </main>
      </div>

      <ToastStack toasts={toasts} onDismiss={dismiss} />
    </div>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Landing />} />
      <Route
        path="/dashboard/*"
        element={
          <LiveProvider>
            <Dashboard />
          </LiveProvider>
        }
      />
      <Route path="*" element={<Landing />} />
    </Routes>
  );
}
