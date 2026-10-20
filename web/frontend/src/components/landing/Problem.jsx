import { LEVELS, simulateHit } from "../../lib/game.js";
import { useCountUp, useInView } from "../../hooks/motion.js";
import Icon from "../ui/Icon.jsx";
import { Card } from "../ui/Card.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

const PAINS = [
  ["hand", "Gestures, not effort", "Point a controller at a target and most games register a hit, whether you threw your whole body into it or flicked your wrist."],
  ["bolt", "The lazy move wins", "The score goes up either way, so the easiest way to win is also the laziest way to move."],
  ["heart", "Effort goes unrewarded", "That matters when the point is exercise. If effort isn't measured it isn't rewarded, and players quickly stop giving it."],
];

const FLICK = 0.9;
const STEADY = 3.0;
const FULL = 6.2;

// One row of the comparison: the same swing under both rules. The bar is how much of the best result the swing earns.
function Row({ label, speed, gesture, damage, best, strong }) {
  const share = best ? damage / best : 0;
  return (
    <tr>
      <th scope="row" className="py-4 pr-4 align-top font-bold text-[var(--ink)]">
        {label}
        <span className="readout block text-xs font-semibold text-[var(--muted)]">{speed} m/s</span>
      </th>
      <td className="w-[34%] py-4 pr-4 align-top">
        <span className="block font-semibold text-[var(--ink-secondary)]">{gesture}</span>
        <span className="mt-2 block h-1.5 overflow-hidden rounded-full bg-[var(--surface-muted)]" aria-hidden>
          <span className="draw-x block h-full rounded-full bg-[var(--border-strong)]" style={{ "--w": 1, "--bar-delay": "120ms" }} />
        </span>
      </td>
      <td className="w-[34%] py-4 align-top">
        <span className={`readout block text-right font-extrabold ${strong ? "text-[var(--accent)]" : "text-[var(--muted)]"}`}>
          {damage > 0 ? `${damage.toFixed(1)} damage` : "No hit"}
        </span>
        <span className="mt-2 block h-1.5 overflow-hidden rounded-full bg-[var(--surface-muted)]" aria-hidden>
          <span className="draw-x block h-full rounded-full bg-[var(--accent)]" style={{ "--w": share, "--bar-delay": strong ? "320ms" : "220ms" }} />
        </span>
      </td>
    </tr>
  );
}

// The big bento card: one monster, one slow and one fast swing, scored by both rules. Bars grow and the damage counts up once.
function Comparison() {
  const full = simulateHit(FULL, "Easy");
  const flick = simulateHit(FLICK, "Easy");
  const steady = simulateHit(STEADY, "Easy");
  const [seenRef, seen] = useInView({ rootMargin: "0px 0px -20% 0px" });
  const shown = useCountUp(full.damage, seen, 1100);
  const flickDamage = flick.counts ? flick.damage : 0;
  return (
    <Reveal className="lg:col-span-7 lg:row-span-3">
      <Card className="spotlight flex h-full flex-col overflow-hidden" data-testid="problem-compare">
        <div ref={seenRef} className="relative border-b border-[var(--border)] bg-[var(--accent-soft)] px-6 py-7 sm:px-8">
          <p className="t-kicker text-[var(--accent-strong)]">Level 1, same monster, two swings</p>
          <div className="mt-5 flex flex-wrap items-end gap-x-5 gap-y-2">
            <p className="t-figure text-[var(--accent-strong)]" aria-label={`${full.damage.toFixed(1)} damage`}>
              {shown.toFixed(1)}
            </p>
            <p className="measure-tight pb-1 text-[0.95rem] font-semibold leading-relaxed text-[var(--ink-secondary)]">
              damage from a {FULL} m/s swing. A {FLICK} m/s flick is below the {LEVELS.Easy.swing.min} m/s floor and does nothing at all.
            </p>
          </div>
        </div>
        <div className="flex flex-1 flex-col px-6 py-5 sm:px-8">
          <table className="w-full text-left text-[0.95rem]">
            <caption className="sr-only">What each kind of game does with a slow and a fast swing</caption>
            <thead>
              <tr className="border-b-2 border-[var(--ink)]">
                <th scope="col" className="t-kicker pb-3 pr-4 font-bold">Swing</th>
                <th scope="col" className="t-kicker pb-3 pr-4 font-bold">Gesture scoring</th>
                <th scope="col" className="t-kicker pb-3 text-right font-bold text-[var(--accent-strong)]">STRIKEFIT</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--border)]">
              <Row label="Wrist flick" speed={FLICK} gesture="Hit" damage={flickDamage} best={full.damage} />
              <Row label="Steady swing" speed={STEADY} gesture="Hit" damage={steady.damage} best={full.damage} strong />
              <Row label="Full swing" speed={FULL} gesture="Hit" damage={full.damage} best={full.damage} strong />
            </tbody>
          </table>
          <p className="mt-auto flex items-start gap-2 pt-5 text-sm font-medium text-[var(--muted)]">
            <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0" />
            Gesture scoring gives both the same result, so there&apos;s no reason to try harder. Here the harder swing is the only one that lands.
          </p>
        </div>
      </Card>
    </Reveal>
  );
}

export default function Problem() {
  return (
    <section id="problem" aria-labelledby="problem-title" className="pb-20 pt-24 lg:pb-24 lg:pt-36">
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        <SectionHead id="problem-title" index="01" eyebrow="The problem" title="Most VR fitness games count movements. Few measure effort." layout="split">
          STRIKEFIT reads the real speed of each controller at the moment of impact. Too slow and the hit doesn&apos;t count. Faster swings deal more damage and earn more XP.
        </SectionHead>

        <div className="mt-12 grid gap-4 lg:mt-16 lg:grid-cols-12">
          <Comparison />
          {PAINS.map(([icon, title, body], i) => (
            <Reveal key={title} index={i + 1} className="lg:col-span-5">
              <Card className="spotlight h-full overflow-hidden p-6" as="article">
                <div className="flex items-center gap-3">
                  <span className="grid h-9 w-9 place-items-center rounded-[10px] bg-[var(--accent-soft)] text-[var(--accent-strong)]">
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
