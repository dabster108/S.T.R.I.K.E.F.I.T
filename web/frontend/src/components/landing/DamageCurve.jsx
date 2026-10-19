import { LEVELS, simulateHit } from "../../lib/game.js";
import { useInView } from "../../hooks/motion.js";

const W = 320;
const H = 132;
const PAD = { l: 30, r: 10, t: 10, b: 24 };
const MAX_V = 8;

// Damage against swing speed for one level, drawn from the game's own damage rule (simulateHit). The line draws itself the
// first time it scrolls into view (stroke-dashoffset); `marker` is the speed the demo slider is on.
export default function DamageCurve({ level = "Easy", marker = null }) {
  const [ref, seen] = useInView({ rootMargin: "0px 0px -15% 0px" });
  const s = LEVELS[level].swing;
  const pts = [];
  for (let v = 0; v <= MAX_V + 1e-6; v += 0.1) pts.push([v, simulateHit(v, level).damage]);
  const top = Math.max(...pts.map((p) => p[1])) || 1;
  const x = (v) => PAD.l + (v / MAX_V) * (W - PAD.l - PAD.r);
  const y = (d) => H - PAD.b - (d / top) * (H - PAD.t - PAD.b);
  const line = pts.map(([v, d], i) => `${i ? "L" : "M"}${x(v).toFixed(1)} ${y(d).toFixed(1)}`).join(" ");
  const area = `${line} L${x(MAX_V)} ${y(0)} L${x(0)} ${y(0)} Z`;
  const mv = marker == null ? null : Math.min(MAX_V, Math.max(0, marker));
  const md = mv == null ? null : simulateHit(mv, level).damage;
  const summary = `Damage against swing speed on ${LEVELS[level].name}: zero below ${s.min} m/s, then rising to ${top.toFixed(0)} damage at ${MAX_V} m/s.`;

  return (
    <figure ref={ref} className={`curve ${seen ? "is-drawn" : ""} mt-5 max-w-sm`}>
      <svg viewBox={`0 0 ${W} ${H}`} className="block w-full" role="img" aria-label={summary}>
        <line x1={PAD.l} x2={W - PAD.r} y1={y(0)} y2={y(0)} stroke="var(--border-strong)" />
        <line x1={PAD.l} x2={PAD.l} y1={PAD.t} y2={y(0)} stroke="var(--border)" />
        <line x1={x(s.min)} x2={x(s.min)} y1={PAD.t} y2={y(0)} stroke="var(--accent-ring)" strokeDasharray="3 3" />
        <path d={area} className="curve-area" fill="var(--accent-soft)" />
        <path d={line} pathLength="1" className="curve-line" fill="none" stroke="var(--accent)" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
        {mv != null && (
          <circle cx={x(mv)} cy={y(md)} r="4.5" fill="var(--surface)" stroke="var(--accent-strong)" strokeWidth="2" style={{ transition: "cx 150ms, cy 150ms" }} />
        )}
        <g className="readout" fontSize="9" fontWeight="700" fill="var(--muted)">
          <text x={PAD.l} y={H - 8}>0</text>
          <text x={x(s.min)} y={H - 8} textAnchor="middle">{s.min}</text>
          <text x={W - PAD.r} y={H - 8} textAnchor="end">{MAX_V} m/s</text>
          <text x={PAD.l - 5} y={PAD.t + 6} textAnchor="end">{top.toFixed(0)}</text>
          <text x={PAD.l - 5} y={y(0)} textAnchor="end">0</text>
        </g>
      </svg>
      <figcaption className="mt-1 text-xs font-medium text-[var(--muted)]">Damage per hit, {LEVELS[level].name}: nothing under {s.min} m/s, then a steady climb.</figcaption>
    </figure>
  );
}
