import { useEffect, useRef, useState } from "react";

const query = "(prefers-reduced-motion: reduce)";

export function useReducedMotion() {
  const [reduced, setReduced] = useState(() => typeof window !== "undefined" && window.matchMedia(query).matches);
  useEffect(() => {
    const m = window.matchMedia(query);
    const on = () => setReduced(m.matches);
    m.addEventListener("change", on);
    return () => m.removeEventListener("change", on);
  }, []);
  return reduced;
}

// Adds `is-visible` once the element scrolls into view (CSS does the animation).
// One shared, rAF-throttled scroll check. An element is revealed once its top
// passes 92% of the viewport height, so content that was scrolled past quickly
// or jumped over (anchor links) is never left invisible.
const pending = new Set();
let scheduled = false;
function check() {
  scheduled = false;
  const line = window.innerHeight * 0.92;
  for (const el of pending) {
    if (el.getBoundingClientRect().top < line) {
      el.classList.add("is-visible");
      pending.delete(el);
    }
  }
}
function schedule() {
  if (!scheduled) {
    scheduled = true;
    requestAnimationFrame(check);
  }
}
if (typeof window !== "undefined") {
  window.addEventListener("scroll", schedule, { passive: true });
  window.addEventListener("resize", schedule);
}

export function useReveal() {
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return undefined;
    pending.add(el);
    schedule();
    return () => pending.delete(el);
  }, []);
  return ref;
}

// Animates 0 -> target once `start` is true. Reduced motion: jumps straight to target.
export function useCountUp(target, start, duration = 1400) {
  const reduced = useReducedMotion();
  const [value, setValue] = useState(0);
  useEffect(() => {
    if (!start || typeof target !== "number") return;
    if (reduced) {
      setValue(target);
      return;
    }
    let raf;
    const t0 = performance.now();
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / duration);
      setValue(target * (1 - Math.pow(1 - p, 3)));
      if (p < 1) raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [target, start, duration, reduced]);
  return value;
}

// Smoothly eases a displayed number toward a moving target (HP bars, counters).
export function useTween(target, duration = 450) {
  const reduced = useReducedMotion();
  const [value, setValue] = useState(target);
  const from = useRef(target);
  const current = useRef(target);
  useEffect(() => {
    if (typeof target !== "number") return;
    if (reduced) {
      current.current = target;
      setValue(target);
      return;
    }
    from.current = current.current;
    let raf;
    const t0 = performance.now();
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / duration);
      const v = from.current + (target - from.current) * (1 - Math.pow(1 - p, 3));
      current.current = v;
      setValue(v);
      if (p < 1) raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [target, duration, reduced]);
  return value;
}
