import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { LEVELS, TIER_LABEL, simulateHit } from "../../lib/game.js";
import { useInView, useMagnetic, useParallax, useReducedMotion } from "../../hooks/motion.js";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

const LINES = [
  ["Swing", "harder."],
  ["Hit", "harder."],
];

// Example hits computed with the game's own formula (Level 1), used by the illustration only.
const ex = (v) => simulateHit(v, "Easy").damage.toFixed(1);
const FEED = [
  { icon: "bolt", tone: "text-[var(--tier-full)] bg-blue-50", text: `Full-power hit · 6.2 m/s`, value: `−${ex(6.2)}` },
  { icon: "timer", tone: "text-rose-600 bg-rose-50", text: "Monster wind-up · heavy, 1.25 s", value: "" },
  { icon: "shieldCheck", tone: "text-[var(--accent)] bg-blue-50", text: "Stepped out of reach · dodged", value: "" },
  { icon: "bolt", tone: "text-[var(--tier-medium)] bg-blue-50", text: `Medium hit · 3.4 m/s`, value: `−${ex(3.4)}` },
  { icon: "swords", tone: "text-gray-700 bg-gray-100", text: "Weapon clash · parried", value: "0" },
  { icon: "hand", tone: "text-[var(--tier-tap)] bg-blue-50", text: `Tap · 1.3 m/s`, value: `−${ex(1.3)}` },
];

// Sample swings for the illustration; tiers and damage come from the Level 1 formula.
const SWINGS = [3.4, 1.3, 6.2, 2.2, 5.4];
const SPEED_MAX = LEVELS.Easy.swing.cap;
const RAMP_MS = 700;
const HOLD_MS = 1800;

// Replays SWINGS on a loop: speed climbs 0 -> v, then the hit lands. Paused off screen.
function useSwingCycle(active) {
  const reduced = useReducedMotion();
  const [state, setState] = useState({ n: 0, speed: 6.2, landed: true });
  const n = useRef(0);
  useEffect(() => {
    if (reduced || !active) return undefined;
    let raf;
    let timer;
    const swing = () => {
      n.current += 1;
      const target = SWINGS[n.current % SWINGS.length];
      const t0 = performance.now();
      const tick = (now) => {
        const p = Math.min(1, (now - t0) / RAMP_MS);
        setState({ n: n.current, speed: target * (1 - Math.pow(1 - p, 3)), landed: p === 1 });
        if (p < 1) raf = requestAnimationFrame(tick);
        else timer = setTimeout(swing, HOLD_MS);
      };
      raf = requestAnimationFrame(tick);
    };
    timer = setTimeout(swing, 900);
    return () => {
      cancelAnimationFrame(raf);
      clearTimeout(timer);
    };
  }, [reduced, active]);
  const hit = simulateHit(state.speed, "Easy");
  return { ...state, tier: hit.tier, damage: hit.damage };
}

const GAUGE_START = 135;
const GAUGE_SWEEP = 270;
const angleFor = (v) => GAUGE_START + (GAUGE_SWEEP * Math.min(v, SPEED_MAX)) / SPEED_MAX;
function arc(from, to, r = 25) {
  const pt = (v) => {
    const a = (angleFor(v) * Math.PI) / 180;
    return `${(32 + r * Math.cos(a)).toFixed(2)} ${(32 + r * Math.sin(a)).toFixed(2)}`;
  };
  const large = angleFor(to) - angleFor(from) > 180 ? 1 : 0;
  return `M ${pt(from)} A ${r} ${r} 0 ${large} 1 ${pt(to)}`;
}
const { min, tiers } = LEVELS.Easy.swing;
const SEGMENTS = [
  { tier: "none", from: 0, to: min, color: "var(--border-strong)" },
  { tier: "tap", from: min, to: tiers.light, color: "var(--tier-tap)" },
  { tier: "light", from: tiers.light, to: tiers.medium, color: "var(--tier-light)" },
  { tier: "medium", from: tiers.medium, to: tiers.full, color: "var(--tier-medium)" },
  { tier: "full", from: tiers.full, to: SPEED_MAX, color: "var(--tier-full)" },
];

function Gauge({ speed, tier }) {
  const color = SEGMENTS.find((s) => s.tier === tier)?.color;
  return (
    <svg viewBox="0 0 64 64" className="h-[4.5rem] w-[4.5rem] shrink-0" aria-hidden>
      {SEGMENTS.map((s) => (
        <path key={s.tier} d={arc(s.from + 0.08, s.to - 0.08)} fill="none" stroke={s.color} strokeWidth="5" strokeLinecap="round" opacity="0.22" />
      ))}
      {speed > 0.05 && <path d={arc(0, speed)} fill="none" stroke={color} strokeWidth="5" strokeLinecap="round" />}
      <g style={{ transform: `rotate(${angleFor(speed)}deg)`, transformOrigin: "32px 32px" }}>
        <line x1="32" y1="32" x2="50" y2="32" stroke="var(--ink)" strokeWidth="2.5" strokeLinecap="round" />
      </g>
      <circle cx="32" cy="32" r="4" fill="var(--ink)" />
      <circle cx="32" cy="32" r="1.5" fill="white" />
    </svg>
  );
}

function SpeedReadout({ cycle }) {
  const { speed, tier, damage, landed, n } = cycle;
  return (
    <div className="flex items-center gap-3.5 rounded-2xl bg-white/85 py-3 pl-3 pr-5 shadow-[0_28px_50px_-22px_rgb(30_58_138/0.5),0_10px_20px_-12px_rgb(16_24_40/0.18)] backdrop-blur-xl">
      <Gauge speed={speed} tier={tier} />
      <div className="min-w-[8.5rem]">
        <p className="text-[0.68rem] font-bold uppercase tracking-[0.14em] text-[var(--muted)]">Swing speed</p>
        <p className="mt-1 flex items-baseline gap-1 leading-none text-[var(--ink)]">
          <span className="text-[1.9rem] font-extrabold tabular-nums tracking-tight">{speed.toFixed(1)}</span>
          <span className="text-sm font-bold">m/s</span>
        </p>
        <p className="mt-1.5 flex h-4 items-center gap-1.5 overflow-hidden text-xs font-extrabold text-[var(--ink)]">
          <span key={tier} className="tier-swap">
            {TIER_LABEL[tier]}
          </span>
          {damage > 0 && (
            <>
              <span className="text-[var(--muted)]" aria-hidden>
                ·
              </span>
              <span key={landed ? `hit-${n}` : "ramp"} className={`tabular-nums ${landed ? "tier-swap" : ""}`}>
                {damage.toFixed(1)} dmg
              </span>
            </>
          )}
        </p>
      </div>
    </div>
  );
}

function Magnetic({ children }) {
  const ref = useMagnetic(0.22);
  return (
    <span ref={ref} className="inline-flex transition-transform duration-300 ease-out will-change-transform">
      {children}
    </span>
  );
}

function LivePill({ status }) {
  if (!status) return null;
  const live = status.state === "live";
  const text = live ? (status.count === 1 ? "A fight is being played right now" : `${status.count} fights are being played right now`) : "The arena is open · no fight right now";
  const cls = `fade-up inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-semibold ring-1 ring-inset backdrop-blur ${
    live ? "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200 hover:bg-emerald-100" : "bg-white/80 text-[var(--ink-secondary)] ring-[var(--border)]"
  }`;
  const inner = (
    <>
      <span className={`h-2 w-2 rounded-full ${live ? "status-live bg-[var(--good)]" : "bg-[var(--accent)]"}`} aria-hidden />
      {text}
      {live && <Icon name="chevronRight" className="h-3.5 w-3.5" strokeWidth={2.4} />}
    </>
  );
  return live ? (
    <Link to="/dashboard/live" className={`press ${cls}`}>
      {inner}
    </Link>
  ) : (
    <p className={cls} role="status">
      {inner}
    </p>
  );
}

function SwingFx() {
  return (
    <svg viewBox="0 0 200 200" className="pointer-events-none absolute -right-8 -top-12 hidden h-44 w-44 sm:block" aria-hidden>
      <defs>
        <linearGradient id="arc-g" x1="0" y1="1" x2="1" y2="0">
          <stop offset="0" stopColor="var(--blue-300)" stopOpacity="0" />
          <stop offset="0.6" stopColor="var(--blue-400)" stopOpacity="0.7" />
          <stop offset="1" stopColor="var(--accent)" />
        </linearGradient>
      </defs>
      <g className="swing-arc" style={{ transformOrigin: "46px 168px" }}>
        <path d="M46 48 A120 120 0 0 1 158 104" fill="none" stroke="url(#arc-g)" strokeWidth="9" strokeLinecap="round" />
        <path d="M46 70 A98 98 0 0 1 140 112" fill="none" stroke="url(#arc-g)" strokeWidth="3" strokeLinecap="round" opacity="0.6" />
      </g>
      <g>
        <circle className="ripple-ring" cx="152" cy="120" r="20" fill="none" stroke="var(--accent)" strokeWidth="2" />
        <circle className="ripple-ring" cx="152" cy="120" r="20" fill="none" stroke="var(--blue-400)" strokeWidth="2" />
        <circle className="ripple-ring" cx="152" cy="120" r="20" fill="none" stroke="var(--blue-300)" strokeWidth="2" />
        <circle cx="152" cy="120" r="4" fill="var(--accent)" />
      </g>
    </svg>
  );
}

function MockBar({ label, tone, className, pop }) {
  const color = tone === "monster" ? "var(--hp-monster)" : "var(--hp-player)";
  const trail = tone === "monster" ? "var(--hp-monster-trail)" : "var(--hp-player-trail)";
  return (
    <div>
      <div className="relative mb-1.5 flex items-center justify-between text-[0.72rem] font-semibold">
        <span className="text-[var(--ink-secondary)]">{label}</span>
        {pop}
        <span className="text-[var(--muted)]">HP</span>
      </div>
      <div className="relative h-2.5 overflow-hidden rounded-full bg-gray-100">
        <div className={`mock-hp ${className} absolute inset-0 rounded-full`} style={{ background: trail, animationDelay: "0.25s" }} />
        <div className={`mock-hp ${className} absolute inset-0 rounded-full`} style={{ background: color }} />
      </div>
    </div>
  );
}

function LiveTag() {
  return (
    <span className="live-tag inline-flex items-center gap-1.5 rounded-full bg-[var(--accent)] py-1 pl-2 pr-2.5 text-[0.68rem] font-extrabold uppercase tracking-[0.12em] text-white shadow-[0_6px_14px_-6px_rgb(37_99_235/0.7)]">
      <span className="eq flex h-2.5 items-end gap-[2px]" aria-hidden>
        <i />
        <i />
        <i />
      </span>
      Live
    </span>
  );
}

function Mockup() {
  const rows = [...FEED, ...FEED.slice(0, 3)];
  const [ref, inView] = useInView({ rootMargin: "0px", once: false });
  const cycle = useSwingCycle(inView);
  const landedTier = cycle.landed ? cycle.tier : null;
  return (
    <div ref={ref} className="relative">
      <SwingFx />
      <div className="float-y">
        <div className="relative rounded-2xl border border-[var(--border)] bg-white/95 p-4 shadow-[0_30px_60px_-25px_rgb(30_58_138/0.35),0_12px_24px_-12px_rgb(16_24_40/0.12)] backdrop-blur sm:p-5">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-2">
              <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent)]">
                <Icon name="live" className="h-4 w-4" />
              </span>
              <span className="text-sm font-bold text-[var(--ink)]">Live Arena</span>
            </div>
            <div className="flex items-center gap-2.5">
              <span className="hidden items-center gap-1 text-[0.68rem] font-bold text-[var(--muted)] min-[400px]:inline-flex">
                <Icon name="dashboard" className="h-3.5 w-3.5" strokeWidth={2.2} />
                Dashboard preview
              </span>
              <LiveTag />
            </div>
          </div>

          <div className="mt-4 flex items-center gap-3">
            <span className="flex h-9 w-9 items-center justify-center rounded-full bg-[var(--accent)] text-xs font-extrabold text-white">P1</span>
            <div className="min-w-0">
              <p className="text-sm font-bold text-[var(--ink)]">Player 1</p>
              <p className="text-xs font-semibold text-[var(--muted)]">Level 1 · dual hammers</p>
            </div>
            {landedTier && (
              <span key={`${landedTier}-${cycle.n}`} className="tier-swap ml-auto rounded-lg bg-blue-50 px-2 py-1 text-xs font-extrabold text-[var(--tier-full)]">
                {TIER_LABEL[landedTier]}
              </span>
            )}
          </div>

          <div className="relative mt-4 space-y-3">
            <MockBar label="Player" tone="player" className="mock-hp-b" />
            <MockBar
              label="Monster"
              tone="monster"
              className=""
              pop={
                cycle.landed &&
                cycle.damage > 0 && (
                  <span key={cycle.n} className="hit-pop absolute right-7 -top-1 rounded-md bg-rose-600 px-1.5 py-0.5 text-xs font-extrabold tabular-nums text-white shadow">
                    −{cycle.damage.toFixed(1)}
                  </span>
                )
              }
            />
          </div>

          <div className="mt-4 overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface-muted)]" style={{ height: "7.5rem" }}>
            <ul className="mock-feed">
              {rows.map((r, i) => (
                <li key={i} className="flex h-10 items-center gap-2.5 border-b border-[var(--border)] px-3 text-xs" aria-hidden={i >= FEED.length || undefined}>
                  <span className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full ${r.tone}`}>
                    <Icon name={r.icon} className="h-3.5 w-3.5" strokeWidth={2.4} />
                  </span>
                  <span className="min-w-0 flex-1 truncate font-semibold text-[var(--ink-secondary)]">{r.text}</span>
                  {r.value && <span className="font-extrabold tabular-nums text-[var(--ink)]">{r.value}</span>}
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>

      <div className="float-y absolute -bottom-16 left-3 sm:-left-8 lg:-bottom-20 lg:-left-14" style={{ animationDelay: "-3s" }}>
        <SpeedReadout cycle={cycle} />
      </div>
    </div>
  );
}

// The numbers the whole game hangs on, read from lib/game.js (which mirrors the Unity values).
const L1 = LEVELS.Easy.swing;
const SPECS = [
  { value: L1.min.toFixed(1), unit: "m/s", label: "Slowest swing that registers a hit" },
  { value: L1.tiers.full.toFixed(1), unit: "m/s", label: "Full power on Level 1" },
  { value: `${L1.maxMul}×`, unit: "", label: "Damage multiplier at the speed cap" },
  { value: String(L1.cap), unit: "m/s", label: "Speed cap, where damage stops growing" },
];

function SpecStrip() {
  return (
    <div className="fade-up relative border-t border-[var(--border)] bg-white/60 backdrop-blur-sm" style={{ "--d": "620ms" }}>
      <dl className="mx-auto grid max-w-6xl grid-cols-2 px-4 sm:px-6 lg:grid-cols-[repeat(4,minmax(0,1fr))_auto]">
        {SPECS.map((s, i) => (
          <div key={s.label} className={`border-[var(--border)] py-5 pr-4 ${i ? "lg:pl-6" : ""} ${i % 2 ? "border-l pl-4" : ""} ${i === 2 ? "lg:border-l" : ""} ${i > 1 ? "border-t lg:border-t-0" : ""}`}>
            <dt className="t-kicker">{s.label}</dt>
            <dd className="readout mt-2 flex items-baseline gap-1 text-[var(--ink)]">
              <span className="text-2xl font-extrabold tracking-tight">{s.value}</span>
              {s.unit && <span className="text-sm font-bold text-[var(--muted)]">{s.unit}</span>}
            </dd>
          </div>
        ))}
        <p className="hidden items-center border-l border-[var(--border)] pl-6 text-xs font-semibold leading-relaxed text-[var(--muted)] lg:flex">
          Measured at the
          <br />
          moment of impact
        </p>
      </dl>
    </div>
  );
}

export default function Hero({ status }) {
  const bg = useParallax(0.25);
  const art = useParallax(-0.06);
  let w = 0;
  return (
    <section id="top" aria-labelledby="hero-title" className="relative -mt-[var(--nav-h)] overflow-hidden bg-[var(--surface)] pt-[var(--nav-h)]">
      <div ref={bg} className="pointer-events-none absolute inset-0" aria-hidden>
        <div className="blob left-[55%] top-[-12%] h-[34rem] w-[34rem] bg-[var(--accent-ring)] opacity-70" />
        <div className="blob blob-b left-[-10%] top-[30%] h-[26rem] w-[26rem] bg-[var(--blue-100)] opacity-90" />
        <div className="blob blob-c left-[70%] top-[45%] h-[22rem] w-[22rem] bg-[var(--blue-300)] opacity-35" />
        <div
          className="absolute inset-0 opacity-60"
          style={{
            backgroundImage: "linear-gradient(var(--border) 1px, transparent 1px), linear-gradient(90deg, var(--border) 1px, transparent 1px)",
            backgroundSize: "56px 56px",
            maskImage: "radial-gradient(ellipse 70% 60% at 50% 35%, #000 20%, transparent 75%)",
            WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 50% 35%, #000 20%, transparent 75%)",
          }}
        />
      </div>

      <div className="relative mx-auto grid max-w-6xl gap-16 px-4 pb-24 pt-12 sm:px-6 lg:grid-cols-12 lg:gap-8 lg:pb-28 lg:pt-20">
        <div className="lg:col-span-7">
          <p className="fade-up flex flex-wrap items-center gap-x-3 gap-y-1">
            <span className="t-kicker text-[var(--accent-strong)]">VR fitness research prototype</span>
            <span className="h-px w-10 bg-[var(--accent-ring)]" aria-hidden />
            <span className="t-kicker">Meta Quest 2</span>
          </p>
          <h1 id="hero-title" className="t-display mt-6 text-[var(--ink)]">
            {LINES.map((line, li) => (
              <span key={li} className="block">
                {line.map((word) => {
                  const i = w++;
                  return (
                    <span key={word + i} className="word-mask mr-[0.22em] last:mr-0">
                      <span className={`word ${li === 1 ? "bg-gradient-to-r from-[var(--accent)] to-[var(--blue-900)] bg-clip-text text-transparent" : ""}`} style={{ "--w": i }}>
                        {word}
                      </span>
                    </span>
                  );
                })}
              </span>
            ))}
          </h1>
          <p className="fade-up measure-tight mt-7 text-lg font-medium leading-relaxed text-[var(--ink-secondary)] sm:text-xl" style={{ "--d": "380ms" }}>
            STRIKEFIT is a VR combat game where damage comes from how fast you actually swing. Flick your wrist and nothing happens; put your body into it
            and the monster feels it.
          </p>
          <div className="fade-up mt-8 flex flex-wrap items-center gap-3" style={{ "--d": "480ms" }}>
            <Magnetic>
              <Link to="/dashboard" className={buttonClass("primary", "lg")}>
                Open the dashboard <Icon name="arrowRight" className="h-4 w-4" strokeWidth={2.4} />
              </Link>
            </Magnetic>
            <Magnetic>
              <Link to="/dashboard/live" className={buttonClass("secondary", "lg", "glow")}>
                <Icon name="play" className="h-4 w-4" strokeWidth={2.2} /> Watch the live arena
              </Link>
            </Magnetic>
          </div>
          <div className="mt-6 min-h-[2rem]" aria-live="polite">
            <LivePill status={status} />
          </div>
        </div>

        {/* offset below the headline and pushed past the container edge on wide screens */}
        <div ref={art} className="mx-auto w-full max-w-md lg:col-span-5 lg:mx-0 lg:mt-14 lg:max-w-none lg:-mr-6 xl:-mr-20">
          <div className="fade-up" style={{ "--d": "300ms" }}>
            <Mockup />
          </div>
        </div>
      </div>

      <SpecStrip />
    </section>
  );
}
