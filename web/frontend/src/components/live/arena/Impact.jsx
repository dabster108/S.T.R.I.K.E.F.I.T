import { useEffect, useRef, useState } from "react";
import { useReducedMotion } from "../../../hooks/motion.js";

// The Live Arena's impact moments. Everything here is transform and opacity only, runs once per event (Web Animations API, so a new
// event restarts it cleanly), and is skipped entirely under reduced motion or Calm mode.

const EASE = "cubic-bezier(0.22, 1, 0.36, 1)";

/** A quick scale "punch" and an accent flash on a card when a full-power hit lands. Attach `ref` to the card and `flash` to an overlay inside it. */
export function usePunch(impact) {
  const reduced = useReducedMotion();
  const card = useRef(null);
  const flash = useRef(null);
  useEffect(() => {
    if (reduced || impact?.kind !== "full" || !card.current) return;
    card.current.animate([{ transform: "scale(1)" }, { transform: "scale(1.035)", offset: 0.35 }, { transform: "scale(1)" }], { duration: 320, easing: EASE });
    flash.current?.animate([{ opacity: 0 }, { opacity: 0.22, offset: 0.2 }, { opacity: 0 }], { duration: 420, easing: "ease-out" });
  }, [impact?.key, impact?.kind, reduced]);
  return { card, flash };
}

/** One ring that expands and fades when weapons clash. */
export function useRing(clashAt) {
  const reduced = useReducedMotion();
  const ring = useRef(null);
  useEffect(() => {
    if (reduced || !clashAt || !ring.current) return;
    ring.current.animate([{ transform: "scale(0.45)", opacity: 0.85 }, { transform: "scale(1.5)", opacity: 0 }], { duration: 640, easing: EASE });
  }, [clashAt, reduced]);
  return ring;
}

function Pop({ pop, onDone }) {
  const el = useRef(null);
  useEffect(() => {
    const a = el.current?.animate(
      [
        { transform: "translate3d(0, 6px, 0) scale(0.8)", opacity: 0 },
        { transform: "translate3d(0, -4px, 0) scale(1.12)", opacity: 1, offset: 0.2 },
        { transform: "translate3d(0, -30px, 0) scale(1)", opacity: 0 },
      ],
      { duration: 950, easing: EASE, fill: "forwards" }
    );
    if (!a) return undefined;
    a.onfinish = () => onDone(pop.key);
    return () => a.cancel();
  }, [pop.key, onDone]);
  return (
    <span ref={el} className="absolute right-0 top-0 text-xl font-extrabold tabular-nums text-[var(--hp-monster)] opacity-0 [text-shadow:0_1px_0_var(--surface)]" aria-hidden>
      +{pop.damage}
    </span>
  );
}

/** Floating "+damage" numbers that rise from the monster's health bar. aria-hidden: the kill feed already says it. */
export function DamagePops({ impact }) {
  const reduced = useReducedMotion();
  const [pops, setPops] = useState([]);
  useEffect(() => {
    if (reduced || impact?.kind !== "full") return;
    setPops((p) => [...p.slice(-3), { key: impact.key, damage: impact.damage }]);
  }, [impact?.key, impact?.kind, impact?.damage, reduced]);
  const done = (key) => setPops((p) => p.filter((x) => x.key !== key));
  if (!pops.length) return null;
  return (
    <span className="pointer-events-none absolute -top-7 right-0 h-7 w-24" aria-hidden>
      {pops.map((p) => (
        <Pop key={p.key} pop={p} onDone={done} />
      ))}
    </span>
  );
}
