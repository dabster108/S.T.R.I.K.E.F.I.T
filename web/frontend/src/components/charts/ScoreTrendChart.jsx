import { useId, useMemo, useState } from "react";
import { formatDateTime, formatShortDate } from "../../lib/format.js";
import { sessionTime } from "../../lib/derive.js";

const formatHourMinute = (iso) => new Date(iso).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
import { OutcomeBadge } from "../ui/Badge.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import { useElementWidth } from "./useElementWidth.js";

const HEIGHT = 240;
const PAD = { top: 16, right: 16, bottom: 30, left: 44 };

function niceMax(v) {
  if (v <= 0) return 10;
  const pow = 10 ** Math.floor(Math.log10(v));
  const step = [1, 2, 2.5, 5, 10].find((m) => m * pow >= v / 4) * pow;
  return Math.ceil(v / step) * step;
}

// Single-series score line over sessions (oldest → newest) with crosshair tooltip.
export default function ScoreTrendChart({ sessions, onSelect }) {
  const [ref, width] = useElementWidth(320);
  const [active, setActive] = useState(null);
  const gradId = useId().replace(/:/g, "");

  const geo = useMemo(() => {
    const n = sessions.length;
    if (!n) return null;
    const innerW = Math.max(40, width - PAD.left - PAD.right);
    const innerH = HEIGHT - PAD.top - PAD.bottom;
    const maxY = niceMax(Math.max(...sessions.map((s) => Number(s.score) || 0)));
    const x = (i) => PAD.left + (n === 1 ? innerW / 2 : (i / (n - 1)) * innerW);
    const y = (v) => PAD.top + innerH - (v / maxY) * innerH;
    const points = sessions.map((s, i) => ({ x: x(i), y: y(Number(s.score) || 0), s, i }));
    const line = points.map((p, i) => `${i ? "L" : "M"}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(" ");
    const base = PAD.top + innerH;
    const area = n > 1 ? `${line} L${points[n - 1].x.toFixed(1)},${base} L${points[0].x.toFixed(1)},${base} Z` : null;
    const ticks = [0, 0.25, 0.5, 0.75, 1].map((f) => ({ v: Math.round(maxY * f), y: y(maxY * f) }));
    const labelEvery = Math.max(1, Math.ceil(n / Math.max(2, Math.floor(innerW / 70))));
    // Label with clock time when everything falls within ~a day, otherwise with dates.
    const spanMs = sessionTime(sessions[n - 1]) - sessionTime(sessions[0]);
    const label = spanMs < 36 * 3600 * 1000 ? formatHourMinute : formatShortDate;
    return { points, line, area, ticks, base, labelEvery, label };
  }, [sessions, width]);

  if (!geo) {
    return (
      <EmptyState
        icon="score"
        title="No sessions in this range"
        description="Finish a battle in the headset (with the backend running) and your score line appears here."
      />
    );
  }

  const hover = active !== null ? geo.points[active] : null;

  const nearest = (clientX, rect) => {
    const px = clientX - rect.left;
    let best = 0;
    geo.points.forEach((p, i) => {
      if (Math.abs(p.x - px) < Math.abs(geo.points[best].x - px)) best = i;
    });
    return best;
  };

  return (
    <div ref={ref} className="relative w-full min-w-0 select-none overflow-hidden">
      <svg
        width={width}
        height={HEIGHT}
        className="block"
        role="img"
        aria-label={`Score trend across ${sessions.length} sessions`}
        onMouseMove={(e) => setActive(nearest(e.clientX, e.currentTarget.getBoundingClientRect()))}
        onMouseLeave={() => setActive(null)}
        onClick={() => hover && onSelect?.(hover.s)}
        style={{ cursor: onSelect && hover ? "pointer" : "default" }}
      >
        <defs>
          <linearGradient id={gradId} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="var(--accent)" stopOpacity="0.18" />
            <stop offset="100%" stopColor="var(--accent)" stopOpacity="0" />
          </linearGradient>
        </defs>

        {geo.ticks.map((t) => (
          <g key={t.v}>
            <line x1={PAD.left} x2={width - PAD.right} y1={t.y} y2={t.y} stroke={t.v === 0 ? "var(--border-strong)" : "var(--grid)"} />
            <text x={PAD.left - 8} y={t.y + 3.5} textAnchor="end" fontSize="10.5" fill="var(--muted)" className="tabular-nums">
              {t.v}
            </text>
          </g>
        ))}

        {geo.area && <path d={geo.area} fill={`url(#${gradId})`} className="chart-area" />}
        {geo.points.length > 1 && (
          <path d={geo.line} fill="none" stroke="var(--accent)" strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" className="chart-line" />
        )}

        {hover && <line x1={hover.x} x2={hover.x} y1={PAD.top} y2={geo.base} stroke="var(--border-strong)" strokeDasharray="3 3" />}

        {geo.points.map((p) => (
          <circle
            key={p.s.id}
            cx={p.x}
            cy={p.y}
            r={active === p.i ? 5.5 : 4}
            fill="var(--surface)"
            stroke="var(--accent)"
            strokeWidth={active === p.i ? 2.5 : 2}
          />
        ))}

        {geo.points.map((p) => {
          const last = p.i === geo.points.length - 1;
          if (p.i % geo.labelEvery !== 0 && !last) return null;
          // Skip a regular label that would collide with the pinned last one.
          if (!last && geo.points.length - 1 - p.i < geo.labelEvery && p.i !== 0) return null;
          const anchor = geo.points.length > 1 && p.i === 0 ? "start" : last && geo.points.length > 1 ? "end" : "middle";
          return (
            <text key={`l-${p.s.id}`} x={p.x} y={HEIGHT - 10} textAnchor={anchor} fontSize="10.5" fill="var(--muted)">
              {geo.label(p.s.endedAt || p.s.startedAt)}
            </text>
          );
        })}
        )}
      </svg>

      {hover && (
        <div
          className="pointer-events-none absolute z-10 w-44 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2.5 text-xs shadow-lg"
          style={{
            left: Math.min(Math.max(hover.x - 88, 0), width - 176),
            top: Math.max(0, hover.y - 96),
          }}
        >
          <div className="flex items-center justify-between gap-2">
            <span className="text-base font-bold tabular-nums text-[var(--ink)]">{hover.s.score}</span>
            <OutcomeBadge outcome={hover.s.outcome} />
          </div>
          <p className="mt-1 truncate text-[var(--ink-secondary)]">
            {hover.s.playerName} · {hover.s.level}
          </p>
          <p className="text-[var(--muted)]">{formatDateTime(hover.s.endedAt || hover.s.startedAt)}</p>
        </div>
      )}
    </div>
  );
}
