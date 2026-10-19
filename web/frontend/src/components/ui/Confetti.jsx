import { useEffect, useMemo, useState } from "react";
import { useReducedMotion } from "../../hooks/motion.js";

// A short burst of confetti over its parent (which must be position: relative). Plays once each time `fire` changes to a
// new truthy value; nothing at all under prefers-reduced-motion. Colours are the dashboard's existing palette classes.
const COLORS = ["bg-[var(--accent)]", "bg-amber-500", "bg-emerald-600", "bg-rose-600", "bg-[var(--accent-strong)]"];
const PIECES = 36;
const MAX_DELAY_MS = 8 * 40; // the longest per-piece delay below: a piece starts at most this long after the burst

// pieces: how many small elements (default 36); ms: how long the whole burst lasts, the last piece included (default 1800).
export default function Confetti({ fire, pieces: count = PIECES, ms = 1800 }) {
  const reduced = useReducedMotion();
  const [run, setRun] = useState(null);
  useEffect(() => {
    if (!fire || reduced) return undefined;
    setRun(fire);
    const t = setTimeout(() => setRun(null), ms);
    return () => clearTimeout(t);
  }, [fire, reduced, ms]);
  const pieces = useMemo(
    () =>
      Array.from({ length: count }, (_, i) => ({
        left: `${(i * 97) % 100}%`,
        delay: `${(i % 9) * 40}ms`,
        dx: `${((i * 37) % 120) - 60}px`,
        rot: `${(i * 53) % 360}deg`,
        cls: COLORS[i % COLORS.length],
        w: i % 3 === 0 ? "w-1.5" : "w-2",
      })),
    [count]
  );
  if (!run) return null;
  return (
    <div className="pointer-events-none absolute inset-x-0 top-0 z-10 h-0 overflow-visible" aria-hidden>
      {pieces.map((p, i) => (
        <span key={`${run}-${i}`} className={`confetti-piece absolute top-0 h-3 rounded-sm ${p.w} ${p.cls}`} style={{ left: p.left, "--d": p.delay, "--dx": p.dx, "--rot": p.rot, "--dur": `${Math.max(400, ms - MAX_DELAY_MS)}ms` }} />
      ))}
    </div>
  );
}
