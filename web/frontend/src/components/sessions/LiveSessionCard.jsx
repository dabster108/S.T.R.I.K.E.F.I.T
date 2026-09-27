import { useEffect, useState } from "react";
import { formatDuration, formatNumber } from "../../lib/format.js";
import Icon from "../ui/Icon.jsx";

// Shown while a session_start has no matching session_complete yet.
export default function LiveSessionCard({ live }) {
  const [, tick] = useState(0);
  useEffect(() => {
    const t = setInterval(() => tick((n) => n + 1), 1000);
    return () => clearInterval(t);
  }, []);

  const elapsed = (Date.now() - new Date(live.startedAt).getTime()) / 1000;

  return (
    <div className="flash-in overflow-hidden rounded-2xl border border-blue-200 bg-gradient-to-r from-[var(--accent-soft)] to-[var(--surface)] shadow-sm">
      <div className="flex flex-wrap items-center gap-x-8 gap-y-3 px-5 py-4">
        <div className="flex items-center gap-3">
          <span className="relative flex h-10 w-10 items-center justify-center rounded-full bg-[var(--accent)] text-white">
            <Icon name="headset" className="h-5 w-5" />
            <span className="status-live absolute -right-0.5 -top-0.5 h-3 w-3 rounded-full border-2 border-white bg-[var(--good)]" />
          </span>
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-[var(--accent)]">Battle in progress</p>
            <p className="text-sm font-semibold text-[var(--ink)]">{live.playerName}</p>
          </div>
        </div>
        {[
          { label: "Elapsed", value: formatDuration(elapsed) },
          { label: "Hits", value: formatNumber(live.hits) },
          { label: "Damage", value: formatNumber(live.damage) },
          { label: "Score", value: formatNumber(live.score) },
        ].map((m) => (
          <div key={m.label}>
            <p className="text-[0.68rem] font-medium text-[var(--muted)]">{m.label}</p>
            <p className="text-lg font-bold tabular-nums text-[var(--ink)]">{m.value}</p>
          </div>
        ))}
      </div>
    </div>
  );
}
