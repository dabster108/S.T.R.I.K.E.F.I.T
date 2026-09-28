import Icon from "../ui/Icon.jsx";
import { formatPercent } from "../../lib/format.js";
import { useCountUp, useInView } from "../../hooks/motion.js";

// Victory / defeat share as one stacked bar (2px surface gap between segments)
// with an icon + label legend so the split never relies on color alone.
export default function OutcomeSplit({ victories, defeats, other = 0 }) {
  const total = victories + defeats + other;
  const segments = [
    { key: "victory", label: "Victories", value: victories, bar: "bg-[var(--good)]", text: "text-[var(--good-ink)]", icon: "check" },
    { key: "defeat", label: "Defeats", value: defeats, bar: "bg-[var(--bad)]", text: "text-[var(--bad)]", icon: "xCircle" },
    { key: "other", label: "Other", value: other, bar: "bg-gray-300", text: "text-[var(--muted)]", icon: "clock" },
  ].filter((s) => s.value > 0 || s.key !== "other");

  const winRate = total ? (victories / total) * 100 : 0;
  const [ref, inView] = useInView({ rootMargin: "0px" });
  const shown = useCountUp(winRate, inView, 1200);

  return (
    <div ref={ref} className="px-5 py-5">
      <div className="flex items-baseline gap-2">
        <span className="t-stat text-[var(--ink)]">{formatPercent(shown)}</span>
        <span className="text-xs font-semibold text-[var(--muted)]">win rate</span>
      </div>

      <div className="mt-4 flex h-3 w-full gap-[2px] overflow-hidden rounded-full bg-gray-100" role="img" aria-label={`${victories} victories, ${defeats} defeats`}>
        {total > 0 &&
          inView &&
          segments
            .filter((s) => s.value > 0)
            .map((s, i) => (
              <div key={s.key} className="h-full first:rounded-l-full last:rounded-r-full" style={{ width: `${(s.value / total) * 100}%` }} title={`${s.label}: ${s.value}`}>
                <div className={`${s.bar} grow-x h-full w-full`} style={{ "--i": i * 2 }} />
              </div>
            ))}
      </div>

      <ul className="mt-4 space-y-2">
        {segments.map((s) => (
          <li key={s.key} className="flex items-center justify-between text-sm">
            <span className={`inline-flex items-center gap-2 font-semibold ${s.text}`}>
              <Icon name={s.icon} className="h-4 w-4" />
              <span className="text-[var(--ink-secondary)]">{s.label}</span>
            </span>
            <span className="tabular-nums text-[var(--ink)]">
              <span className="font-bold">{s.value}</span>
              <span className="ml-1.5 text-xs font-semibold text-[var(--muted)]">{total ? formatPercent((s.value / total) * 100) : "0%"}</span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
