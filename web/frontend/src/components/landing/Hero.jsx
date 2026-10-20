import { Link } from "react-router-dom";
import { LEVELS } from "../../lib/game.js";
import { useMagnetic, useParallax } from "../../hooks/motion.js";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import { usePauseOffscreen } from "../../motion/usePauseOffscreen.js";
import DashboardDemo from "./DashboardDemo.jsx";
import SwingTicker from "./SwingTicker.jsx";

const LINES = [
  ["Swing", "harder."],
  ["Hit", "harder."],
];

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
    <div className="fade-up relative border-t border-[var(--border)] bg-[var(--surface)]/80" style={{ "--d": "620ms" }}>
      <p className="mx-auto max-w-6xl px-4 pt-4 text-xs font-semibold text-[var(--muted)] sm:px-6">The game's own numbers, measured at the moment of impact</p>
      <dl className="mx-auto grid max-w-6xl grid-cols-2 px-4 sm:px-6 lg:grid-cols-4">
        {SPECS.map((s, i) => (
          <div key={s.label} className={`border-[var(--border)] py-5 pr-4 ${i ? "lg:pl-6" : ""} ${i % 2 ? "border-l pl-4" : ""} ${i === 2 ? "lg:border-l" : ""} ${i > 1 ? "border-t lg:border-t-0" : ""}`}>
            <dt className="t-kicker">{s.label}</dt>
            <dd className="readout mt-2 flex items-baseline gap-1 text-[var(--ink)]">
              <span className="text-2xl font-extrabold tracking-tight tabular-nums">{s.value}</span>
              {s.unit && <span className="text-sm font-bold text-[var(--muted)]">{s.unit}</span>}
            </dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

export default function Hero({ status }) {
  const bg = useParallax(0.25);
  const art = useParallax(-0.06);
  const pause = usePauseOffscreen(); // the drifting gradient and the dashboard preview stop while the hero is scrolled away
  let w = 0;
  return (
    <section ref={pause} id="top" aria-labelledby="hero-title" className="relative -mt-[var(--nav-h)] overflow-hidden bg-[var(--surface)] pt-[var(--nav-h)]">
      <div ref={bg} className="pointer-events-none absolute inset-0" aria-hidden>
        <div className="blob left-[55%] top-[-12%] h-[34rem] w-[34rem] bg-[var(--accent-ring)] opacity-70" />
        <div className="blob blob-b left-[-10%] top-[30%] h-[26rem] w-[26rem] bg-[var(--blue-100)] opacity-90" />
        <div className="blob blob-c left-[70%] top-[45%] h-[22rem] w-[22rem] bg-[var(--blue-300)] opacity-35" />
        <div
          className="absolute inset-0 opacity-80"
          style={{
            backgroundImage: "radial-gradient(circle at 1px 1px, var(--border-strong) 1.1px, transparent 1.6px)",
            backgroundSize: "26px 26px",
            maskImage: "radial-gradient(ellipse 70% 60% at 50% 35%, #000 20%, transparent 75%)",
            WebkitMaskImage: "radial-gradient(ellipse 70% 60% at 50% 35%, #000 20%, transparent 75%)",
          }}
        />
      </div>

      <div className="relative mx-auto grid max-w-6xl gap-16 px-4 pb-24 pt-12 sm:px-6 lg:grid-cols-12 lg:gap-8 lg:pb-28 lg:pt-20">
        <div className="lg:col-span-7">
          <p className="rise flex flex-wrap items-center gap-x-3 gap-y-1">
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
          <p className="rise measure-tight mt-7 text-lg font-medium leading-relaxed text-[var(--ink-secondary)] sm:text-xl" style={{ "--d": "380ms" }}>
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
          <div className="fade-up mt-6" style={{ "--d": "560ms" }}>
            <SwingTicker />
          </div>
        </div>

        {/* offset below the headline and pushed past the container edge on wide screens */}
        <div ref={art} className="mx-auto w-full max-w-md lg:col-span-5 lg:mx-0 lg:mt-14 lg:max-w-none lg:-mr-6 xl:-mr-20">
          <div className="fade-up relative" style={{ "--d": "300ms" }}>
            <SwingFx />
            <DashboardDemo />
          </div>
        </div>
      </div>

      <SpecStrip />
    </section>
  );
}
