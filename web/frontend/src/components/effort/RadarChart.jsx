// A small SVG radar chart (no chart library: the dashboard draws its own SVG charts). `axes`: [{ key, label, score }] with score
// 0-100; an axis with a null score is left out by the caller, never drawn as 0. `scale` enlarges text for the 1080 px export card.
export const polar = (cx, cy, r, i, n) => {
  const a = -Math.PI / 2 + (i * 2 * Math.PI) / n;
  return { x: cx + r * Math.cos(a), y: cy + r * Math.sin(a), cos: Math.cos(a), sin: Math.sin(a) };
};

export default function RadarChart({ axes, size = 360, scale = 1, caption = "Radar chart" }) {
  const n = axes.length;
  // Wider than tall: the labels at the left and right ("Guard timing", "Consistency") need room that a square canvas doesn't have.
  const width = Math.round(size * 1.45);
  const cx = width / 2;
  const cy = size / 2;
  const R = size / 2 - 64 * scale; // room above and below for the labels around the rings
  const ring = (f) => axes.map((_, i) => polar(cx, cy, R * f, i, n)).map((p) => `${p.x},${p.y}`).join(" ");
  const pts = axes.map((a, i) => polar(cx, cy, (R * Math.max(0, Math.min(100, a.score))) / 100, i, n));
  const label = `${caption}: ${axes.map((a) => `${a.label} ${a.score}`).join(", ")}. Scores run from 0 to 100.`;
  return (
    <svg width={width} height={size} viewBox={`0 0 ${width} ${size}`} role="img" aria-label={label} className="block">
      {[0.25, 0.5, 0.75, 1].map((f) => (
        <polygon key={f} points={ring(f)} fill={f === 1 ? "var(--surface-muted)" : "none"} stroke={f === 1 ? "var(--border-strong)" : "var(--border)"} strokeWidth={f === 1 ? 1.5 : 1} strokeDasharray={f === 1 ? undefined : "4 4"} />
      ))}
      {axes.map((a, i) => {
        const end = polar(cx, cy, R, i, n);
        return <line key={a.key} x1={cx} y1={cy} x2={end.x} y2={end.y} stroke="var(--border)" />;
      })}
      <polygon points={pts.map((p) => `${p.x},${p.y}`).join(" ")} fill="var(--accent)" fillOpacity="0.16" stroke="var(--accent)" strokeWidth={2.5 * scale} strokeLinejoin="round" />
      {pts.map((p, i) => (
        <circle key={axes[i].key} cx={p.x} cy={p.y} r={4.5 * scale} fill="var(--surface)" stroke="var(--accent)" strokeWidth={2.5 * scale} />
      ))}
      {axes.map((a, i) => {
        const p = polar(cx, cy, R + 18 * scale, i, n);
        const anchor = Math.abs(p.cos) < 0.25 ? "middle" : p.cos > 0 ? "start" : "end";
        const dy = p.sin > 0.3 ? 14 * scale : p.sin < -0.3 ? -6 * scale : 4 * scale;
        return (
          <g key={a.key}>
            <text x={p.x} y={p.y + dy - 7 * scale} textAnchor={anchor} fontSize={13 * scale} fontWeight="700" fill="var(--ink-secondary)">{a.label}</text>
            <text x={p.x} y={p.y + dy + 9 * scale} textAnchor={anchor} fontSize={15 * scale} fontWeight="800" fill="var(--ink)" className="tabular-nums">{a.score}</text>
          </g>
        );
      })}
    </svg>
  );
}
