import { Link } from "react-router-dom";
import { Star } from "lucide-react";
import { levelName } from "../../lib/game.js";
import { nextStep } from "../../lib/engage.js";
import { formatNumber } from "../../lib/format.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import { Skeleton } from "../ui/Feedback.jsx";
import { LevelBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";

// Three mastery stars per level (achievements.mastery from the API: cleared; cleared within a damage limit; cleared fast
// or with a full-power share). Thresholds are "tuning in progress, set from playtest data" and the card says so.

export function Stars({ stars = [], size = "h-4 w-4", label }) {
  const earned = stars.filter((s) => s.earned).length;
  return (
    <span className="inline-flex items-center gap-0.5" role="img" aria-label={label || `${earned} of 3 stars`}>
      {[0, 1, 2].map((i) => {
        const s = stars[i];
        return (
          <Star
            key={i}
            className={`${size} ${s?.earned ? "text-amber-500" : "text-[var(--border-strong)]"}`}
            fill={s?.earned ? "currentColor" : "none"}
            strokeWidth={2}
            aria-hidden="true"
          >
            {s && <title>{`${s.label}: ${s.earned ? "earned" : "not yet"}`}</title>}
          </Star>
        );
      })}
    </span>
  );
}

// Level 1 -> Level 2 -> Level 3, each with its stars and the closest figures behind them.
export function JourneyStrip({ mastery }) {
  if (!mastery) return <Skeleton className="h-28 w-full" />;
  return (
    <Card className="overflow-hidden" aria-label="Journey: mastery stars per level">
      <CardHeader title="Journey" subtitle={`${mastery.starsEarned} of ${mastery.starsTotal} stars · ${mastery.note}`} />
      <ol className="grid gap-px bg-[var(--border)] sm:grid-cols-3">
        {mastery.levels.map((l, i) => (
          <li key={l.level} className="relative bg-[var(--surface)] px-5 py-4">
            <div className="flex items-center justify-between gap-2">
              <span className="flex items-center gap-2 text-sm font-bold text-[var(--ink)]">
                <LevelBadge level={l.level} /> {levelName(l.level)}
              </span>
              <Stars stars={l.stars} size="h-5 w-5" label={`${levelName(l.level)}: ${l.starsEarned} of 3 stars`} />
            </div>
            <ul className="mt-3 space-y-1 text-xs font-medium text-[var(--ink-secondary)]">
              {l.stars.map((s) => (
                <li key={s.id} className="flex items-start gap-1.5">
                  <Icon name={s.earned ? "check" : "target"} className={`mt-px h-3.5 w-3.5 shrink-0 ${s.earned ? "text-[var(--good)]" : "text-[var(--muted)]"}`} />
                  <span>
                    {s.label}
                    {s.measurable === false && <span className="text-[var(--muted)]"> (health loss not recorded for these wins)</span>}
                  </span>
                </li>
              ))}
            </ul>
            <p className="mt-3 text-xs font-semibold text-[var(--muted)]">
              {l.played
                ? `${l.wins} win${l.wins === 1 ? "" : "s"} in ${l.played} fight${l.played === 1 ? "" : "s"}${l.best.score ? ` · best ${formatNumber(l.best.score)}` : ""}`
                : "Not played yet"}
            </p>
            {i < 2 && <Icon name="chevronRight" className="absolute -right-2.5 top-1/2 z-[1] hidden h-5 w-5 -translate-y-1/2 rounded-full bg-[var(--surface)] text-[var(--muted)] sm:block" />}
          </li>
        ))}
      </ol>
    </Card>
  );
}

export function NextStepCard({ mastery, challenges, name }) {
  const step = mastery ? nextStep({ mastery: mastery.levels, rest: challenges?.rest, challenges }) : null;
  return (
    <Card className="flex h-full flex-col p-5">
      <p className="t-kicker">Next step</p>
      {!mastery ? (
        <Skeleton className="mt-3 h-16 w-full" />
      ) : step ? (
        <>
          <p className="mt-2 flex items-center gap-2 text-lg font-extrabold tracking-tight text-[var(--ink)]">
            <Icon name={step.kind === "rest" ? "coffee" : step.kind === "star" ? "sparkles" : step.kind === "challenge" ? "target" : "compass"} className="h-5 w-5 text-[var(--accent-strong)]" />
            {step.title}
          </p>
          <p className="mt-1.5 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{step.text}</p>
          {step.level && step.kind !== "rest" && (
            <Link to={`/monsters#${step.level}`} className="press mt-3 inline-flex w-fit items-center gap-1 rounded text-xs font-semibold text-[var(--accent)] hover:underline">
              How to beat {levelName(step.level)} <Icon name="arrowRight" className="h-3 w-3" />
            </Link>
          )}
        </>
      ) : (
        <p className="mt-2 text-sm font-medium text-[var(--muted)]">Nothing to suggest yet{name ? ` for ${name}` : ""}.</p>
      )}
      <p className="mt-auto pt-4 text-[0.7rem] font-semibold text-[var(--muted)]">Suggested by simple rules, not AI.</p>
    </Card>
  );
}
