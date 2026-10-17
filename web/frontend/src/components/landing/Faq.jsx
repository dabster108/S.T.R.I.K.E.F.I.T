import { LEVELS } from "../../lib/game.js";
import { Reveal } from "./shared.jsx";

// A short FAQ with answers taken from the project's own documentation and code (README, backend/fitness.js, lib/game.js). Native
// <details> elements: keyboard accessible, no script. Nothing here is a claim the repository does not make.
const { min, cap } = LEVELS.Easy.swing;

const QA = [
  [
    "What headset do I need?",
    "A Meta Quest 2. The game is built for it (a standalone Android build) and tested on it. A Quest 3 should run it too, because it plays Quest 2 apps, but that has not been tested.",
  ],
  [
    "How does weapon damage work?",
    `The game reads the real speed of each controller. A swing slower than ${min} m/s does nothing. Above that, damage grows with speed until ${cap} m/s, up to ${LEVELS.Easy.swing.maxMul} times the base value on Level 1 and ${LEVELS.Medium.swing.maxMul} times on Level 2. Each hit is labelled light, medium or full power by its speed.`,
  ],
  [
    "How are calories estimated?",
    "They are an estimate, always marked \"est.\", not a measurement. The dashboard uses the MET method with an assumed 70 kg body mass and an assumed intensity scaled by how hard you swing. The headset has no heart-rate sensor.",
  ],
  [
    "Is it safe?",
    "Stop if you feel dizzy or in pain. The game has no walking: you move by stepping, so keep your play space clear.",
  ],
  [
    "How is my data stored?",
    "Each fight is sent from the headset to the project's server and stored in a database: your player name, level, score, hits, damage, duration and health left, plus the events of the fight (hits, attacks). There is no login: a player is just the name chosen in the headset. Story Mode progress stays on the headset.",
  ],
];

export default function Faq() {
  return (
    <section aria-labelledby="faq-title" className="mx-auto max-w-3xl px-4 pb-24 pt-4 sm:px-6">
      <Reveal>
        <h2 id="faq-title" className="t-h2 text-[var(--ink)]">
          Questions
        </h2>
      </Reveal>
      <Reveal delay={80} className="mt-8 divide-y divide-[var(--border)] overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]">
        {QA.map(([q, a]) => (
          <details key={q} className="group">
            <summary className="press flex cursor-pointer list-none items-center justify-between gap-4 px-5 py-4 text-left text-base font-bold text-[var(--ink)] hover:bg-[var(--surface-muted)] focus-visible:outline focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-[var(--accent)] [&::-webkit-details-marker]:hidden">
              {q}
              <span className="text-lg font-bold text-[var(--accent-strong)] transition-transform group-open:rotate-45" aria-hidden>
                +
              </span>
            </summary>
            <p className="px-5 pb-5 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{a}</p>
          </details>
        ))}
      </Reveal>
    </section>
  );
}
