import { useEffect } from "react";
import { Link } from "react-router-dom";
import { LEVELS, SWING_SPEED_CAP, TIER_LABEL, swingBands, simulateHit } from "../lib/game.js";
import * as M from "../content/method.js";
import { Brand } from "../components/landing/LandingNav.jsx";
import DamageCurve from "../components/landing/DamageCurve.jsx";
import { Card, SectionRule } from "../components/ui/Card.jsx";
import { Banner } from "../components/ui/Feedback.jsx";
import Icon from "../components/ui/Icon.jsx";
import Button from "../components/ui/Button.jsx";

// /about/method: how the numbers are made, for a reader of the thesis or an examiner. Public. Every figure is read from the same
// modules the game pages use (game.js thresholds, the calorie constants mirrored from the backend), so it cannot drift from them.
// Print: the browser's Print dialog gives a clean A4 document (styles.css, "Method page").

const SECTIONS = [
  ["question", "Research question"],
  ["metric", "The effort metric"],
  ["calories", "Calorie estimate"],
  ["design", "Study design"],
  ["limits", "Limitations"],
  ["ethics", "Ethics"],
  ["refs", "References"],
];

const h2 = "t-h3 text-[var(--ink)]";
const p = "mt-3 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]";

function Section({ id, index, title, children }) {
  return (
    <section id={id} aria-labelledby={`${id}-h`} className="method-section scroll-mt-24 pt-10 first:pt-0">
      <SectionRule index={String(index).padStart(2, "0")} title={title} />
      <h2 id={`${id}-h`} className="sr-only">{title}</h2>
      {children}
    </section>
  );
}

function Formula({ children, note }) {
  return (
    <figure className="method-card mt-4 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-5 py-4">
      <pre className="whitespace-pre-wrap font-mono text-[0.85rem] font-semibold leading-relaxed text-[var(--ink)]">{children}</pre>
      {note && <figcaption className="mt-2 text-xs font-medium text-[var(--muted)]">{note}</figcaption>}
    </figure>
  );
}

function Thresholds() {
  const levels = ["Easy", "Medium", "Hard"];
  return (
    <div className="method-card scrollbar-thin mt-4 overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--surface)]">
      <table className="w-full min-w-[34rem] text-left text-sm">
        <caption className="sr-only">Swing speed thresholds in metres per second, per level</caption>
        <thead>
          <tr className="border-b border-[var(--border)] text-xs text-[var(--muted)]">
            <th scope="col" className="px-4 py-3 font-semibold">Level</th>
            {["none", "tap", "light", "medium", "full"].map((t) => (
              <th key={t} scope="col" className="px-3 py-3 text-right font-semibold">{TIER_LABEL[t]}</th>
            ))}
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {levels.map((k) => {
            const bands = swingBands(k, SWING_SPEED_CAP);
            return (
              <tr key={k} className="border-b border-[var(--border)] last:border-0">
                <th scope="row" className="px-4 py-3 font-bold text-[var(--ink)]">{LEVELS[k].name} <span className="font-medium text-[var(--muted)]">· {LEVELS[k].difficulty}</span></th>
                {["none", "tap", "light", "medium", "full"].map((t) => {
                  const b = bands.find((x) => x.id === t);
                  return <td key={t} className="px-3 py-3 text-right">{b ? (t === "full" ? `≥ ${b.from} m/s` : t === "none" ? `< ${b.to} m/s` : `${b.from} to ${b.to}`) : "—"}</td>;
                })}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export default function MethodView() {
  useEffect(() => {
    document.title = "Method · STRIKEFIT";
    return () => {
      document.title = "STRIKEFIT · Swing harder, hit harder";
    };
  }, []);
  const e = LEVELS.Easy.swing;
  const ex = M.estimateKcal(3, 60); // 60 s at an average tier of "medium"
  const flick = simulateHit(0.9, "Easy");
  const full = simulateHit(6.2, "Easy");
  return (
    <div className="method-page min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <header className="no-print border-b border-[var(--border)] bg-[var(--surface)]">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3 px-4 py-3.5 sm:px-6">
          <Link to="/" aria-label="STRIKEFIT home" className="rounded-xl"><Brand /></Link>
          <div className="flex flex-wrap items-center gap-2">
            <Link to="/dashboard/lab" className="press rounded-lg px-3 py-2 text-sm font-semibold text-[var(--ink-secondary)] hover:text-[var(--accent)]">Thesis Lab</Link>
            <Link to="/study" className="press rounded-lg px-3 py-2 text-sm font-semibold text-[var(--ink-secondary)] hover:text-[var(--accent)]">Study</Link>
            <Button icon="download" onClick={() => window.print()}>Print or save as PDF</Button>
          </div>
        </div>
      </header>

      <div className="mx-auto grid max-w-5xl gap-10 px-4 py-12 sm:px-6 lg:grid-cols-[13rem_minmax(0,1fr)] lg:py-16">
        <nav aria-label="On this page" className="no-print hidden lg:block">
          <ol className="sticky top-8 space-y-2 text-sm">
            {SECTIONS.map(([id, label], i) => (
              <li key={id} className="flex items-baseline gap-3">
                <span className="t-index w-5 text-[var(--muted)]">{String(i + 1).padStart(2, "0")}</span>
                <a href={`#${id}`} className="rounded font-semibold text-[var(--ink-secondary)] hover:text-[var(--accent)]">{label}</a>
              </li>
            ))}
          </ol>
        </nav>

        <main id="main" className="min-w-0">
          <p className="t-eyebrow">Research method</p>
          <h1 className="t-h1 mt-2 text-[var(--ink)]">How STRIKEFIT measures effort</h1>
          <p className={p}>A research prototype at Softwarica College. This page says exactly what is measured, what is estimated, and what the numbers cannot tell you.</p>

          <div className="mt-8 divide-y divide-transparent">
            <Section id="question" index={1} title="Research question">
              <blockquote className="method-card mt-4 rounded-xl border-l-4 border-[var(--accent)] bg-[var(--accent-soft)] px-5 py-4 text-lg font-bold leading-snug text-[var(--ink)]">{M.RESEARCH_QUESTION}</blockquote>
              <p className={p}>Four smaller questions follow from it:</p>
              <ol className="mt-2 space-y-1.5 text-[0.95rem] font-medium text-[var(--ink-secondary)]">
                {M.SUB_QUESTIONS.map((q, i) => <li key={q} className="flex gap-3"><span className="t-index pt-0.5 text-[var(--muted)]">{String.fromCharCode(97 + i)}</span>{q}</li>)}
              </ol>
            </Section>

            <Section id="metric" index={2} title="The effort metric">
              <p className={p}>
                Effort is the <strong className="text-[var(--ink)]">speed of the controller at the moment it hits</strong>, in metres per second. The headset reports the raw speed with every hit; the game takes the faster of the controller's tracked speed and the weapon's own motion. A hit counts only above a minimum speed, and damage then rises with speed.
              </p>
              <Formula note={`Level 1 values: minimum ${e.min} m/s, cap ${e.cap} m/s, ×${e.maxMul} at the cap, base ${e.base}, weapon ×${e.weaponMul}. Levels 2 and 3 use their own values (game.js).`}>
{`v        = max(controller speed, weapon speed)          [m/s]
v < min  → no hit, 0 damage
t        = (min(v, cap) − min) / (cap − min)             0 … 1
damage   = (base × (1 + (maxMul − 1) × t) + 0.08 × v) × weaponMul`}
              </Formula>
              <p className={p}>Swings are sorted into tiers by speed. The thresholds below are the values in the Unity scenes.</p>
              <Thresholds />
              <div className="mt-6 grid gap-6 md:grid-cols-2">
                <div>
                  <h3 className="text-sm font-extrabold text-[var(--ink)]">Damage against speed, Level 1</h3>
                  <DamageCurve level="Easy" />
                </div>
                <div>
                  <h3 className="text-sm font-extrabold text-[var(--ink)]">A worked example</h3>
                  <p className={p}>
                    On Level 1 a {0.9} m/s wrist flick is below the {e.min} m/s floor: <strong className="text-[var(--ink)]">{flick.counts ? flick.damage.toFixed(1) : "0"} damage</strong>. A {6.2} m/s full swing is {TIER_LABEL[full.tier].toLowerCase()}: <strong className="text-[var(--ink)]">{full.damage.toFixed(1)} damage</strong> and {full.xp} XP. Gesture scoring would count both as one hit.
                  </p>
                </div>
              </div>
              <h3 className="mt-8 text-sm font-extrabold text-[var(--ink)]">Why speeds are clamped at {SWING_SPEED_CAP} m/s</h3>
              <p className={p}>
                The game stops scaling damage at {SWING_SPEED_CAP} m/s, but the headset can report higher raw speeds (a fast flick, a tracking spike). Every average, peak and chart on this site clamps to {SWING_SPEED_CAP} m/s before computing anything, so a figure can never read higher than the game could use, and one glitch cannot move an average. The stored data keeps the raw value.
              </p>
              <h3 className="mt-8 text-sm font-extrabold text-[var(--ink)]">Effort fade</h3>
              <p className={p}>A fight is split into thirds of its running time. Fade is the change in average swing speed from the first third to the last third, as a percentage; under ±5% reads as “held steady”. It needs at least 6 landed swings, 2 in each end third, and it compares a fight with itself, so it needs no comparison between people.</p>
              <Formula>{`fade % = (average speed, last third − average speed, first third) / average speed, first third × 100`}</Formula>
            </Section>

            <Section id="calories" index={3} title="Calorie estimate">
              <Banner tone="warn" title="An estimate, not a measurement.">The headset reports no heart rate or oxygen use. Calories on this site are calculated, and are always labelled “estimated”.</Banner>
              <p className={p}>The standard MET method: energy = MET × body mass × time. Body mass is not collected, so every estimate assumes <strong className="text-[var(--ink)]">{M.CALORIE.bodyMassKg} kg</strong>. The intensity (MET) is scaled by how hard the player swung: a session of all-tap swings is priced at MET {M.CALORIE.metMin}, a session of all-full-power swings at MET {M.CALORIE.metMax}, linearly in between by the average tier weight (tap 1, light 2, medium 3, full 4).</p>
              <Formula note={`Worked example: 60 s at an average tier weight of 3 (medium) → MET ${ex.met.toFixed(1)} → ${ex.kcal.toFixed(1)} kcal.`}>
{`MET  = ${M.CALORIE.metMin} + (${M.CALORIE.metMax} − ${M.CALORIE.metMin}) × (average tier weight − 1) / 3
kcal = MET × ${M.CALORIE.bodyMassKg} kg × hours`}
              </Formula>
              <p className={p}>The “minutes of walking or jogging” figures divide the same energy by the MET of brisk walking ({M.CALORIE.metWalking}) and jogging ({M.CALORIE.metJogging}) from the Compendium of Physical Activities. The {M.CALORIE.metMin} to {M.CALORIE.metMax} range is an assumption, not taken from a source, and should be replaced with a cited value for active VR sword games before the thesis relies on it. A session with no landed swings gets no estimate at all.</p>
            </Section>

            <Section id="design" index={4} title="Study design">
              <p className={p}><strong className="text-[var(--ink)]">Sample.</strong> A pilot of up to 20 adults (codes P01 to P20), recruited in person by the researcher, each playing on a Meta Quest 2.</p>
              <h3 className="mt-6 text-sm font-extrabold text-[var(--ink)]">Procedure</h3>
              <ol className="mt-2 space-y-2 text-[0.95rem] font-medium text-[var(--ink-secondary)]">
                {M.PROCEDURE.map((s, i) => <li key={s} className="flex gap-3"><span className="t-index pt-0.5 text-[var(--accent)]">{String(i + 1).padStart(2, "0")}</span>{s}</li>)}
              </ol>
              <h3 className="mt-6 text-sm font-extrabold text-[var(--ink)]">Measures</h3>
              <div className="method-card scrollbar-thin mt-3 overflow-x-auto rounded-xl border border-[var(--border)] bg-[var(--surface)]">
                <table className="w-full min-w-[36rem] text-left text-sm">
                  <caption className="sr-only">What is measured, how, and where it is shown</caption>
                  <thead><tr className="border-b border-[var(--border)] text-xs text-[var(--muted)]"><th scope="col" className="px-4 py-3 font-semibold">Measure</th><th scope="col" className="px-3 py-3 font-semibold">How</th><th scope="col" className="px-4 py-3 font-semibold">Where</th></tr></thead>
                  <tbody>
                    {M.MEASURES.map(([a, b, c]) => (
                      <tr key={a} className="border-b border-[var(--border)] last:border-0 align-top">
                        <th scope="row" className="px-4 py-3 font-bold text-[var(--ink)]">{a}</th>
                        <td className="px-3 py-3 font-medium text-[var(--ink-secondary)]">{b}</td>
                        <td className="px-4 py-3 font-medium text-[var(--muted)]">{c}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p className={p}>SUS is scored (odd answer − 1) + (5 − even answer), summed and multiplied by 2.5, giving 0 to 100; 68 is the benchmark commonly quoted for an average system. The win rate used everywhere is victories ÷ (victories + defeats), with abandoned fights shown apart.</p>
            </Section>

            <Section id="limits" index={5} title="Limitations">
              <ul className="mt-4 space-y-3">
                {M.LIMITATIONS.map(([k, v]) => (
                  <li key={k} className="method-card flex gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3.5">
                    <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--warn)]" />
                    <p className="text-sm font-medium leading-relaxed text-[var(--ink-secondary)]"><strong className="text-[var(--ink)]">{k}.</strong> {v}</p>
                  </li>
                ))}
              </ul>
            </Section>

            <Section id="ethics" index={6} title="Ethics">
              <ul className="mt-4 list-disc space-y-2 pl-5 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)] marker:text-[var(--accent)]">
                {M.ETHICS.map((t) => <li key={t}>{t}</li>)}
              </ul>
              <p className={p}>Approval reference: <span className="font-semibold text-[var(--ink)]">to be added by the author before the thesis is submitted</span> (set <code className="rounded bg-[var(--surface-muted)] px-1">ETHICS_REFERENCE</code> in <code className="rounded bg-[var(--surface-muted)] px-1">content/studyContent.js</code> to show it on the consent screen too).</p>
              <p className="mt-4"><Link to="/study" className="press no-print inline-flex items-center gap-1.5 text-sm font-bold text-[var(--accent)] hover:underline">Read the consent text on the study page <Icon name="arrowRight" className="h-3.5 w-3.5" /></Link></p>
            </Section>

            <Section id="refs" index={7} title="References">
              <p className="mt-3 text-xs font-semibold text-[var(--warn-ink)]">Written from memory: check volume, issue and page numbers against the originals before citing.</p>
              <ol className="mt-3 list-decimal space-y-2 pl-5 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">
                {M.REFERENCES.map((r) => <li key={r}>{r}</li>)}
              </ol>
            </Section>
          </div>
        </main>
      </div>
    </div>
  );
}
