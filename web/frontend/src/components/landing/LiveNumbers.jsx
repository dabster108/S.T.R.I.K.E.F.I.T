import { formatRelative } from "../../lib/format.js";
import { useCountUp, useInView } from "../../hooks/motion.js";
import Icon from "../ui/Icon.jsx";
import { Reveal, SectionHead, card, usePublicStats } from "./shared.jsx";

function Tile({ label, value, icon, start, i }) {
  const v = useCountUp(value, start, 1600);
  return (
    <Reveal delay={i * 100}>
      <div className={`${card} lift relative h-full overflow-hidden p-6 hover:border-[var(--accent-ring)]`}>
        <div className="pointer-events-none absolute -right-10 -top-10 h-32 w-32 rounded-full bg-[var(--accent-soft)]" aria-hidden />
        <div className="relative flex items-center justify-between gap-2">
          <p className="text-sm font-bold text-[var(--ink-secondary)]">{label}</p>
          <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--accent)] text-white shadow-[var(--shadow-glow)]">
            <Icon name={icon} className="h-5 w-5" />
          </span>
        </div>
        <p className="relative mt-5 text-5xl font-extrabold leading-none tracking-tight text-[var(--ink)] tabular-nums">{Math.round(v).toLocaleString()}</p>
      </div>
    </Reveal>
  );
}

export default function LiveNumbers() {
  const { state, stats: s } = usePublicStats();
  const [ref, inView] = useInView({ rootMargin: "0px 0px -15% 0px" });
  const tiles = [
    ["Sessions played", s?.totalSessions, "sessions"],
    ["Swings that counted", s?.totalSwings, "bolt"],
    ["Players", s?.totalPlayers, "players"],
  ];
  return (
    <section id="numbers" aria-labelledby="numbers-title" className="mx-auto max-w-6xl px-4 py-24 sm:px-6 lg:py-32">
      <SectionHead id="numbers-title" eyebrow="Live numbers" title="Recorded so far">
        Straight from the game: every finished session a headset has reported.
      </SectionHead>
      <div ref={ref} className="mt-14" aria-live="polite" aria-busy={state === "loading"}>
        {state === "ok" ? (
          <>
            <div className="grid gap-4 sm:grid-cols-3">
              {tiles.map(([label, value, icon], i) => (
                <Tile key={label} label={label} value={value} icon={icon} start={inView} i={i} />
              ))}
            </div>
            <p className="mt-5 text-sm font-medium text-[var(--muted)]">
              {s.lastSessionAt ? `Last fight finished ${formatRelative(s.lastSessionAt)}. ` : ""}
              {s.levelsAvailable} levels playable, {s.levelsPlanned} planned. A swing counts when it reaches the minimum speed and lands on the monster.
            </p>
          </>
        ) : state === "loading" ? (
          <div className="grid gap-4 sm:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <div key={i} className={`${card} p-6`}>
                <div className="skeleton h-4 w-32" />
                <div className="skeleton mt-6 h-11 w-28" />
              </div>
            ))}
          </div>
        ) : (
          <Reveal className={`${card} flex flex-col items-center px-6 py-14 text-center`}>
            <span className="relative flex h-16 w-16 items-center justify-center">
              <span className="absolute inset-0 rounded-full bg-[var(--accent-soft)]" />
              <span className="absolute inset-2 rounded-full border-2 border-dashed border-[var(--accent-ring)]" />
              <Icon name="headset" className="relative h-7 w-7 text-[var(--accent)]" />
            </span>
            <p className="mt-4 text-lg font-extrabold tracking-tight text-[var(--ink)]">No sessions yet</p>
            <p className="mt-1 max-w-sm text-sm font-medium text-[var(--muted)]">The first finished fight will show up here{s?.liveSessions ? "; one is being played right now." : "."}</p>
          </Reveal>
        )}
      </div>
    </section>
  );
}
