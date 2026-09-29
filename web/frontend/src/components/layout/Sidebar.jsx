import { useLayoutEffect, useRef, useState } from "react";
import { Link, NavLink, useLocation } from "react-router-dom";
import Icon from "../ui/Icon.jsx";
import Tooltip from "../ui/Tooltip.jsx";

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

const isActive = (item, pathname) => (item.end ? pathname === item.to || pathname === `${item.to}/` : pathname.startsWith(item.to));

// A highlight that slides to the active item instead of jumping.
function NavList({ onNavigate, liveCount, collapsed = false }) {
  const { pathname } = useLocation();
  const box = useRef(null);
  const [pos, setPos] = useState(null);
  const activeIndex = NAV.findIndex((item) => isActive(item, pathname));

  useLayoutEffect(() => {
    const el = activeIndex >= 0 ? box.current?.querySelectorAll("[data-nav]")[activeIndex] : null;
    if (!el) return setPos(null);
    const r = el.getBoundingClientRect();
    setPos({ y: r.top - box.current.getBoundingClientRect().top, h: r.height });
  }, [activeIndex, collapsed]);

  return (
    <nav aria-label="Main" ref={box} className="relative space-y-1">
      <span
        className="nav-indicator pointer-events-none absolute inset-x-0 top-0 rounded-lg bg-[var(--accent-soft)] ring-1 ring-inset ring-[var(--accent-ring)]"
        style={{ height: pos?.h ?? 40, transform: `translateY(${pos?.y ?? 0}px)`, opacity: pos ? 1 : 0 }}
        aria-hidden
      >
        <span className="absolute left-0 top-2 bottom-2 w-[3px] rounded-r-full bg-[var(--accent)]" />
      </span>
      {NAV.map((item) => {
        const live = item.icon === "live" && liveCount > 0;
        return (
          <Tooltip key={item.to} label={item.label} side="right" disabled={!collapsed} className="flex w-full">
            <NavLink
              to={item.to}
              end={item.end}
              data-nav
              aria-label={collapsed ? item.label : undefined}
              onClick={() => onNavigate?.()}
              className={({ isActive: on }) =>
                `press group relative flex w-full items-center gap-3 rounded-lg py-2.5 text-sm font-semibold ${collapsed ? "justify-center px-0" : "px-3"} ${
                  on ? "text-[var(--accent-strong)]" : "text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
                }`
              }
            >
              {({ isActive: on }) => (
                <>
                  <span className="relative">
                    <Icon name={item.icon} className={`h-[18px] w-[18px] transition-transform duration-200 group-hover:scale-110 ${on ? "" : "text-[var(--muted)] group-hover:text-[var(--ink-secondary)]"}`} />
                    {live && collapsed && <span className="status-live absolute -right-1 -top-1 h-2 w-2 rounded-full bg-[var(--good)] ring-2 ring-[var(--surface)]" aria-hidden />}
                  </span>
                  {!collapsed && <span className="flex-1">{item.label}</span>}
                  {live && !collapsed && (
                    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--good-soft)] px-1.5 py-0.5 text-xs font-extrabold uppercase tracking-wide text-[var(--good-ink)]">
                      <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--good)]" aria-hidden />
                      {liveCount} live
                    </span>
                  )}
                </>
              )}
            </NavLink>
          </Tooltip>
        );
      })}
    </nav>
  );
}

export function Brand({ to = "/dashboard", compact = false }) {
  return (
    <Link to={to} className="press flex items-center gap-2.5 rounded-xl" aria-label={compact ? "STRIKEFIT overview" : undefined}>
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-extrabold text-white shadow-[var(--shadow-glow)]">SF</span>
      {!compact && (
        <span>
          <span className="block text-sm font-extrabold leading-tight tracking-tight text-[var(--ink)]">STRIKEFIT</span>
          <span className="block text-xs font-semibold text-[var(--muted)]">VR fitness telemetry</span>
        </span>
      )}
    </Link>
  );
}

const DOT = { live: "bg-[var(--good)]", connecting: "bg-[var(--warn)]", offline: "bg-[var(--bad)]" };
const DOT_LABEL = { live: "connected", connecting: "connecting", offline: "offline" };

// Secondary links under the main menu, styled exactly like the menu items.
function FooterLinks({ connection, collapsed = false, onNavigate }) {
  const { pathname } = useLocation();
  const item = (on) =>
    `press group relative flex w-full items-center gap-3 rounded-lg py-2.5 text-sm font-semibold ${collapsed ? "justify-center px-0" : "px-3"} ${
      on ? "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]" : "text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
    }`;
  const icon = (on) => `h-[18px] w-[18px] ${on ? "" : "text-[var(--muted)] group-hover:text-[var(--ink-secondary)]"}`;
  const onConnection = pathname.startsWith("/dashboard/connection");
  const dot = connection ? DOT[connection] || DOT.offline : null;
  return (
    <div className="space-y-1 border-t border-[var(--border)] pt-3">
      <Tooltip label="Connection" side="right" disabled={!collapsed} className="flex w-full">
        <Link to="/dashboard/connection" onClick={() => onNavigate?.()} aria-label={collapsed ? "Connection" : undefined} className={item(onConnection)}>
          <span className="relative">
            <Icon name="server" className={icon(onConnection)} />
            {collapsed && dot && <span className={`absolute -right-1 -top-1 h-2 w-2 rounded-full ring-2 ring-[var(--surface)] ${dot}`} aria-hidden />}
          </span>
          {!collapsed && <span className="flex-1">Connection</span>}
          {!collapsed && dot && (
            <span className={`h-2 w-2 rounded-full ${dot}`} title={`Server ${DOT_LABEL[connection] || "offline"}`}>
              <span className="sr-only">Server {DOT_LABEL[connection] || "offline"}</span>
            </span>
          )}
        </Link>
      </Tooltip>
      <Tooltip label="Back to site" side="right" disabled={!collapsed} className="flex w-full">
        <Link to="/" onClick={() => onNavigate?.()} aria-label={collapsed ? "Back to site" : undefined} className={item(false)}>
          <Icon name="chevronLeft" className={icon(false)} />
          {!collapsed && <span className="flex-1">Back to site</span>}
        </Link>
      </Tooltip>
    </div>
  );
}

export function Sidebar({ connection, liveCount, collapsed, onToggle }) {
  return (
    <aside className="fixed inset-y-0 left-0 z-30 hidden w-[var(--sb)] flex-col border-r border-[var(--border)] bg-[var(--surface)] px-3 py-5 lg:flex">
      <div className={`flex items-center ${collapsed ? "flex-col gap-3" : "justify-between px-2"}`}>
        <Brand compact={collapsed} />
        <Tooltip label={collapsed ? "Expand sidebar" : "Collapse sidebar"} side={collapsed ? "right" : "bottom"}>
          <button
            type="button"
            onClick={onToggle}
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
            aria-expanded={!collapsed}
            className="press rounded-lg p-1.5 text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
          >
            <Icon name={collapsed ? "sidebarOpen" : "sidebarClose"} className="h-[18px] w-[18px]" />
          </button>
        </Tooltip>
      </div>
      <div className="mt-8 flex-1">
        {!collapsed && <p className="mb-2 px-3 text-xs font-bold uppercase tracking-wider text-[var(--muted)]">Menu</p>}
        <NavList liveCount={liveCount} collapsed={collapsed} />
      </div>
      <FooterLinks connection={connection} collapsed={collapsed} />
    </aside>
  );
}

export function MobileNav({ open, onClose, liveCount, connection }) {
  const panel = useRef(null);
  useLayoutEffect(() => {
    if (!open) return undefined;
    panel.current?.querySelector("button")?.focus();
    const onKey = (e) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div className="fade-in fixed inset-0 z-40 bg-gray-900/30 lg:hidden" onClick={onClose}>
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-label="Menu"
        className="drawer-in ml-auto h-full w-72 max-w-[85vw] border-l border-[var(--border)] bg-[var(--surface)] p-4 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-6 flex items-center justify-between">
          <Brand />
          <button type="button" onClick={onClose} aria-label="Close menu" className="press rounded-lg p-2 text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]">
            <Icon name="x" className="h-5 w-5" />
          </button>
        </div>
        <NavList onNavigate={onClose} liveCount={liveCount} />
        <div className="mt-4">
          <FooterLinks connection={connection} onNavigate={onClose} />
        </div>
      </div>
    </div>
  );
}
