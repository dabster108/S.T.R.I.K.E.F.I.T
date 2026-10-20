import { Link } from "react-router-dom";
import { levelFor, guardians } from "../../lib/story.js";
import { MONSTER_GLOW } from "../../lib/monsters.js";
import MonsterPortrait from "../ui/MonsterPortrait.jsx";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import { Kicker, Reveal } from "./shared.jsx";

// Landing: Story Mode (three chapters, one line each) and the three guardians. Chapter lines are the per-level intro lines in
// content/story.json (the same ones the headset shows on its chapter cards); names, weapons and lore are the guardians[] entries.
// Portraits are the same <MonsterPortrait> (same images, same glow) as /monsters.
const KEYS = ["Easy", "Medium", "Hard"];

function Guardian({ g, delay }) {
  const key = KEYS[g.level - 1];
  const glow = MONSTER_GLOW[key];
  return (
    <Reveal delay={delay} className="h-full">
      <Link
        to={`/story#guardians`}
        className="press group flex h-full flex-col items-center rounded-3xl border border-[var(--border)] bg-[var(--surface)] px-6 py-8 text-center shadow-[var(--shadow-card)] transition-shadow hover:shadow-[var(--shadow-lift)]"
      >
        <MonsterPortrait level={key} size="md" className="transition-transform duration-300 group-hover:scale-[1.03]" />
        <p className="mt-6 text-xs font-bold uppercase tracking-wider text-[var(--muted)]">Chapter {g.level}</p>
        <h3 className="mt-1 text-xl font-extrabold tracking-tight text-[var(--ink)]">{g.name}</h3>
        <p className="mt-1 text-sm font-bold" style={{ color: glow.outline }}>
          {g.weapon}
        </p>
        <p className="mt-3 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{levelFor(g.level)?.intro}</p>
        <span className="press mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-xs font-bold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)] transition-transform group-hover:translate-x-0.5">
          Read the lore <Icon name="arrowRight" className="h-3.5 w-3.5" strokeWidth={2.6} />
        </span>
      </Link>
    </Reveal>
  );
}

export default function StoryMode() {
  if (!guardians.length) return null;
  return (
    <section id="monsters" aria-labelledby="story-mode-title" className="mx-auto max-w-6xl px-4 pb-28 pt-4 sm:px-6 lg:pt-8">
      <Reveal className="max-w-2xl">
        <Kicker index="06">Story Mode</Kicker>
        <h2 id="story-mode-title" className="t-h2 mt-5 text-[var(--ink)]">
          Three chapters. Three guardians.
        </h2>
        <p className="measure mt-5 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">
          Story Mode in the headset links the three levels into one run: a card for each guardian, a few words between the fights, and an
          ending. Free play is still there. The portraits are the actual Unity models.
        </p>
      </Reveal>
      <div className="mt-12 grid gap-5 sm:grid-cols-3">
        {guardians.map((g, i) => (
          <Guardian key={g.id} g={g} delay={i * 100} />
        ))}
      </div>
      <Reveal delay={300} className="mt-8 flex flex-wrap items-center justify-center gap-3 text-center">
        <Link to="/story#guardians" className={buttonClass("primary", "lg", "glow")}>
          Meet the guardians <Icon name="arrowRight" className="h-4 w-4" strokeWidth={2.4} />
        </Link>
        <Link to="/monsters" className={buttonClass("secondary", "lg")}>
          See the monsters in full
        </Link>
      </Reveal>
    </section>
  );
}
