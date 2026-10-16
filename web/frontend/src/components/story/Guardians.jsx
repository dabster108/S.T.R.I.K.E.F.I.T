import { Link } from "react-router-dom";
import story from "../../content/story.json";
import { LEVEL_KEYS, LEVELS } from "../../lib/game.js";
import { GUARDIAN_HABITS, MONSTER_GLOW, MONSTER_TIPS, MONSTER_WEAPON, TUNING_NOTE } from "../../lib/monsters.js";
import { LevelBadge } from "../ui/Badge.jsx";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import MonsterPortrait from "../ui/MonsterPortrait.jsx";
import { Kicker, Reveal } from "../landing/shared.jsx";

// "Meet the guardians": the three guardians after the story slides. One per screen on desktop, stacked cards on phones.
// Names and intro lines come from content/story.json (the file the headset reads; the synced copy is imported, never
// retyped). Portraits are the same <MonsterPortrait> as /monsters and the landing hero (same images, same level glow:
// Level 1 yellow-green, Level 2 violet, Level 3 orange with electric-blue embers); its reserved aspect ratio and lazy loading
// mean nothing shifts. Weapons and habits come from lib/monsters.js (design notes), marked "tuning in progress".

const ROMAN = ["I", "II", "III"];

function Guardian({ entry, index }) {
  const glow = MONSTER_GLOW[entry.key];
  const meta = LEVELS[entry.key];
  const habits = (GUARDIAN_HABITS[entry.key] || []).map((i) => MONSTER_TIPS[entry.key]?.[i]).filter(Boolean);
  const flip = index % 2 === 1; // portrait on the right for the second guardian, desktop only
  return (
    <article
      aria-labelledby={`guardian-${entry.key}`}
      className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)] sm:p-7 lg:flex lg:min-h-[80vh] lg:items-center lg:rounded-none lg:border-0 lg:bg-transparent lg:p-0 lg:shadow-none"
    >
      <div className="grid w-full min-w-0 grid-cols-[minmax(0,1fr)] items-center gap-8 lg:grid-cols-12 lg:gap-14">
        <Reveal from={flip ? "right" : "left"} className={`flex justify-center lg:col-span-5 ${flip ? "lg:order-last" : ""}`}>
          <MonsterPortrait level={entry.key} size="lg" className="max-w-full" />
        </Reveal>
        <Reveal delay={90} className="lg:col-span-7">
          <p className="flex items-center gap-3">
            <span className="t-index" aria-hidden>
              {ROMAN[index] ?? index + 1}
            </span>
            <LevelBadge level={entry.key} />
            <span className="t-eyebrow">Level {entry.level}</span>
          </p>
          <h3 id={`guardian-${entry.key}`} className="t-h2 mt-4 text-[var(--ink)]">
            {entry.guardian}
          </h3>
          <p className="mt-2 text-sm font-bold" style={{ color: glow.outline }}>
            {glow.name} glow
          </p>
          {entry.intro && (
            <blockquote className="measure mt-5 border-l-4 pl-4 text-lg font-medium italic leading-relaxed text-[var(--ink-secondary)]" style={{ borderColor: glow.outline }}>
              {entry.intro}
            </blockquote>
          )}
          <dl className="mt-6 grid gap-x-8 gap-y-4 sm:grid-cols-2">
            <div>
              <dt className="t-kicker">Weapon</dt>
              <dd className="mt-1 text-base font-bold text-[var(--ink)]">{MONSTER_WEAPON[entry.key]}</dd>
            </div>
            <div>
              <dt className="t-kicker">Arena</dt>
              <dd className="mt-1 text-base font-bold text-[var(--ink)]">{meta?.environment}</dd>
            </div>
          </dl>
          {habits.length > 0 && (
            <div className="mt-6">
              <p className="t-kicker">How it fights</p>
              <ul className="mt-2 space-y-2">
                {habits.map((h) => (
                  <li key={h} className="flex gap-2.5 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">
                    <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: glow.outline }} aria-hidden />
                    <span>{h}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Reveal>
      </div>
    </article>
  );
}

export default function Guardians() {
  const levels = (story.levels ?? []).slice().sort((a, b) => LEVEL_KEYS.indexOf(a.key) - LEVEL_KEYS.indexOf(b.key));
  if (!levels.length) return null;
  return (
    <section aria-labelledby="guardians-title" className="mt-14">
      <Reveal className="max-w-2xl">
        <Kicker>The guardians</Kicker>
        <h2 id="guardians-title" className="t-h2 mt-5 text-[var(--ink)]">
          Three guardians remain.
        </h2>
        <p className="mt-3 text-sm font-semibold text-[var(--muted)]">{TUNING_NOTE}</p>
      </Reveal>
      <div className="mt-8 space-y-6 lg:mt-4 lg:space-y-0">
        {levels.map((e, i) => (
          <Guardian key={e.key} entry={e} index={i} />
        ))}
      </div>
      <Reveal className="mt-10 flex flex-wrap items-center gap-3 border-t border-[var(--border)] pt-8">
        <Link to="/monsters" className={buttonClass("secondary", "lg")}>
          Meet all guardians <Icon name="arrowRight" className="h-4 w-4" strokeWidth={2.4} />
        </Link>
        <Link to="/dashboard" className={buttonClass("primary", "lg", "glow")}>
          Play now
        </Link>
        <p className="w-full text-xs font-medium text-[var(--muted)] sm:w-auto sm:flex-1">The game is played in VR on a Meta Quest 2; your fights then appear on the dashboard.</p>
      </Reveal>
    </section>
  );
}
