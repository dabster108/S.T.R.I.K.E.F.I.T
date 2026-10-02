import { Link } from "react-router-dom";
import { levelName } from "../../lib/game.js";
import { DIRECTION_TEXT, formatKcal, formatLongDate, formatMinutes, isNum } from "../../lib/fitness.js";
import { formatDuration, formatNumber } from "../../lib/format.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";
import Tooltip from "../ui/Tooltip.jsx";
import { TrendChip } from "../ui/StatTile.jsx";
import { BarChart, LEVEL_COLORS, Legend, LineChart, TIERS } from "../charts/Charts.jsx";

const ROUGH = "A rough comparison, not specific to this movement";

// "Estimated 6.6 kcal" with the walking comparison underneath. Renders nothing without an estimate.
export function CalorieEstimate({ kcal, walkingMinutes, joggingMinutes, className = "" }) {
  if (!isNum(kcal)) return null;
  return (
    <div className={className}>
      <p className="text-base font-extrabold tabular-nums text-[var(--ink)]">
        {formatKcal(kcal)} <span className="text-xs font-semibold text-[var(--muted)]">estimated</span>
      </p>
      <WorkoutEquivalent walkingMinutes={walkingMinutes} joggingMinutes={joggingMinutes} />
    </div>
  );
}

export function WorkoutEquivalent({ walkingMinutes, joggingMinutes }) {
  if (!isNum(walkingMinutes)) return null;
  return (
    <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">
      <Tooltip label={ROUGH}>
        <span tabIndex={0} className="inline-flex cursor-help items-center gap-1 rounded">
          roughly equivalent to {formatMinutes(walkingMinutes)} of brisk walking
          {isNum(joggingMinutes) && <> or {formatMinutes(joggingMinutes)} of jogging</>}
          <Icon name="info" className="h-3 w-3" />
        </span>
      </Tooltip>
    </p>
  );
}

export function EstimateNote({ children }) {
  return (
    <p className="flex items-start gap-2 px-5 pb-4 text-xs font-medium text-[var(--muted)]">
      <Icon name="info" className="mt-px h-3.5 w-3.5 shrink-0" />
      <span>{children}</span>
    </p>
  );
}

const pctText = (v) => `${Math.round(v)}%`;

// Share of swings in each tier, with raw counts in the labels. `breakdown` is { counts, percent, total }.
export function SwingTierChart({ breakdown, caption = "Swings by strength" }) {
  if (!breakdown?.total) return <EmptyState icon="gauge" title="No swing strengths recorded" description="Swing tiers come from the live-telemetry build; this session has none." />;
  return (
    <div>
      <BarChart
        caption={caption}
        categories={TIERS.map((t) => `${t.label} (${breakdown.counts[t.key]})`)}
        series={[{ key: "pct", label: "Share of swings", colors: TIERS.map((t) => t.color), values: TIERS.map((t) => breakdown.percent[t.key] ?? 0) }]}
        yFormat={pctText}
        showValues
        height={220}
      />
      <p className="mt-2 text-xs font-medium text-[var(--muted)]">
        {formatNumber(breakdown.total)} swings counted · average strength {breakdown.avgTierWeight?.toFixed(1)} on a 1 (tap) to 4 (full power) scale
      </p>
    </div>
  );
}

const SHORT = { 1: "Tap", 2: "Light", 3: "Med.", 4: "Full" };
const tierTick = (v) => (Number.isInteger(v) ? SHORT[v] || "" : v.toFixed(1));

// Average swing strength in equal slices of the fight. `trend` is the backend's exertionTrend.
export function ExertionChart({ trend, caption = "Effort over the course of the fight" }) {
  const points = (trend?.buckets || []).filter((b) => isNum(b.avgTierWeight)).map((b) => ({ x: (b.fromPct + b.toPct) / 2, y: b.avgTierWeight, note: `${b.swings ?? b.sessions} ${b.swings !== undefined ? "swings" : "sessions"}` }));
  if (points.length < 2) return <EmptyState icon="activity" title="Not enough timed swings" description="This chart needs swings landing in at least two parts of the fight." />;
  return (
    <div>
      <LineChart
        caption={`${caption} (x axis: % of the fight elapsed)`}
        height={240}
        yDomain={[1, 4]}
        series={[{ key: "effort", label: "Average swing strength", color: "var(--accent)", points }]}
        xTicks={points.map((p) => p.x)}
        xFormat={(x) => `${x - 10}–${x + 10}`}
        yFormat={tierTick}
      />
      {trend.direction ? (
        <p className="mt-2 text-xs font-semibold text-[var(--ink-secondary)]">
          {DIRECTION_TEXT[trend.direction]} <span className="font-medium text-[var(--muted)]">{trend.directionNote}</span>
        </p>
      ) : (
        trend.directionNote && <p className="mt-2 text-xs font-medium text-[var(--muted)]">{trend.directionNote}</p>
      )}
    </div>
  );
}

// Grouped bars: for each swing strength, the share of swings on each level.
export function LevelIntensityChart({ levels }) {
  const withData = (levels || []).filter((l) => l.swings > 0);
  const without = (levels || []).filter((l) => l.swings === 0);
  const note = without.length ? `No swing data recorded yet for ${without.map((l) => levelName(l.level)).join(", ")}.` : null;
  if (!withData.length) return <EmptyState icon="layers" title="No swing data on any level yet" description={note || undefined} />;
  return (
    <div>
      <BarChart
        caption="Share of swings by strength, per level"
        categories={TIERS.map((t) => t.label)}
        series={withData.map((l) => ({ key: l.level, label: levelName(l.level), color: LEVEL_COLORS[l.level] || "var(--accent)", values: TIERS.map((t) => l.percent[t.key] ?? 0) }))}
        yFormat={pctText}
        height={230}
      />
      <Legend items={withData.map((l) => ({ label: levelName(l.level), color: LEVEL_COLORS[l.level] || "var(--accent)" }))} />
      <ul className="mt-3 divide-y divide-[var(--border)] border-y border-[var(--border)] text-xs">
        {(levels || []).map((l) => (
          <li key={l.level} className="flex flex-wrap items-center justify-between gap-2 py-2">
            <span className="inline-flex items-center gap-1.5 font-bold text-[var(--ink)]">
              <span className="h-2.5 w-2.5 rounded-sm" style={{ background: LEVEL_COLORS[l.level] }} aria-hidden />
              {levelName(l.level)}
            </span>
            <span className="font-semibold tabular-nums text-[var(--ink-secondary)]">
              {l.swings > 0 ? `average strength ${l.avgTierWeight.toFixed(2)} of 4 · ${formatNumber(l.swings)} swings in ${l.sessionsWithSwingData} session${l.sessionsWithSwingData === 1 ? "" : "s"}` : "no swings recorded"}
            </span>
          </li>
        ))}
      </ul>
      {(note || withData.length < 2) && <p className="mt-2 text-xs font-medium text-[var(--muted)]">{note || "Only one level has swing data, so there is nothing to compare yet."}</p>}
    </div>
  );
}

const BEST_ROWS = [
  ["highestSwingVelocity", "Fastest single swing", (b) => `${b.value.toFixed(1)} m/s`],
  ["highestFullSwingPercent", "Most full-power swings in a session", (b) => `${Math.round(b.value)}%`],
  ["highestEstimatedCalories", "Most calories in a session (estimate)", (b) => formatKcal(b.value)],
  ["longestSession", "Longest session", (b) => formatDuration(b.value)],
  ["highestScore", "Highest score", (b) => formatNumber(b.value)],
];

export function PersonalBests({ bests }) {
  const rows = BEST_ROWS.filter(([k]) => bests?.[k]);
  return (
    <Card className="overflow-hidden">
      <CardHeader title="Personal bests" subtitle="Each record links to the session it came from" />
      {rows.length ? (
        <ul className="grid gap-px bg-[var(--border)] sm:grid-cols-2 xl:grid-cols-5">
          {rows.map(([key, label, fmt]) => {
            const b = bests[key];
            return (
              <li key={key} className="bg-[var(--surface)] px-5 py-4">
                <p className="t-kicker">{label}</p>
                <p className="mt-2 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{fmt(b)}</p>
                {key === "highestEstimatedCalories" && <WorkoutEquivalent walkingMinutes={b.estimatedWalkingMinutes} joggingMinutes={b.estimatedJoggingMinutes} />}
                {key === "highestFullSwingPercent" && <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">of {b.swingsInSession} swings</p>}
                <Link to={`/dashboard/session/${encodeURIComponent(b.sessionId)}`} className="press mt-2 inline-flex items-center gap-1 rounded text-xs font-semibold text-[var(--accent)] hover:underline">
                  {formatLongDate(b.date)} · {levelName(b.level)}
                  <Icon name="arrowRight" className="h-3 w-3" />
                </Link>
              </li>
            );
          })}
        </ul>
      ) : (
        <EmptyState icon="trophy" title="No records yet" description="Records appear once a session with swings has been finished." />
      )}
    </Card>
  );
}

// Latest session against the player's earlier average: real numbers beside the arrow.
export function VsAverage({ progress }) {
  if (!progress) return null;
  const rows = [
    ["Score", progress.score, (v) => formatNumber(v), 0],
    ["Swing strength", progress.swingIntensity, (v) => `${v.toFixed(2)}`, 2],
    ["Calories (est.)", progress.estimatedCalories, (v) => formatKcal(v), 1],
  ].filter(([, c]) => c);
  if (!rows.length) return null;
  return (
    <div className="border-t border-[var(--border)] px-5 py-4">
      <p className="t-kicker">Latest session vs your average</p>
      <ul className="mt-3 flex flex-wrap gap-x-8 gap-y-3">
        {rows.map(([label, c, fmt, digits]) => (
          <li key={label}>
            <p className="text-xs font-semibold text-[var(--muted)]">{label}</p>
            <p className="mt-0.5 flex flex-wrap items-center gap-2">
              <span className="text-lg font-extrabold tabular-nums text-[var(--ink)]">{fmt(c.latest)}</span>
              <TrendChip value={c.difference} label={Math.abs(c.difference).toFixed(digits)} suffix="vs avg" />
            </p>
            <p className="text-xs font-medium text-[var(--muted)]">your average over {c.sessionsInAverage} earlier session{c.sessionsInAverage === 1 ? "" : "s"}: {fmt(c.average)}</p>
          </li>
        ))}
      </ul>
      <p className="mt-3 text-xs font-medium text-[var(--muted)]">{progress.note}</p>
    </div>
  );
}
