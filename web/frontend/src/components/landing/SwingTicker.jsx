import { useEffect, useRef } from "react";
import { SWING_SPEED_CAP, TIER_LABEL, LEVELS, simulateHit, tierFor } from "../../lib/game.js";
import { LOOP_SEC, STILL_FRAME_SPEED, swingSpeedAt } from "../../lib/swingScript.js";
import { useOnScreen, useReducedMotion } from "../../hooks/motion.js";

// The hero's live swing-speed ticker: one scripted swing loops from 0 to the 8 m/s cap while the number, the bar and the tier colour
// follow it, using Level 1's real thresholds and damage formula (lib/game.js). It paints straight into the DOM every frame (no React
// re-render), only the bar's transform changes, and the loop runs only while the card is on screen. Reduced motion and Calm mode get one
// still frame (a full-power swing). The numbers are scripted: the card says so.

const TIER_COLOR = { none: "var(--border-strong)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };
const S = LEVELS.Easy.swing;
const MARKS = [
  [S.min, "tap"],
  [S.tiers.light, "light"],
  [S.tiers.medium, "medium"],
  [S.tiers.full, "full"],
];

export default function SwingTicker() {
  const reduced = useReducedMotion();
  const [setEl, onScreen] = useOnScreen("0px");
  const root = useRef(null);
  const num = useRef(null);
  const bar = useRef(null);
  const label = useRef(null);
  const dmg = useRef(null);
  const lastTier = useRef(null);

  useEffect(() => {
    const paint = (v) => {
      const tier = tierFor(v, "Easy");
      if (num.current) num.current.textContent = v.toFixed(1);
      if (bar.current) bar.current.style.transform = `scaleX(${Math.min(1, v / SWING_SPEED_CAP)})`;
      if (tier !== lastTier.current) {
        lastTier.current = tier;
        root.current?.style.setProperty("--ticker", TIER_COLOR[tier]);
        if (label.current) label.current.textContent = tier === "none" ? "No hit" : TIER_LABEL[tier];
      }
      if (dmg.current) dmg.current.textContent = tier === "none" ? "below the 1.2 m/s floor" : `${simulateHit(v, "Easy").damage.toFixed(1)} damage`;
    };
    if (reduced) {
      paint(STILL_FRAME_SPEED);
      return undefined;
    }
    if (!onScreen) return undefined;
    let raf;
    const t0 = performance.now();
    const tick = (now) => {
      paint(swingSpeedAt((now - t0) / 1000));
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [reduced, onScreen]);

  return (
    <div
      ref={(n) => {
        root.current = n;
        setEl(n);
      }}
      className="w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)]"
      style={{ "--ticker": TIER_COLOR.none }}
      role="img"
      aria-label={`Example swing, scripted: speed rises from 0 to ${SWING_SPEED_CAP} metres per second and passes through each strength tier; a loop takes ${LOOP_SEC.toFixed(1)} seconds`}
    >
      <div aria-hidden>
        <div className="flex items-baseline justify-between gap-3">
          <p className="t-kicker">Live swing · Level 1</p>
          <p className="text-[0.68rem] font-semibold text-[var(--muted)]">scripted example</p>
        </div>
        <div className="mt-2 flex items-end justify-between gap-4">
          <p className="flex items-baseline gap-1.5 tabular-nums text-[var(--ink)]">
            <span ref={num} className="inline-block w-[2.9ch] text-right text-5xl font-extrabold leading-none tracking-tight">
              0.0
            </span>
            <span className="text-base font-bold text-[var(--muted)]">m/s</span>
          </p>
          <div className="min-w-0 pb-1 text-right">
            <p className="flex items-center justify-end gap-2 text-sm font-extrabold text-[var(--ink)]">
              <span className="h-2.5 w-2.5 rounded-full" style={{ background: "var(--ticker)" }} />
              <span ref={label} className="inline-block min-w-[5.5rem] text-left">No hit</span>
            </p>
            <p ref={dmg} className="mt-0.5 text-xs font-semibold tabular-nums text-[var(--muted)]">
              below the 1.2 m/s floor
            </p>
          </div>
        </div>
        <div className="relative mt-3 h-2.5 overflow-hidden rounded-full bg-gray-100">
          <div ref={bar} className="absolute inset-0 origin-left rounded-full will-change-transform" style={{ transform: "scaleX(0)", background: "var(--ticker)" }} />
        </div>
        <div className="relative mt-1 h-4 text-[0.65rem] font-bold tabular-nums text-[var(--muted)]">
          {MARKS.map(([v, tier]) => (
            <span key={tier} className="absolute -translate-x-1/2" style={{ left: `${(v / SWING_SPEED_CAP) * 100}%` }}>
              {v}
            </span>
          ))}
          <span className="absolute right-0">{SWING_SPEED_CAP}</span>
        </div>
      </div>
    </div>
  );
}
