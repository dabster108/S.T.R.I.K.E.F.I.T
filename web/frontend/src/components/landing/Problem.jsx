import { useState } from "react";
import { LEVELS, simulateHit, tierFor } from "../../lib/game.js";
import { useInView } from "../../hooks/motion.js";
import Icon from "../ui/Icon.jsx";
import { Card } from "../ui/Card.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

const PAINS = [
  ["hand", "Gestures, not effort", "Point a controller at a target and most games register a hit whether you threw your whole body into it or flicked your wrist."],
  ["bolt", "The lazy move wins", "The score goes up either way, so the easiest way to win is also the laziest way to move."],
  ["heart", "Effort goes unrewarded", "That matters when the point is exercise. If effort isn't measured it isn't rewarded, and players quickly stop giving it."],
];

const TIER_LABEL = { none: "No hit", tap: "Tap", light: "Light", medium: "Medium", full: "Full power" };
const TIER_COLOR = { none: "var(--muted)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };

// Live slider: drag 0 – 8 m/s and see gesture scoring vs STRIKEFIT side by side.
function SwingSlider() {
  const [speed, setSpeed] = useState(3.0);
  const [seenRef, seen] = useInView({ rootMargin: "0px 0px -20% 0px" });
  const level = "Easy";
  const hit = simulateHit(speed, level);
  const tier = hit.counts ? tierFor(speed, level) : "none";
  const min = LEVELS[level].swing.min;

  return (
    <div ref={seenRef} className="flex h-full flex-col">
      {/* header */}
      <div className="border-b border-[var(--border)] bg-[var(--accent-soft)] px-6 py-5 sm:px-8">
        <p className="t-kicker text-[var(--accent-strong)]">Level 1 · same swing, two games</p>
        <p className="mt-2 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">
          Drag the speed. See what each scoring method does with it.
        </p>
      </div>

      <div className="flex flex-1 flex-col gap-5 px-6 py-5 sm:px-8">
        {/* slider */}
        <div>
          <div className="mb-3 flex items-baseline justify-between gap-2">
            <label htmlFor="swing-speed" className="t-kicker">
              Swing speed
            </label>
            <span className="readout text-lg font-extrabold tabular-nums text-[var(--ink)]">{speed.toFixed(1)} m/s</span>
          </div>
          <input
            id="swing-speed"
            type="range"
            min={0}
            max={8}
            step={0.1}
            value={speed}
            onChange={(e) => setSpeed(Number(e.target.value))}
            className="h-2 w-full cursor-pointer appearance-none rounded-full bg-[var(--surface-muted)] accent-[var(--accent)]"
            aria-valuetext={`${speed.toFixed(1)} metres per second`}
          />
          <div className="mt-1.5 flex justify-between text-[0.65rem] font-semibold text-[var(--muted)]" aria-hidden>
            <span>0</span><span>2</span><span>4</span><span>6</span><span>8 m/s</span>
          </div>
        </div>

        {/* comparison columns */}
        <div className="grid grid-cols-2 gap-3">
          {/* gesture scoring */}
          <div className="flex flex-col gap-2 rounded-xl bg-[var(--surface-muted)] p-3.5">
            <p className="t-kicker">Gesture scoring</p>
            <p className="text-lg font-extrabold text-[var(--ink-secondary)]">
              {speed > 0 ? "Hit" : "No hit"}
            </p>
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-[var(--border)]">
              <div className="h-full rounded-full bg-[var(--border-strong)]" style={{ width: speed > 0 ? "100%" : "0%" }} />
            </div>
            <p className="text-xs font-medium text-[var(--muted)]">
              {speed > 0 ? "Full credit regardless of speed" : "No motion detected"}
            </p>
          </div>

          {/* STRIKEFIT */}
          <div className="flex flex-col gap-2 rounded-xl bg-[var(--accent-soft)] p-3.5">
            <p className="t-kicker text-[var(--accent-strong)]">STRIKEFIT</p>
            <p
              className="text-lg font-extrabold tabular-nums"
              style={{ color: hit.counts ? TIER_COLOR[tier] : "var(--muted)" }}
            >
              {hit.counts ? `${hit.damage.toFixed(1)} dmg` : "No hit"}
            </p>
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-[var(--border)]">
              <div
                className="h-full rounded-full transition-[width] duration-150"
                style={{
                  width: hit.counts ? `${Math.min(100, (hit.damage / simulateHit(8, level).damage) * 100)}%` : "0%",
                  background: hit.counts ? TIER_COLOR[tier] : "transparent",
                }}
              />
            </div>
            <p className="text-xs font-medium text-[var(--muted)]">
              {hit.counts
                ? `${TIER_LABEL[tier]} · ×${hit.multiplier?.toFixed(2) ?? "—"}`
                : `Under ${min} m/s floor`}
            </p>
          </div>
        </div>

        <p className="flex items-start gap-2 text-xs font-medium text-[var(--muted)]">
          <Icon name="info" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          Gesture scoring gives every swing above zero the same result. STRIKEFIT does nothing below {min} m/s and scales above it.
        </p>
      </div>
    </div>
  );
}

export default function Problem() {
  return (
    <section id="problem" aria-labelledby="problem-title" className="pb-20 pt-24 lg:pb-24 lg:pt-36">
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        <SectionHead id="problem-title" index="01" eyebrow="The problem" title="Most VR fitness games count movements. Few measure effort." layout="split">
          STRIKEFIT reads the real speed of each controller at the moment of impact. Too slow and the hit doesn&apos;t count. Faster swings deal more damage and earn more XP.
        </SectionHead>

        <div className="mt-12 grid gap-4 lg:mt-16 md:grid-cols-3 lg:grid-cols-12 lg:grid-rows-3">
          {/* large bento card: slider */}
          <Reveal className="md:col-span-3 lg:col-span-7 lg:row-span-3">
            <Card className="spotlight flex h-full flex-col overflow-hidden" data-testid="problem-compare">
              <SwingSlider />
            </Card>
          </Reveal>

          {/* three pain-point boxes */}
          {PAINS.map(([icon, title, body], i) => (
            <Reveal key={title} index={i + 1} className="lg:col-span-5">
              <Card className="spotlight h-full overflow-hidden p-5 sm:p-6" as="article">
                <div className="flex items-center gap-3">
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] bg-[var(--accent-soft)] text-[var(--accent-strong)]">
                    <Icon name={icon} className="h-[18px] w-[18px]" />
                  </span>
                  <span className="t-index text-[var(--muted)]">1.{i + 1}</span>
                </div>
                <h3 className="t-h3 mt-4 text-[var(--ink)]">{title}</h3>
                <p className="mt-2 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{body}</p>
              </Card>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
