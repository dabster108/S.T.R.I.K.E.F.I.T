import { Link } from "react-router-dom";
import { api } from "../../lib/api.js";
import { levelName } from "../../lib/game.js";
import { formatKcal, formatLongDate, isNum, shortDay } from "../../lib/fitness.js";
import { formatNumber } from "../../lib/format.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";
import Tooltip from "../ui/Tooltip.jsx";
import { BarChart, LEVEL_COLORS } from "../charts/Charts.jsx";
import { EstimateNote, WorkoutEquivalent } from "./Fitness.jsx";

// Site-wide calorie burn. Every figure comes from GET /api/insights/calories, which sums the
// per-session estimates from web/backend/fitness.js; nothing is calculated here.
export default function CalorieInsights() {
  const { data, error, reload } = useApi(() => api.calorieInsights(), []);
  const refresh = useThrottledCallback(reload, 4000);
  useLiveMessages((type) => (type === "session_finished" || type === "session_deleted") && refresh());

  if (error && !data) {
    return (
      <Card>
        <EmptyState icon="activity" title="Calorie insights will appear here" description="They load as soon as the server answers." />
      </Card>
    );
  }
  if (!data) {
    return (
      <div className="grid gap-6 xl:grid-cols-12" aria-hidden>
        <div className="skeleton h-56 rounded-2xl xl:col-span-12" />
      </div>
    );
  }

  const days = data.caloriesPerDay;
  const hasDays = days.some((d) => isNum(d.estimatedCalories));
  const levels = data.byLevel;
  const levelsWithData = levels.filter((l) => !l.insufficientData);
  const noData = levels.filter((l) => l.insufficientData);
  const levelColor = (l) => LEVEL_COLORS[l] || "var(--accent)";

  return (
    <div className="space-y-6">
      <Card className="overflow-hidden">
        <div className="grid gap-px bg-[var(--border)] md:grid-cols-[1.6fr_1fr_1fr]">
          <div className="bg-[var(--surface)] px-5 py-5 sm:px-6">
            <p className="t-kicker">
              <Tooltip label={data.estimate.note || "Estimate, not a measurement"} side="bottom">
                <span tabIndex={0} className="inline-flex cursor-help items-center gap-1 rounded">
                  Total estimated calories burned (estimated)
                  <Icon name="info" className="h-3 w-3" />
                </span>
              </Tooltip>
            </p>
            <p className="t-figure-md mt-2 text-[var(--ink)]">{isNum(data.totalEstimatedCalories) ? formatKcal(data.totalEstimatedCalories) : "—"}</p>
            <WorkoutEquivalent walkingMinutes={data.estimatedWalkingMinutes} joggingMinutes={data.estimatedJoggingMinutes} />
          </div>
          <div className="bg-[var(--surface)] px-5 py-5 sm:px-6">
            <p className="t-kicker">Average per session (est.)</p>
            <p className="mt-2 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{formatKcal(data.avgCaloriesPerSession)}</p>
            <p className="mt-1 text-xs font-semibold text-[var(--muted)]">across {formatNumber(data.sessionsEstimated)} session{data.sessionsEstimated === 1 ? "" : "s"}</p>
          </div>
          <div className="bg-[var(--surface)] px-5 py-5 sm:px-6">
            <p className="t-kicker">Sessions left out</p>
            <p className="mt-2 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{formatNumber(data.sessionsExcluded)}</p>
            <p className="mt-1 text-xs font-semibold text-[var(--muted)]">of {formatNumber(data.sessionsConsidered)} · no swings recorded</p>
          </div>
        </div>
        <EstimateNote>
          {data.excludedNote} {data.estimate.note}
        </EstimateNote>
      </Card>

      <div className="grid gap-6 xl:grid-cols-12">
        <Card className="overflow-hidden xl:col-span-7">
          <CardHeader title="Calories per day (estimated)" subtitle="All players, last 30 days" />
          <div className="px-3 pb-4 pt-4 sm:px-5">
            {hasDays ? (
              <BarChart
                caption="Estimated calories burned per day across all players, last 30 days"
                categories={days.map((d) => shortDay(d.day))}
                series={[{ key: "kcal", label: "Estimated kcal", color: "var(--accent)", values: days.map((d) => d.estimatedCalories ?? 0) }]}
                yFormat={(v) => formatNumber(v)}
              />
            ) : (
              <EmptyState icon="calendar" title="No estimated burns in the last 30 days" />
            )}
          </div>
        </Card>

        <Card className="overflow-hidden xl:col-span-5">
          <CardHeader title="Per level (estimated)" subtitle="Average estimated calories per session" />
          <div className="px-3 pb-4 pt-4 sm:px-5">
            {levelsWithData.length ? (
              <BarChart
                caption="Average estimated calories per session, per level"
                categories={levelsWithData.map((l) => levelName(l.level))}
                series={[{ key: "avg", label: "Avg est. kcal per session", colors: levelsWithData.map((l) => levelColor(l.level)), values: levelsWithData.map((l) => l.avgCaloriesPerSession ?? 0) }]}
                yFormat={(v) => formatNumber(v)}
                showValues
                height={200}
              />
            ) : (
              <EmptyState icon="layers" title="Not enough data yet" description="No level has recorded swings yet." />
            )}
            <ul className="mt-3 divide-y divide-[var(--border)] border-y border-[var(--border)] text-xs">
              {levels.map((l) => (
                <li key={l.level} className="flex flex-wrap items-center justify-between gap-2 py-2">
                  <span className="inline-flex items-center gap-1.5 font-bold text-[var(--ink)]">
                    <span className="h-2.5 w-2.5 rounded-sm" style={{ background: levelColor(l.level) }} aria-hidden />
                    {levelName(l.level)}
                  </span>
                  <span className="font-semibold tabular-nums text-[var(--ink-secondary)]">
                    {l.insufficientData
                      ? `not enough data yet (${l.sessions} session${l.sessions === 1 ? "" : "s"}, none with swings)`
                      : `${formatKcal(l.totalEstimatedCalories)} total · ${l.sessionsEstimated} session${l.sessionsEstimated === 1 ? "" : "s"}`}
                  </span>
                </li>
              ))}
            </ul>
            {noData.length > 0 && <p className="mt-2 text-xs font-medium text-[var(--muted)]">No swing data recorded yet for {noData.map((l) => levelName(l.level)).join(", ")}, so no calorie figure is shown for {noData.length === 1 ? "it" : "them"}.</p>}
          </div>
        </Card>
      </div>

      <Card className="overflow-hidden">
        <CardHeader title="Top burns" subtitle="Highest estimated calories in a single session, all players" />
        {data.topBurns.length ? (
          <ol className="divide-y divide-[var(--border)]">
            {data.topBurns.map((b, i) => (
              <li key={b.sessionId}>
                <Link to={`/dashboard/session/${encodeURIComponent(b.sessionId)}`} className="press flex flex-wrap items-center justify-between gap-3 px-5 py-3 hover:bg-[var(--accent-soft)]">
                  <span className="flex items-center gap-3">
                    <span className="w-5 text-sm font-extrabold tabular-nums text-[var(--muted)]">{i + 1}</span>
                    <span>
                      <span className="block text-sm font-bold text-[var(--ink)]">{b.playerName}</span>
                      <span className="block text-xs font-medium text-[var(--muted)]">
                        {formatLongDate(b.date)} · {levelName(b.level)}
                      </span>
                    </span>
                  </span>
                  <span className="text-base font-extrabold tabular-nums text-[var(--ink)]">
                    {formatKcal(b.estimatedCalories)} <span className="text-xs font-semibold text-[var(--muted)]">estimated</span>
                  </span>
                </Link>
              </li>
            ))}
          </ol>
        ) : (
          <EmptyState icon="trophy" title="No estimated burns yet" description="Sessions with recorded swings appear here." />
        )}
      </Card>
    </div>
  );
}
