import { useId, useState } from "react";
import { LEVELS, TIER_LABEL, roundToInt, simulateHit } from "../../lib/game.js";
import { SegmentedControl } from "../ui/Controls.jsx";

const MAX_V = 8;
const TIER_COLOR = { none: "var(--border-strong)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };
const LEVEL_OPTS = [
  { id: "Easy", label: "Level 1 · hammers" },
  { id: "Medium", label: "Level 2 · swords" },
];

export default function SwingDemo() {
  const [level, setLevel] = useState("Easy");
  const [speed, setSpeed] = useState(3.4);
  const id = useId();
  const s = LEVELS[level].swing;
  const hit = simulateHit(speed, level);
  const pct = (v) => `${(v / MAX_V) * 100}%`;
  const zones = [
    { from: 0, to: s.min, tier: "none" },
    { from: s.min, to: s.tiers.light, tier: "tap" },
    { from: s.tiers.light, to: s.tiers.medium, tier: "light" },
    { from: s.tiers.medium, to: s.tiers.full, tier: "medium" },
    { from: s.tiers.full, to: MAX_V, tier: "full" },
  ];

  return (
    <div className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--border)] px-5 py-4">
        <div>
          <h3 className="text-sm font-semibold text-[var(--ink)]">Try it: drag the swing speed</h3>
          <p className="mt-0.5 text-xs text-[var(--muted)]">Real thresholds and damage formula from the game</p>
        </div>
        <SegmentedControl label="Level" options={LEVEL_OPTS} value={level} onChange={setLevel} />
      </div>

      <div className="grid gap-8 px-5 py-6 lg:grid-cols-5">
        <div className="lg:col-span-3">
          <div className="flex items-baseline justify-between">
            <label htmlFor={id} className="text-xs font-medium text-[var(--muted)]">
              Controller speed at impact
            </label>
            <output htmlFor={id} className="text-3xl font-bold tabular-nums tracking-tight text-[var(--ink)]">
              {speed.toFixed(1)} <span className="text-sm font-medium text-[var(--muted)]">m/s</span>
            </output>
          </div>

          <div className="relative mt-4">
            <div className="flex h-3 w-full gap-[2px] overflow-hidden rounded-full" aria-hidden>
              {zones.map((z) => (
                <span
                  key={z.tier}
                  className="h-full transition-opacity duration-200"
                  style={{ width: pct(z.to - z.from), background: TIER_COLOR[z.tier], opacity: hit.tier === z.tier ? 1 : 0.35 }}
                />
              ))}
            </div>
            <span
              className="absolute -top-1 h-5 w-1 -translate-x-1/2 rounded-full bg-[var(--ink)] shadow-sm"
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
          <div className="relative h-8 text-[0.68rem] tabular-nums text-[var(--muted)]" aria-hidden>
            {zones.map((z, i) => (
              <span key={z.tier} className="absolute -translate-x-1/2" style={{ left: pct(z.from), top: i % 2 ? 14 : 0 }}>
                {z.from ? z.from.toFixed(1) : "0"}
              </span>
            ))}
            <span className="absolute -translate-x-full whitespace-nowrap" style={{ left: "100%", top: 14 }}>
              8 m/s
            </span>
          </div>
        </div>

        <div className="lg:col-span-2" aria-live="polite">
          <div className="flex items-center gap-3">
            <span className="h-10 w-1.5 rounded-full" style={{ background: TIER_COLOR[hit.tier] }} aria-hidden />
            <div>
              <p className={`text-2xl font-bold tracking-tight ${hit.counts ? "text-[var(--ink)]" : "text-[var(--muted)]"}`}>{TIER_LABEL[hit.tier]}</p>
              <p className="text-xs text-[var(--muted)]">{hit.counts ? "The swing counts" : `Below ${s.min} m/s the game ignores it`}</p>
            </div>
          </div>
          <dl className="mt-5 grid grid-cols-3 gap-3 border-t border-[var(--border)] pt-4">
            {[
              ["Multiplier", hit.counts ? `×${hit.multiplier.toFixed(2)}` : "—"],
              ["Damage", hit.damage.toFixed(1)],
              ["XP", hit.counts ? `+${hit.xp}` : "0"],
            ].map(([k, v]) => (
              <div key={k}>
                <dt className="text-[0.7rem] font-medium text-[var(--muted)]">{k}</dt>
                <dd className="text-lg font-bold tabular-nums text-[var(--ink)]">{v}</dd>
              </div>
            ))}
          </dl>
        </div>
      </div>

      <details className="border-t border-[var(--border)] px-5 py-3 text-xs text-[var(--ink-secondary)]">
        <summary className="cursor-pointer font-medium text-[var(--accent)]">Where these numbers come from</summary>
        <ul className="mt-2 list-disc space-y-1 pl-4 text-[var(--muted)]">
          <li>
            {LEVELS[level].name} tiers: no hit below {s.min} m/s, tap from {s.min}, light from {s.tiers.light}, medium from {s.tiers.medium}, full power from{" "}
            {s.tiers.full} m/s (XPManager and HitDetection values in the level scene).
          </li>
          <li>
            Damage = {s.base} base × a multiplier rising from ×1 at {s.min} m/s to ×{s.maxMul} at {s.cap} m/s, plus 0.08 per m/s, × {s.weaponMul} for the{" "}
            {level === "Easy" ? "hammer" : "sword"}.
          </li>
          <li>
            XP = {s.xpHit} + half the damage, +{s.xpFull} for full power, +{roundToInt(s.xpFull * 0.5)} for medium; taps earn half.
          </li>
          <li>Shown for a side-on hit with stamina left and no wrist-rotation bonus. Hitting the monster's front adds 25% in the headset.</li>
        </ul>
      </details>
    </div>
  );
}
