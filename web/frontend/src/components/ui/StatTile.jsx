import { useCountUp, useInView } from "../../hooks/motion.js";
import Sparkline from "../charts/Sparkline.jsx";
import { useElementWidth } from "../charts/useElementWidth.js";
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
// real previous value exists. `feature` is the one large tile a row leads with:
// bigger figure, full-width sparkline. `icon={null}` drops the icon chip.
export default function StatTile({
  label,
  value,
  count,
  format = (v) => Math.round(v).toLocaleString(),
  hint,
  icon = "bolt",
  tone = "blue",
  trend,
  spark,
  sparkLabel,
  feature = false,
  className = "",
  children,
}) {
  const [ref, inView] = useInView({ rootMargin: "0px" });
  const numeric = typeof count === "number" && Number.isFinite(count);
  const figure = numeric ? <CountUp value={count} format={format} start={inView} /> : value;
  const hasSpark = spark && spark.length > 1;
  return (
    <div
      ref={ref}
      className={`lift flex min-w-0 flex-col rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] hover:border-[var(--accent-ring)] ${feature ? "p-6 sm:p-7" : "p-5"} ${className}`}
    >
      <div className="flex items-center justify-between gap-2">
        <p className={feature ? "t-kicker" : "text-xs font-semibold uppercase tracking-wide text-[var(--muted)]"}>{label}</p>
        {icon && (
          <span className={`inline-flex h-8 w-8 items-center justify-center rounded-lg ${TONES[tone] || TONES.blue}`}>
            <Icon name={icon} className="h-4 w-4" />
          </span>
        )}
      </div>
      {feature ? (
        <>
          <p className="t-figure mt-5 truncate text-[var(--ink)]">{figure}</p>
          <div className="mt-auto pt-6">
            {hasSpark && <FluidSpark values={spark} label={sparkLabel || `${label} trend`} animate={inView} />}
          </div>
        </>
      ) : (
        <div className="mt-3 flex items-end justify-between gap-3">
          <p className="t-stat truncate text-[var(--ink)]">{figure}</p>
          {hasSpark && <Sparkline values={spark} width={88} height={30} label={sparkLabel || `${label} trend`} animate={inView} />}
        </div>
      )}
      <div className="mt-3 flex min-h-[1.5rem] flex-wrap items-center gap-2 text-xs font-medium text-[var(--muted)]">
        {trend && <TrendChip {...trend} />}
        {hint && <span className="min-w-0 truncate">{hint}</span>}
      </div>
      {children}
    </div>
  );
}

// Sparkline that fills its container's width (for feature tiles).
function FluidSpark({ values, label, animate }) {
  const [ref, width] = useElementWidth(0);
  return (
    <div ref={ref} className="w-full">
      {width > 0 && <Sparkline values={values} width={width} height={56} label={label} animate={animate} />}
    </div>
  );
}

// A ruled ledger of figures in one card: no icons, the numbers carry it.
// items: [{ label, value, sub, lead }]; `lead` sets one figure larger than the rest.
export function FigureStrip({ items, className = "", caption }) {
  const rest = items.filter((it) => !it.lead).length;
  return (
    <div className={`min-w-0 overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] ${className}`}>
      {/* the 1px gaps over a border-coloured background are the rules between figures */}
      <dl className="grid grid-cols-2 gap-px bg-[var(--border)] md:flex">
        {items.map((it, i) => (
          <div
            key={it.label}
            className={`min-w-0 bg-[var(--surface)] px-5 py-4 sm:px-6 sm:py-5 ${it.lead ? "col-span-2 md:flex-[1.6]" : "md:flex-1"} ${
              !it.lead && rest % 2 && i === items.length - 1 ? "col-span-2" : ""
            }`}
          >
            <dt className="t-kicker">{it.label}</dt>
            <dd className={`mt-2 truncate text-[var(--ink)] ${it.lead ? "t-figure-md" : "text-2xl font-extrabold tracking-tight tabular-nums"}`}>{it.value}</dd>
            {it.sub && <dd className="mt-1 truncate text-xs font-semibold text-[var(--muted)]">{it.sub}</dd>}
          </div>
        ))}
      </dl>
      {caption && <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)] sm:px-6">{caption}</p>}
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
