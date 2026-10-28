import { formatNumber } from "../../lib/format.js";

const SIZE = 220;
const STROKE = 34;
const RADIUS = (SIZE - STROKE) / 2;
const CENTER = SIZE / 2;

function polar(angle) {
  return {
    x: CENTER + RADIUS * Math.cos(angle),
    y: CENTER + RADIUS * Math.sin(angle),
  };
}

function arcPath(start, end) {
  const span = end - start;
  if (span >= Math.PI * 2 - 1e-5) {
    const mid = start + Math.PI;
    return `${arcPath(start, mid)} ${arcPath(mid, end - 1e-5)}`;
  }
  const p1 = polar(start);
  const p2 = polar(end);
  const large = span > Math.PI ? 1 : 0;
  return `M${p1.x.toFixed(2)},${p1.y.toFixed(2)} A${RADIUS},${RADIUS} 0 ${large} 1 ${p2.x.toFixed(2)},${p2.y.toFixed(2)}`;
}

export default function DonutChart({ segments = [], ariaLabel = "Donut chart" }) {
  const total = segments.reduce((sum, seg) => sum + (seg.value || 0), 0);
  let angle = -Math.PI / 2;
  const arcs = segments
    .filter((seg) => seg.value > 0)
    .map((seg) => {
      const sweep = (seg.value / total) * Math.PI * 2;
      const next = angle + sweep;
      const d = arcPath(angle, next);
      angle = next;
      return { ...seg, d };
    });

  return (
    <figure className="mx-auto w-full max-w-[240px]">
      <svg viewBox={`0 0 ${SIZE} ${SIZE}`} role="img" aria-label={ariaLabel} className="block h-auto w-full">
        {total === 0 && <circle cx={CENTER} cy={CENTER} r={RADIUS} fill="none" stroke="var(--border)" strokeWidth={STROKE} />}
        {arcs.map((seg) => (
          <path key={seg.key} d={seg.d} fill="none" stroke={seg.color} strokeWidth={STROKE} strokeLinecap="butt" />
        ))}
      </svg>
      <ul className="mt-4 flex flex-wrap items-center justify-center gap-x-5 gap-y-2 text-sm font-semibold text-[var(--ink-secondary)]">
        {segments.map((seg) => (
          <li key={seg.key} className="inline-flex items-center gap-2">
            <span className="h-2.5 w-2.5 rounded-full" style={{ background: seg.color }} aria-hidden />
            {seg.label} ({formatNumber(seg.value || 0)})
          </li>
        ))}
      </ul>
    </figure>
  );
}
