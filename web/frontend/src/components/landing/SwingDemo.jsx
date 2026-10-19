import { useEffect, useId, useState } from "react";
import { LEVELS, TIER_LABEL, roundToInt, simulateHit } from "../../lib/game.js";
import { useTween } from "../../hooks/motion.js";
import { SegmentedControl } from "../ui/Controls.jsx";

const MAX_V = 8;
const TIER_COLOR = { none: "var(--border-strong)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };
const LEVEL_OPTS = [
  { id: "Easy", label: "Level 1 · hammers" },
  { id: "Medium", label: "Level 2 · swords" },
];

// Highlight for the part of the demo that matches the active "How it works" step.
const zone = (on) =>
  `rounded-xl transition-[background-color,box-shadow] duration-500 ${on ? "bg-[var(--accent-soft)] shadow-[inset_0_0_0_2px_var(--accent-ring)]" : "shadow-[inset_0_0_0_2px_transparent]"}`;

export default function SwingDemo({ step = null, onChange }) {
  const [level, setLevel] = useState("Easy");
  const [speed, setSpeed] = useState(3.4);
  const id = useId();
  useEffect(() => {
    onChange?.({ level, speed });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [level, speed]);
  const s = LEVELS[level].swing;
  const hit = simulateHit(speed, level);
  const maxDamage = simulateHit(s.cap, level).damage;
  const dmg = useTween(hit.damage, 350);
  const xp = useTween(hit.xp, 350);
  const pct = (v) => `${(v / MAX_V) * 100}%`;
  const zones = [
    { from: 0, to: s.min, tier: "none" },
    { from: s.min, to: s.tiers.light, tier: "tap" },
    { from: s.tiers.light, to: s.tiers.medium, tier: "light" },
    { from: s.tiers.medium, to: s.tiers.full, tier: "medium" },
    { from: s.tiers.full, to: MAX_V, tier: "full" },
  ];
  const chips = [
    ["No hit", 0.8, `< ${s.min}`],
    ["Tap", s.min, `${s.min}`],
    ["Light", s.tiers.light, `${s.tiers.light}`],
    ["Medium", s.tiers.medium, `${s.tiers.medium.toFixed(1)}`],
    ["Full power", s.tiers.full, `${s.tiers.full.toFixed(1)}`],
  ];

  return (
    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-lift)]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--border)] px-5 py-4">
        <div>
          <h3 className="text-base font-bold text-[var(--ink)]">Try it: drag the swing speed</h3>
          <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">Real thresholds and damage formula from the game</p>
        </div>
        <SegmentedControl label="Level" options={LEVEL_OPTS} value={level} onChange={setLevel} />
      </div>

      <div className="space-y-4 p-4 sm:p-5">
        <div className={`${zone(step === 1)} flex items-end justify-between gap-3 p-3`}>
          <label htmlFor={id} className="text-xs font-bold uppercase tracking-wide text-[var(--muted)]">
            Controller speed at impact
          </label>
          <output htmlFor={id} className="text-4xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">
            {speed.toFixed(1)}
            <span className="ml-1 text-base font-bold text-[var(--muted)]">m/s</span>
          </output>
        </div>

        <div className={`${zone(step === 0)} p-3`}>
          <div className="relative">
            <div className="flex h-3.5 w-full gap-[2px] overflow-hidden rounded-full" aria-hidden>
              {zones.map((z) => (
                <span
                  key={z.tier}
                  className="h-full transition-opacity duration-200"
                  style={{ width: pct(z.to - z.from), background: TIER_COLOR[z.tier], opacity: hit.tier === z.tier ? 1 : 0.3 }}
                />
              ))}
            </div>
            <span
              className="absolute -top-1.5 h-6 w-1.5 -translate-x-1/2 rounded-full bg-[var(--ink)] shadow-md transition-[left] duration-150"
              style={{ left: pct(speed) }}
              aria-hidden
            />
          </div>
          <input
            id={id}
            type="range"
            min="0"
            max={MAX_V}
            step="0.1"
            value={speed}
            onChange={(e) => setSpeed(Number(e.target.value))}
            aria-valuetext={`${speed.toFixed(1)} metres per second: ${TIER_LABEL[hit.tier]}`}
            className="mt-3 w-full cursor-pointer accent-[var(--accent)]"
          />
          <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label="Jump to a tier threshold">
            {chips.map(([label, v, txt]) => {
              const on = label === TIER_LABEL[hit.tier];
              return (
                <button
                  key={label}
                  type="button"
                  onClick={() => setSpeed(v)}
                  className={`press rounded-full px-2.5 py-1 text-xs font-bold ring-1 ring-inset ${
                    on ? "bg-[var(--accent)] text-white ring-[var(--accent)]" : "bg-[var(--surface)] text-[var(--ink-secondary)] ring-[var(--border)] hover:ring-[var(--accent-ring)] hover:text-[var(--accent-strong)]"
                  }`}
                >
                  {label} <span className={on ? "text-blue-100" : "text-[var(--muted)]"}>{txt}</span>
                </button>
              );
            })}
          </div>
        </div>

        <div className="grid gap-3 sm:grid-cols-2" aria-live="polite">
          <div className={`${zone(step === 3)} flex items-center gap-3 p-3`}>
            <span className="h-12 w-1.5 shrink-0 rounded-full transition-colors" style={{ background: TIER_COLOR[hit.tier] }} aria-hidden />
            <div className="min-w-0">
              <p className={`text-2xl font-extrabold tracking-tight ${hit.counts ? "text-[var(--ink)]" : "text-[var(--muted)]"}`}>{TIER_LABEL[hit.tier]}</p>
              <p className="text-xs font-semibold text-[var(--muted)]">
                {hit.counts ? (
                  <>
                    <span className="font-extrabold text-[var(--accent)]">+{Math.round(xp)} XP</span> {speed >= 4.8 ? "· heavy impact sound" : "· normal impact sound"}
                  </>
                ) : (
                  `Below ${s.min} m/s the game ignores it`
                )}
              </p>
            </div>
          </div>
          <div className={`${zone(step === 2)} p-3`}>
            <div className="flex items-baseline justify-between">
              <p className="text-xs font-bold uppercase tracking-wide text-[var(--muted)]">Damage</p>
              <p className="text-xs font-bold text-[var(--muted)]">{hit.counts ? `×${hit.multiplier.toFixed(2)}` : "—"}</p>
            </div>
            <p className="text-2xl font-extrabold tabular-nums text-[var(--ink)]">{dmg.toFixed(1)}</p>
            <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-gray-100">
              <div
                className="h-full origin-left rounded-full bg-gradient-to-r from-[var(--tier-tap)] to-[var(--tier-full)] transition-transform duration-300"
                style={{ transform: `scaleX(${maxDamage ? hit.damage / maxDamage : 0})` }}
              />
            </div>
          </div>
        </div>
      </div>

      <details className="group border-t border-[var(--border)] px-5 py-3 text-xs text-[var(--ink-secondary)]">
        <summary className="cursor-pointer rounded font-bold text-[var(--accent)] hover:text-[var(--accent-strong)]">Where these numbers come from</summary>
        <ul className="mt-2 list-disc space-y-1 pl-4 font-medium leading-relaxed">
          <li>
            {LEVELS[level].name} tiers: no hit below {s.min} m/s, tap from {s.min}, light from {s.tiers.light}, medium from {s.tiers.medium}, full power from {s.tiers.full} m/s,
            speed capped at {s.cap} m/s (XPManager and HitDetection values in {level === "Easy" ? "Level1_Easy.unity" : "Level2_Medium.unity"}).
          </li>
          <li>
            Damage = {s.base} base × a multiplier rising from ×1 at {s.min} m/s to ×{s.maxMul} at {s.cap} m/s, plus 0.08 per m/s, × {s.weaponMul} for the {level === "Easy" ? "hammer" : "sword"}.
          </li>
          <li>
            XP = {s.xpHit} + half the damage, +{s.xpFull} for full power, +{roundToInt(s.xpFull * 0.5)} for medium; taps earn half.
          </li>
          <li>Shown for a side-on hit with stamina left and no wrist-rotation bonus. Hitting the monster&apos;s front adds 25% in the headset.</li>
        </ul>
      </details>
    </div>
  );
}
