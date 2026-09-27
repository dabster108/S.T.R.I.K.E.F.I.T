import { useCallback, useEffect, useMemo, useState } from "react";
import { useTelemetry } from "./hooks/useTelemetry.js";
import { useHashRoute } from "./hooks/useHashRoute.js";
import { filterByRange, liveSession } from "./lib/derive.js";
import { formatNumber } from "./lib/format.js";
import { MobileNav, MobileTopbar, NAV, Sidebar } from "./components/layout/Sidebar.jsx";
import { Banner, DashboardSkeleton } from "./components/ui/Feedback.jsx";
import { ToastStack, useToasts } from "./components/ui/Toasts.jsx";
import SessionDrawer from "./components/sessions/SessionDrawer.jsx";
import DashboardView from "./views/DashboardView.jsx";
import LiveView from "./views/LiveView.jsx";
import SessionsView from "./views/SessionsView.jsx";
import LeaderboardView from "./views/LeaderboardView.jsx";
import PlayersView from "./views/PlayersView.jsx";
import SettingsView from "./views/SettingsView.jsx";

const RANGE_KEY = "vrfitness.range";

function readRange() {
  try {
    return localStorage.getItem(RANGE_KEY) || "all";
  } catch {
    return "all";
  }
}

export default function App() {
  const [route] = useHashRoute();
  const view = NAV.some((n) => n.id === route.view) ? route.view : "dashboard";
  const [range, setRangeState] = useState(readRange);
  const [openId, setOpenId] = useState(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const { toasts, push, dismiss } = useToasts();

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
  const live = useMemo(() => liveSession(t.events), [t.events]);
  const openSession = useCallback((id) => setOpenId(id), []);

  useEffect(() => {
    const label = NAV.find((n) => n.id === view)?.label || "Dashboard";
    document.title = `${label} · Monster Battle`;
    window.scrollTo({ top: 0 });
  }, [view]);

  const offline = !t.loading && t.connection === "offline" && !t.health?.ok;

  function renderView() {
    switch (view) {
      case "live":
        return <LiveView events={t.events} connection={t.connection} lastUpdate={t.lastUpdate} />;
      case "sessions":
        return <SessionsView sessions={filtered} range={range} onRange={setRange} openSession={openSession} />;
      case "leaderboard":
        return <LeaderboardView sessions={t.sessions} openSession={openSession} />;
      case "players":
        return <PlayersView sessions={t.sessions} />;
      case "settings":
        return (
          <SettingsView
            connection={t.connection}
            health={t.health}
            checkHealth={t.checkHealth}
            pingRoundTrip={t.pingRoundTrip}
            refresh={t.refresh}
            toast={push}
          />
        );
      default:
        return (
          <DashboardView
            sessions={filtered}
            allSessions={t.sessions}
            events={t.events}
            range={range}
            onRange={setRange}
            openSession={openSession}
          />
        );
    }
  }

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <Sidebar current={view} connection={t.connection} liveActive={Boolean(live)} health={t.health} />
      <MobileTopbar connection={t.connection} onMenu={() => setMenuOpen(true)} />
      <MobileNav open={menuOpen} onClose={() => setMenuOpen(false)} current={view} liveActive={Boolean(live)} />

      <div className="min-w-0 lg:pl-[var(--sidebar-w)]">
        <main className="mx-auto max-w-7xl space-y-6 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          {offline && view !== "settings" && (
            <Banner
              title="Can't reach the backend"
              action={
                <a
                  href="#/settings"
                  className="rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 text-xs font-medium text-[var(--ink-secondary)] shadow-sm hover:text-[var(--ink)]"
                >
                  Connection help
                </a>
              }
            >
              Start it with <code>cd web/backend && npm start</code>. The dashboard reconnects on its own and shows the last data it loaded.
            </Banner>
          )}

          {t.loading ? (
            <DashboardSkeleton />
          ) : (
            <div key={view} className="view-in min-w-0">
              {renderView()}
            </div>
          )}

          <footer className="pt-4 text-center text-xs text-[var(--muted)]">Fitness Monster Battle · VR combat telemetry</footer>
        </main>
      </div>

      {openId && (
        <SessionDrawer
          sessionId={openId}
          fallback={t.sessions.find((s) => s.id === openId)}
          onClose={() => setOpenId(null)}
          onDeleted={(s) => push({ tone: "info", title: "Session deleted", detail: s ? `${s.playerName} · ${s.level}` : undefined })}
        />
      )}

      <ToastStack toasts={toasts} onDismiss={dismiss} />
    </div>
  );
}
