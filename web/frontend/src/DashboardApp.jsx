import { lazy, Suspense, useCallback, useEffect, useMemo, useReducer, useRef, useState } from "react";
import { Link, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { useTelemetry } from "./hooks/useTelemetry.js";
import { filterByRange } from "./lib/derive.js";
import { GO_WINDOW_MS, goTarget } from "./lib/shortcuts.js";
import { TITLES } from "./lib/breadcrumbs.js";
import { formatNumber } from "./lib/format.js";
import { LiveProvider, useLiveIds, useLiveMessages } from "./live/LiveProvider.jsx";
import { liveStore } from "./live/liveStore.js";
import { MobileNav, Sidebar } from "./components/layout/Sidebar.jsx";
import Topbar from "./components/layout/Topbar.jsx";
import CommandPalette from "./components/layout/CommandPalette.jsx";
import ShortcutsOverlay, { isTypingTarget } from "./components/layout/ShortcutsOverlay.jsx";
import { ToastStack, useToasts } from "./components/ui/Toasts.jsx";
import { anotherModalOpen, AUTO_START_DELAY_MS, hasSeenTour, markTourSeen, TOUR_EVENT, tourAutoStartAllowed } from "./components/onboarding/tourState.js";
import {
  AnalyticsSkeleton,
  CompareSkeleton,
  EngagementSkeleton,
  ExportSkeleton,
  LabSkeleton,
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
const SettingsView = lazy(() => import("./views/SettingsView.jsx"));
const DashboardNotFound = lazy(() => import("./views/NotFoundView.jsx").then((m) => ({ default: m.DashboardNotFound })));
const LabView = lazy(() => import("./views/LabView.jsx"));
const EngagementView = lazy(() => import("./views/EngagementView.jsx"));
const ExportView = lazy(() => import("./views/ExportView.jsx"));
const GuideView = lazy(() => import("./views/GuideView.jsx"));
const StoryView = lazy(() => import("./views/StoryView.jsx"));
const Tour = lazy(() => import("./components/onboarding/Tour.jsx"));

const RANGE_KEY = "vrfitness.range";
const SIDEBAR_KEY = "strikefit.sidebar";
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
  lab: LabSkeleton,
  export: ExportSkeleton,
  engagement: EngagementSkeleton,
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
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const { toasts, push, dismiss } = useToasts();
  // Fights that are being played, not ones lingering for their result moment: the badge must agree with the Live Arena page.
  const liveIds = useLiveIds();
  const [, recount] = useReducer((n) => n + 1, 0);
  useLiveMessages((type) => (type === "session_started" || type === "session_finished" || type === "session_deleted") && recount());
  const liveCount = liveIds.filter((id) => liveStore.getSession(id)?.session.status === "live").length;

  const onSession = useCallback(
    (s) =>
      push({
        tone: s.outcome === "victory" ? "good" : s.outcome === "defeat" ? "bad" : "info",
        title: `${s.playerName}: ${s.outcome === "victory" ? "Victory" : s.outcome === "defeat" ? "Defeat" : "Battle finished"}`,
        detail: `${s.level} · ${formatNumber(s.score)} pts · ${s.hitCount || 0} hits`,
      }),
    [push]
  );

  // The recent-events list (about 130 kB) only feeds the Overview's "Latest activity": every other page skips it.
  const t = useTelemetry({ onSession, withEvents: section === "" });

  // Guided tour. It starts by itself only when the visitor is on the Overview, has not seen it, and nothing else is open;
  // it waits AUTO_START_DELAY_MS so the page has painted, and re-checks all of that when the wait is over. The header's
  // Guide button always opens it (replays never change the flag). The "seen" flag is saved when the tour closes any way
  // (Done, Skip, X, Esc, click outside), but only if it really opened (tourOpened), never for a start that did not happen.
  const [touring, setTouring] = useState(false);
  const tourOpened = useRef(false);
  const overlayOpen = useRef(false);
  overlayOpen.current = paletteOpen || menuOpen || shortcutsOpen;
  useEffect(() => {
    const open = () => setTouring(true);
    window.addEventListener(TOUR_EVENT, open);
    return () => window.removeEventListener(TOUR_EVENT, open);
  }, []);
  useEffect(() => {
    if (!tourAutoStartAllowed({ pathname, seen: hasSeenTour(), modalOpen: false })) return undefined;
    const timer = setTimeout(() => {
      if (tourAutoStartAllowed({ pathname: window.location.pathname, seen: hasSeenTour(), modalOpen: overlayOpen.current || anotherModalOpen() })) setTouring(true);
    }, AUTO_START_DELAY_MS);
    return () => clearTimeout(timer); // leaving the Overview inside the wait cancels it
  }, [pathname]);
  const closeTour = useCallback(() => {
    setTouring(false);
    if (tourOpened.current) markTourSeen();
    tourOpened.current = false;
  }, []);

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
    document.title = `${TITLES[section] || "Page not found"} · STRIKEFIT`;
    if (renamed) return;
    window.scrollTo({ top: 0 });
    setMenuOpen(false);
  }, [pathname, section, renamed]);

  useEffect(() => {
    let armedAt = 0; // "G" pressed: the next letter jumps to a page
    const onKey = (e) => {
      const plain = !e.metaKey && !e.ctrlKey && !e.altKey;
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPaletteOpen((o) => !o);
      } else if (e.key === "?" && plain && !isTypingTarget(e.target)) {
        e.preventDefault();
        setShortcutsOpen((o) => !o);
      } else if (plain && !e.shiftKey && !isTypingTarget(e.target) && !overlayOpen.current && !anotherModalOpen()) {
        const k = e.key.toLowerCase();
        if (armedAt && Date.now() - armedAt < GO_WINDOW_MS && goTarget(k)) {
          e.preventDefault();
          armedAt = 0;
          navigate(goTarget(k));
        } else {
          armedAt = k === "g" ? Date.now() : 0;
        }
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [navigate]);

  // Pages that load their own data don't need to wait for the telemetry snapshot.
  const needsTelemetry = ["", "sessions", "leaderboard", "players"].includes(section);
  const Fallback = SKELETONS[section] || GenericSkeleton;
  const overview = <DashboardView sessions={filtered} allSessions={t.sessions} events={t.events} eventsReady={t.eventsReady} range={range} onRange={setRange} openSession={openSession} />;

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]" style={{ "--sb": collapsed ? "var(--sidebar-w-collapsed)" : "var(--sidebar-w)" }}>
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:font-semibold focus:shadow-lg"
      >
        Skip to content
      </a>
      <Sidebar liveCount={liveCount} collapsed={collapsed} onToggle={toggleSidebar} />
      <MobileNav open={menuOpen} onClose={() => setMenuOpen(false)} liveCount={liveCount} />

      <div className="min-w-0 lg:pl-[var(--sb)]">
        <Topbar onMenu={() => setMenuOpen(true)} onSearch={() => setPaletteOpen(true)} />
        {/* the Live Arena gets a wider column: it is meant to be read from across a room on a projector */}
        <main id="main" className={`mx-auto min-h-[calc(100svh-4.5rem)] px-4 py-6 sm:px-6 lg:px-8 lg:py-8 ${section === "live" ? "max-w-[1760px]" : "max-w-7xl"}`}>
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
                  <Route path="lab" element={<LabView />} />
                  <Route path="engagement" element={<EngagementView />} />
                  <Route path="export" element={<ExportView />} />
                  <Route path="guide" element={<GuideView />} />
                  {/* the Connection page was linked from the top bar, the guide and the palette but had lost its route */}
                  <Route
                    path="connection"
                    element={<SettingsView connection={t.connection} health={t.health} checkHealth={t.checkHealth} pingRoundTrip={t.pingRoundTrip} refresh={t.refresh} toast={push} />}
                  />
                  <Route path="story" element={<Suspense fallback={<GenericSkeleton />}><StoryView /></Suspense>} />
                  <Route path="*" element={<DashboardNotFound />} />
                </Routes>
              </Suspense>
            </div>
          )}

          <footer className="mt-12 flex flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-[var(--border)] pt-6 text-xs font-semibold text-[var(--muted)]">
            <p>STRIKEFIT · VR fitness research prototype</p>
            <nav aria-label="Footer" className="flex flex-wrap items-center gap-x-4 gap-y-2">
              {[
                ["/dashboard/guide", "User guide"],
                ["/dashboard/connection", "Connection"],
                ["/dashboard/export", "Export data"],
                ["/", "Public site"],
              ].map(([to, label]) => (
                <Link key={to} to={to} className="press rounded hover:text-[var(--accent)] hover:underline">
                  {label}
                </Link>
              ))}
            </nav>
            <p className="flex items-center gap-1.5">
              <kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">Ctrl</kbd>+<kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">K</kbd> jump ·
              <button type="button" onClick={() => setShortcutsOpen(true)} className="press rounded font-semibold text-[var(--accent)] hover:text-[var(--accent-strong)] hover:underline">
                <kbd className="rounded border border-[var(--border)] bg-[var(--surface)] px-1 font-sans">?</kbd> shortcuts
              </button>
            </p>
          </footer>
        </main>
      </div>

      <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} onShortcuts={() => setShortcutsOpen(true)} />
      <ShortcutsOverlay open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />
      {touring && !t.loading && (
        <Suspense fallback={null}>
          <Tour onClose={closeTour} onOpened={() => (tourOpened.current = true)} />
        </Suspense>
      )}
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
