import { useState } from "react";
import { formatWinRate } from "../../lib/outcomes.js";
import { LevelBadge } from "../ui/Badge.jsx";
import { EmptyState } from "../ui/Feedback.jsx";

// Horizontal bars: one measure (avg score) per level, single hue, value labelled.
export default function LevelBars({ rows }) {
  const [active, setActive] = useState(null);
  if (!rows.length) {
    return <EmptyState icon="target" title="No levels played yet" description="Per-level averages show once sessions are recorded." />;
  }
  const max = Math.max(1, ...rows.map((r) => r.avgScore));

  return (
    <ul className="space-y-4 px-5 py-5">
      {rows.map((r, i) => (
        <li key={r.level} className="relative" onMouseEnter={() => setActive(r.level)} onMouseLeave={() => setActive(null)}>
          <div className="mb-1.5 flex items-center justify-between gap-2 text-xs">
            <LevelBadge level={r.level} />
            <span className="font-semibold text-[var(--muted)]">
              <span className="text-sm font-extrabold tabular-nums text-[var(--ink)]">{Math.round(r.avgScore)}</span> avg score
              <span className="font-medium"> · decided fights</span>
            </span>
          </div>
          <div className="h-2.5 w-full rounded-full bg-gray-100">
            <div className="h-full" style={{ width: `${(r.avgScore / max) * 100}%` }}>
              <div
                className={`grow-x h-full w-full rounded-full bg-[var(--accent)] transition-opacity duration-200 ${active && active !== r.level ? "opacity-40" : ""}`}
                style={{ "--i": i }}
              />
            </div>
          </div>
          <p className="mt-1.5 text-xs font-medium text-[var(--muted)]">
            {r.sessions} session{r.sessions === 1 ? "" : "s"} · {r.winRateKnown ? formatWinRate(r.winRate) : "—"} win rate
            {r.abandoned > 0 ? ` (${r.abandoned} abandoned left out)` : ""} · best {r.bestScore}
          </p>
        </li>
      ))}
    </ul>
  );
}
