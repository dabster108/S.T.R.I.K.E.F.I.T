import { Link, NavLink } from "react-router-dom";
import Icon from "../ui/Icon.jsx";
import { ConnectionBadge } from "../ui/Badge.jsx";

export const NAV = [
  { to: "/dashboard", label: "Overview", icon: "dashboard", end: true },
  { to: "/dashboard/live", label: "Live Arena", icon: "live" },
  { to: "/dashboard/players", label: "Players", icon: "players" },
  { to: "/dashboard/sessions", label: "Sessions", icon: "sessions" },
  { to: "/dashboard/leaderboard", label: "Leaderboard", icon: "trophy" },
  { to: "/dashboard/compare", label: "Compare", icon: "compare" },
  { to: "/dashboard/analytics", label: "Analytics", icon: "analytics" },
  { to: "/dashboard/export", label: "Export", icon: "download" },
];

function NavList({ onNavigate, liveCount }) {
  return (
    <nav aria-label="Main" className="space-y-0.5">
      {NAV.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          end={item.end}
          onClick={() => onNavigate?.()}
          className={({ isActive }) =>
            `group flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
              isActive
                ? "bg-[var(--accent-soft)] text-[var(--accent)]"
                : "text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
            }`
          }
        >
          {({ isActive }) => (
            <>
              <Icon name={item.icon} className={`h-[18px] w-[18px] ${isActive ? "" : "text-[var(--muted)] group-hover:text-[var(--ink-secondary)]"}`} />
              <span className="flex-1">{item.label}</span>
              {item.icon === "live" && liveCount > 0 && (
                <span className="inline-flex items-center gap-1 rounded-full bg-[var(--good-soft)] px-1.5 py-0.5 text-[0.62rem] font-bold uppercase tracking-wide text-[var(--good)]">
                  <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--good)]" aria-hidden />
                  {liveCount} live
                </span>
              )}
            </>
          )}
        </NavLink>
      ))}
    </nav>
  );
}

export function Brand({ to = "/dashboard" }) {
  return (
    <Link to={to} className="flex items-center gap-2.5 rounded-xl">
      <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-bold text-white shadow-sm">SF</span>
      <span>
        <span className="block text-sm font-semibold leading-tight text-[var(--ink)]">STRIKEFIT</span>
        <span className="block text-xs text-[var(--muted)]">VR fitness telemetry</span>
      </span>
    </Link>
  );
}

export function Sidebar({ connection, liveCount, health }) {
  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden w-[var(--sidebar-w)] flex-col border-r border-[var(--border)] bg-[var(--surface)] px-3 py-5 lg:flex">
      <div className="px-2">
        <Brand />
      </div>
      <div className="mt-8 flex-1">
        <p className="mb-2 px-3 text-[0.65rem] font-semibold uppercase tracking-wider text-[var(--muted)]">Menu</p>
        <NavList liveCount={liveCount} />
      </div>
      <Link
        to="/dashboard/connection"
        className="rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] p-3 transition-colors hover:border-[var(--border-strong)]"
      >
        <div className="flex items-center justify-between">
          <span className="text-xs font-medium text-[var(--ink-secondary)]">Backend</span>
          <ConnectionBadge state={connection} />
        </div>
        <p className="mt-2 truncate text-[0.7rem] text-[var(--muted)]">
          {health?.ok ? `${health.latencyMs} ms · ${health.streamClients ?? health.clients ?? 0} dashboard${(health.streamClients ?? health.clients) === 1 ? "" : "s"}` : "Not reachable"}
        </p>
      </Link>
    </aside>
  );
}

export function MobileNav({ open, onClose, liveCount }) {
  if (!open) return null;
  return (
    <div className="fade-in fixed inset-0 z-40 bg-gray-900/30 lg:hidden" onClick={onClose}>
      <div
        className="drawer-in ml-auto h-full w-72 max-w-[85vw] border-l border-[var(--border)] bg-[var(--surface)] p-4 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-6 flex items-center justify-between">
          <Brand />
          <button type="button" onClick={onClose} aria-label="Close menu" className="rounded-lg p-2 text-[var(--muted)] hover:bg-[var(--surface-muted)]">
            <Icon name="x" className="h-5 w-5" />
          </button>
        </div>
        <NavList onNavigate={onClose} liveCount={liveCount} />
        <Link to="/dashboard/connection" onClick={onClose} className="mt-4 flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)]">
          <Icon name="settings" className="h-[18px] w-[18px] text-[var(--muted)]" />
          Connection
        </Link>
      </div>
    </div>
  );
}
