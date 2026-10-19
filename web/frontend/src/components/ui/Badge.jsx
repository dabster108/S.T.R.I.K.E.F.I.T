import Icon from "./Icon.jsx";

const OUTCOMES = {
  victory: { label: "Victory", icon: "check", cls: "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200" },
  defeat: { label: "Defeat", icon: "xCircle", cls: "bg-[var(--bad-soft)] text-[var(--bad-ink)] ring-red-200" },
};

// Outcome always carries an icon + label, never color alone.
export function OutcomeBadge({ outcome }) {
  const o = OUTCOMES[outcome] || { label: outcome || "Unknown", icon: null, cls: "bg-gray-100 text-[var(--ink-secondary)] ring-gray-200" };
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ring-1 ring-inset ${o.cls}`}>
      {o.icon && <Icon name={o.icon} className="h-3 w-3" strokeWidth={2.4} />}
      <span className="capitalize">{o.label.replace("_", " ")}</span>
    </span>
  );
}

const LEVEL_CLS = {
  Easy: "bg-sky-50 text-sky-700 ring-sky-200",
  Medium: "bg-amber-50 text-amber-700 ring-amber-200",
  Hard: "bg-rose-50 text-rose-700 ring-rose-200",
};

export function LevelBadge({ level }) {
  return (
    <span className={`inline-flex rounded-md px-1.5 py-0.5 text-xs font-semibold ring-1 ring-inset ${LEVEL_CLS[level] || "bg-gray-50 text-gray-700 ring-gray-200"}`}>
      {level || "—"}
    </span>
  );
}

// Four stream states collapse to the three the UI names.
const CONNECTION = {
  live: { label: "Connected", dot: "bg-[var(--good)] status-live", text: "text-[var(--good-ink)]", bg: "bg-[var(--good-soft)] ring-emerald-200" },
  connecting: { label: "Reconnecting", dot: "bg-[var(--warn)] status-live", text: "text-amber-700", bg: "bg-[var(--warn-soft)] ring-amber-200" },
  offline: { label: "Offline", dot: "bg-[var(--bad)]", text: "text-[var(--bad-ink)]", bg: "bg-[var(--bad-soft)] ring-red-200" },
};

export function ConnectionBadge({ state, compact = false, label }) {
  const c = CONNECTION[state] || CONNECTION.offline;
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset ${c.bg} ${c.text}`}
      role="status"
      aria-label={`Backend connection: ${label || c.label}`}
    >
      <span className={`h-2 w-2 rounded-full ${c.dot}`} aria-hidden />
      {!compact && (label || c.label)}
    </span>
  );
}

export function Pill({ children, className = "" }) {
  return (
    <span className={`inline-flex items-center rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-xs font-semibold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)] ${className}`}>
      {children}
    </span>
  );
}
