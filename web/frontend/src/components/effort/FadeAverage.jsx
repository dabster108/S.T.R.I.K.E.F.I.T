import { Link } from "react-router-dom";
import { api } from "../../lib/api.js";
import { useApi } from "../../hooks/useApi.js";
import { FADE_PLAIN, FADE_TONE, formatFade, speakFade } from "../../lib/effort.js";
import { levelShort } from "../../lib/game.js";
import { formatShortDate } from "../../lib/format.js";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Button from "../ui/Button.jsx";
import { FadeTile } from "./FadePanel.jsx";

// A player's average effort fade over their last 10 finished fights (GET /api/players/:name/fade), with each fight as a chip
// that opens it. Fights without enough swings are listed as "not enough swings" and left out of the average.
const AVG_MIN_H = 280; // skeleton and loaded card share it: no jump when the numbers arrive

export default function FadeAverage({ name }) {
  const { data, error, reload } = useApi(() => api.playerFade(name, 10), [name]);
  if (error && !data) {
    return <EmptyState icon="clock" title="Effort fade is unavailable right now" description="It loads from the server as soon as it answers." action={<Button onClick={reload}>Try again</Button>} />;
  }
  if (!data || data.playerName !== name) {
    return (
      <div className="grid content-start gap-5 p-5 lg:grid-cols-[minmax(0,17rem)_minmax(0,1fr)]" style={{ minHeight: AVG_MIN_H }} aria-hidden>
        <Skeleton className="h-44 w-full rounded-xl" />
        <Skeleton className="h-44 w-full rounded-xl" />
      </div>
    );
  }
  const avg = data.average;
  const ok = avg.status === "ok";
  return (
    <div className="grid content-start gap-5 p-5 lg:grid-cols-[minmax(0,17rem)_minmax(0,1fr)]" style={{ minHeight: AVG_MIN_H }}>
      <div>
        {ok ? (
          <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-5">
            <p className="t-kicker">Average fade · last {data.last} fights</p>
            <p className="mt-3 flex flex-wrap items-baseline gap-x-3 gap-y-2">
              <span className={`text-3xl font-extrabold tracking-tight tabular-nums ${FADE_TONE[avg.label].text}`} aria-label={`Average fade ${speakFade(avg.fadePct)}`}>{formatFade(avg.fadePct)}</span>
              <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-extrabold ring-1 ring-inset ${FADE_TONE[avg.label].chip}`}>{avg.labelText}</span>
            </p>
            <p className="mt-3 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{FADE_PLAIN[avg.label]}</p>
            <p className="mt-3 border-t border-[var(--border)] pt-3 text-xs font-medium text-[var(--muted)]">
              Average of {avg.sessionsUsed} of {avg.sessionsConsidered} fights that had enough swings.
            </p>
          </div>
        ) : (
          <FadeTile fade={{ status: "insufficient", reason: avg.reason }} />
        )}
      </div>
      <div className="min-w-0">
        <p className="text-xs font-bold text-[var(--ink)]">Each fight, newest first</p>
        <ul className="mt-3 flex flex-wrap gap-2">
          {data.fights.map((f) => (
            <li key={f.sessionId}>
              <Link
                to={`/dashboard/session/${encodeURIComponent(f.sessionId)}`}
                className="press flex min-w-[7.5rem] flex-col rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2 hover:border-[var(--accent-ring)]"
              >
                <span className="text-xs font-semibold text-[var(--muted)]">
                  {levelShort(f.level)} · {formatShortDate(f.startedAt)}
                </span>
                {f.fade.status === "ok" ? (
                  <span className={`mt-0.5 text-base font-extrabold tabular-nums ${FADE_TONE[f.fade.label].text}`} aria-label={`Fade ${speakFade(f.fade.fadePct)}, ${f.fade.labelText}`}>
                    {formatFade(f.fade.fadePct)} <span className="text-xs font-bold">{f.fade.labelText}</span>
                  </span>
                ) : (
                  <span className="mt-0.5 text-xs font-bold text-[var(--muted)]">Not enough swings</span>
                )}
              </Link>
            </li>
          ))}
        </ul>
        <p className="mt-3 text-xs font-medium text-[var(--muted)]">
          Fade compares the average swing speed in the last third of a fight with the first third. Positive means swings got faster.
        </p>
      </div>
    </div>
  );
}
