import { useEffect, useRef, useState } from "react";
import { LEVELS } from "../../lib/game.js";
import SwingDemo from "./SwingDemo.jsx";
import TypedLine from "./TypedLine.jsx";
import DamageCurve from "./DamageCurve.jsx";
import DataPath from "./DataPath.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

const L1 = LEVELS.Easy.swing;
const L2 = LEVELS.Medium.swing;

// [title, body, readout]. Readouts restate the rule with the game's own constants.
const STEPS = [
  ["Swing", "Swing a hammer or sword at the monster. Each hand is tracked on its own, so both arms count.", "hands tracked: left + right, independently"],
  [
    "Velocity measured",
    "At contact the game takes the faster of the controller's tracked speed and the weapon's own motion, in metres per second.",
    "v = max(controller speed, weapon speed)",
  ],
  ["Damage scaled", `Below ${L1.min} m/s nothing happens. Above it, damage climbs with speed up to a cap at ${L1.cap} m/s.`, `v < ${L1.min} → 0 · ${L1.min}–${L1.cap} m/s → ×1 to ×${L1.maxMul}`],
  [
    "Feedback and XP",
    `A heavier impact sound for big swings, damage numbers where the hit lands, and XP weighted toward full power: from ${L1.tiers.full} m/s on Level 1 and ${L2.tiers.full} m/s on Level 2.`,
    `full-power bonus: +${L1.xpFull} XP on L1, +${L2.xpFull} XP on L2`,
  ],
];

// The step crossing the middle of the viewport is the active one.
function useActiveStep(count) {
  const refs = useRef([]);
  const [active, setActive] = useState(0);
  useEffect(() => {
    const io = new IntersectionObserver(
      (items) => {
        for (const it of items) if (it.isIntersecting) setActive(Number(it.target.dataset.step));
      },
      { rootMargin: "-45% 0px -45% 0px" }
    );
    refs.current.slice(0, count).forEach((el) => el && io.observe(el));
    return () => io.disconnect();
  }, [count]);
  return [refs, active];
}

export default function HowItWorks() {
  const [refs, active] = useActiveStep(STEPS.length);
  const [demo, setDemo] = useState({ level: "Easy", speed: 3.4 });
  return (
    <section id="how" aria-labelledby="how-title" className="border-y border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 pb-24 pt-20 sm:px-6 lg:pb-32 lg:pt-28">
        <SectionHead id="how-title" index="02" eyebrow="How it works" title="From arm speed to damage in four steps" layout="split">
          Every hit goes through the same pipeline, in the headset, in the frame it lands.
        </SectionHead>

        <div className="mt-14 grid gap-10 lg:mt-20 lg:grid-cols-12 lg:gap-12">
          {/* demo first on wide screens: the steps scroll past it on the right */}
          <div className="lg:sticky lg:top-[calc(var(--nav-h)+1.5rem)] lg:col-span-7 lg:self-start">
            <Reveal from="left">
              <div className="mb-3 flex items-baseline justify-between gap-4" aria-hidden>
                <span className="t-kicker">Try it · drag the speed</span>
                <span className="readout text-xs font-bold text-[var(--muted)]">
                  step <span className="text-[var(--accent)]">{active + 1}</span> / {STEPS.length}
                </span>
              </div>
              <SwingDemo step={active} onChange={setDemo} />
            </Reveal>
          </div>

          <ol className="relative lg:col-span-5">
            <span className="absolute bottom-8 left-[1.05rem] top-8 hidden w-px bg-[var(--border)] lg:block" aria-hidden />
            <span
              className="step-rail absolute left-[1.05rem] top-8 hidden w-px bg-[var(--accent)] lg:block"
              style={{ height: "calc(100% - 4rem)", transform: `scaleY(${(active + 1) / STEPS.length})` }}
              aria-hidden
            />
            {STEPS.map(([title, body, readout], i) => {
              const on = i === active;
              return (
                <li
                  key={title}
                  ref={(el) => (refs.current[i] = el)}
                  data-step={i}
                  aria-current={on ? "step" : undefined}
                  className="relative flex items-center py-6 lg:min-h-[46vh] lg:py-0"
                >
                  <div className="grid w-full grid-cols-[2.25rem_minmax(0,1fr)] gap-x-5">
                    <span
                      className={`relative z-[1] flex h-9 w-9 items-center justify-center rounded-full bg-[var(--surface)] text-xs font-extrabold tabular-nums ring-1 transition-colors duration-500 ${
                        on ? "text-[var(--accent)] ring-[var(--accent)]" : "text-[var(--muted)] ring-[var(--border-strong)]"
                      }`}
                    >
                      {String(i + 1).padStart(2, "0")}
                    </span>
                    <div className={`transition-opacity duration-500 ${on ? "opacity-100" : "lg:opacity-55"}`}>
                      <h3 className="t-h3 pt-1 text-[var(--ink)]">{title}</h3>
                      <p className="measure-tight mt-2 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{body}</p>
                      <p
                        className={`readout mt-4 inline-block border-l-2 py-1 pl-3 text-[0.8125rem] font-bold transition-colors duration-500 ${
                          on ? "border-[var(--accent)] text-[var(--accent-strong)]" : "border-[var(--border-strong)] text-[var(--muted)]"
                        }`}
                      >
                        <TypedLine text={readout} active={on} />
                      </p>
                      {i === 2 && <DamageCurve level={demo.level} marker={demo.speed} />}
                    </div>
                  </div>
                </li>
              );
            })}
          </ol>
        </div>

        <DataPath />
      </div>
    </section>
  );
}
