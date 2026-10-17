import { Kicker, Reveal, THESIS } from "./shared.jsx";

const QUESTION = ["If the game measures and rewards real physical effort,", "rather than any movement that reaches the target,", "do players work harder and keep coming back?"];

const COLOPHON = [
  ["Institution", "Softwarica College"],
  ["Supervisor", "Manoj Shrestha"],
  ["Platform", "Meta Quest 2 · Unity · Meta XR SDK"],
  ["Target group", "Young adults aged 18 to 24"],
];

export default function Thesis() {
  return (
    <section id="thesis" aria-labelledby="thesis-title" className="border-t border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto max-w-6xl px-4 pb-24 pt-28 sm:px-6 lg:pb-28 lg:pt-40">
        <Reveal>
          <Kicker index="08">About the thesis · research question</Kicker>
        </Reveal>

        {/* the question is the loudest thing in the section, set as a pull quote on an accent rule */}
        <Reveal className="mt-10 border-l-4 border-[var(--accent)] pl-6 sm:pl-10 lg:mr-[8.333%]">
          <blockquote className="t-quote text-balance text-[var(--ink)]">
            {QUESTION.map((line, i) => (
              <span key={line} className="line-mask">
                <span style={{ "--l": i }}>{line}</span>
              </span>
            ))}
          </blockquote>
        </Reveal>

        <div className="mt-20 grid gap-12 border-t border-[var(--border)] pt-10 lg:grid-cols-12 lg:gap-10">
          <Reveal className="lg:col-span-6">
            <h2 id="thesis-title" className="t-h3 max-w-lg text-[var(--ink)]">
              {THESIS}
            </h2>
            <p className="measure mt-4 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">
              The project applies immersive VR and gamification to promote physical activity and long-term fitness engagement among young adults aged 18 to 24. The dashboard records every
              session so effort and engagement can be measured rather than assumed.
            </p>
          </Reveal>

          <Reveal from="right" delay={120} className="lg:col-span-5 lg:col-start-8">
            <dl className="border-t-2 border-[var(--ink)]">
              {COLOPHON.map(([k, v]) => (
                <div key={k} className="grid grid-cols-[7.5rem_minmax(0,1fr)] gap-4 border-b border-[var(--border)] py-3 text-sm">
                  <dt className="t-kicker pt-0.5">{k}</dt>
                  <dd className="font-bold text-[var(--ink)]">{v}</dd>
                </div>
              ))}
            </dl>
            <p className="mt-4 inline-flex items-center gap-2 text-xs font-bold text-[var(--accent-strong)]">
              <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--accent)]" aria-hidden />
              Active research prototype
            </p>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
