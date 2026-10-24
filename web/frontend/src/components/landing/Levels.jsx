import { useState } from "react";
import { DEMO_CLIP } from "../../content/showcaseShots.js";
import { demoCredit } from "../../lib/demoCredit.js";
import { LEVELS, swingBands } from "../../lib/game.js";
import { useTilt } from "../../hooks/motion.js";
import { LevelBadge } from "../ui/Badge.jsx";
import DemoVideo from "../ui/DemoVideo.jsx";
import Icon from "../ui/Icon.jsx";
import MonsterPortrait from "../ui/MonsterPortrait.jsx";
import { usePauseOffscreen } from "../../motion/index.js";
import { Kicker, Reveal } from "./shared.jsx";

// Behaviour text: Assets/Documentation/context.md (2026-10-02 / 2026-10-09 entries), as of 2026-10-11.
const BEHAVIOUR = {
  Easy: "Slow, clearly telegraphed attacks: a slash, a heavy blow and a special, with longer wind-ups for the bigger hits. Raises its guard from time to time.",
  Medium:
    "Faster and more varied: picks from seven attacks, from quick sweeps and a twin-strike combo to a spinning blow, a ground slam and a leaping cleave, and forces its special the first time it drops to 40% health.",
  Hard: "Two axes, one in each hand: 14 attacks including an unblockable ground slam and three that are thrown from range, plus its own guard. Three phases, the last one enraged (faster attacks, shorter telegraphs); the guard it raises adapts to whichever side you keep swinging from.",
};

const MAX_V = 8;
const BAND_COLOR = { none: "var(--border)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };

// Tier bands on a 0–8 m/s ruler (the same bands as the Live Arena gauge), thresholds printed under their ticks. The bar draws
// left to right once the card is revealed.
function TierRuler({ levelKey }) {
  const bands = swingBands(levelKey, MAX_V);
  const first = bands.find((b) => b.id !== "none");
  const full = bands.find((b) => b.id === "full");
  const ticks = bands.filter((b) => b.id === "tap" || b.id === "medium" || b.id === "full").map((b) => b.from);
  return (
    <div className="mt-6" role="img" aria-label={`Swings count from ${first.from} m/s; full power from ${full.from} m/s`}>
      <div className="draw-x flex h-2.5 gap-[2px] overflow-hidden rounded-full" style={{ "--w": 1, "--bar-delay": "200ms" }}>
        {bands.map((b) => (
          <span key={b.id} style={{ width: `${((b.to - b.from) / MAX_V) * 100}%`, background: BAND_COLOR[b.id] }} />
        ))}
      </div>
      <div className="readout relative mt-1.5 h-4 text-[0.6875rem] font-bold text-[var(--muted)]" aria-hidden>
        {ticks.map((t) => (
          <span key={t} className="absolute -translate-x-1/2" style={{ left: `${(t / MAX_V) * 100}%` }}>
            {t.toFixed(1)}
          </span>
        ))}
        <span className="absolute right-0">{MAX_V} m/s</span>
      </div>
    </div>
  );
}

// Same box as the clip, so a missing file (or one still being checked) shifts nothing.
function ClipFallback({ l }) {
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-gradient-to-br from-[var(--accent-soft)] via-[var(--surface)] to-[var(--surface-muted)] px-4 text-center" data-empty-shot>
      <Icon name="headset" className="h-6 w-6 text-[var(--accent)]" aria-hidden />
      <p className="text-xs font-semibold text-[var(--muted)]">A clip of the {l.name} fight goes here.</p>
    </div>
  );
}

// The demo clip of this level's fight (Levels: Level 2), muted and looping while on screen.
function LevelClip({ l }) {
  const [status, setStatus] = useState("checking");
  return (
    <figure className="mt-6">
      <div className="relative aspect-video overflow-hidden rounded-xl bg-[var(--surface-muted)] ring-1 ring-[var(--border)]">
        <DemoVideo mp4={DEMO_CLIP.mp4} webm={DEMO_CLIP.webm} poster={DEMO_CLIP.poster} label={DEMO_CLIP.alt} fallback={<ClipFallback l={l} />} onStatus={setStatus} />
      </div>
      {/* the credit describes a real clip: hidden (space kept, nothing shifts) while there is none */}
      <figcaption className={`mt-2 text-xs font-medium leading-relaxed text-[var(--muted)] ${status === "ok" ? "" : "invisible"}`} aria-hidden={status !== "ok"}>
        {demoCredit()}
      </figcaption>
    </figure>
  );
}

function PlayableCard({ l, wide }) {
  const tilt = useTilt(8);
  const facts = l.swing
    ? [
        ["Weapons", l.weapons],
        ["Arena", l.environment],
        ["Monster", l.monster],
        ["Health", `${l.hp} each`],
      ]
    : [
        ["Weapons", l.weapons],
        ["Arena", l.environment],
        ["Monster", l.monster],
        ["Monster health", l.hp],
        ["Attacks", `${l.attacks}, incl. 3 ranged`],
        ["Phases", `${l.phases}, last one enraged`],
      ];
  return (
    <article ref={tilt} className="tilt glow-border" aria-label={`${l.name}, playable`}>
      <div className={`relative flex h-full flex-col rounded-[calc(var(--radius-xl)-1.5px)] bg-[var(--surface)] ${wide ? "p-7 sm:p-9" : "p-7"}`}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <span className={`block font-extrabold leading-none tracking-[-0.06em] text-[var(--accent)] tabular-nums ${wide ? "text-[5.5rem] sm:text-[7rem]" : "text-[4.5rem]"}`} aria-hidden>
              {String(l.number).padStart(2, "0")}
            </span>
            <h3 className="mt-3 flex items-center gap-2 text-2xl font-extrabold tracking-tight text-[var(--ink)]">
              {l.name} <LevelBadge level={l.key} />
            </h3>
            <span className="mt-3 inline-flex items-center gap-1.5 rounded-full bg-[var(--good-soft)] px-2.5 py-1 text-xs font-bold text-[var(--good-ink)] ring-1 ring-inset ring-emerald-200">
              <Icon name="check" className="h-3.5 w-3.5" strokeWidth={2.4} /> Playable
            </span>
          </div>
          <MonsterPortrait level={l.key} size="sm" glow={false} className={`!w-28 shrink-0 [mask-image:radial-gradient(ellipse_at_center,#000_58%,transparent_92%)] ${wide ? "sm:!w-44" : "sm:!w-36"}`} />
        </div>

        <dl className={`mt-6 grid gap-x-6 border-t border-[var(--border)] text-sm ${wide ? "sm:grid-cols-2" : ""}`}>
          {facts.map(([k, v]) => (
            <div key={k} className="flex justify-between gap-4 border-b border-[var(--border)] py-2.5">
              <dt className="font-semibold text-[var(--muted)]">{k}</dt>
              <dd className="text-right font-bold text-[var(--ink)]">{v}</dd>
            </div>
          ))}
        </dl>
        <p className="measure mt-5 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{BEHAVIOUR[l.key]}</p>
        {l.key === DEMO_CLIP.level && <LevelClip l={l} />}
        <TierRuler levelKey={l.key} />
      </div>
    </article>
  );
}

export default function Levels() {
  const { Easy, Medium, Hard } = LEVELS;
  const pause = usePauseOffscreen();
  return (
    <section ref={pause} id="levels" aria-labelledby="levels-title" className="mx-auto max-w-6xl px-4 pb-28 pt-24 sm:px-6 lg:pb-36 lg:pt-32">
      {/* heading sits on the right; the cards below start from the left */}
      <Reveal className="lg:ml-[41.666%]">
        <Kicker index="05">Levels</Kicker>
        <h2 id="levels-title" className="t-h2 mt-5 max-w-xl text-[var(--ink)]">
          Three arenas, each a step up
        </h2>
        <p className="measure mt-5 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">All three are playable, each a harder fight than the last.</p>
      </Reveal>

      <div className="mt-14 grid gap-5 lg:mt-16 lg:grid-cols-12 lg:items-start lg:gap-0">
        <Reveal className="lg:col-span-7">
          <PlayableCard l={Easy} wide />
        </Reveal>
        {/* dropped down and pulled left so it overlaps Level 1 slightly */}
        <Reveal delay={120} className="relative z-[1] lg:col-span-5 lg:-ml-8 lg:mt-24">
          <PlayableCard l={Medium} />
        </Reveal>

        <Reveal delay={200} className="lg:col-span-12 lg:mt-8">
          <PlayableCard l={Hard} wide />
        </Reveal>
      </div>
    </section>
  );
}
