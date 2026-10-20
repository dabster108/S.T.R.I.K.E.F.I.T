import { FALLBACK_ICON, MILESTONE_ICON, MILESTONE_REFRESH_MS } from "../../config/milestones.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { api } from "../../lib/api.js";
import { hasTimeline, milestoneDate, milestoneStatus, progressPercent } from "../../lib/milestones.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Reveal } from "../../motion/index.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";

// The player's milestones as a vertical timeline (GET /api/players/:name/milestones): what was reached and when, newest at the bottom like a
// history, then "Next up" with the next three shown dimmed with a progress bar. The list rises in with the shared <Reveal> motion (nothing under
// reduced motion or in Calm mode). Loading, nothing-yet and error each have their own state.

function Node({ icon, reached }) {
  return (
    <span
      className={`absolute left-0 top-0.5 flex h-8 w-8 items-center justify-center rounded-full ring-4 ring-[var(--surface)] ${reached ? "bg-[var(--accent)] text-white" : "border border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)] text-[var(--muted)]"}`}
      aria-hidden
    >
      <Icon name={icon} className="h-4 w-4" strokeWidth={2.2} />
    </span>
  );
}

function Reached({ m, i }) {
  return (
    <Reveal as="li" index={i} className="relative pb-6 pl-12 last:pb-0" data-testid="milestone">
      <Node icon={MILESTONE_ICON[m.kind] || FALLBACK_ICON} reached />
      <div className="flex flex-wrap items-baseline justify-between gap-x-3">
        <p className="text-sm font-extrabold text-[var(--ink)]">{m.title}</p>
        <time dateTime={m.reachedAt} className="text-xs font-semibold tabular-nums text-[var(--muted)]">
          {milestoneDate(m.day)}
        </time>
      </div>
      <p className="mt-0.5 text-sm font-medium text-[var(--ink-secondary)]">{m.line}</p>
      <span className="sr-only">{milestoneStatus(m)}</span>
    </Reveal>
  );
}

function Upcoming({ m, i }) {
  const pct = progressPercent(m.progress);
  return (
    <Reveal as="li" index={i} className="relative pb-6 pl-12 last:pb-0" data-testid="milestone-upcoming">
      <Node icon={MILESTONE_ICON[m.kind] || FALLBACK_ICON} reached={false} />
      <div className="opacity-70">
        <div className="flex flex-wrap items-baseline justify-between gap-x-3">
          <p className="text-sm font-extrabold text-[var(--ink)]">{m.title}</p>
          <span className="text-xs font-semibold tabular-nums text-[var(--muted)]">{pct}%</span>
        </div>
        <p className="mt-0.5 text-sm font-medium text-[var(--ink-secondary)]">{m.line}</p>
        <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-[var(--grid)]" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} aria-label={`Progress to ${m.title}`}>
          <div className="h-full origin-left rounded-full bg-[var(--accent)]" style={{ transform: `scaleX(${pct / 100})` }} />
        </div>
      </div>
    </Reveal>
  );
}

export default function MilestoneTimeline({ name }) {
  const { data: fetched, error, reload } = useApi(() => api.playerMilestones(name), [name]);
  const refresh = useThrottledCallback(reload, MILESTONE_REFRESH_MS);
  useLiveMessages((type, payload) => {
    if (payload?.session?.playerName === name && (type === "session_finished" || type === "session_deleted")) refresh();
  });
  const data = fetched && fetched.playerName === name ? fetched : undefined;
  const achieved = data?.achieved || [];
  const upcoming = data?.upcoming || [];

  return (
    <Card className="overflow-hidden" id="player-milestones">
      <CardHeader title="Milestones" subtitle="What you reached and when, and what is next" />
      {!data ? (
        error ? (
          <EmptyState icon="milestone" title="Milestones will appear here" description="They load as soon as the server answers." />
        ) : (
          <div className="space-y-4 p-5" aria-hidden>
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-12 w-full" />
            ))}
          </div>
        )
      ) : !hasTimeline(data) ? (
        <EmptyState icon="milestone" title="No milestones yet" description="Your first finished fight starts the timeline." />
      ) : (
        <div className="px-5 py-5">
          {achieved.length > 0 ? (
            <ol className="relative before:absolute before:bottom-2 before:left-[0.9375rem] before:top-2 before:w-px before:bg-[var(--border)]" aria-label="Milestones reached, oldest first">
              {achieved.map((m, i) => (
                <Reached key={m.id} m={m} i={i} />
              ))}
            </ol>
          ) : (
            <div className="rounded-xl border border-dashed border-[var(--border-strong)] px-4 py-5 text-center" data-testid="milestones-none-yet">
              <p className="text-sm font-extrabold text-[var(--ink)]">No milestones yet</p>
              <p className="mt-1 text-sm font-medium text-[var(--muted)]">Your first finished fight starts the timeline.</p>
            </div>
          )}
          {upcoming.length > 0 && (
            <>
              <p className="t-kicker mb-4 mt-6">Next up</p>
              <ol className="relative before:absolute before:bottom-2 before:left-[0.9375rem] before:top-2 before:w-px before:border-l before:border-dashed before:border-[var(--border-strong)]" aria-label="Next milestones, closest first">
                {upcoming.map((m, i) => (
                  <Upcoming key={m.id} m={m} i={i} />
                ))}
              </ol>
            </>
          )}
          <p className="mt-5 text-xs font-medium text-[var(--muted)]">{data.streakRule}</p>
        </div>
      )}
    </Card>
  );
}
