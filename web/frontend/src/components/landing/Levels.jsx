import { LEVELS } from "../../lib/game.js";
import { useTilt } from "../../hooks/motion.js";
import { LevelBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

const BEHAVIOUR = {
  Easy: "Slow, clearly telegraphed attacks: a slash, a heavy blow and a special, with longer wind-ups for the bigger hits. Raises its guard from time to time.",
  Medium:
    "Faster and more varied: picks from seven attacks, from quick sweeps and a twin-strike combo to a spinning blow, a ground slam and a leaping cleave, and forces its special the first time it drops to 40% health.",
  Hard: "The design is set; the level isn't built yet.",
};
const ICON = { Easy: "hammer", Medium: "swords", Hard: "shield" };

function TierStrip({ swing }) {
  const max = 8;
  const segs = [
    [0, swing.min, "var(--border)"],
    [swing.min, swing.tiers.light, "var(--tier-tap)"],
    [swing.tiers.light, swing.tiers.medium, "var(--tier-light)"],
    [swing.tiers.medium, swing.tiers.full, "var(--tier-medium)"],
    [swing.tiers.full, max, "var(--tier-full)"],
  ];
  return (
    <div className="mt-5">
      <div className="flex h-2 gap-[2px] overflow-hidden rounded-full" aria-hidden>
        {segs.map(([a, b, c]) => (
          <span key={a} style={{ width: `${((b - a) / max) * 100}%`, background: c }} />
        ))}
      </div>
      <p className="mt-2 text-xs font-semibold text-[var(--muted)]">
        Counts from {swing.min} m/s · full power from {swing.tiers.full} m/s
      </p>
    </div>
  );
}

function LevelCard({ l }) {
  const tilt = useTilt(6);
  const planned = l.status === "planned";
  const body = (
    <div className={`relative flex h-full flex-col rounded-[calc(var(--radius-xl)-1.5px)] p-6 ${planned ? "bg-[var(--surface-muted)]" : "bg-[var(--surface)]"}`}>
      <div className="flex items-start justify-between gap-3">
        <span
          className={`flex h-12 w-12 items-center justify-center rounded-xl ${planned ? "bg-[var(--surface)] text-[var(--muted)] ring-1 ring-inset ring-[var(--border)]" : "bg-[var(--accent-soft)] text-[var(--accent)] ring-1 ring-inset ring-[var(--accent-ring)]"}`}
        >
          <Icon name={ICON[l.key]} className="h-6 w-6" />
        </span>
        {planned ? (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--surface)] px-2.5 py-1 text-xs font-extrabold uppercase tracking-wide text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border-strong)]">
            <Icon name="clock" className="h-3.5 w-3.5" strokeWidth={2.4} /> Planned
          </span>
        ) : (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--good-soft)] px-2.5 py-1 text-xs font-bold text-[var(--good-ink)] ring-1 ring-inset ring-emerald-200">
            <Icon name="check" className="h-3.5 w-3.5" strokeWidth={2.4} /> Playable
          </span>
        )}
      </div>
      <div className="mt-5 flex items-center gap-2">
        <h3 className="text-2xl font-extrabold tracking-tight text-[var(--ink)]">{l.name}</h3>
        <LevelBadge level={l.key} />
      </div>
      <dl className="mt-4 divide-y divide-[var(--border)] text-sm">
        {[["Weapons", l.weapons], ["Environment", l.environment], ["Monster", l.monster], l.hp && ["Health", `${l.hp} each, you and the monster`]]
          .filter(Boolean)
          .map(([k, v]) => (
            <div key={k} className="flex justify-between gap-4 py-2.5">
              <dt className="font-semibold text-[var(--muted)]">{k}</dt>
              <dd className="text-right font-bold text-[var(--ink)]">{v}</dd>
            </div>
          ))}
      </dl>
      <p className="mt-4 flex-1 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{BEHAVIOUR[l.key]}</p>
      {l.swing ? <TierStrip swing={l.swing} /> : <p className="mt-5 rounded-lg border border-dashed border-[var(--border-strong)] px-3 py-2 text-xs font-semibold text-[var(--muted)]">Swing thresholds will be set when the level is built.</p>}
    </div>
  );
  return planned ? (
    <article ref={tilt} className="tilt h-full rounded-2xl border-2 border-dashed border-[var(--border-strong)]" aria-label={`${l.name}, planned`}>
      {body}
    </article>
  ) : (
    <article ref={tilt} className="tilt glow-border h-full" aria-label={`${l.name}, playable`}>
      {body}
    </article>
  );
}

export default function Levels() {
  return (
    <section id="levels" aria-labelledby="levels-title" className="mx-auto max-w-6xl px-4 py-24 sm:px-6 lg:py-32">
      <SectionHead id="levels-title" eyebrow="Levels" title="Three arenas, each a step up">
        Two are playable today. The third is designed and on the way.
      </SectionHead>
      <div className="mt-14 grid gap-5 lg:grid-cols-3">
        {Object.values(LEVELS).map((l, i) => (
          <Reveal key={l.key} delay={i * 110} className="h-full">
            <LevelCard l={l} />
          </Reveal>
        ))}
      </div>
    </section>
  );
}
