import { LEVELS } from "../../lib/game.js";
import { Reveal } from "./shared.jsx";

// "How to play in 60 seconds": five steps, every number from lib/game.js (the game's own values).
const { min, cap } = LEVELS.Easy.swing;

const STEPS = [
  ["Pick a name", "Put on the headset and point the laser at your name (Player 1, 2, 3 or Guest). The fight records under it."],
  ["Start", "Point at Start and pull the trigger, or press B."],
  ["Swing", `Slower than ${min} m/s counts for nothing. Faster hits harder, up to ${cap} m/s. Both hands are tracked.`],
  ["Step aside", "The monster telegraphs each attack. Step back or sideways and its swing misses."],
  ["Again", "A retries, B pauses. Play Again, Level Select and Exit Game are on the result panel."],
];

export default function HowTo60() {
  return (
    <section aria-labelledby="how60-title" className="mx-auto max-w-6xl px-4 pb-20 pt-20 sm:px-6 lg:pt-24">
      <Reveal className="max-w-2xl">
        <h2 id="how60-title" className="t-h2 text-[var(--ink)]">
          How to play in 60 seconds
        </h2>
      </Reveal>
      <ol className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        {STEPS.map(([title, text], i) => (
          <Reveal key={title} as="li" delay={i * 60} className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)]">
            <span className="t-index text-[var(--accent-strong)]" aria-hidden>
              {i + 1}
            </span>
            <p className="mt-2 text-base font-extrabold tracking-tight text-[var(--ink)]">{title}</p>
            <p className="mt-2 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{text}</p>
          </Reveal>
        ))}
      </ol>
    </section>
  );
}
