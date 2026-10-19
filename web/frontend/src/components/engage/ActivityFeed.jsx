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

// Each kind has its own icon and tone, so a glance down the column tells cleared levels from badges from challenges.
const TYPES = {
  level_cleared: { icon: "trophy", tone: "bg-[var(--warn-soft)] text-[var(--warn-ink)]", verb: "cleared", label: (r) => r.label },
  personal_record: { icon: "sparkles", tone: "bg-[var(--accent-soft)] text-[var(--accent-strong)]", verb: "set a new best on", label: (r) => r.label },
  goal_met: { icon: "target", tone: "bg-[var(--good-soft)] text-[var(--good-ink)]", verb: "met their daily goal", label: () => null },
  challenge_done: { icon: "check", tone: "bg-[var(--good-soft)] text-[var(--good-ink)]", verb: "completed", label: (r) => `“${r.label}”` },
  badge_unlocked: { icon: "medal", tone: "bg-[var(--warn-soft)] text-[var(--warn-ink)]", verb: "unlocked", label: (r) => r.label },
};

// "Today" / "Yesterday" / "Mon 13 Oct": rows are grouped under the day they happened.
function dayLabel(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "Earlier";
  const key = (x) => x.toDateString();
  const today = new Date();
  const yesterday = new Date(today.getTime() - 864e5);
  if (key(d) === key(today)) return "Today";
  if (key(d) === key(yesterday)) return "Yesterday";
  return d.toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" });
}

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
        <ol className="scrollbar-thin max-h-[26rem] overflow-y-auto" aria-live="polite">
          {items.map((r, i) => {
            const t = TYPES[r.type];
            const day = dayLabel(r.at);
            const label = t.label({ ...r, label: shortName(r.label, 40) });
            return (
              <li key={r.id}>
                {(i === 0 || dayLabel(items[i - 1].at) !== day) && (
                  <p className="sticky top-0 z-[1] border-b border-[var(--border)] bg-[var(--surface-muted)] px-5 py-1.5 text-[0.68rem] font-extrabold uppercase tracking-wider text-[var(--muted)]">{day}</p>
                )}
                <div className="flex items-center gap-3 border-b border-[var(--border)] px-5 py-2.5 last:border-0 hover:bg-[var(--surface-muted)]">
                  <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${t.tone}`} aria-hidden>
                    <Icon name={t.icon} className="h-4 w-4" />
                  </span>
                  <p className="min-w-0 flex-1 text-sm text-[var(--ink-secondary)]">
                    <Link to={`/dashboard/player/${encodeURIComponent(r.playerName)}`} className="press rounded font-bold text-[var(--ink)] hover:text-[var(--accent)]">
                      {shortName(r.playerName, 24)}
                    </Link>{" "}
                    <span className="font-medium">{t.verb}</span>
                    {label && <span className="font-semibold text-[var(--ink)]"> {label}</span>}
                  </p>
                  <time dateTime={r.at} className="shrink-0 text-xs font-semibold tabular-nums text-[var(--muted)]">
                    {formatRelative(r.at)}
                  </time>
                </div>
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
