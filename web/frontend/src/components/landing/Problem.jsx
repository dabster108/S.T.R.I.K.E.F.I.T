import { simulateHit } from "../../lib/game.js";
import Icon from "../ui/Icon.jsx";
import { Reveal, SectionHead, card } from "./shared.jsx";

const PAINS = [
  ["hand", "Gestures, not effort", "Point a controller at a target and most games register a hit, whether you threw your whole body into it or flicked your wrist."],
  ["trendDown", "The lazy move wins", "The score goes up either way, so the easiest way to win is also the laziest way to move."],
  ["activity", "Effort goes unrewarded", "That matters when the point is exercise. If effort isn't measured it isn't rewarded, and players quickly stop giving it."],
];

export default function Problem() {
  const full = simulateHit(6.2, "Easy");
  const compare = [
    { title: "Gesture-based scoring", rows: [["Wrist flick", "Hit"], ["Full swing", "Hit"]], note: "Same result, so there's no reason to try harder.", accent: false },
    { title: "STRIKEFIT, Level 1", rows: [["Wrist flick, 0.9 m/s", "No hit"], ["Full swing, 6.2 m/s", `${full.damage.toFixed(1)} damage`]], note: "The harder swing is the only one that lands hard.", accent: true },
  ];
  return (
    <section id="problem" aria-labelledby="problem-title" className="mx-auto max-w-6xl px-4 py-24 sm:px-6 lg:py-32">
      <SectionHead id="problem-title" eyebrow="The problem" title="Most VR fitness games count movements. Few measure effort.">
        STRIKEFIT reads the real speed of each controller at the moment of impact. Too slow and the hit doesn&apos;t count. Faster swings deal more damage and earn more XP.
      </SectionHead>

      <div className="mt-14 grid gap-4 md:grid-cols-3">
        {PAINS.map(([icon, title, body], i) => (
          <Reveal key={title} delay={i * 110}>
            <article className={`${card} lift h-full p-6 hover:border-[var(--accent-ring)]`}>
              <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent)] ring-1 ring-inset ring-[var(--accent-ring)]">
                <Icon name={icon} className="h-5 w-5" />
              </span>
              <h3 className="t-h3 mt-5 text-[var(--ink)]">{title}</h3>
              <p className="mt-2 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)]">{body}</p>
            </article>
          </Reveal>
        ))}
      </div>

      <div className="mt-6 grid gap-4 md:grid-cols-2">
        {compare.map((c, i) => (
          <Reveal
            key={c.title}
            from={i ? "right" : "left"}
            delay={120 + i * 90}
            className={`${card} p-6 ${c.accent ? "ring-2 ring-inset ring-[var(--accent-ring)]" : ""}`}
          >
            <div className="flex items-center justify-between">
              <p className={`text-xs font-bold uppercase tracking-wider ${c.accent ? "text-[var(--accent)]" : "text-[var(--muted)]"}`}>{c.title}</p>
              {c.accent && <Icon name="check" className="h-5 w-5 text-[var(--accent)]" />}
            </div>
            <ul className="mt-4 divide-y divide-[var(--border)]">
              {c.rows.map(([k, v]) => (
                <li key={k} className="flex items-center justify-between py-3 text-[0.95rem]">
                  <span className="font-semibold text-[var(--ink-secondary)]">{k}</span>
                  <span className={`font-extrabold ${v === "No hit" ? "text-[var(--muted)]" : c.accent ? "text-[var(--accent)]" : "text-[var(--ink)]"}`}>{v}</span>
                </li>
              ))}
            </ul>
            <p className="mt-3 text-sm font-medium text-[var(--muted)]">{c.note}</p>
          </Reveal>
        ))}
      </div>
    </section>
  );
}
