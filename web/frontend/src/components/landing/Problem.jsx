import { LEVELS, simulateHit } from "../../lib/game.js";
import Icon from "../ui/Icon.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

const PAINS = [
  ["Gestures, not effort", "Point a controller at a target and most games register a hit, whether you threw your whole body into it or flicked your wrist."],
  ["The lazy move wins", "The score goes up either way, so the easiest way to win is also the laziest way to move."],
  ["Effort goes unrewarded", "That matters when the point is exercise. If effort isn't measured it isn't rewarded, and players quickly stop giving it."],
];

const FLICK = 0.9;
const FULL = 6.2;

export default function Problem() {
  const full = simulateHit(FULL, "Easy");
  const flick = simulateHit(FLICK, "Easy");
  const rows = [
    { swing: "Wrist flick", speed: FLICK, gesture: "Hit", ours: flick.counts ? `${flick.damage.toFixed(1)} damage` : "No hit", strong: false },
    { swing: "Full swing", speed: FULL, gesture: "Hit", ours: `${full.damage.toFixed(1)} damage`, strong: true },
  ];
  return (
    <section id="problem" aria-labelledby="problem-title" className="pb-20 pt-24 lg:pb-24 lg:pt-36">
      <div className="mx-auto grid max-w-6xl gap-12 px-4 sm:px-6 lg:grid-cols-12 lg:gap-10">
        <div className="lg:col-span-5">
          <div className="lg:sticky lg:top-[calc(var(--nav-h)+2.5rem)]">
            <SectionHead id="problem-title" index="01" eyebrow="The problem" title="Most VR fitness games count movements. Few measure effort.">
              STRIKEFIT reads the real speed of each controller at the moment of impact. Too slow and the hit doesn&apos;t count. Faster swings deal more damage and earn more XP.
            </SectionHead>
          </div>
        </div>

        <ol className="lg:col-span-6 lg:col-start-7 lg:pt-16">
          {PAINS.map(([title, body], i) => (
            <Reveal as="li" key={title} delay={i * 110} className="grid grid-cols-[2.75rem_minmax(0,1fr)] gap-x-4 border-t border-[var(--border)] py-8 first:border-[var(--ink)] last:pb-0">
              <span className="t-index pt-1.5 text-[var(--muted)]">1.{i + 1}</span>
              <div>
                <h3 className="t-h3 text-[var(--ink)]">{title}</h3>
                <p className="measure-tight mt-2 font-medium leading-relaxed text-[var(--ink-secondary)]">{body}</p>
              </div>
            </Reveal>
          ))}
        </ol>
      </div>

      {/* the comparison runs off the right edge of the page */}
      <div className="mx-auto mt-20 max-w-6xl px-4 sm:px-6 lg:mt-28">
        <Reveal from="right" className="bleed-right overflow-hidden rounded-l-2xl border-y border-l border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]">
          <div className="grid lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
            <div className="relative border-b border-[var(--border)] bg-[var(--accent-soft)] px-6 py-8 sm:px-8 lg:border-b-0 lg:border-r lg:py-10">
              <p className="t-kicker text-[var(--accent-strong)]">Level 1, same monster, two swings</p>
              <p className="t-figure mt-5 text-[var(--accent-strong)]">{full.damage.toFixed(1)}</p>
              <p className="measure-tight mt-4 text-[0.95rem] font-semibold leading-relaxed text-[var(--ink-secondary)]">
                damage from a {FULL} m/s swing. A {FLICK} m/s flick is below the {LEVELS.Easy.swing.min} m/s floor and does nothing at all.
              </p>
            </div>

            <div className="px-6 py-6 sm:px-8 lg:py-8 lg:pr-[max(2rem,calc(50vw-36rem+1.5rem))]">
              <table className="w-full text-left text-[0.95rem]">
                <caption className="sr-only">What each kind of game does with a slow and a fast swing</caption>
                <thead>
                  <tr className="border-b-2 border-[var(--ink)]">
                    <th scope="col" className="t-kicker pb-3 pr-4 font-bold">
                      Swing
                    </th>
                    <th scope="col" className="t-kicker pb-3 pr-4 font-bold">
                      Gesture scoring
                    </th>
                    <th scope="col" className="t-kicker pb-3 text-right font-bold text-[var(--accent-strong)]">
                      STRIKEFIT
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--border)]">
                  {rows.map((r) => (
                    <tr key={r.swing}>
                      <th scope="row" className="py-4 pr-4 font-bold text-[var(--ink)]">
                        {r.swing}
                        <span className="readout block text-xs font-semibold text-[var(--muted)]">{r.speed} m/s</span>
                      </th>
                      <td className="py-4 pr-4 font-semibold text-[var(--ink-secondary)]">{r.gesture}</td>
                      <td className={`readout py-4 text-right font-extrabold ${r.strong ? "text-[var(--accent)]" : "text-[var(--muted)]"}`}>{r.ours}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="mt-5 flex items-start gap-2 text-sm font-medium text-[var(--muted)]">
                <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0" />
                Gesture scoring gives both the same result, so there&apos;s no reason to try harder. Here the harder swing is the only one that lands.
              </p>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
