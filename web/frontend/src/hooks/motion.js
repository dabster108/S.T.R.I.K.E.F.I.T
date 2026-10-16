import { useCallback, useEffect, useRef, useState } from "react";

const query = "(prefers-reduced-motion: reduce)";
const hasWindow = typeof window !== "undefined";
const osReduced = () => hasWindow && window.matchMedia(query).matches;

// Calm mode (header menu): turns off confetti, tilt, embers and floating reactions. It defaults ON when the OS asks for
// reduced motion; an explicit choice is kept in localStorage. Everything that already honours prefers-reduced-motion
// through useReducedMotion() honours calm mode too, and <html data-calm> lets CSS do the same.
const CALM_KEY = "strikefit.calm";
const calmListeners = new Set();
function readCalm() {
  try {
    const v = localStorage.getItem(CALM_KEY);
    if (v === "1") return true;
    if (v === "0") return false;
  } catch {
    /* storage blocked: fall back to the OS setting */
  }
  return osReduced();
}
let calm = hasWindow ? readCalm() : false;
const applyCalm = () => hasWindow && document.documentElement.toggleAttribute("data-calm", calm);
applyCalm();
export const calmStore = {
  get: () => calm,
  set(next) {
    calm = Boolean(next);
    try {
      localStorage.setItem(CALM_KEY, calm ? "1" : "0");
    } catch {
      /* private mode: the choice lasts for this page only */
    }
    applyCalm();
    calmListeners.forEach((l) => l());
  },
  subscribe(l) {
    calmListeners.add(l);
    return () => calmListeners.delete(l);
  },
};
export function useCalmMode() {
  const [value, setValue] = useState(calm);
  useEffect(() => calmStore.subscribe(() => setValue(calm)), []);
  return [value, calmStore.set];
}

export const prefersReducedMotion = () => osReduced() || calm;

export function useReducedMotion() {
  const [reduced, setReduced] = useState(prefersReducedMotion);
  useEffect(() => {
    const m = window.matchMedia(query);
    const on = () => setReduced(prefersReducedMotion());
    m.addEventListener("change", on);
    const off = calmStore.subscribe(on);
    return () => {
      m.removeEventListener("change", on);
      off();
    };
  }, []);
  return reduced;
}

// One shared IntersectionObserver per option set. Elements already scrolled past
// (anchor jumps) count as seen, so nothing is ever left invisible above the fold.
const observers = new Map();
function observe(el, cb, { rootMargin = "0px 0px -8% 0px", once = true } = {}) {
  if (!hasWindow || typeof IntersectionObserver === "undefined") {
    cb(true);
    return () => {};
  }
  const key = `${rootMargin}|${once}`;
  let entry = observers.get(key);
  if (!entry) {
    const callbacks = new Map();
    const io = new IntersectionObserver(
      (items) => {
        for (const it of items) {
          const fn = callbacks.get(it.target);
          if (!fn) continue;
          const seen = it.isIntersecting || it.boundingClientRect.bottom < 0;
          fn(seen, it);
          if (seen && once) {
            io.unobserve(it.target);
            callbacks.delete(it.target);
          }
        }
      },
      { rootMargin, threshold: [0, 0.01] }
    );
    entry = { io, callbacks };
    observers.set(key, entry);
  }
  entry.callbacks.set(el, cb);
  entry.io.observe(el);
  return () => {
    entry.callbacks.delete(el);
    entry.io.unobserve(el);
  };
}

// Adds `is-visible` once the element scrolls into view (CSS does the animation).
export function useReveal(options) {
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return undefined;
    return observe(el, (seen) => seen && el.classList.add("is-visible"), options);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return ref;
}

// [ref, inView]. once=false keeps tracking (used to pause loops off screen).
export function useInView({ rootMargin = "0px 0px -10% 0px", once = true } = {}) {
  const [el, setEl] = useState(null);
  const [inView, setInView] = useState(false);
  useEffect(() => {
    if (!el) return undefined;
    return observe(el, (seen) => setInView(seen), { rootMargin, once });
  }, [el, rootMargin, once]);
  return [setEl, inView];
}

// [ref, onScreen]: true only while the element is actually in the viewport. Unlike
// useInView, content already scrolled past doesn't count; used to pause loops.
export function useOnScreen(rootMargin = "0px") {
  const [el, setEl] = useState(null);
  const [on, setOn] = useState(false);
  useEffect(() => {
    if (!el) return undefined;
    if (typeof IntersectionObserver === "undefined") {
      setOn(true);
      return undefined;
    }
    const io = new IntersectionObserver(([it]) => setOn(it.isIntersecting), { rootMargin });
    io.observe(el);
    return () => io.disconnect();
  }, [el, rootMargin]);
  return [setEl, on];
}

// Animates 0 -> target once `start` is true. Reduced motion: jumps straight to target.
export function useCountUp(target, start = true, duration = 1400) {
  const reduced = useReducedMotion();
  const [value, setValue] = useState(0);
  const from = useRef(0);
  useEffect(() => {
    if (!start || typeof target !== "number" || !Number.isFinite(target)) return undefined;
    if (reduced) {
      from.current = target;
      setValue(target);
      return undefined;
    }
    const begin = from.current;
    let raf;
    const t0 = performance.now();
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / duration);
      const v = begin + (target - begin) * (1 - Math.pow(1 - p, 3));
      from.current = v;
      setValue(v);
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
  const current = useRef(target);
  useEffect(() => {
    if (typeof target !== "number") return undefined;
    if (reduced) {
      current.current = target;
      setValue(target);
      return undefined;
    }
    const from = current.current;
    let raf;
    const t0 = performance.now();
    const tick = (now) => {
      const p = Math.min(1, (now - t0) / duration);
      const v = from + (target - from) * (1 - Math.pow(1 - p, 3));
      current.current = v;
      setValue(v);
      if (p < 1) raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [target, duration, reduced]);
  return value;
}

// Shared rAF-throttled scroll listener; callbacks write styles directly (no re-render).
const scrollFns = new Set();
let scrollQueued = false;
function runScroll() {
  scrollQueued = false;
  for (const fn of scrollFns) fn(window.scrollY);
}
function onScroll() {
  if (!scrollQueued) {
    scrollQueued = true;
    requestAnimationFrame(runScroll);
  }
}
export function useScroll(fn) {
  const ref = useRef(fn);
  ref.current = fn;
  useEffect(() => {
    const cb = (y) => ref.current(y);
    if (!scrollFns.size) {
      window.addEventListener("scroll", onScroll, { passive: true });
      window.addEventListener("resize", onScroll);
    }
    scrollFns.add(cb);
    cb(window.scrollY);
    return () => {
      scrollFns.delete(cb);
      if (!scrollFns.size) {
        window.removeEventListener("scroll", onScroll);
        window.removeEventListener("resize", onScroll);
      }
    };
  }, []);
}

// Pulls an element a few pixels toward the pointer (hero buttons).
export function useMagnetic(strength = 0.25) {
  const reduced = useReducedMotion();
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || reduced || !window.matchMedia("(pointer: fine)").matches) return undefined;
    const move = (e) => {
      const r = el.getBoundingClientRect();
      const x = (e.clientX - (r.left + r.width / 2)) * strength;
      const y = (e.clientY - (r.top + r.height / 2)) * strength;
      el.style.transform = `translate3d(${x.toFixed(1)}px, ${y.toFixed(1)}px, 0)`;
    };
    const leave = () => {
      el.style.transform = "";
    };
    el.addEventListener("pointermove", move);
    el.addEventListener("pointerleave", leave);
    return () => {
      el.removeEventListener("pointermove", move);
      el.removeEventListener("pointerleave", leave);
    };
  }, [reduced, strength]);
  return ref;
}

// 3D tilt toward the pointer; sets --rx/--ry for the .tilt class.
export function useTilt(max = 7) {
  const reduced = useReducedMotion();
  const ref = useRef(null);
  useEffect(() => {
    const el = ref.current;
    if (!el || reduced || !window.matchMedia("(pointer: fine)").matches) return undefined;
    const move = (e) => {
      const r = el.getBoundingClientRect();
      const px = (e.clientX - r.left) / r.width - 0.5;
      const py = (e.clientY - r.top) / r.height - 0.5;
      el.style.setProperty("--rx", `${(-py * max).toFixed(2)}deg`);
      el.style.setProperty("--ry", `${(px * max).toFixed(2)}deg`);
      el.style.setProperty("--ty", "-4px");
    };
    const leave = () => {
      el.style.setProperty("--rx", "0deg");
      el.style.setProperty("--ry", "0deg");
      el.style.setProperty("--ty", "0px");
    };
    el.addEventListener("pointermove", move);
    el.addEventListener("pointerleave", leave);
    return () => {
      el.removeEventListener("pointermove", move);
      el.removeEventListener("pointerleave", leave);
    };
  }, [reduced, max]);
  return ref;
}

// Moves an element at `speed` × the scroll offset while it's near the top (hero parallax).
export function useParallax(speed = 0.15) {
  const reduced = useReducedMotion();
  const ref = useRef(null);
  const apply = useCallback(
    (y) => {
      const el = ref.current;
      if (!el) return;
      if (reduced || y > window.innerHeight * 1.2) return;
      el.style.transform = `translate3d(0, ${(y * speed).toFixed(1)}px, 0)`;
    },
    [reduced, speed]
  );
  useScroll(apply);
  return ref;
}
