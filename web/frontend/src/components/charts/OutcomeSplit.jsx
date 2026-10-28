import Icon from "../ui/Icon.jsx";
import { formatPercent } from "../../lib/format.js";
import { winRate as rateOf } from "../../lib/outcomes.js";
import { useCountUp, useInView, useReducedMotion } from "../../hooks/motion.js";
import DonutChart from "./DonutChart.jsx";

// Victory / defeat share as one stacked bar (2px surface gap between segments)
// with an icon + label legend so the split never relies on color alone.
// The headline rate is the shared rule (lib/outcomes.js): victories / (victories + defeats). Abandoned fights are
// not part of it; they get their own row and a grey segment so the bar still adds up to every fight.
export default function OutcomeSplit({ victories, defeats, abandoned = 0, other = 0 }) {
  const segments = [
    { key: "victory", label: "Victories", value: victories, color: "var(--good)", text: "text-[var(--good-ink)]", icon: "check" },
    { key: "defeat", label: "Defeats", value: defeats, color: "var(--bad)", text: "text-[var(--bad)]", icon: "xCircle" },
    { key: "abandoned", label: "Abandoned", value: abandoned, color: "#6b7280", text: "text-[var(--muted)]", icon: "clock" },
    { key: "other", label: "Other", value: other, color: "#9ca3af", text: "text-[var(--muted)]", icon: "clock" },
  ].filter((s) => s.value > 0 || (s.key !== "other" && s.key !== "abandoned"));

  const winRate = rateOf(victories, defeats); // null when nothing was decided
  const [ref, seen] = useInView({ rootMargin: "0px" });
  const reduced = useReducedMotion();
  const inView = seen || reduced; // nothing animates under reduced motion / Calm mode, so the bar is drawn at once
  const shown = useCountUp(winRate ?? 0, inView, 1200);

  return (
    <div ref={ref} className="px-5 py-5">
      <div className="flex items-baseline gap-2">
        <span className="t-stat text-[var(--ink)]">{winRate === null ? "—" : formatPercent(shown)}</span>
        <span className="text-xs font-semibold text-[var(--muted)]">win rate · decided fights</span>
      </div>

      <div className="mt-4">
        {inView && <DonutChart segments={segments} ariaLabel={`${victories} victories, ${defeats} defeats, ${abandoned} abandoned`} />}
      </div>

      <p className="mt-3 text-xs font-medium text-[var(--muted)]">
        {winRate === null ? "No fight has been won or lost yet." : `${victories} won of ${victories + defeats} decided`}
        {abandoned > 0 ? `; ${abandoned} abandoned fight${abandoned === 1 ? "" : "s"} left out.` : "."}
      </p>

      <ul className="mt-3 space-y-2">
        {segments.map((s) => (
          <li key={s.key} className="flex items-center justify-between text-sm">
            <span className={`inline-flex items-center gap-2 font-semibold ${s.text}`}>
              <Icon name={s.icon} className="h-4 w-4" />
              <span className="text-[var(--ink-secondary)]">{s.label}</span>
            </span>
            <span className="tabular-nums text-[var(--ink)]">
              <span className="font-bold">{s.value}</span>
              {/* Victories and defeats are a share of DECIDED fights, so they add up to the headline rate; abandoned / other get no percentage. */}
              {(s.key === "victory" || s.key === "defeat") && (
                <span className="ml-1.5 text-xs font-semibold text-[var(--muted)]">{victories + defeats ? formatPercent((s.value / (victories + defeats)) * 100) : "—"}</span>
              )}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
