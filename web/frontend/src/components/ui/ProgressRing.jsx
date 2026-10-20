// The one progress ring (challenges, daily goal): same track, stroke and colours everywhere; green once done.
// No stroke animation (stroke-dashoffset is a paint property, and the dashboard animates transform and opacity only).
const SIZES = { md: { box: 72, r: 30, stroke: 7, cls: "h-[4.5rem] w-[4.5rem]" }, lg: { box: 112, r: 47, stroke: 10, cls: "h-28 w-28" } };

export default function ProgressRing({ percent, done = false, size = "md", label, children }) {
  const s = SIZES[size] || SIZES.md;
  const circ = 2 * Math.PI * s.r;
  const p = Math.max(0, Math.min(100, Number(percent) || 0));
  const c = s.box / 2;
  return (
    <div className={`relative shrink-0 ${s.cls}`} role="img" aria-label={label}>
      <svg viewBox={`0 0 ${s.box} ${s.box}`} className="h-full w-full -rotate-90" aria-hidden>
        <circle cx={c} cy={c} r={s.r} fill="none" stroke="var(--grid)" strokeWidth={s.stroke} />
        {p > 0 && (
          <circle
            cx={c}
            cy={c}
            r={s.r}
            fill="none"
            stroke={done ? "var(--good)" : "var(--accent)"}
            strokeWidth={s.stroke}
            strokeLinecap="round"
            strokeDasharray={circ}
            strokeDashoffset={circ * (1 - p / 100)}
          />
        )}
      </svg>
      <span className="absolute inset-0 flex flex-col items-center justify-center text-center">{children}</span>
    </div>
  );
}
