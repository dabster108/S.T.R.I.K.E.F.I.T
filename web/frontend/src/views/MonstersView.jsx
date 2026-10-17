import { useEffect } from "react";
import { Link, useSearchParams } from "react-router-dom";
import LandingNav from "../components/landing/LandingNav.jsx";
import { FinalCta, Footer } from "../components/landing/FinalCta.jsx";
import { Kicker, Reveal } from "../components/landing/shared.jsx";
import MonsterPortrait from "../components/ui/MonsterPortrait.jsx";
import { Stars } from "../components/engage/Mastery.jsx";
import { api } from "../lib/api.js";
import { shortName } from "../lib/engage.js";
import { useApi } from "../hooks/useApi.js";
import Icon from "../components/ui/Icon.jsx";
import { LEVELS, LEVEL_KEYS } from "../lib/game.js";
import { MONSTER_GLOW, MONSTER_LABEL } from "../lib/monsters.js";
import { guardianForKey } from "../lib/story.js";

function Fact({ label, value }) {
  return (
    <div className="flex flex-col gap-0.5 border-t border-[var(--border)] py-3 first:border-t-0 first:pt-0">
      <dt className="t-kicker">{label}</dt>
      <dd className="text-sm font-semibold text-[var(--ink)]">{value}</dd>
    </div>
  );
}

// One full section per monster. Facts come straight from lib/game.js (the LEVELS table, which cites
// context.md / project_progress.md); the lore and tips are read from the shared content/story.json (guardians[]), the same text
// the headset's Guardians screen and chapter cards show.
// A player's mastery stars for this monster (when a player is picked above).
function PlayerStars({ entry, player }) {
  if (!entry) return null;
  return (
    <div className="mt-4 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)]">
      <p className="flex flex-wrap items-center gap-2 text-sm font-bold text-[var(--ink)]">
        {shortName(player, 24)}: <Stars stars={entry.stars} size="h-5 w-5" label={`${entry.starsEarned} of 3 stars`} />
        <span className="text-xs font-semibold text-[var(--muted)]">{entry.played ? `${entry.wins} win${entry.wins === 1 ? "" : "s"} in ${entry.played} fight${entry.played === 1 ? "" : "s"}` : "not fought yet"}</span>
      </p>
      <ul className="mt-1.5 space-y-0.5 text-xs font-medium text-[var(--ink-secondary)]">
        {entry.stars.map((st) => (
          <li key={st.id} className="flex items-start gap-1.5">
            <Icon name={st.earned ? "check" : "target"} className={`mt-px h-3.5 w-3.5 shrink-0 ${st.earned ? "text-[var(--good)]" : "text-[var(--muted)]"}`} />
            {st.label}
          </li>
        ))}
      </ul>
    </div>
  );
}

function MonsterSection({ levelKey, index, mastery = null, player = "" }) {
  const l = LEVELS[levelKey];
  const glow = MONSTER_GLOW[levelKey];
  const guardian = guardianForKey(levelKey);
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
            {guardian && (
              <p className="mt-2 text-sm font-semibold text-[var(--ink-secondary)]">
                The guardian: <span className="font-extrabold text-[var(--ink)]">{guardian.name}</span>
              </p>
            )}
            {guardian?.lore && (
              <blockquote className="measure mt-5 border-l-4 pl-4 text-base font-medium italic leading-relaxed text-[var(--ink-secondary)]" style={{ borderColor: glow.outline }}>
                {guardian.lore}
              </blockquote>
            )}
            <PlayerStars entry={mastery?.levels.find((l) => l.level === levelKey)} player={player} />
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
              {(guardian?.tips ?? []).map((tip, i) => (
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

// Optional ?player=: show that player's mastery stars on each monster.
function usePlayerMastery() {
  const [params, setParams] = useSearchParams();
  const player = params.get("player") || "";
  const players = useApi(() => api.players().catch(() => []), []);
  const ach = useApi(() => (player ? api.playerAchievements(player) : Promise.resolve(null)), [player]);
  const mastery = ach.data && ach.data.playerName === player ? ach.data.mastery : null;
  const names = (players.data || []).map((p) => p.playerName).sort((x, y) => x.localeCompare(y));
  const choose = (v) => setParams(v ? { player: v } : {}, { replace: true });
  return { player, mastery, names, choose };
}

export default function MonstersView() {
  useEffect(() => {
    document.title = "Monsters · STRIKEFIT";
  }, []);
  const { player, mastery, names, choose } = usePlayerMastery();
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
        {names.length > 0 && (
          <div className="mt-4 flex flex-wrap items-center gap-2 text-sm font-semibold text-[var(--ink-secondary)]">
            <label htmlFor="stars-player">Show stars for</label>
            <select
              id="stars-player"
              value={player}
              onChange={(e) => choose(e.target.value)}
              className="max-w-[16rem] rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 text-sm font-semibold text-[var(--ink)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-ring)]"
            >
              <option value="">No player</option>
              {names.map((n) => (
                <option key={n} value={n}>
                  {shortName(n, 28)}
                </option>
              ))}
            </select>
            {player && mastery && <span className="text-xs text-[var(--muted)]">{mastery.starsEarned} of {mastery.starsTotal} stars · {mastery.note}</span>}
          </div>
        )}
        {LEVEL_KEYS.map((k, i) => (
          <MonsterSection key={k} levelKey={k} index={i} mastery={player ? mastery : null} player={player} />
        ))}
      </main>
      <FinalCta />
      <Footer />
    </div>
  );
}
