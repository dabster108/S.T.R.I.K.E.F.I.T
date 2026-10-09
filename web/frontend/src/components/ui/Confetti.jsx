import { useEffect, useMemo, useState } from "react";
import { useReducedMotion } from "../../hooks/motion.js";

// A short burst of confetti over its parent (which must be position: relative). Plays once each time `fire` changes to a
// new truthy value; nothing at all under prefers-reduced-motion. Colours are the dashboard's existing palette classes.
const COLORS = ["bg-[var(--accent)]", "bg-amber-500", "bg-emerald-600", "bg-rose-600", "bg-[var(--accent-strong)]"];
const PIECES = 36;

export default function Confetti({ fire }) {
  const reduced = useReducedMotion();
  const [run, setRun] = useState(null);
  useEffect(() => {
    if (!fire || reduced) return undefined;
    setRun(fire);
    const t = setTimeout(() => setRun(null), 1800);
    return () => clearTimeout(t);
  }, [fire, reduced]);
  const pieces = useMemo(
    () =>
      Array.from({ length: PIECES }, (_, i) => ({
        left: `${(i * 97) % 100}%`,
        delay: `${(i % 9) * 40}ms`,
        dx: `${((i * 37) % 120) - 60}px`,
        rot: `${(i * 53) % 360}deg`,
        cls: COLORS[i % COLORS.length],
        w: i % 3 === 0 ? "w-1.5" : "w-2",
      })),
    []
  );
  if (!run) return null;
  return (
    <div className="pointer-events-none absolute inset-x-0 top-0 z-10 h-0 overflow-visible" aria-hidden>
      {pieces.map((p, i) => (
        <span key={`${run}-${i}`} className={`confetti-piece absolute top-0 h-3 rounded-sm ${p.w} ${p.cls}`} style={{ left: p.left, "--d": p.delay, "--dx": p.dx, "--rot": p.rot }} />
      ))}
    </div>
  );
}
