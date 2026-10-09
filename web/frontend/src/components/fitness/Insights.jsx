import { useState } from "react";
import { api } from "../../lib/api.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

const ICONS = { effort_trend: "activity", full_share: "bolt", best_worst: "layers", guard: "shield" };
const TITLES = { effort_trend: "Effort over a fight", full_share: "Full-power share", best_worst: "Best and hardest level", guard: "Guards and openings" };

// What the data behind each sentence was (shown under it, so no sentence stands without its numbers).
function basis(i) {
  const d = i.data || {};
  if (i.id === "effort_trend") return `${d.fights} fights, ${d.hits} hits; mean speed per third ${d.perThird.map((v) => `${v} m/s`).join(" / ")}`;
  if (i.id === "full_share") return `${d.full} full-power hits of ${d.tiered} with a strength tier`;
  if (i.id === "best_worst") return `win rate over won and lost fights per level`;
  if (i.id === "guard") return `${d.hits} hits in ${d.fights} fights with guard telemetry; ${d.blocked} blocked`;
  return "";
}

function have(p) {
  const h = p.have || {};
  if (p.id === "effort_trend") return `${h.fights ?? 0} fights qualify, ${h.hits ?? 0} hits`;
  if (p.id === "full_share") return `${h.tieredHits ?? 0} tiered hits`;
  if (p.id === "best_worst") return h.sameWinRate ? "win rates are equal" : `${h.levelsWithEnough ?? 0} level(s) qualify`;
  if (p.id === "guard") return `${h.hits ?? 0} hits in ${h.fights ?? 0} fights with guard telemetry`;
  return "";
}

// Plain-language insights (GET /api/players/:name/insights): only sentences with enough data behind them.
export function InsightsCard({ name }) {
  const { data, error, reload } = useApi(() => api.playerInsights(name), [name]);
  const refresh = useThrottledCallback(reload, 4000);
  useLiveMessages((type, payload) => {
    if (type === "session_finished" && payload?.session?.playerName === name) refresh();
  });
  const [showPending, setShowPending] = useState(false);

  if (!data) {
    return error ? (
      <Card>
        <EmptyState icon="info" title="Insights will appear here" description="They load as soon as the server answers." />
      </Card>
    ) : (
      <Skeleton className="h-40 w-full" />
    );
  }
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="What the data says"
        subtitle="A sentence appears only once there is enough data behind it; the minimum is shown with each one"
        actions={
          data.pending.length > 0 && (
            <Button icon={showPending ? "chevronUp" : "chevronDown"} onClick={() => setShowPending((v) => !v)} aria-expanded={showPending}>
              {showPending ? "Hide" : "Show"} what's missing ({data.pending.length})
            </Button>
          )
        }
      />
      {data.insights.length ? (
        <ul className="stagger divide-y divide-[var(--border)]">
          {data.insights.map((i) => (
            <li key={i.id} className="flex gap-3 px-5 py-4">
              <span className="mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent-strong)]">
                <Icon name={ICONS[i.id] || "info"} className="h-4 w-4" />
              </span>
              <div className="min-w-0">
                <p className="text-xs font-semibold uppercase tracking-wide text-[var(--muted)]">{TITLES[i.id] || i.id}</p>
                <p className="mt-0.5 text-sm font-bold leading-relaxed text-[var(--ink)]">{i.text}</p>
                <p className="mt-1 text-xs font-medium text-[var(--muted)]">
                  Based on {basis(i)}. Minimum: {i.minimum}.
                </p>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p className="px-5 py-5 text-sm font-medium text-[var(--muted)]">Not enough data for any insight yet. Each one needs a few more fights; open "what's missing" to see how many.</p>
      )}
      {showPending && data.pending.length > 0 && (
        <ul className="border-t border-[var(--border)] bg-[var(--surface-muted)] px-5 py-3 text-xs font-medium text-[var(--ink-secondary)]">
          {data.pending.map((p) => (
            <li key={p.id} className="py-1">
              <span className="font-bold text-[var(--ink)]">{TITLES[p.id] || p.id}:</span> needs {p.minimum}; so far {have(p)}.
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
