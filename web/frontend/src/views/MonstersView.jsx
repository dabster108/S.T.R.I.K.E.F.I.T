import { useEffect } from "react";
import { Link } from "react-router-dom";
import LandingNav from "../components/landing/LandingNav.jsx";
import { FinalCta, Footer } from "../components/landing/FinalCta.jsx";
import { Kicker, Reveal } from "../components/landing/shared.jsx";
import MonsterPortrait from "../components/ui/MonsterPortrait.jsx";
import Icon from "../components/ui/Icon.jsx";
import { LEVELS, LEVEL_KEYS } from "../lib/game.js";
import { MONSTER_GLOW, MONSTER_LABEL, MONSTER_TIPS } from "../lib/monsters.js";

function Fact({ label, value }) {
  return (
    <div className="flex flex-col gap-0.5 border-t border-[var(--border)] py-3 first:border-t-0 first:pt-0">
      <dt className="t-kicker">{label}</dt>
      <dd className="text-sm font-semibold text-[var(--ink)]">{value}</dd>
    </div>
  );
}

// One full section per monster. Facts come straight from lib/game.js (the LEVELS table, which cites
// context.md / project_progress.md); tips come from MONSTER_TIPS, written from the same design docs.
function MonsterSection({ levelKey, index }) {
  const l = LEVELS[levelKey];
  const glow = MONSTER_GLOW[levelKey];
    const facts = [
    ["Fights", `${l.name} · ${l.monster}`],
    ["Weapons", l.weapons],
    ["Arena", l.environment],
    ["Health", l.hp.toLocaleString("en-US")],
    ...(l.attacks ? [["Attacks", String(l.attacks)]] : []),
    ...(l.phases ? [["Phases", String(l.phases)]] : []),
  ];
  return (
    <section id={levelKey} aria-labelledby={`${levelKey}-title`} className="scroll-mt-24 border-t border-[var(--border)] py-16 first:border-t-0 sm:py-20">
      <div className="grid items-center gap-10 lg:grid-cols-12 lg:gap-14">
        <Reveal className="flex justify-center lg:col-span-5">
          <MonsterPortrait level={levelKey} size="lg" />
        </Reveal>
        <div className="lg:col-span-7">
          <Reveal>
            <Kicker index={`0${index + 1}`}>{MONSTER_LABEL[levelKey]}</Kicker>
            <h2 id={`${levelKey}-title`} className="t-h2 mt-5 text-[var(--ink)]">
              {l.monster}
            </h2>
            <p className="mt-2 text-sm font-bold" style={{ color: glow.outline }}>
              {glow.name} glow
            </p>
          </Reveal>
          <Reveal delay={100}>
            <dl className="mt-8 grid gap-x-8 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-[var(--shadow-card)] sm:grid-cols-2">
              {facts.map(([label, value]) => (
                <Fact key={label} label={label} value={value} />
              ))}
            </dl>
          </Reveal>
          <Reveal delay={180}>
            <h3 className="mt-10 text-lg font-extrabold tracking-tight text-[var(--ink)]">How to beat it</h3>
            <ol className="mt-4 space-y-3">
              {MONSTER_TIPS[levelKey].map((tip, i) => (
                <li key={tip} className="flex gap-3 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">
                  <span className="t-index mt-0.5 w-5 shrink-0 text-[var(--accent-strong)]">{i + 1}</span>
                  <span>{tip}</span>
                </li>
              ))}
            </ol>
          </Reveal>
        </div>
      </div>
    </section>
  );
}

export default function MonstersView() {
  useEffect(() => {
    document.title = "Monsters · STRIKEFIT";
  }, []);
  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:font-semibold focus:shadow-lg"
      >
        Skip to content
      </a>
      <LandingNav live={false} />
      <main id="main" className="mx-auto max-w-6xl px-4 pb-8 pt-12 sm:px-6 lg:pt-16">
        <Reveal className="max-w-2xl">
          <Link to="/" className="press inline-flex items-center gap-1.5 text-sm font-bold text-[var(--accent-strong)] hover:underline">
            <Icon name="chevronLeft" className="h-4 w-4" strokeWidth={2.6} /> Back to STRIKEFIT
          </Link>
          <h1 className="t-h1 mt-6 text-[var(--ink)]">The three monsters</h1>
          <p className="measure mt-5 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">
            Each portrait is rendered from the game's own Unity model. The facts are the values the game uses, and
            the tips are drawn from the design notes behind each fight.
          </p>
        </Reveal>
        <nav aria-label="Monsters" className="mt-8 flex flex-wrap gap-2">
          {LEVEL_KEYS.map((k) => (
            <a
              key={k}
              href={`#${k}`}
              className="press rounded-full border border-[var(--border)] bg-[var(--surface)] px-4 py-2 text-sm font-bold text-[var(--ink-secondary)] hover:text-[var(--accent-strong)]"
            >
              {LEVELS[k].name}: {LEVELS[k].monster}
            </a>
          ))}
        </nav>
        {LEVEL_KEYS.map((k, i) => (
          <MonsterSection key={k} levelKey={k} index={i} />
        ))}
      </main>
      <FinalCta />
      <Footer />
    </div>
  );
}
