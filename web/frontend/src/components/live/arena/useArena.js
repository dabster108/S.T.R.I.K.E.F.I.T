import { useEffect, useReducer, useRef, useState } from "react";
import { useLiveMessages } from "../../../live/LiveProvider.jsx";
import { impactOf, initialArena, nextExpiry } from "../../../lib/arena.js";
import { reduceArena } from "../../../lib/arenaApp.js";
import { clampSpeed } from "../../../lib/game.js";

const SWING_IDLE_MS = 2500; // the gauge falls back to "No hit" when no swing has landed for this long

/**
 * The transient parts of one live fight that the stream only ever announces once: the attack the monster is winding up, whether its guard
 * is up, a weapon clash, and the latest swing. Listens to the shared stream (it never opens or changes a connection) and wakes itself
 * only when something on screen is about to expire.
 */
export function useArena(id, levelKey, enraged) {
  const [state, setState] = useState(initialArena);
  const [swing, setSwing] = useState(null);
  const [impact, setImpact] = useState(null); // the latest full-power hit or clash, { key, kind, damage? }: drives the one-shot impact animations
  const impactSeq = useRef(0);
  const [, redraw] = useReducer((n) => n + 1, 0);
  const enragedRef = useRef(enraged);
  enragedRef.current = enraged;
  const idle = useRef(null);

  useEffect(() => {
    setState(initialArena());
    setSwing(null);
    setImpact(null);
  }, [id]);

  useLiveMessages((type, e) => {
    if (type !== "event_created" || e?.sessionId !== id) return;
    const at = performance.now();
    setState((s) => reduceArena(s, e, { levelKey, enraged: enragedRef.current, at }));
    const im = impactOf(e);
    if (im) setImpact({ ...im, key: ++impactSeq.current });
    const speed = e.type === "hit" ? e.payload?.swingSpeed : null;
    if (typeof speed === "number" && Number.isFinite(speed)) {
      setSwing({ speed: clampSpeed(speed), tier: typeof e.payload.tier === "string" ? e.payload.tier.toLowerCase() : null, at });
      clearTimeout(idle.current);
      idle.current = setTimeout(() => setSwing(null), SWING_IDLE_MS);
    }
  });
  useEffect(() => () => clearTimeout(idle.current), []);

  const now = performance.now();
  const expiry = nextExpiry(state, now);
  useEffect(() => {
    if (expiry === null) return undefined;
    const t = setTimeout(redraw, Math.max(30, expiry - performance.now() + 30));
    return () => clearTimeout(t);
  }, [expiry, state]);

  return { state, swing, now, impact };
}
