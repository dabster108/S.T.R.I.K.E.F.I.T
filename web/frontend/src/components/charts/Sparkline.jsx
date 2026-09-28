import { useId } from "react";

// Tiny inline trend. Shape only — the surrounding card carries the numbers.
export default function Sparkline({ values, width = 120, height = 32, label = "Trend", animate = true, color = "var(--accent)" }) {
  const gid = useId().replace(/:/g, "");
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
  const pts = values.map((v, i) => [2 + (i / (values.length - 1)) * (width - 4), height - 3 - ((v - min) / span) * (height - 6)]);
  const d = pts.map(([x, y], i) => `${i ? "L" : "M"}${x.toFixed(1)},${y.toFixed(1)}`).join(" ");
  const area = `${d} L${pts[pts.length - 1][0].toFixed(1)},${height} L${pts[0][0].toFixed(1)},${height} Z`;
  const [lx, ly] = pts[pts.length - 1];
  return (
    <svg width={width} height={height} role="img" aria-label={label} className="block shrink-0 overflow-visible">
      <defs>
        <linearGradient id={gid} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.22" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      {animate && (
        <>
          <path d={area} fill={`url(#${gid})`} className="chart-area" />
          <path d={d} pathLength="1" fill="none" stroke={color} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" className="chart-line" />
          <circle cx={lx} cy={ly} r="3" fill={color} stroke="var(--surface)" strokeWidth="1.5" className="dot-in" />
        </>
      )}
    </svg>
  );
}
