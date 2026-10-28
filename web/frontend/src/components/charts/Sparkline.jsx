import { smoothPath } from "../../lib/effort.js";

// Tiny inline trend. Shape only — the surrounding card carries the numbers.
export default function Sparkline({ values, width = 120, height = 32, label = "Trend", animate = true, color = "var(--accent)" }) {
  if (!values || values.length < 2) {
    return (
      <div style={{ width, height }} className="flex items-center text-xs font-medium text-[var(--muted)]">
        Not enough data
      </div>
    );
  }
  const max = Math.max(...values);
  const min = Math.min(...values);
  const span = max - min || 1;
  const pts = values.map((v, i) => ({
    x: 2 + (i / (values.length - 1)) * (width - 4),
    y: height - 4 - ((v - min) / span) * (height - 9),
  }));
  const d = smoothPath(pts.map((p) => ({ x: Number(p.x.toFixed(1)), y: Number(p.y.toFixed(1)) })));
  const latest = pts[pts.length - 1];
  return (
    <svg width={width} height={height} role="img" aria-label={label} className="block shrink-0 overflow-visible">
      {animate && (
        <>
          <path d={d} pathLength="1" fill="none" stroke={color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" className="chart-line" />
          <circle cx={latest.x} cy={latest.y} r="5.5" fill={color} opacity="0.14" />
          <circle cx={latest.x} cy={latest.y} r="2.7" fill={color} className="dot-in" />
        </>
      )}
    </svg>
  );
}
