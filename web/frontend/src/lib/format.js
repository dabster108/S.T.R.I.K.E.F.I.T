const safeDate = (iso) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? null : d;
};

export function formatDateTime(iso) {
  const d = safeDate(iso);
  return d ? d.toLocaleString(undefined, { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }) : "—";
}

export function formatShortDate(iso) {
  const d = safeDate(iso);
  return d ? d.toLocaleDateString(undefined, { month: "short", day: "numeric" }) : "—";
}

export function formatClock(iso) {
  const d = safeDate(iso);
  return d ? d.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "—";
}

export function formatRelative(iso) {
  const d = safeDate(iso);
  if (!d) return "—";
  const sec = Math.round((Date.now() - d.getTime()) / 1000);
  if (sec < 10) return "just now";
  if (sec < 60) return `${sec}s ago`;
  const min = Math.round(sec / 60);
  if (min < 60) return `${min}m ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const day = Math.round(hr / 24);
  return day < 30 ? `${day}d ago` : formatShortDate(iso);
}

export function formatDuration(sec) {
  const s = Math.round(Number(sec) || 0);
  if (s <= 0) return "—";
  if (s < 60) return `${s}s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ${String(s % 60).padStart(2, "0")}s`;
  return `${Math.floor(m / 60)}h ${String(m % 60).padStart(2, "0")}m`;
}

export function formatNumber(n) {
  return (Math.round(Number(n) || 0)).toLocaleString();
}

export function formatPercent(n) {
  return `${Math.round((Number(n) || 0) * 10) / 10}%`;
}
