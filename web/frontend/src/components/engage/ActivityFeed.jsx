import { Link } from "react-router-dom";
import { api } from "../../lib/api.js";
import { formatRelative } from "../../lib/format.js";
import { shortName } from "../../lib/engage.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";

// Recent milestones across all players, from stored data (GET /api/activity), refreshed when the stream reports a
// finished fight, a goal or a challenge. Names and event types only, at most 20 rows, newest first.

const TYPES = {
  level_cleared: { icon: "trophy", verb: (r) => `cleared ${r.label}` },
  personal_record: { icon: "sparkles", verb: (r) => `set a new best on ${r.label}` },
  goal_met: { icon: "target", verb: () => "met their daily goal" },
  challenge_done: { icon: "check", verb: (r) => `completed “${r.label}”` },
  badge_unlocked: { icon: "medal", verb: (r) => `unlocked ${r.label}` },
};

export default function ActivityFeed({ className = "", limit = 20, title = "Activity" }) {
  const { data, error, reload } = useApi(() => api.activity(limit), [limit]);
  const refresh = useThrottledCallback(reload, 3000);
  useLiveMessages((type) => ["session_finished", "goal_met", "challenge_done", "session_deleted", "player_renamed"].includes(type) && refresh());
  const items = (data?.items || []).filter((r) => TYPES[r.type]).slice(0, limit);
  return (
    <Card className={`overflow-hidden ${className}`}>
      <CardHeader title={title} subtitle="Milestones from every player, newest first" />
      {!data ? (
        error ? (
          <EmptyState icon="activity" title="Activity will appear here" description="It loads as soon as the server answers." />
        ) : (
          <div className="space-y-2 p-5" aria-hidden>
            {[0, 1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-8 w-full" />
            ))}
          </div>
        )
      ) : items.length ? (
        <ol className="scrollbar-thin max-h-[26rem] divide-y divide-[var(--border)] overflow-y-auto" aria-live="polite">
          {items.map((r) => {
            const t = TYPES[r.type];
            return (
              <li key={r.id} className="flex items-center gap-3 px-5 py-2.5">
                <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent-strong)]">
                  <Icon name={t.icon} className="h-4 w-4" />
                </span>
                <p className="min-w-0 flex-1 text-sm font-medium text-[var(--ink-secondary)]">
                  <Link to={`/dashboard/player/${encodeURIComponent(r.playerName)}`} className="press rounded font-bold text-[var(--ink)] hover:text-[var(--accent)]">
                    {shortName(r.playerName, 24)}
                  </Link>{" "}
                  {t.verb({ ...r, label: shortName(r.label, 40) })}
                </p>
                <time dateTime={r.at} className="shrink-0 text-xs font-semibold text-[var(--muted)]">
                  {formatRelative(r.at)}
                </time>
              </li>
            );
          })}
        </ol>
      ) : (
        <EmptyState icon="activity" title="No milestones yet" description="Cleared levels, new bests, goals, challenges and badges appear here as they happen." />
      )}
    </Card>
  );
}
