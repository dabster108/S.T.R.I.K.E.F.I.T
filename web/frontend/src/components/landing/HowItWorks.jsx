import { useEffect, useRef, useState } from "react";
import { LEVELS } from "../../lib/game.js";
import Icon from "../ui/Icon.jsx";
import SwingDemo from "./SwingDemo.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

const L1 = LEVELS.Easy.swing;
const L2 = LEVELS.Medium.swing;

const STEPS = [
  ["hammer", "Swing", "Swing a hammer or sword at the monster. Each hand is tracked on its own, so both arms count."],
  ["gauge", "Velocity measured", "At contact the game takes the faster of the controller's tracked speed and the weapon's own motion, in metres per second."],
  ["bolt", "Damage scaled", `Below ${L1.min} m/s nothing happens. Above it, damage climbs with speed up to a cap at ${L1.cap} m/s.`],
  [
    "sparkles",
    "Feedback and XP",
    `A heavier impact sound for big swings, damage numbers where the hit lands, and XP weighted toward full power: from ${L1.tiers.full} m/s on Level 1 and ${L2.tiers.full} m/s on Level 2.`,
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
  return (
    <section id="how" aria-labelledby="how-title" className="border-y border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 py-24 sm:px-6 lg:py-32">
        <SectionHead id="how-title" eyebrow="How it works" title="From arm speed to damage in four steps">
          Every hit goes through the same pipeline, in the headset, in the frame it lands.
        </SectionHead>

        <div className="mt-14 grid gap-10 lg:grid-cols-[0.9fr_1.1fr] lg:gap-14">
          <ol className="relative space-y-4 lg:space-y-0">
            <span className="absolute bottom-6 left-[1.35rem] top-6 hidden w-0.5 rounded-full bg-[var(--border)] lg:block" aria-hidden />
            <span
              className="step-rail absolute left-[1.35rem] top-6 hidden w-0.5 rounded-full bg-[var(--accent)] lg:block"
              style={{ height: "calc(100% - 3rem)", transform: `scaleY(${(active + 1) / STEPS.length})` }}
              aria-hidden
            />
            {STEPS.map(([icon, title, body], i) => {
              const on = i === active;
              return (
                <li
                  key={title}
                  ref={(el) => (refs.current[i] = el)}
                  data-step={i}
                  aria-current={on ? "step" : undefined}
                  className="relative flex items-center lg:min-h-[52vh]"
                >
                  <div
                    className={`relative flex w-full gap-4 rounded-2xl border p-5 transition-[background-color,border-color,box-shadow] duration-500 ${
                      on ? "border-[var(--accent-ring)] bg-[var(--surface)] shadow-[var(--shadow-lift)]" : "border-transparent bg-transparent"
                    }`}
                  >
                    <span
                      className={`relative z-[1] flex h-11 w-11 shrink-0 items-center justify-center rounded-xl text-sm font-extrabold transition-colors duration-500 ${
                        on ? "bg-[var(--accent)] text-white shadow-[var(--shadow-glow)]" : "bg-[var(--accent-soft)] text-[var(--accent)] ring-1 ring-inset ring-[var(--accent-ring)]"
                      }`}
                    >
                      <Icon name={icon} className="h-5 w-5" />
                    </span>
                    <div>
                      <p className="text-xs font-bold uppercase tracking-wider text-[var(--accent)]">Step {i + 1}</p>
                      <h3 className="t-h3 mt-1 text-[var(--ink)]">{title}</h3>
                      <p className="mt-1.5 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{body}</p>
                    </div>
                  </div>
                </li>
              );
            })}
          </ol>

          <div className="lg:sticky lg:top-[calc(var(--nav-h)+1.5rem)] lg:self-start">
            <Reveal from="right">
              <div className="mb-3 flex flex-wrap items-center gap-2" aria-hidden>
                {STEPS.map(([, title], i) => (
                  <span
                    key={title}
                    className={`rounded-full px-2.5 py-1 text-xs font-bold transition-colors duration-500 ${
                      i === active ? "bg-[var(--accent)] text-white" : "bg-[var(--surface-muted)] text-[var(--muted)] ring-1 ring-inset ring-[var(--border)]"
                    }`}
                  >
                    {i + 1}. {title}
                  </span>
                ))}
              </div>
              <SwingDemo step={active} />
            </Reveal>
          </div>
        </div>
      </div>
    </section>
  );
}
