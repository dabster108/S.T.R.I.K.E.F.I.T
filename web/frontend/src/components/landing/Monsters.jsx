import { Link } from "react-router-dom";
import { LEVELS } from "../../lib/game.js";
import { MONSTER_LABEL } from "../../lib/monsters.js";
import MonsterPortrait from "../ui/MonsterPortrait.jsx";
import Icon from "../ui/Icon.jsx";
import { Kicker, Reveal } from "./shared.jsx";

function MonsterCard({ l, delay }) {
  return (
    <Reveal delay={delay} className="h-full">
      <Link
        to={`/monsters#${l.key}`}
        className="press group flex h-full flex-col items-center rounded-3xl border border-[var(--border)] bg-[var(--surface)] px-6 py-8 text-center shadow-[var(--shadow-card)] transition-shadow hover:shadow-[var(--shadow-lift)]"
      >
        <MonsterPortrait level={l.key} size="md" className="transition-transform duration-300 group-hover:scale-[1.03]" />
        <h3 className="mt-6 text-xl font-extrabold tracking-tight text-[var(--ink)]">{l.monster}</h3>
        <p className="mt-1 text-sm font-bold text-[var(--accent-strong)]">
          {l.name} · {MONSTER_LABEL[l.key]}
        </p>
        <p className="mt-3 text-sm font-medium text-[var(--muted)]">{l.weapons}</p>
        <span className="press mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-xs font-bold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)] transition-transform group-hover:translate-x-0.5">
          How to beat it <Icon name="arrowRight" className="h-3.5 w-3.5" strokeWidth={2.6} />
        </span>
      </Link>
    </Reveal>
  );
}

// Landing-page teaser for the three monsters; the full write-up is at /monsters.
export default function Monsters() {
  const { Easy, Medium, Hard } = LEVELS;
  return (
    <section id="monsters" aria-labelledby="monsters-title" className="mx-auto max-w-6xl px-4 pb-28 pt-4 sm:px-6 lg:pt-8">
      <Reveal className="max-w-2xl">
        <Kicker index="05">Know your enemy</Kicker>
        <h2 id="monsters-title" className="t-h2 mt-5 text-[var(--ink)]">
          Three monsters, rendered from the game
        </h2>
        <p className="measure mt-5 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">
          Not concept art — these are the actual Unity models. Open one for real tips on beating it.
        </p>
      </Reveal>
      <div className="mt-12 grid gap-5 sm:grid-cols-3">
        <MonsterCard l={Easy} delay={0} />
        <MonsterCard l={Medium} delay={100} />
        <MonsterCard l={Hard} delay={200} />
      </div>
      <Reveal delay={280} className="mt-8 text-center">
        <Link to="/monsters" className="press inline-flex items-center gap-1.5 text-sm font-bold text-[var(--accent-strong)] hover:underline">
          See all three in full <Icon name="arrowRight" className="h-4 w-4" strokeWidth={2.6} />
        </Link>
      </Reveal>
    </section>
  );
}
