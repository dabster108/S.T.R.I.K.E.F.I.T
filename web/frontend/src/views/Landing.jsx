import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../lib/api.js";
import { LEVELS, simulateHit } from "../lib/game.js";
import { formatNumber } from "../lib/format.js";
import { useReveal } from "../hooks/motion.js";
import Icon from "../components/ui/Icon.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import SwingDemo from "../components/landing/SwingDemo.jsx";

const GITHUB = "https://github.com/dabster108/S.T.R.I.K.E.F.I.T";
const THESIS = "Design, Development, and Evaluation of a Virtual Reality-Based Gamified Fitness System";
const SECTIONS = [
  ["problem", "Problem"],
  ["how", "How it works"],
  ["levels", "Levels"],
  ["combat", "Combat"],
  ["numbers", "Numbers"],
  ["thesis", "Thesis"],
];
const btnPrimary =
  "inline-flex items-center justify-center gap-2 rounded-lg border border-transparent bg-[var(--accent)] px-3.5 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-[var(--accent-strong)]";
const btnSecondary =
  "inline-flex items-center justify-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3.5 py-2 text-sm font-medium text-[var(--ink-secondary)] shadow-sm transition-colors hover:border-[var(--border-strong)] hover:text-[var(--ink)]";
const card = "rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]";

function Reveal({ as: Tag = "div", className = "", delay = 0, children }) {
  const ref = useReveal();
  return (
    <Tag ref={ref} className={`reveal ${className}`} style={delay ? { transitionDelay: `${delay}ms` } : undefined}>
      {children}
    </Tag>
  );
}

function SectionHead({ id, eyebrow, title, children }) {
  return (
    <Reveal className="max-w-2xl">
      <p className="text-xs font-semibold uppercase tracking-wider text-[var(--accent)]">{eyebrow}</p>
      <h2 id={id} className="mt-2 text-2xl font-bold tracking-tight text-[var(--ink)] sm:text-3xl">
        {title}
      </h2>
      {children && <p className="mt-3 text-base leading-relaxed text-[var(--muted)]">{children}</p>}
    </Reveal>
  );
}

// ------------------------------------------------------------------ live status (from /api/live)

function useLiveStatus() {
  const [status, setStatus] = useState({ state: "checking", count: 0 });
  useEffect(() => {
    let alive = true;
    const check = () =>
      api
        .live()
        .then((d) => alive && setStatus({ state: d.count ? "live" : "idle", count: d.count }))
        .catch(() => alive && setStatus((s) => ({ ...s, state: "unknown" })));
    check();
    const t = setInterval(check, 20000);
    return () => {
      alive = false;
      clearInterval(t);
    };
  }, []);
  return status;
}

function LivePill({ status }) {
  const text = {
    checking: "Checking the arena…",
    live: status.count === 1 ? "A fight is being played right now" : `${status.count} fights are being played right now`,
    idle: "No fight in progress right now",
    unknown: "Arena status unavailable (the server may be waking up)",
  }[status.state];
  const live = status.state === "live";
  const cls = `inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-semibold ring-1 ring-inset ${
    live ? "bg-[var(--good-soft)] text-[var(--good)] ring-emerald-200" : "bg-[var(--surface)] text-[var(--muted)] ring-[var(--border)]"
  }`;
  const inner = (
    <>
      <span className={`h-2 w-2 rounded-full ${live ? "status-live bg-[var(--good)]" : status.state === "checking" ? "bg-[var(--warn)]" : "bg-gray-300"}`} aria-hidden />
      {text}
      {live && <Icon name="chevronRight" className="h-3.5 w-3.5" strokeWidth={2.2} />}
    </>
  );
  return live ? (
    <Link to="/dashboard/live" className={`${cls} hover:underline`}>
      {inner}
    </Link>
  ) : (
    <p className={cls} role="status">
      {inner}
    </p>
  );
}

// ------------------------------------------------------------------ hero visual: Level 1's real tier scale

function TierGauge() {
  const s = LEVELS.Easy.swing;
  const W = 320;
  const H = 190;
  const cx = 160;
  const cy = 170;
  const r = 130;
  const max = 8;
  const pt = (v, rr) => {
    const a = Math.PI - (v / max) * Math.PI;
    return [cx + rr * Math.cos(a), cy - rr * Math.sin(a)];
  };
  const arc = (a, b) => {
    const [x0, y0] = pt(a, r);
    const [x1, y1] = pt(b, r);
    return `M${x0} ${y0}A${r} ${r} 0 0 1 ${x1} ${y1}`;
  };
  const zones = [
    [0, s.min, "var(--border)"],
    [s.min, s.tiers.light, "var(--tier-tap)"],
    [s.tiers.light, s.tiers.medium, "var(--tier-light)"],
    [s.tiers.medium, s.tiers.full, "var(--tier-medium)"],
    [s.tiers.full, max, "var(--tier-full)"],
  ];
  const [nx, ny] = pt(6.2, r - 26);
  const full = simulateHit(6.2, "Easy");
  return (
    <figure className={`${card} p-5`}>
      <figcaption className="flex items-center justify-between text-xs">
        <span className="font-semibold text-[var(--ink)]">Level 1 swing tiers</span>
        <span className="text-[var(--muted)]">controller speed, m/s</span>
      </figcaption>
      <svg viewBox={`0 0 ${W} ${H}`} className="mt-3 w-full" role="img" aria-label="Swing speed scale: under 1.2 metres per second no hit, then tap, light, medium, and full power from 5 metres per second.">
        {zones.map(([a, b, c]) => (
          <path key={a} d={arc(a + 0.05, b - 0.05)} fill="none" stroke={c} strokeWidth="16" />
        ))}
        {[0, 2, 4, 6, 8].map((v) => {
          const [x, y] = pt(v, r + 22);
          return (
            <text key={v} x={x} y={y + 4} textAnchor="middle" fontSize="10.5" fill="var(--muted)">
              {v}
            </text>
          );
        })}
        <line x1={cx} y1={cy} x2={nx} y2={ny} stroke="var(--ink)" strokeWidth="3" strokeLinecap="round" />
        <circle cx={cx} cy={cy} r="6" fill="var(--surface)" stroke="var(--ink)" strokeWidth="3" />
      </svg>
      <div className="mt-2 grid grid-cols-3 gap-2 border-t border-[var(--border)] pt-3 text-center">
        {[
          ["Example swing", "6.2 m/s"],
          ["Tier", "Full power"],
          ["Damage", full.damage.toFixed(1)],
        ].map(([k, v]) => (
          <div key={k}>
            <p className="text-[0.68rem] text-[var(--muted)]">{k}</p>
            <p className="text-sm font-semibold tabular-nums text-[var(--ink)]">{v}</p>
          </div>
        ))}
      </div>
      <p className="mt-3 text-[0.7rem] text-[var(--muted)]">An example computed with the game's formula, not a recorded session.</p>
    </figure>
  );
}

// ------------------------------------------------------------------ sections

function Nav({ live }) {
  const [open, setOpen] = useState(false);
  return (
    <header className="sticky top-0 z-30 border-b border-[var(--border)] bg-[var(--surface)]/90 backdrop-blur-md">
      <div className="mx-auto flex max-w-6xl items-center gap-3 px-4 py-3 sm:gap-6 sm:px-6">
        <a href="#top" className="flex items-center gap-2.5 rounded-xl" aria-label="STRIKEFIT, back to top">
          <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-bold text-white shadow-sm">SF</span>
          <span className="text-sm font-semibold text-[var(--ink)]">STRIKEFIT</span>
        </a>
        <nav aria-label="Sections" className="hidden flex-1 items-center gap-5 lg:flex">
          {SECTIONS.map(([id, label]) => (
            <a key={id} href={`#${id}`} className="text-sm font-medium text-[var(--ink-secondary)] hover:text-[var(--accent)]">
              {label}
            </a>
          ))}
        </nav>
        <div className="ml-auto flex items-center gap-2">
          <span className="hidden sm:block">
            <Link to="/dashboard/live" className={btnSecondary}>
              <span className={`h-2 w-2 rounded-full ${live ? "status-live bg-[var(--good)]" : "bg-gray-300"}`} aria-hidden />
              Watch Live
            </Link>
          </span>
          <Link to="/dashboard" className={btnPrimary}>
            <span className="sm:hidden">Dashboard</span>
            <span className="hidden sm:inline">Open Dashboard</span>
          </Link>
          <button
            type="button"
            className="rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] lg:hidden"
            aria-expanded={open}
            aria-label={open ? "Close menu" : "Open menu"}
            onClick={() => setOpen((o) => !o)}
          >
            <Icon name={open ? "x" : "menu"} className="h-5 w-5" />
          </button>
        </div>
      </div>
      {open && (
        <nav aria-label="Sections" className="fade-in border-t border-[var(--border)] bg-[var(--surface)] px-4 py-2 lg:hidden">
          {SECTIONS.map(([id, label]) => (
            <a key={id} href={`#${id}`} onClick={() => setOpen(false)} className="block rounded-lg px-3 py-2.5 text-sm font-medium text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)]">
              {label}
            </a>
          ))}
          <Link to="/dashboard/live" className="block rounded-lg px-3 py-2.5 text-sm font-medium text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] sm:hidden">
            Watch Live
          </Link>
        </nav>
      )}
    </header>
  );
}

function Hero({ status }) {
  return (
    <section id="top" aria-labelledby="hero-title" className="relative overflow-hidden border-b border-[var(--border)] bg-[var(--surface)]">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(60%_60%_at_85%_20%,var(--accent-soft),transparent_70%)]" aria-hidden />
      <div className="relative mx-auto grid max-w-6xl items-center gap-12 px-4 py-16 sm:px-6 lg:grid-cols-2 lg:py-24">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-[var(--accent)]">VR fitness research prototype · Meta Quest 2</p>
          <h1 id="hero-title" className="mt-3 text-4xl font-bold tracking-tight text-[var(--ink)] sm:text-5xl lg:text-6xl">
            Swing harder.
            <br />
            <span className="text-[var(--accent)]">Hit harder.</span>
          </h1>
          <p className="mt-5 max-w-xl text-lg leading-relaxed text-[var(--muted)]">
            STRIKEFIT is a VR combat game where damage comes from how fast you actually swing. Flick your wrist and nothing happens; put your
            body into it and the monster feels it.
          </p>
          <div className="mt-8 flex flex-wrap gap-3">
            <Link to="/dashboard" className={btnPrimary}>
              Open the dashboard <Icon name="arrowRight" className="h-4 w-4" />
            </Link>
            <Link to="/dashboard/live" className={btnSecondary}>
              Watch the live arena
            </Link>
          </div>
          <div className="mt-6">
            <LivePill status={status} />
          </div>
        </div>
        <div className="mx-auto w-full max-w-md">
          <TierGauge />
        </div>
      </div>
    </section>
  );
}

function Problem() {
  const full = simulateHit(6.2, "Easy");
  return (
    <section id="problem" aria-labelledby="problem-title" className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
      <div className="grid gap-10 lg:grid-cols-2">
        <SectionHead id="problem-title" eyebrow="The problem" title="Most VR fitness games count movements. Few measure effort." />
        <Reveal className="space-y-4 text-base leading-relaxed text-[var(--ink-secondary)]">
          <p>
            Point a controller at a target and most games register a hit, whether you threw your whole body into it or flicked your wrist. The score
            goes up either way, so the easiest way to win is also the laziest way to move.
          </p>
          <p>That matters when the point is exercise. If effort isn't measured it isn't rewarded, and players quickly stop giving it.</p>
          <p className="text-[var(--ink)]">
            STRIKEFIT reads the real speed of each controller at the moment of impact. Too slow and the hit doesn't count. Faster swings deal more
            damage and earn more XP.
          </p>
        </Reveal>
      </div>
      <div className="mt-10 grid gap-4 md:grid-cols-2">
        {[
          { title: "Gesture-based scoring", rows: [["Wrist flick", "Hit"], ["Full swing", "Hit"]], note: "Same result, so there's no reason to try harder.", accent: false },
          { title: "STRIKEFIT, Level 1", rows: [["Wrist flick, 0.9 m/s", "No hit"], ["Full swing, 6.2 m/s", `${full.damage.toFixed(1)} damage`]], note: "The harder swing is the only one that lands hard.", accent: true },
        ].map((c, i) => (
          <Reveal key={c.title} delay={i * 80} className={`${card} p-5 ${c.accent ? "ring-1 ring-inset ring-[var(--accent-ring)]" : ""}`}>
            <p className={`text-xs font-semibold uppercase tracking-wider ${c.accent ? "text-[var(--accent)]" : "text-[var(--muted)]"}`}>{c.title}</p>
            <ul className="mt-3 divide-y divide-[var(--border)]">
              {c.rows.map(([k, v]) => (
                <li key={k} className="flex items-center justify-between py-2.5 text-sm">
                  <span className="text-[var(--ink-secondary)]">{k}</span>
                  <span className={`font-semibold ${v === "No hit" ? "text-[var(--muted)]" : c.accent ? "text-[var(--accent)]" : "text-[var(--ink)]"}`}>{v}</span>
                </li>
              ))}
            </ul>
            <p className="mt-3 text-xs text-[var(--muted)]">{c.note}</p>
          </Reveal>
        ))}
      </div>
    </section>
  );
}

const STEPS = [
  ["Swing", "Swing a hammer or sword at the monster. Each hand is tracked on its own, so both arms count."],
  ["Velocity measured", "At contact the game takes the faster of the controller's tracked speed and the weapon's own motion, in metres per second."],
  ["Damage scaled", "Below 1.2 m/s nothing happens. Above it, damage climbs with speed up to a cap at 8 m/s."],
  ["Feedback and XP", "A heavier impact sound for big swings, damage numbers where the hit lands, and XP weighted toward full-power swings."],
];

function HowItWorks() {
  return (
    <section id="how" aria-labelledby="how-title" className="border-y border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
        <SectionHead id="how-title" eyebrow="How it works" title="From arm speed to damage in four steps">
          Every hit goes through the same pipeline, in the headset, in the frame it lands.
        </SectionHead>
        <ol className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {STEPS.map(([title, body], i) => (
            <Reveal as="li" key={title} delay={i * 70} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-muted)] p-5">
              <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">{i + 1}</span>
              <h3 className="mt-3 text-sm font-semibold text-[var(--ink)]">{title}</h3>
              <p className="mt-1 text-sm leading-relaxed text-[var(--muted)]">{body}</p>
            </Reveal>
          ))}
        </ol>
        <Reveal className="mt-8">
          <SwingDemo />
        </Reveal>
      </div>
    </section>
  );
}

const BEHAVIOUR = {
  Easy: "Slow, clearly telegraphed attacks: a slash, a heavy blow and a special, with longer wind-ups for the bigger hits. Raises its guard from time to time.",
  Medium: "Faster and more varied: picks from seven attacks, from quick sweeps and a twin-strike combo to a spinning blow, a ground slam and a leaping cleave, and forces its special the first time it drops to 40% health.",
  Hard: "Planned. The design is set; the level isn't built yet.",
};

function Levels() {
  return (
    <section id="levels" aria-labelledby="levels-title" className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
      <SectionHead id="levels-title" eyebrow="Levels" title="Three arenas, each a step up" />
      <div className="mt-10 grid gap-4 lg:grid-cols-3">
        {Object.values(LEVELS).map((l, i) => (
          <Reveal as="article" key={l.key} delay={i * 80} className={`${card} flex flex-col p-5 ${l.status === "planned" ? "bg-[var(--surface-muted)]" : ""}`}>
            <div className="flex items-center justify-between">
              <h3 className="text-lg font-bold tracking-tight text-[var(--ink)]">{l.name}</h3>
              {l.status === "planned" ? (
                <span className="inline-flex items-center rounded-full bg-[var(--surface)] px-2 py-0.5 text-[0.7rem] font-semibold text-[var(--muted)] ring-1 ring-inset ring-[var(--border)]">Planned</span>
              ) : (
                <LevelBadge level={l.key} />
              )}
            </div>
            <dl className="mt-4 divide-y divide-[var(--border)] text-sm">
              {[
                ["Weapons", l.weapons],
                ["Environment", l.environment],
                ["Monster", l.monster],
                l.hp && ["Health", `${l.hp} each, you and the monster`],
              ]
                .filter(Boolean)
                .map(([k, v]) => (
                  <div key={k} className="flex justify-between gap-4 py-2">
                    <dt className="text-[var(--muted)]">{k}</dt>
                    <dd className="text-right font-medium text-[var(--ink)]">{v}</dd>
                  </div>
                ))}
            </dl>
            <p className="mt-4 text-sm leading-relaxed text-[var(--muted)]">{BEHAVIOUR[l.key]}</p>
          </Reveal>
        ))}
      </div>
    </section>
  );
}

const FEATURES = [
  ["clock", "blue", "Telegraphed attacks", "Every monster attack starts with a wind-up: the body flashes and a cue sound plays before the blow lands. On Level 1 that's 0.9 s for a slash, 1.25 s for a heavy and 1.5 s for a special. Step out of reach in time and it misses."],
  ["bolt", "amber", "Multiple attack tiers", "Level 1 mixes slashes, heavies and specials, with more of the big hits as the monster weakens. Level 2 draws from a weighted table of seven attacks in five categories."],
  ["target", "indigo", "Defend state", "The monster periodically raises its guard. Hits that land while it's guarding do 70% less damage, so timing matters as much as force."],
  ["x", "rose", "Weapon clash", "Strike the monster's weapon instead of its body and the game detects a parry: sparks and a clash sound, and no damage to the monster."],
  ["signal", "green", "Layered sound", "Separate sounds for normal and heavy hits (above 4.8 m/s), the monster's wind-up, swing and impact, its guard and block, weapon clashes, and your own hurt and defeat cues."],
  ["dashboard", "slate", "World-space HUD", "Health, score, round timer, stamina and XP sit in the arena instead of stuck to your face, with damage numbers appearing where each hit lands."],
];
const TONES = {
  blue: "bg-blue-50 text-blue-600",
  indigo: "bg-indigo-50 text-indigo-600",
  green: "bg-emerald-50 text-emerald-600",
  amber: "bg-amber-50 text-amber-600",
  rose: "bg-rose-50 text-rose-600",
  slate: "bg-gray-100 text-gray-600",
};

function Combat() {
  return (
    <section id="combat" aria-labelledby="combat-title" className="border-y border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
        <SectionHead id="combat-title" eyebrow="Combat" title="A monster that fights back">
          Hitting a target is a drill. Something that winds up, guards and swings at you makes you move.
        </SectionHead>
        <div className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map(([icon, tone, title, body], i) => (
            <Reveal as="article" key={title} delay={(i % 3) * 70} className="rounded-2xl border border-[var(--border)] bg-[var(--surface-muted)] p-5">
              <span className={`inline-flex h-8 w-8 items-center justify-center rounded-lg ${TONES[tone]}`}>
                <Icon name={icon} className="h-4 w-4" />
              </span>
              <h3 className="mt-3 text-sm font-semibold text-[var(--ink)]">{title}</h3>
              <p className="mt-1 text-sm leading-relaxed text-[var(--muted)]">{body}</p>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}

function Numbers() {
  const [data, setData] = useState({ state: "loading" });
  const load = () => {
    setData({ state: "loading" });
    api
      .publicStats()
      .then((stats) => setData({ state: "ok", stats }))
      .catch(() => setData({ state: "error" }));
  };
  useEffect(load, []);
  const s = data.stats;
  const tiles = [
    ["Sessions played", s?.totalSessions, "sessions", "blue"],
    ["Swings that counted", s?.totalSwings, "bolt", "amber"],
    ["Players", s?.totalPlayers, "players", "green"],
  ];
  return (
    <section id="numbers" aria-labelledby="numbers-title" className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
      <SectionHead id="numbers-title" eyebrow="Live numbers" title="Recorded so far">
        Straight from the backend: every finished session the game has reported.
      </SectionHead>
      <Reveal className="mt-10" aria-live="polite">
        {data.state === "error" ? (
          <div className={`${card} flex flex-col items-center px-6 py-12 text-center`}>
            <p className="text-sm font-semibold text-[var(--ink)]">The numbers aren't available right now</p>
            <p className="mt-1 max-w-sm text-xs text-[var(--muted)]">The server may still be waking up.</p>
            <button type="button" onClick={load} className={`${btnSecondary} mt-4`}>
              Try again
            </button>
          </div>
        ) : data.state === "ok" && s.totalSessions === 0 ? (
          <div className={`${card} flex flex-col items-center px-6 py-12 text-center`}>
            <span className="flex h-11 w-11 items-center justify-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]">
              <Icon name="headset" className="h-5 w-5" />
            </span>
            <p className="mt-3 text-sm font-semibold text-[var(--ink)]">No sessions yet</p>
            <p className="mt-1 max-w-sm text-xs text-[var(--muted)]">The first finished fight will show up here{s.liveSessions ? "; one is being played right now." : "."}</p>
          </div>
        ) : (
          <>
            <div className="grid gap-4 sm:grid-cols-3">
              {tiles.map(([label, value, icon, tone]) => (
                <div key={label} className={`${card} p-5`}>
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-xs font-medium text-[var(--muted)]">{label}</p>
                    <span className={`inline-flex h-8 w-8 items-center justify-center rounded-lg ${TONES[tone]}`}>
                      <Icon name={icon} className="h-4 w-4" />
                    </span>
                  </div>
                  {data.state === "loading" ? (
                    <div className="skeleton mt-3 h-8 w-24" aria-hidden />
                  ) : (
                    <p className="mt-2 text-[1.75rem] font-bold leading-none tracking-tight text-[var(--ink)]">{formatNumber(value)}</p>
                  )}
                </div>
              ))}
            </div>
            {data.state === "ok" && (
              <p className="mt-4 text-xs text-[var(--muted)]">
                {s.levelsAvailable} levels playable, {s.levelsPlanned} planned. A swing counts when it reaches the minimum speed and lands on the monster.
              </p>
            )}
          </>
        )}
      </Reveal>
    </section>
  );
}

const STACK = [
  ["Unity", "Game engine"],
  ["Meta XR SDK", "Controller tracking and input"],
  ["Meta Quest 2", "Standalone headset"],
  ["Node.js", "API server (Express)"],
  ["PostgreSQL", "Sessions and events, on Neon"],
  ["Render", "Hosts the API"],
  ["Vercel", "Hosts this site"],
  ["Server-Sent Events", "Pushes live fights to the dashboard"],
  ["React", "This site and the dashboard"],
];

function Stack() {
  return (
    <section aria-labelledby="stack-title" className="border-y border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
        <SectionHead id="stack-title" eyebrow="Tech stack" title="What it's built with" />
        <Reveal as="ul" className="mt-10 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {STACK.map(([name, role]) => (
            <li key={name} className="flex items-center justify-between gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-4 py-3">
              <span className="text-sm font-semibold text-[var(--ink)]">{name}</span>
              <span className="text-right text-xs text-[var(--muted)]">{role}</span>
            </li>
          ))}
        </Reveal>
      </div>
    </section>
  );
}

function Thesis() {
  return (
    <section id="thesis" aria-labelledby="thesis-title" className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
      <SectionHead id="thesis-title" eyebrow="About the thesis" title="The research behind the game" />
      <Reveal className={`${card} mt-10 p-6 sm:p-8`}>
        <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-2.5 py-1 text-xs font-semibold text-[var(--accent)] ring-1 ring-inset ring-[var(--accent-ring)]">
          <span className="h-1.5 w-1.5 rounded-full bg-[var(--accent)]" aria-hidden />
          Active research prototype
        </span>
        <p className="mt-4 text-xl font-semibold leading-snug tracking-tight text-[var(--ink)] sm:text-2xl">{THESIS}</p>
        <dl className="mt-5 grid gap-4 border-t border-[var(--border)] pt-5 sm:grid-cols-3">
          {[
            ["Institution", "Softwarica College"],
            ["Supervisor", "Manoj Shrestha"],
            ["Platform", "Meta Quest 2 · Unity · Meta XR SDK"],
          ].map(([k, v]) => (
            <div key={k}>
              <dt className="text-xs font-medium text-[var(--muted)]">{k}</dt>
              <dd className="mt-0.5 text-sm font-semibold text-[var(--ink)]">{v}</dd>
            </div>
          ))}
        </dl>
        <p className="mt-5 max-w-3xl text-base leading-relaxed text-[var(--ink-secondary)]">
          The project applies immersive VR and gamification to promote physical activity and long-term fitness engagement among young adults aged 18
          to 24. STRIKEFIT tests one specific idea within that: if the game measures and rewards real physical effort, rather than any movement that
          reaches the target, do players work harder and keep coming back? The dashboard records every session so effort and engagement can be
          measured rather than assumed.
        </p>
      </Reveal>
    </section>
  );
}

function Footer() {
  return (
    <footer className="border-t border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto flex max-w-6xl flex-wrap items-start justify-between gap-6 px-4 py-10 sm:px-6">
        <div>
          <p className="flex items-center gap-2.5">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-bold text-white shadow-sm">SF</span>
            <span className="text-sm font-semibold text-[var(--ink)]">STRIKEFIT</span>
          </p>
          <p className="mt-3 max-w-sm text-xs text-[var(--muted)]">A research prototype. Every number on this site comes from real sessions recorded by the game.</p>
        </div>
        <nav aria-label="Footer" className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
          <Link to="/dashboard" className="text-[var(--ink-secondary)] hover:text-[var(--accent)]">
            Dashboard
          </Link>
          <Link to="/dashboard/live" className="text-[var(--ink-secondary)] hover:text-[var(--accent)]">
            Live Arena
          </Link>
          <Link to="/dashboard/analytics" className="text-[var(--ink-secondary)] hover:text-[var(--accent)]">
            Analytics
          </Link>
          <a href={GITHUB} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 text-[var(--ink-secondary)] hover:text-[var(--accent)]">
            <Icon name="github" className="h-4 w-4" /> GitHub
          </a>
        </nav>
      </div>
    </footer>
  );
}

export default function Landing() {
  const status = useLiveStatus();
  useEffect(() => {
    document.title = "STRIKEFIT · Swing harder, hit harder";
  }, []);
  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <a href="#main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:shadow-lg">
        Skip to content
      </a>
      <Nav live={status.state === "live"} />
      <main id="main">
        <Hero status={status} />
        <Problem />
        <HowItWorks />
        <Levels />
        <Combat />
        <Numbers />
        <Stack />
        <Thesis />
      </main>
      <Footer />
    </div>
  );
}
