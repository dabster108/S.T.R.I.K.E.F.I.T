import { useInView } from "../../hooks/motion.js";
import Icon from "../ui/Icon.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

// Loops only run while the card is on screen.
function Bento({ className = "", icon, title, children, art, delay = 0 }) {
  const [ref, inView] = useInView({ once: false, rootMargin: "0px" });
  return (
    <Reveal delay={delay} className={className}>
      <article
        ref={ref}
        className={`bento lift group flex h-full flex-col overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] hover:border-[var(--accent-ring)] ${inView ? "is-visible" : ""}`}
      >
        <div className="relative flex min-h-[9.5rem] flex-1 items-center justify-center overflow-hidden bg-gradient-to-br from-[var(--accent-soft)] via-[var(--surface)] to-[var(--surface)] px-6 py-6" aria-hidden>
          {art}
        </div>
        <div className="border-t border-[var(--border)] p-5">
          <div className="flex items-center gap-2">
            <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent)] transition-transform duration-300 group-hover:-rotate-6 group-hover:scale-110">
              <Icon name={icon} className="h-4 w-4" />
            </span>
            <h3 className="text-base font-bold tracking-tight text-[var(--ink)]">{title}</h3>
          </div>
          <p className="mt-2 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{children}</p>
        </div>
      </article>
    </Reveal>
  );
}

const WINDUPS = [
  ["Slash", 0.9],
  ["Heavy", 1.25],
  ["Special", 1.5],
];

function TelegraphArt() {
  return (
    <div className="flex w-full max-w-lg items-center gap-8">
      <svg viewBox="0 0 120 120" className="h-28 w-28 shrink-0">
        <circle cx="60" cy="60" r="50" fill="var(--hp-monster-trail)" opacity="0.35" />
        <circle className="anim-windup" cx="60" cy="60" r="46" fill="none" stroke="var(--hp-monster)" strokeWidth="4" />
        <circle cx="60" cy="60" r="26" fill="var(--hp-monster)" />
        <circle className="anim-flash" cx="60" cy="60" r="30" fill="#fff" />
        <path d="M50 56h20M60 46v20" stroke="#fff" strokeWidth="4" strokeLinecap="round" opacity="0.9" />
      </svg>
      <ul className="flex-1 space-y-3">
        {WINDUPS.map(([name, s], i) => (
          <li key={name}>
            <div className="mb-1 flex justify-between text-xs font-bold">
              <span className="text-[var(--ink-secondary)]">{name}</span>
              <span className="tabular-nums text-[var(--ink)]">{s} s</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-white">
              <div className="h-full" style={{ width: `${(s / 1.5) * 100}%` }}>
                <div className="grow-x h-full rounded-full bg-gradient-to-r from-[var(--hp-monster-trail)] to-[var(--hp-monster)]" style={{ "--i": i }} />
              </div>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

function DefendArt() {
  return (
    <div className="relative flex items-center">
      <span className="anim-deflect absolute -left-2 top-1/2 h-1.5 w-10 -translate-y-1/2 rounded-full bg-[var(--hp-player)]" />
      <span className="anim-shield flex h-20 w-20 items-center justify-center rounded-2xl bg-[var(--accent)] text-white shadow-[var(--shadow-glow)]">
        <Icon name="shield" className="h-10 w-10" strokeWidth={2.2} />
      </span>
      <span className="ml-4 rounded-lg bg-white px-2.5 py-1 text-lg font-extrabold text-[var(--ink)] shadow-sm ring-1 ring-[var(--border)]">−70%</span>
    </div>
  );
}

const SPARKS = [
  [16, -18],
  [-18, -14],
  [20, 6],
  [-14, 12],
  [2, -24],
  [-4, 20],
];
function ClashArt() {
  return (
    <div className="relative h-28 w-28">
      <span className="anim-blade-l absolute bottom-3 left-1/2 h-24 w-2 -translate-x-1/2 rounded-full bg-gradient-to-t from-[var(--ink-secondary)] to-[var(--blue-300)]" />
      <span className="anim-blade-r absolute bottom-3 left-1/2 h-24 w-2 -translate-x-1/2 rounded-full bg-gradient-to-t from-[var(--ink-secondary)] to-[var(--blue-400)]" />
      {SPARKS.map(([x, y], i) => (
        <span key={i} className="anim-spark absolute left-1/2 top-[30%] h-1.5 w-1.5 rounded-full bg-[var(--warn)]" style={{ "--sx": `${x}px`, "--sy": `${y}px` }} />
      ))}
    </div>
  );
}

const WAVE = [0.4, 0.7, 0.5, 0.9, 0.6, 1, 0.75, 0.45, 0.85, 0.55, 0.95, 0.65, 0.4, 0.8, 0.5, 0.7];
function SoundArt() {
  return (
    <div className="flex h-20 items-center gap-1.5">
      {WAVE.map((h, i) => (
        <span key={i} className="anim-wave w-1.5 rounded-full bg-gradient-to-t from-[var(--accent)] to-[var(--blue-300)]" style={{ height: `${h * 100}%`, "--i": i }} />
      ))}
    </div>
  );
}

function HudArt() {
  const bar = (label, color, delay) => (
    <div>
      <div className="mb-1 flex justify-between text-[0.68rem] font-bold uppercase tracking-wide text-[var(--muted)]">
        <span>{label}</span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-gray-100">
        <div className="anim-hud h-full rounded-full" style={{ background: color, animationDelay: delay }} />
      </div>
    </div>
  );
  return (
    <div className="w-full max-w-[13rem] space-y-2.5 rounded-xl border border-[var(--border)] bg-white/90 p-3 shadow-[var(--shadow-lift)] [transform:perspective(500px)_rotateY(-10deg)]">
      {bar("Health", "var(--hp-player)", "0s")}
      {bar("Monster", "var(--hp-monster)", "-1.2s")}
      {bar("Stamina", "var(--tier-tap)", "-2.4s")}
      <div className="flex items-center justify-between pt-1 text-xs font-extrabold text-[var(--ink)]">
        <span className="inline-flex items-center gap-1">
          <Icon name="timer" className="h-3.5 w-3.5 text-[var(--accent)]" /> Round timer
        </span>
        <span className="text-[var(--accent)]">+XP</span>
      </div>
    </div>
  );
}

function TiersArt() {
  const cols = [
    ["Standard", "var(--tier-tap)"],
    ["Heavy", "var(--tier-light)"],
    ["Combo", "var(--tier-medium)"],
    ["Slam", "var(--tier-medium)"],
    ["Special", "var(--tier-full)"],
  ];
  return (
    <div className="flex h-24 items-end gap-3">
      {cols.map(([label, c], i) => (
        <div key={label} className="flex h-full w-12 flex-col items-center justify-end gap-1.5">
          <span className="anim-tier w-full rounded-t-lg" style={{ height: `${40 + i * 14}%`, background: c, "--i": i }} />
          <span className="text-[0.68rem] font-bold text-[var(--muted)]">{label}</span>
        </div>
      ))}
    </div>
  );
}

export default function Combat() {
  return (
    <section id="combat" aria-labelledby="combat-title" className="border-y border-[var(--border)] bg-[var(--surface-muted)]">
      <div className="mx-auto max-w-6xl px-4 py-24 sm:px-6 lg:py-32">
        <SectionHead id="combat-title" eyebrow="Combat" title="A monster that fights back">
          Hitting a target is a drill. Something that winds up, guards and swings at you makes you move.
        </SectionHead>
        <div className="mt-14 grid gap-4 md:grid-cols-2 lg:grid-cols-6">
          <Bento className="md:col-span-2 lg:col-span-4" icon="timer" title="Telegraphed attacks" art={<TelegraphArt />}>
            Every monster attack starts with a wind-up: the body flashes and a cue sound plays before the blow lands. On Level 1 that&apos;s 0.9 s for a slash, 1.25 s for a heavy and 1.5 s for
            a special. Step out of reach in time and it misses.
          </Bento>
          <Bento className="lg:col-span-2" delay={80} icon="shield" title="Defend state" art={<DefendArt />}>
            The monster periodically raises its guard. Hits that land while it&apos;s guarding do 70% less damage, so timing matters as much as force.
          </Bento>
          <Bento className="lg:col-span-2" icon="swords" title="Weapon clash" art={<ClashArt />}>
            Strike the monster&apos;s weapon instead of its body and the game detects a parry: sparks and a clash sound, and no damage to the monster.
          </Bento>
          <Bento className="lg:col-span-2" delay={80} icon="waveform" title="Layered sound" art={<SoundArt />}>
            Separate sounds for normal and heavy hits (above 4.8 m/s), the monster&apos;s wind-up, swing and impact, its guard and block, weapon clashes, and your own hurt and defeat cues.
          </Bento>
          <Bento className="md:col-span-2 lg:col-span-2" delay={160} icon="monitor" title="World-space HUD" art={<HudArt />}>
            Health, score, round timer, stamina and XP sit in the arena instead of stuck to your face, with damage numbers appearing where each hit lands.
          </Bento>
          <Bento className="md:col-span-2 lg:col-span-6" icon="layers" title="Multiple attack tiers" art={<TiersArt />}>
            Level 1 mixes slashes, heavies and specials, with more of the big hits as the monster weakens. Level 2 draws from a weighted table of seven attacks in five categories.
          </Bento>
        </div>
      </div>
    </section>
  );
}
