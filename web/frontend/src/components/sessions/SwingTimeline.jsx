import { useElementWidth } from "../charts/useElementWidth.js";

// Swing speed (each hit, dot coloured by its strength tier) and monster HP (% of max, line) over the fight, with marker
// ticks for hits, monster guards, openings and clashes, and a cursor at the replay position. Click or drag to seek.
// Uses only the dashboard's existing colours (tier tokens, --hp-monster, the scrubber's marker classes).
const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const P = (e) => (e.payload && typeof e.payload === "object" && !Array.isArray(e.payload) ? e.payload : {});
const TIER = { tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };
const MARKS = [
  { type: "hit", label: "Hit", cls: "fill-amber-500" },
  { type: "monster_guard", label: "Monster guard", cls: "fill-blue-500" },
  { type: "opening_hit", label: "Opening", cls: "fill-emerald-500" },
  { type: "clash", label: "Clash", cls: "fill-gray-400" },
];
const clock = (s) => `${String(Math.floor(Math.max(0, s) / 60)).padStart(2, "0")}:${String(Math.floor(Math.max(0, s) % 60)).padStart(2, "0")}`;

export default function SwingTimeline({ events, duration, t, onSeek }) {
  const [ref, width] = useElementWidth();
  const hits = events.filter((e) => e.type === "hit" && isNum(P(e).swingSpeed));
  const hp = events.filter((e) => isNum(P(e).monsterHp) && isNum(P(e).monsterMaxHp) && P(e).monsterMaxHp > 0);
  const marks = events.filter((e) => MARKS.some((m) => m.type === e.type));
  if (!hits.length && hp.length < 2) {
    return <p className="rounded-xl border border-dashed border-[var(--border)] px-4 py-6 text-center text-xs font-medium text-[var(--muted)]">No swing-speed or monster-HP samples in this session, so there is nothing to plot. The event list below still replays.</p>;
  }
  const height = 190;
  const pad = { t: 22, r: 44, b: 22, l: 40 };
  const iw = Math.max(10, width - pad.l - pad.r);
  const ih = height - pad.t - pad.b;
  const d = Math.max(0.1, duration);
  const vMax = Math.max(4, Math.ceil(Math.max(0, ...hits.map((e) => P(e).swingSpeed))));
  const sx = (x) => pad.l + (Math.min(d, Math.max(0, x)) / d) * iw;
  const sv = (v) => pad.t + ih - (Math.min(vMax, v) / vMax) * ih;
  const sh = (f) => pad.t + ih - Math.min(1, Math.max(0, f)) * ih;
  const hpPath = hp.map((e, i) => `${i ? "H" : "M"}${sx(e.t)}${i ? `V${sh(P(e).monsterHp / P(e).monsterMaxHp)}` : `,${sh(P(e).monsterHp / P(e).monsterMaxHp)}`}`).join("");
  const seekAt = (clientX, el) => {
    const r = el.getBoundingClientRect();
    onSeek(Math.min(d, Math.max(0, ((clientX - r.left - pad.l) / iw) * d)));
  };
  const present = MARKS.filter((m) => marks.some((e) => e.type === m.type));
  return (
    <div ref={ref} className="min-w-0">
      <svg
        width={width}
        height={height}
        role="img"
        aria-label={`Swing speed and monster health over ${clock(d)}: ${hits.length} hits with a speed, ${marks.length} markers`}
        className="block cursor-crosshair touch-none select-none"
        onPointerDown={(e) => {
          e.currentTarget.setPointerCapture(e.pointerId);
          seekAt(e.clientX, e.currentTarget);
        }}
        onPointerMove={(e) => e.currentTarget.hasPointerCapture(e.pointerId) && seekAt(e.clientX, e.currentTarget)}
      >
        {/* grid + axes */}
        {[0, 0.5, 1].map((f) => (
          <g key={f}>
            <line x1={pad.l} x2={pad.l + iw} y1={pad.t + ih - f * ih} y2={pad.t + ih - f * ih} stroke="var(--grid)" />
            <text x={pad.l - 6} y={pad.t + ih - f * ih + 3} textAnchor="end" className="fill-[var(--muted)] text-[10px] font-semibold tabular-nums">
              {(vMax * f).toFixed(0)}
            </text>
            <text x={pad.l + iw + 6} y={pad.t + ih - f * ih + 3} className="fill-[var(--muted)] text-[10px] font-semibold tabular-nums">
              {Math.round(f * 100)}%
            </text>
          </g>
        ))}
        <text x={pad.l - 6} y={pad.t - 10} textAnchor="end" className="fill-[var(--muted)] text-[10px] font-bold">m/s</text>
        <text x={pad.l + iw + 6} y={pad.t - 10} className="fill-[var(--muted)] text-[10px] font-bold">HP</text>
        {[0, 0.5, 1].map((f) => (
          <text key={f} x={pad.l + f * iw} y={height - 6} textAnchor={f === 0 ? "start" : f === 1 ? "end" : "middle"} className="fill-[var(--muted)] text-[10px] font-semibold tabular-nums">
            {clock(d * f)}
          </text>
        ))}
        {/* marker ticks along the top */}
        {marks.map((e) => {
          const m = MARKS.find((x) => x.type === e.type);
          const lane = MARKS.indexOf(m);
          return <rect key={e.id} x={sx(e.t) - 1} y={2 + lane * 4} width={2} height={4} className={`${m.cls} ${e.t <= t ? "" : "opacity-35"}`} />;
        })}
        {/* monster HP */}
        {hp.length > 1 && <path d={hpPath} fill="none" stroke="var(--hp-monster)" strokeWidth={2} />}
        {/* swing speed per hit */}
        {hits.map((e) => (
          <circle key={e.id} cx={sx(e.t)} cy={sv(P(e).swingSpeed)} r={3.2} fill={TIER[String(P(e).tier || "").toLowerCase()] || "var(--blue-400)"} opacity={e.t <= t ? 1 : 0.35} />
        ))}
        {/* cursor */}
        <line x1={sx(t)} x2={sx(t)} y1={pad.t - 4} y2={pad.t + ih} stroke="var(--accent)" strokeWidth={2} />
      </svg>
      <ul className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--ink-secondary)]" aria-label="Chart legend">
        <li className="inline-flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-full bg-[var(--tier-full)]" aria-hidden /> Swing speed (dot colour = tier)
        </li>
        <li className="inline-flex items-center gap-1.5">
          <span className="h-[2px] w-4 bg-[var(--hp-monster)]" aria-hidden /> Monster HP
        </li>
        {present.map((m) => (
          <li key={m.type} className="inline-flex items-center gap-1.5">
            <svg width="6" height="8" aria-hidden>
              <rect width="2" height="8" x="2" className={m.cls} />
            </svg>
            {m.label}
          </li>
        ))}
        {!marks.some((e) => e.type === "monster_guard" || e.type === "opening_hit") && (
          <li className="font-medium text-[var(--muted)]">No guard or opening events: recorded before the guard telemetry, or none happened</li>
        )}
      </ul>
    </div>
  );
}
