import { LEVELS } from "../../lib/game.js";
import { useTilt } from "../../hooks/motion.js";
import { LevelBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";
import { Kicker, Reveal } from "./shared.jsx";

const BEHAVIOUR = {
  Easy: "Slow, clearly telegraphed attacks: a slash, a heavy blow and a special, with longer wind-ups for the bigger hits. Raises its guard from time to time.",
  Medium:
    "Faster and more varied: picks from seven attacks, from quick sweeps and a twin-strike combo to a spinning blow, a ground slam and a leaping cleave, and forces its special the first time it drops to 40% health.",
  Hard: "The design is set; the level isn't built yet.",
};

const MAX_V = 8;

// Tier bands on a 0–8 m/s ruler, with the thresholds printed under their ticks.
function TierRuler({ swing }) {
  const segs = [
    [0, swing.min, "var(--border)"],
    [swing.min, swing.tiers.light, "var(--tier-tap)"],
    [swing.tiers.light, swing.tiers.medium, "var(--tier-light)"],
    [swing.tiers.medium, swing.tiers.full, "var(--tier-medium)"],
    [swing.tiers.full, MAX_V, "var(--tier-full)"],
  ];
  const ticks = [swing.min, swing.tiers.medium, swing.tiers.full];
  return (
    <div className="mt-6" role="img" aria-label={`Swings count from ${swing.min} m/s; full power from ${swing.tiers.full} m/s`}>
      <div className="flex h-2.5 gap-[2px] overflow-hidden rounded-full">
        {segs.map(([a, b, c]) => (
          <span key={a} style={{ width: `${((b - a) / MAX_V) * 100}%`, background: c }} />
        ))}
      </div>
      <div className="readout relative mt-1.5 h-4 text-[0.6875rem] font-bold text-[var(--muted)]" aria-hidden>
        {ticks.map((t) => (
          <span key={t} className="absolute -translate-x-1/2" style={{ left: `${(t / MAX_V) * 100}%` }}>
            {t.toFixed(1)}
          </span>
        ))}
        <span className="absolute right-0">{MAX_V} m/s</span>
      </div>
    </div>
  );
}

function PlayableCard({ l, wide }) {
  const tilt = useTilt(wide ? 4 : 6);
  const facts = [
    ["Weapons", l.weapons],
    ["Arena", l.environment],
    ["Monster", l.monster],
    ["Health", `${l.hp} each`],
  ];
  return (
    <article ref={tilt} className="tilt glow-border" aria-label={`${l.name}, playable`}>
      <div className={`relative flex h-full flex-col rounded-[calc(var(--radius-xl)-1.5px)] bg-[var(--surface)] ${wide ? "p-7 sm:p-9" : "p-7"}`}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <span className={`block font-extrabold leading-none tracking-[-0.06em] text-[var(--accent)] tabular-nums ${wide ? "text-[5.5rem] sm:text-[7rem]" : "text-[4.5rem]"}`} aria-hidden>
              {String(l.number).padStart(2, "0")}
            </span>
            <h3 className="mt-3 flex items-center gap-2 text-2xl font-extrabold tracking-tight text-[var(--ink)]">
              {l.name} <LevelBadge level={l.key} />
            </h3>
          </div>
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--good-soft)] px-2.5 py-1 text-xs font-bold text-[var(--good-ink)] ring-1 ring-inset ring-emerald-200">
            <Icon name="check" className="h-3.5 w-3.5" strokeWidth={2.4} /> Playable
          </span>
        </div>

        <dl className={`mt-6 grid gap-x-6 border-t border-[var(--border)] text-sm ${wide ? "sm:grid-cols-2" : ""}`}>
          {facts.map(([k, v]) => (
            <div key={k} className="flex justify-between gap-4 border-b border-[var(--border)] py-2.5">
              <dt className="font-semibold text-[var(--muted)]">{k}</dt>
              <dd className="text-right font-bold text-[var(--ink)]">{v}</dd>
            </div>
          ))}
        </dl>
        <p className="measure mt-5 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{BEHAVIOUR[l.key]}</p>
        <TierRuler swing={l.swing} />
      </div>
    </article>
  );
}

export default function Levels() {
  const { Easy, Medium, Hard } = LEVELS;
  return (
    <section id="levels" aria-labelledby="levels-title" className="mx-auto max-w-6xl px-4 pb-28 pt-24 sm:px-6 lg:pb-36 lg:pt-32">
      {/* heading sits on the right; the cards below start from the left */}
      <Reveal className="lg:ml-[41.666%]">
        <Kicker index="04">Levels</Kicker>
        <h2 id="levels-title" className="t-h2 mt-5 max-w-xl text-[var(--ink)]">
          Three arenas, each a step up
        </h2>
        <p className="measure mt-5 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">Two are playable today. The third is designed and on the way.</p>
      </Reveal>

      <div className="mt-14 grid gap-5 lg:mt-16 lg:grid-cols-12 lg:items-start lg:gap-0">
        <Reveal className="lg:col-span-7">
          <PlayableCard l={Easy} wide />
        </Reveal>
        {/* dropped down and pulled left so it overlaps Level 1 slightly */}
        <Reveal delay={120} className="relative z-[1] lg:col-span-5 lg:-ml-8 lg:mt-24">
          <PlayableCard l={Medium} />
        </Reveal>

        <Reveal delay={200} className="lg:col-span-12 lg:mt-8">
          <article className="flex flex-col gap-4 rounded-2xl border-2 border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)] px-6 py-5 sm:flex-row sm:items-center sm:gap-8" aria-label={`${Hard.name}, planned`}>
            <span className="text-5xl font-extrabold leading-none tracking-[-0.06em] text-[var(--border-strong)] tabular-nums" aria-hidden>
              03
            </span>
            <div className="min-w-0 flex-1">
              <h3 className="flex flex-wrap items-center gap-2 text-lg font-extrabold tracking-tight text-[var(--ink)]">
                {Hard.name} <LevelBadge level={Hard.key} />
              </h3>
              <p className="mt-1 text-sm font-semibold text-[var(--muted)]">
                {Hard.environment} · {Hard.monster} · weapons {Hard.weapons.toLowerCase()}. Swing thresholds will be set when the level is built.
              </p>
            </div>
            <span className="inline-flex shrink-0 items-center gap-1.5 self-start rounded-full bg-[var(--surface)] px-2.5 py-1 text-xs font-extrabold uppercase tracking-wide text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border-strong)] sm:self-center">
              <Icon name="clock" className="h-3.5 w-3.5" strokeWidth={2.4} /> Planned
            </span>
          </article>
        </Reveal>
      </div>
    </section>
  );
}
