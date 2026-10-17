import { useRef, useState } from "react";
import { Link } from "react-router-dom";
import { levels as storyLevels, guardians } from "../../lib/story.js";
import { LEVELS } from "../../lib/game.js";
import { MONSTER_GLOW } from "../../lib/monsters.js";
import { LevelBadge } from "../ui/Badge.jsx";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import MonsterPortrait from "../ui/MonsterPortrait.jsx";
import { Kicker, Reveal } from "../landing/shared.jsx";

// "Meet the guardians" on /story: three lore cards (portrait, name, weapon, the first line of the lore) that work as tabs, and
// one detail view under them with the full lore, weapon, arena and tips. Everything comes from the shared content/story.json
// (guardians[], levels[]): names, weapon, lore and tips are never retyped here. Keyboard: Tab to the cards, Left / Right / Home /
// End move between them (the ARIA tabs pattern), the detail view is a live region. Portraits are the same <MonsterPortrait> as
// /monsters and the landing page (same images, same level glow), small in the cards and one larger beside the detail.

const keyOf = (level) => storyLevels.find((l) => l.level === level)?.key;

function GuardianCard({ g, selected, onSelect, onKey, tabRef }) {
  const key = keyOf(g.level);
  const glow = MONSTER_GLOW[key];
  return (
    <button
      ref={tabRef}
      type="button"
      role="tab"
      id={`guardian-tab-${g.id}`}
      aria-selected={selected}
      aria-controls="guardian-detail"
      tabIndex={selected ? 0 : -1}
      onClick={onSelect}
      onKeyDown={onKey}
      className={`press group flex min-w-0 flex-col items-center rounded-2xl border bg-[var(--surface)] px-4 py-5 text-center shadow-[var(--shadow-card)] transition-shadow hover:shadow-[var(--shadow-lift)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent)] ${
        selected ? "border-[var(--accent)] ring-2 ring-[var(--accent-ring)]" : "border-[var(--border)]"
      }`}
    >
      <MonsterPortrait level={key} size="sm" className="transition-transform duration-300 group-hover:scale-[1.03]" />
      <span className="mt-4 flex items-center gap-2">
        <LevelBadge level={key} />
        <span className="t-eyebrow">Level {g.level}</span>
      </span>
      <span className="mt-2 text-lg font-extrabold tracking-tight text-[var(--ink)]">{g.name}</span>
      <span className="mt-1 text-sm font-bold" style={{ color: glow?.outline }}>
        {g.weapon}
      </span>
      <span className="mt-3 line-clamp-3 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{g.lore}</span>
      <span className="mt-4 inline-flex items-center gap-1.5 text-xs font-bold text-[var(--accent-strong)]">
        {selected ? "Shown below" : "Show details"} <Icon name={selected ? "check" : "arrowRight"} className="h-3.5 w-3.5" strokeWidth={2.6} />
      </span>
    </button>
  );
}

function GuardianDetail({ g }) {
  const key = keyOf(g.level);
  const glow = MONSTER_GLOW[key];
  const arena = LEVELS[key]?.environment;
  return (
    <div id="guardian-detail" role="tabpanel" aria-labelledby={`guardian-tab-${g.id}`} aria-live="polite" tabIndex={0} className="mt-6 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-card)] sm:p-8">
      <div className="grid min-w-0 grid-cols-[minmax(0,1fr)] items-center gap-8 lg:grid-cols-12 lg:gap-12">
        <div className="flex justify-center lg:col-span-4">
          <MonsterPortrait key={key} level={key} size="md" className="max-w-full" />
        </div>
        <div className="min-w-0 lg:col-span-8">
          <p className="flex flex-wrap items-center gap-3">
            <LevelBadge level={key} />
            <span className="t-eyebrow">Chapter {g.level}</span>
          </p>
          <h3 className="t-h2 mt-4 text-[var(--ink)]">{g.name}</h3>
          <p className="mt-2 text-sm font-bold" style={{ color: glow?.outline }}>
            {glow?.name} glow
          </p>
          <blockquote className="measure mt-5 border-l-4 pl-4 text-lg font-medium italic leading-relaxed text-[var(--ink-secondary)]" style={{ borderColor: glow?.outline }}>
            {g.lore}
          </blockquote>
          <dl className="mt-6 grid gap-x-8 gap-y-4 sm:grid-cols-2">
            <div>
              <dt className="t-kicker">Weapon</dt>
              <dd className="mt-1 text-base font-bold text-[var(--ink)]">{g.weapon}</dd>
            </div>
            <div>
              <dt className="t-kicker">Arena</dt>
              <dd className="mt-1 text-base font-bold text-[var(--ink)]">{arena}</dd>
            </div>
          </dl>
          <div className="mt-6">
            <p className="t-kicker">Tips</p>
            <ul className="mt-2 space-y-2">
              {g.tips.map((t) => (
                <li key={t} className="flex gap-2.5 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">
                  <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: glow?.outline }} aria-hidden />
                  <span>{t}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}

export default function Guardians() {
  const [i, setI] = useState(0);
  const tabs = useRef([]);
  if (!guardians.length) return null;
  const move = (to) => {
    const n = (to + guardians.length) % guardians.length;
    setI(n);
    tabs.current[n]?.focus();
  };
  const onKey = (e) => {
    if (e.key === "ArrowRight" || e.key === "ArrowDown") { e.preventDefault(); move(i + 1); }
    else if (e.key === "ArrowLeft" || e.key === "ArrowUp") { e.preventDefault(); move(i - 1); }
    else if (e.key === "Home") { e.preventDefault(); move(0); }
    else if (e.key === "End") { e.preventDefault(); move(guardians.length - 1); }
  };
  return (
    <section id="guardians" aria-labelledby="guardians-title" className="mt-14 scroll-mt-24">
      <Reveal className="max-w-2xl">
        <Kicker>The guardians</Kicker>
        <h2 id="guardians-title" className="t-h2 mt-5 text-[var(--ink)]">
          Three guardians remain.
        </h2>
        <p className="mt-3 text-sm font-semibold text-[var(--muted)]">Pick one to read about it. The same text is in the headset's Guardians screen.</p>
      </Reveal>
      <div role="tablist" aria-label="Guardians" className="mt-8 grid gap-4 sm:grid-cols-3">
        {guardians.map((g, n) => (
          <GuardianCard key={g.id} g={g} selected={n === i} onSelect={() => setI(n)} onKey={onKey} tabRef={(el) => (tabs.current[n] = el)} />
        ))}
      </div>
      <GuardianDetail g={guardians[i]} />
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
