import Icon from "../ui/Icon.jsx";
import { Reveal, THESIS, card } from "./shared.jsx";

const QUESTION = ["If the game measures and rewards real physical effort,", "rather than any movement that reaches the target,", "do players work harder and keep coming back?"];

export default function Thesis() {
  return (
    <section id="thesis" aria-labelledby="thesis-title" className="border-t border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 py-24 sm:px-6 lg:py-32">
        <Reveal className="max-w-3xl">
          <p className="t-eyebrow">About the thesis</p>
          <h2 id="thesis-title" className="t-h2 mt-3 text-[var(--ink)]">
            {THESIS}
          </h2>
        </Reveal>

        <div className="mt-12 grid gap-6 lg:grid-cols-[1.35fr_1fr]">
          <Reveal className={`${card} relative overflow-hidden p-7 sm:p-10`}>
            <span className="pointer-events-none absolute -left-4 -top-10 select-none text-[10rem] font-extrabold leading-none text-[var(--accent-soft)]" aria-hidden>
              &ldquo;
            </span>
            <p className="relative text-xs font-bold uppercase tracking-wider text-[var(--accent)]">Research question</p>
            <blockquote className="relative mt-4 text-balance text-xl font-bold leading-snug tracking-tight text-[var(--ink)] sm:text-2xl xl:text-[1.6rem]">
              {QUESTION.map((line, i) => (
                <span key={line} className="line-mask">
                  <span style={{ "--l": i }}>{line}</span>
                </span>
              ))}
            </blockquote>
            <p className="relative mt-6 max-w-2xl text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">
              The project applies immersive VR and gamification to promote physical activity and long-term fitness engagement among young adults aged 18 to 24. The dashboard records every
              session so effort and engagement can be measured rather than assumed.
            </p>
          </Reveal>

          <Reveal from="right" delay={120} className="flex flex-col gap-4">
            {[
              ["graduation", "Institution", "Softwarica College"],
              ["player", "Supervisor", "Manoj Shrestha"],
              ["headset", "Platform", "Meta Quest 2 · Unity · Meta XR SDK"],
            ].map(([icon, k, v]) => (
              <div key={k} className={`${card} lift flex items-center gap-4 p-5 hover:border-[var(--accent-ring)]`}>
                <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent)] ring-1 ring-inset ring-[var(--accent-ring)]">
                  <Icon name={icon} className="h-5 w-5" />
                </span>
                <div>
                  <p className="text-xs font-bold uppercase tracking-wider text-[var(--muted)]">{k}</p>
                  <p className="mt-0.5 text-base font-bold text-[var(--ink)]">{v}</p>
                </div>
              </div>
            ))}
            <p className="inline-flex items-center gap-2 self-start rounded-full bg-[var(--accent-soft)] px-3 py-1.5 text-xs font-bold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">
              <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--accent)]" aria-hidden />
              Active research prototype
            </p>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
