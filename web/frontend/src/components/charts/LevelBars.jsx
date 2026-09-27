import { useState } from "react";
import { formatPercent } from "../../lib/format.js";
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
      {rows.map((r) => (
        <li
          key={r.level}
          className="relative"
          onMouseEnter={() => setActive(r.level)}
          onMouseLeave={() => setActive(null)}
        >
          <div className="mb-1.5 flex items-center justify-between gap-2 text-xs">
            <LevelBadge level={r.level} />
            <span className="text-[var(--muted)]">
              <span className="font-semibold tabular-nums text-[var(--ink)]">{Math.round(r.avgScore)}</span> avg score
            </span>
          </div>
          <div className="h-2.5 w-full rounded-full bg-gray-100">
            <div
              className={`h-full rounded-full bg-[var(--accent)] transition-all duration-500 ${active && active !== r.level ? "opacity-40" : ""}`}
              style={{ width: `${(r.avgScore / max) * 100}%` }}
            />
          </div>
          <p className="mt-1.5 text-[0.7rem] text-[var(--muted)]">
            {r.sessions} session{r.sessions === 1 ? "" : "s"} · {formatPercent(r.winRate)} win rate · best {r.bestScore}
          </p>
        </li>
      ))}
    </ul>
  );
}
