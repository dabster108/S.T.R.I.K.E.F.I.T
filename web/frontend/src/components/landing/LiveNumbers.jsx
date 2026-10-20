import { formatRelative } from "../../lib/format.js";
import { useCountUp, useInView } from "../../hooks/motion.js";
import Icon from "../ui/Icon.jsx";
import { levelCountsText } from "../../lib/game.js";
import { Reveal, SectionHead, card, usePublicStats } from "./shared.jsx";

function Count({ value, start }) {
  return Math.round(useCountUp(value, start, 1600)).toLocaleString();
}

// One number leads; the other two sit beside it on a rule.
function Figures({ s, start }) {
  return (
    <div className={`${card} grid overflow-hidden md:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]`}>
      <div className="relative p-7 sm:p-9">
        <p className="t-kicker">Sessions played</p>
        <p className="t-figure mt-5 text-[var(--ink)]">
          <Count value={s.totalSessions} start={start} />
        </p>
        <p className="measure-tight mt-5 text-sm font-medium leading-relaxed text-[var(--muted)]">Finished fights reported by a headset. Fights in progress aren&apos;t counted until they end.</p>
      </div>
      <dl className="grid border-t border-[var(--border)] bg-[var(--surface-muted)] md:border-l md:border-t-0">
        {[
          ["Swings that counted", s.totalSwings, "fast enough and on target"],
          ["Players", s.totalPlayers, "distinct names on the headset"],
        ].map(([label, value, note], i) => (
          <div key={label} className={`flex flex-col justify-center px-7 py-6 sm:px-9 ${i ? "border-t border-[var(--border)]" : ""}`}>
            <dt className="t-kicker">{label}</dt>
            <dd className="t-figure-md mt-3 text-[var(--ink)]">
              <Count value={value} start={start} />
            </dd>
            <dd className="mt-2 text-xs font-semibold text-[var(--muted)]">{note}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

export default function LiveNumbers() {
  const { state, stats: s } = usePublicStats();
  const [ref, inView] = useInView({ rootMargin: "0px 0px -15% 0px" });
  return (
    <section id="numbers" aria-labelledby="numbers-title" className="mx-auto max-w-6xl px-4 py-20 sm:px-6 lg:py-24">
      <div className="grid gap-10 lg:grid-cols-12 lg:gap-12">
        <div className="lg:col-span-4 lg:pt-2">
          <SectionHead id="numbers-title" index="07" eyebrow="Live numbers" title="Recorded so far">
            Straight from the game: every finished session a headset has reported.
          </SectionHead>
          {state === "ok" && (
            <Reveal delay={120} className="mt-8 border-t border-[var(--border)] pt-4">
              <p className="readout text-sm font-semibold leading-relaxed text-[var(--ink-secondary)]">
                {s.lastSessionAt && (
                  <>
                    Last fight finished <span className="font-extrabold text-[var(--ink)]">{formatRelative(s.lastSessionAt)}</span>.
                    <br />
                  </>
                )}
                {levelCountsText()}
              </p>
            </Reveal>
          )}
        </div>

        <div ref={ref} className="lg:col-span-8" aria-live="polite" aria-busy={state === "loading"}>
          {state === "ok" ? (
            <Reveal from="right">
              <Figures s={s} start={inView} />
              <p className="mt-4 text-xs font-medium text-[var(--muted)]">A swing counts when it reaches the minimum speed and lands on the monster.</p>
            </Reveal>
          ) : state === "loading" ? (
            <div className={`${card} grid overflow-hidden md:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]`}>
              <div className="p-7 sm:p-9">
                <div className="skeleton h-3 w-28" />
                <div className="skeleton mt-6 h-20 w-44" />
                <div className="skeleton mt-6 h-3 w-60" />
              </div>
              <div className="space-y-8 border-t border-[var(--border)] bg-[var(--surface-muted)] p-7 sm:p-9 md:border-l md:border-t-0">
                <div className="skeleton h-12 w-32" />
                <div className="skeleton h-12 w-24" />
              </div>
            </div>
          ) : (
            <Reveal className={`${card} flex flex-col items-start gap-4 p-7 sm:flex-row sm:items-center sm:p-9`}>
              {/* 0 only when the server said so; "—" when it hasn't answered */}
              <span className="t-figure text-[var(--border-strong)]" aria-hidden>
                {s ? "0" : "—"}
              </span>
              <div>
                {/* s is null when the server has not answered (it sleeps on the free tier): that is not "no sessions". */}
                <p className="text-lg font-extrabold tracking-tight text-[var(--ink)]">{s ? "No sessions yet" : "Waiting for the server"}</p>
                <p className="measure-tight mt-1 text-sm font-medium text-[var(--muted)]">
                  {s
                    ? `The first finished fight will show up here${s.liveSessions ? "; one is being played right now." : "."}`
                    : "The numbers appear as soon as it answers; this page keeps trying."}
                </p>
              </div>
              <Icon name="headset" className="h-6 w-6 text-[var(--accent)] sm:ml-auto" />
            </Reveal>
          )}
        </div>
      </div>
    </section>
  );
}
