import Icon from "../ui/Icon.jsx";
import { ConnectionBadge } from "../ui/Badge.jsx";

export const NAV = [
  { id: "dashboard", label: "Dashboard", icon: "dashboard" },
  { id: "live", label: "Live feed", icon: "live" },
  { id: "sessions", label: "Sessions", icon: "sessions" },
  { id: "leaderboard", label: "Leaderboard", icon: "trophy" },
  { id: "players", label: "Players", icon: "players" },
  { id: "settings", label: "Connection", icon: "settings" },
];

function NavList({ current, onNavigate, liveActive }) {
  return (
    <nav aria-label="Main" className="space-y-0.5">
      {NAV.map((item) => {
        const active = current === item.id;
        return (
          <a
            key={item.id}
            href={`#/${item.id}`}
            onClick={() => onNavigate?.()}
            aria-current={active ? "page" : undefined}
            className={`group flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
              active
                ? "bg-[var(--accent-soft)] text-[var(--accent)]"
                : "text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
            }`}
          >
            <Icon name={item.icon} className={`h-[18px] w-[18px] ${active ? "" : "text-[var(--muted)] group-hover:text-[var(--ink-secondary)]"}`} />
            <span className="flex-1">{item.label}</span>
            {item.id === "live" && liveActive && (
              <span className="inline-flex items-center gap-1 rounded-full bg-[var(--good-soft)] px-1.5 py-0.5 text-[0.62rem] font-bold uppercase tracking-wide text-[var(--good)]">
                <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--good)]" aria-hidden />
                Live
              </span>
            )}
          </a>
        );
      })}
    </nav>
  );
}

function Brand() {
  return (
    <a href="#/dashboard" className="flex items-center gap-2.5 rounded-xl">
      <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-bold text-white shadow-sm">FM</span>
      <span>
        <span className="block text-sm font-semibold leading-tight text-[var(--ink)]">Monster Battle</span>
        <span className="block text-xs text-[var(--muted)]">VR fitness telemetry</span>
      </span>
    </a>
  );
}

export function Sidebar({ current, connection, liveActive, health }) {
  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden w-[var(--sidebar-w)] flex-col border-r border-[var(--border)] bg-[var(--surface)] px-3 py-5 lg:flex">
      <div className="px-2">
        <Brand />
      </div>
      <div className="mt-8 flex-1">
        <p className="mb-2 px-3 text-[0.65rem] font-semibold uppercase tracking-wider text-[var(--muted)]">Menu</p>
        <NavList current={current} liveActive={liveActive} />
      </div>
      <a
        href="#/settings"
        className="rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] p-3 transition-colors hover:border-[var(--border-strong)]"
      >
        <div className="flex items-center justify-between">
          <span className="text-xs font-medium text-[var(--ink-secondary)]">Backend</span>
          <ConnectionBadge state={connection} />
        </div>
        <p className="mt-2 truncate text-[0.7rem] text-[var(--muted)]">
          {health?.ok ? `${health.latencyMs} ms · ${health.clients ?? 0} client${health.clients === 1 ? "" : "s"}` : "Not reachable"}
        </p>
      </a>
    </aside>
  );
}

export function MobileNav({ open, onClose, current, liveActive }) {
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
        <NavList current={current} onNavigate={onClose} liveActive={liveActive} />
      </div>
    </div>
  );
}

export function MobileTopbar({ connection, onMenu }) {
  return (
    <header className="sticky top-0 z-20 flex items-center justify-between border-b border-[var(--border)] bg-[var(--surface)]/90 px-4 py-3 backdrop-blur-md lg:hidden">
      <Brand />
      <div className="flex items-center gap-2">
        <ConnectionBadge state={connection} compact />
        <button type="button" onClick={onMenu} aria-label="Open menu" className="rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)]">
          <Icon name="menu" className="h-5 w-5" />
        </button>
      </div>
    </header>
  );
}
