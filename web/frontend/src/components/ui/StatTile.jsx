import Icon from "./Icon.jsx";

const TONES = {
  blue: "bg-blue-50 text-blue-600",
  indigo: "bg-indigo-50 text-indigo-600",
  green: "bg-emerald-50 text-emerald-600",
  amber: "bg-amber-50 text-amber-600",
  rose: "bg-rose-50 text-rose-600",
  slate: "bg-gray-100 text-gray-600",
};

export default function StatTile({ label, value, hint, icon = "bolt", tone = "blue", trend, children }) {
  return (
    <div className="flex flex-col rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[0_1px_2px_rgba(16,24,40,0.04)]">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-medium text-[var(--muted)]">{label}</p>
        <span className={`inline-flex h-8 w-8 items-center justify-center rounded-lg ${TONES[tone] || TONES.blue}`}>
          <Icon name={icon} className="h-4 w-4" />
        </span>
      </div>
      <p className="mt-2 text-[1.75rem] font-bold leading-none tracking-tight text-[var(--ink)]">{value}</p>
      <div className="mt-2 flex min-h-[1.25rem] items-center gap-2 text-xs text-[var(--muted)]">
        {trend && <TrendChip {...trend} />}
        {hint && <span className="truncate">{hint}</span>}
      </div>
      {children}
    </div>
  );
}

export function TrendChip({ value, label, goodWhenUp = true }) {
  if (value === null || value === undefined || !Number.isFinite(value)) return null;
  const up = value > 0;
  const flat = value === 0;
  const good = flat ? null : up === goodWhenUp;
  const cls = flat ? "bg-gray-100 text-[var(--muted)]" : good ? "bg-[var(--good-soft)] text-[var(--good)]" : "bg-[var(--bad-soft)] text-[var(--bad)]";
  return (
    <span className={`inline-flex items-center gap-0.5 rounded-md px-1.5 py-0.5 font-semibold tabular-nums ${cls}`}>
      {!flat && <span aria-hidden>{up ? "▲" : "▼"}</span>}
      {up ? "+" : ""}
      {label ?? value}
    </span>
  );
}
