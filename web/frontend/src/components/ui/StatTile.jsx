import { useCountUp, useInView } from "../../hooks/motion.js";
import Sparkline from "../charts/Sparkline.jsx";
import Icon from "./Icon.jsx";

const TONES = {
  blue: "bg-blue-50 text-blue-600",
  indigo: "bg-indigo-50 text-indigo-600",
  green: "bg-emerald-50 text-emerald-600",
  amber: "bg-amber-50 text-amber-600",
  rose: "bg-rose-50 text-rose-600",
  slate: "bg-gray-100 text-gray-700",
};

function CountUp({ value, format, start }) {
  const v = useCountUp(value, start, 1200);
  return format(v);
}

// KPI card. Pass `count` (+ `format`) for an animated number, or `value` for any node.
// `spark` draws a sparkline; `trend` shows a badge and must only be passed when a
// real previous value exists.
export default function StatTile({ label, value, count, format = (v) => Math.round(v).toLocaleString(), hint, icon = "bolt", tone = "blue", trend, spark, sparkLabel, children }) {
  const [ref, inView] = useInView({ rootMargin: "0px" });
  const numeric = typeof count === "number" && Number.isFinite(count);
  return (
    <div ref={ref} className="lift flex min-w-0 flex-col rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)] hover:border-[var(--accent-ring)]">
      <div className="flex items-center justify-between gap-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-[var(--muted)]">{label}</p>
        <span className={`inline-flex h-8 w-8 items-center justify-center rounded-lg ${TONES[tone] || TONES.blue}`}>
          <Icon name={icon} className="h-4 w-4" />
        </span>
      </div>
      <div className="mt-3 flex items-end justify-between gap-3">
        <p className="t-stat truncate text-[var(--ink)]">{numeric ? <CountUp value={count} format={format} start={inView} /> : value}</p>
        {spark && spark.length > 1 && <Sparkline values={spark} width={88} height={30} label={sparkLabel || `${label} trend`} animate={inView} />}
      </div>
      <div className="mt-3 flex min-h-[1.5rem] flex-wrap items-center gap-2 text-xs font-medium text-[var(--muted)]">
        {trend && <TrendChip {...trend} />}
        {hint && <span className="min-w-0 truncate">{hint}</span>}
      </div>
      {children}
    </div>
  );
}

export function TrendChip({ value, label, goodWhenUp = true, suffix }) {
  if (value === null || value === undefined || !Number.isFinite(value)) return null;
  const up = value > 0;
  const flat = value === 0;
  const good = flat ? null : up === goodWhenUp;
  const cls = flat ? "bg-gray-100 text-[var(--ink-secondary)]" : good ? "bg-[var(--good-soft)] text-[var(--good-ink)]" : "bg-[var(--bad-soft)] text-[var(--bad)]";
  return (
    <span className={`inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 font-bold tabular-nums ${cls}`}>
      {!flat && <Icon name={up ? "score" : "trendDown"} className="h-3 w-3" strokeWidth={2.6} />}
      {flat ? "±0" : `${up ? "+" : "−"}${label ?? Math.abs(value)}`}
      {suffix && <span className="font-semibold opacity-80">{suffix}</span>}
    </span>
  );
}
