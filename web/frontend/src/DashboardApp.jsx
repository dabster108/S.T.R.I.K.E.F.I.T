import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { useTelemetry } from "./hooks/useTelemetry.js";
import { filterByRange } from "./lib/derive.js";
import { formatNumber } from "./lib/format.js";
import { LiveProvider, useLiveIds, useLiveMessages } from "./live/LiveProvider.jsx";
import { MobileNav, Sidebar } from "./components/layout/Sidebar.jsx";
import Topbar from "./components/layout/Topbar.jsx";
import CommandPalette from "./components/layout/CommandPalette.jsx";
import { ToastStack, useToasts } from "./components/ui/Toasts.jsx";
import {
  AnalyticsSkeleton,
  CompareSkeleton,
  GenericSkeleton,
  LeaderboardSkeleton,
  LiveSkeleton,
  OverviewSkeleton,
  PlayerSkeleton,
  PlayersSkeleton,
  SessionDetailSkeleton,
  SessionsSkeleton,
} from "./components/ui/Skeletons.jsx";

const DashboardView = lazy(() => import("./views/DashboardView.jsx"));
const LiveView = lazy(() => import("./views/LiveView.jsx"));
const DashboardBroadcastView = lazy(() => import("./views/BroadcastView.jsx").then((m) => ({ default: m.DashboardBroadcastView })));
const SessionsView = lazy(() => import("./views/SessionsView.jsx"));
const SessionDetailView = lazy(() => import("./views/SessionDetailView.jsx"));
const LeaderboardView = lazy(() => import("./views/LeaderboardView.jsx"));
const PlayersView = lazy(() => import("./views/PlayersView.jsx"));
const PlayerView = lazy(() => import("./views/PlayerView.jsx"));
const CompareView = lazy(() => import("./views/CompareView.jsx"));
const AnalyticsView = lazy(() => import("./views/AnalyticsView.jsx"));
const EngagementView = lazy(() => import("./views/EngagementView.jsx"));
const ExportView = lazy(() => import("./views/ExportView.jsx"));
const SettingsView = lazy(() => import("./views/SettingsView.jsx"));
const GuideView = lazy(() => import("./views/GuideView.jsx"));

const RANGE_KEY = "vrfitness.range";
const SIDEBAR_KEY = "strikefit.sidebar";
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
  engagement: "Engagement",
  export: "Export",
  connection: "Connection",
  guide: "User guide",
};
const SKELETONS = {
  "": OverviewSkeleton,
  live: LiveSkeleton,
  players: PlayersSkeleton,
  player: PlayerSkeleton,
  sessions: SessionsSkeleton,
  session: SessionDetailSkeleton,
  leaderboard: LeaderboardSkeleton,
  compare: CompareSkeleton,
  analytics: AnalyticsSkeleton,
};

const read = (key, fallback) => {
  try {
    return localStorage.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
};
const write = (key, value) => {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* private mode — the choice just won't persist */
  }
};

function Dashboard() {
  const location = useLocation();
  const { pathname } = location;
  // A rename swaps the player's URL in place: same page instance, no remount or scroll jump.
  const renamed = Boolean(location.state?.renamedFrom);
  const pageKey = useRef(pathname);
  if (!renamed) pageKey.current = pathname;
  const navigate = useNavigate();
  const section = pathname.replace(/^\/dashboard\/?/, "").split("/")[0];
  const [range, setRangeState] = useState(() => read(RANGE_KEY, "all"));
  const [collapsed, setCollapsed] = useState(() => read(SIDEBAR_KEY, "0") === "1");
  const [menuOpen, setMenuOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);
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

  const t = useTelemetry({ onSession });

  // Every rename, from this tab or any other, confirms once here.
  useLiveMessages((type, payload) => {
    if (type !== "player_renamed") return;
    push({ tone: "good", title: "Player renamed", detail: `${payload.oldName} → ${payload.newName}` });
  });

  const setRange = (r) => {
    setRangeState(r);
    write(RANGE_KEY, r);
  };
  const toggleSidebar = () =>
    setCollapsed((c) => {
      write(SIDEBAR_KEY, c ? "0" : "1");
      return !c;
    });

  const filtered = useMemo(() => filterByRange(t.sessions, range), [t.sessions, range]);
  const openSession = useCallback((id) => navigate(`/dashboard/session/${encodeURIComponent(id)}`), [navigate]);

  useEffect(() => {
    document.title = `${TITLES[section] || "Dashboard"} · STRIKEFIT`;
    if (renamed) return;
    window.scrollTo({ top: 0 });
    setMenuOpen(false);
  }, [pathname, section, renamed]);

  useEffect(() => {
    const onKey = (e) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPaletteOpen((o) => !o);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  // Pages that load their own data don't need to wait for the telemetry snapshot.
  const needsTelemetry = ["", "sessions", "leaderboard", "players", "connection"].includes(section);
  const Fallback = SKELETONS[section] || GenericSkeleton;
  const overview = <DashboardView sessions={filtered} allSessions={t.sessions} events={t.events} range={range} onRange={setRange} openSession={openSession} />;

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]" style={{ "--sb": collapsed ? "var(--sidebar-w-collapsed)" : "var(--sidebar-w)" }}>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:font-semibold focus:shadow-lg"
      >
        Skip to content
      </a>
      <Sidebar connection={t.connection} liveCount={liveCount} collapsed={collapsed} onToggle={toggleSidebar} />
      <MobileNav open={menuOpen} onClose={() => setMenuOpen(false)} liveCount={liveCount} connection={t.connection} />

      <div className="min-w-0 lg:pl-[var(--sb)]">
        <Topbar onMenu={() => setMenuOpen(true)} onSearch={() => setPaletteOpen(true)} />
        <main id="main" className="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          {t.loading && needsTelemetry ? (
            <Fallback />
          ) : (
            <div key={pageKey.current} className="page-in min-w-0">
              <Suspense fallback={<Fallback />}>
                <Routes>
                  <Route index element={overview} />
                  <Route path="live" element={<LiveView />} />
                  <Route path="broadcast" element={<DashboardBroadcastView />} />
                  <Route path="players" element={<PlayersView sessions={t.sessions} />} />
                  <Route path="player/:name" element={<PlayerView />} />
                  <Route path="sessions" element={<SessionsView sessions={filtered} range={range} onRange={setRange} openSession={openSession} />} />
                  <Route
                    path="session/:id"
                    element={<SessionDetailView onDeleted={(s) => push({ tone: "info", title: "Session deleted", detail: s ? `${s.playerName} · ${s.level}` : undefined })} />}
                  />
                  <Route path="leaderboard" element={<LeaderboardView sessions={t.sessions} openSession={openSession} />} />
                  <Route path="compare" element={<CompareView />} />
                  <Route path="analytics" element={<AnalyticsView />} />
                  <Route path="engagement" element={<EngagementView />} />
                  <Route path="export" element={<ExportView />} />
                  <Route path="guide" element={<GuideView />} />
                  <Route
                    path="connection"
                    element={<SettingsView connection={t.connection} health={t.health} checkHealth={t.checkHealth} pingRoundTrip={t.pingRoundTrip} refresh={t.refresh} toast={push} />}
                  />
                  <Route path="*" element={overview} />
                </Routes>
              </Suspense>
            </div>
          )}

          <footer className="pt-10 text-center text-xs font-semibold text-[var(--muted)]">
            STRIKEFIT · VR combat telemetry · <kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">Ctrl</kbd> +{" "}
            <kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">K</kbd> to jump anywhere
          </footer>
        </main>
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
      <ToastStack toasts={toasts} onDismiss={dismiss} />
    </div>
  );
}

export default function DashboardApp() {
  return (
    <LiveProvider>
      <Dashboard />
    </LiveProvider>
  );
}
