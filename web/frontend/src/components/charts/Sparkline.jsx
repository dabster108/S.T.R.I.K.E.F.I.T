// Tiny inline trend. Shape only — the surrounding card carries the numbers.
export default function Sparkline({ values, width = 120, height = 32, label = "Score trend" }) {
  if (!values || values.length < 2) {
    return <div style={{ width, height }} className="flex items-center text-[0.65rem] text-[var(--muted)]">Not enough data</div>;
  }
  const max = Math.max(...values);
  const min = Math.min(...values);
  const span = max - min || 1;
  const pts = values.map((v, i) => [
    2 + (i / (values.length - 1)) * (width - 4),
    height - 3 - ((v - min) / span) * (height - 6),
  ]);
  const d = pts.map(([x, y], i) => `${i ? "L" : "M"}${x.toFixed(1)},${y.toFixed(1)}`).join(" ");
  const [lx, ly] = pts[pts.length - 1];
  return (
    <svg width={width} height={height} role="img" aria-label={label} className="block">
      <path d={d} fill="none" stroke="var(--accent)" strokeWidth="1.75" strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={lx} cy={ly} r="2.75" fill="var(--accent)" stroke="var(--surface)" strokeWidth="1.5" />
    </svg>
  );
}
