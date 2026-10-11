import { useEffect, useRef } from "react";
import { useReducedMotion, useTilt } from "../../hooks/motion.js";
import { MONSTER_GLOW, MONSTER_IMAGES } from "../../lib/monsters.js";

const SIZES = {
  sm: { w: 160, tilt: 5 },
  md: { w: 280, tilt: 6 },
  lg: { w: 420, tilt: 7 },
  xl: { w: 640, tilt: 8 },
};

// At most 40 slow-drifting embers in the level's colour, canvas-drawn so they cost one element, not forty.
// Paused off screen and while the tab is hidden; skipped entirely under prefers-reduced-motion.
function useEmbers(canvasRef, color, active) {
  const reduced = useReducedMotion();
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || reduced || !active) return undefined;
    const ctx = canvas.getContext("2d");
    if (!ctx) return undefined;
    let raf = null;
    let visible = true;
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const resize = () => {
      const r = canvas.getBoundingClientRect();
      canvas.width = Math.max(1, Math.round(r.width * dpr));
      canvas.height = Math.max(1, Math.round(r.height * dpr));
    };
    resize();
    const ro = new ResizeObserver(resize);
    ro.observe(canvas);

    const N = 40;
    const particles = Array.from({ length: N }, () => ({
      x: Math.random(),
      y: 0.55 + Math.random() * 0.5,
      r: 0.6 + Math.random() * 1.6,
      speed: 0.00008 + Math.random() * 0.00014,
      drift: (Math.random() - 0.5) * 0.00006,
      phase: Math.random() * Math.PI * 2,
    }));

    const io = new IntersectionObserver(([e]) => { visible = e.isIntersecting; }, { threshold: 0 });
    io.observe(canvas);
    const onVis = () => { visible = visible && !document.hidden; };
    document.addEventListener("visibilitychange", onVis);

    let last = performance.now();
    const tick = (now) => {
      raf = requestAnimationFrame(tick);
      const dt = now - last;
      last = now;
      if (!visible || document.hidden) return;
      const w = canvas.width, h = canvas.height;
      ctx.clearRect(0, 0, w, h);
      ctx.fillStyle = color;
      for (const p of particles) {
        p.y -= p.speed * dt;
        p.x += p.drift * dt;
        if (p.y < -0.05) { p.y = 1.05; p.x = Math.random(); }
        const twinkle = 0.5 + 0.5 * Math.sin(now * 0.002 + p.phase);
        ctx.globalAlpha = 0.25 + 0.5 * twinkle;
        ctx.beginPath();
        ctx.arc(p.x * w, p.y * h, p.r * dpr, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.globalAlpha = 1;
    };
    raf = requestAnimationFrame(tick);
    return () => {
      cancelAnimationFrame(raf);
      ro.disconnect();
      io.disconnect();
      document.removeEventListener("visibilitychange", onVis);
    };
  }, [canvasRef, color, active, reduced]);
}

/**
 * A rendered monster portrait with its level glow: a pulsing colour aura behind it, a pulsing coloured
 * outline, drifting embers, and a pointer-tilt + glow boost on hover or tap. Falls back to a plain
 * silhouette when the level has no rendered image yet, so the prop contract never breaks.
 *
 * @param {"Easy"|"Medium"|"Hard"} level
 * @param {boolean} glow - embers + pulse on. Off = portrait only (e.g. a dense list).
 * @param {"sm"|"md"|"lg"|"xl"} size
 * @param {number} intensity - 0..1, scales the glow (Live Arena ties this to the monster's HP fraction).
 * @param {boolean} eager - skip lazy-loading (use for the one portrait above the fold, e.g. the hero).
 */
export default function MonsterPortrait({ level, glow = true, size = "md", intensity = 1, eager = false, className = "" }) {
  const dims = SIZES[size] || SIZES.md;
  const tiltRef = useTilt(dims.tilt);
  const canvasRef = useRef(null);
  const img = MONSTER_IMAGES[level];
  const colors = MONSTER_GLOW[level] || MONSTER_GLOW.Easy;
  useEmbers(canvasRef, colors.ember, glow);

  const style = {
    "--mp-aura": colors.aura,
    "--mp-core": colors.core,
    "--mp-outline": colors.outline,
    "--mp-intensity": Math.max(0, Math.min(1, intensity)),
    width: dims.w,
    aspectRatio: img ? img.aspect : 0.8,
  };

  return (
    <div ref={glow ? tiltRef : undefined} className={`monster-portrait tilt ${className}`} style={style} aria-hidden={false}>
      {img ? (
        <picture>
          <source type="image/avif" srcSet={`${img.avif[480]} 480w, ${img.avif[960]} 960w, ${img.avif[1440]} 1440w`} sizes={`${dims.w}px`} />
          <source type="image/webp" srcSet={`${img.webp[480]} 480w, ${img.webp[960]} 960w, ${img.webp[1440]} 1440w`} sizes={`${dims.w}px`} />
          <img
            src={img.webp[960]}
            width={img.width}
            height={img.height}
            alt={`${level === "Hard" ? "The level 3 boss" : `The level ${level === "Easy" ? 1 : 2} monster`}, rendered from the game's model`}
            loading={eager ? "eager" : "lazy"}
            decoding="async"
            style={{ backgroundImage: `url(${img.placeholder})`, backgroundSize: "cover", backgroundPosition: "center" }}
            className="relative z-[1] block h-auto w-full select-none"
            draggable={false}
          />
        </picture>
      ) : (
        <div className="relative z-[1] flex h-full w-full items-center justify-center rounded-3xl bg-[var(--surface-muted)] text-[var(--muted)]" role="img" aria-label={`${level} monster (placeholder)`}>
          <svg viewBox="0 0 100 100" className="h-2/3 w-2/3 opacity-40" aria-hidden>
            <path d="M50 10 L62 38 L92 42 L68 62 L76 90 L50 74 L24 90 L32 62 L8 42 L38 38 Z" fill="currentColor" />
          </svg>
        </div>
      )}
      {glow && <canvas ref={canvasRef} aria-hidden />}
    </div>
  );
}
