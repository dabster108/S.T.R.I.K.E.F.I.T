import { useMemo, useState } from "react";
import { api } from "../lib/api.js";
import { LEVELS, TIER_LABEL, levelName } from "../lib/game.js";
import { formatDuration, formatNumber, formatPercent } from "../lib/format.js";
import { useApi, useThrottledCallback } from "../hooks/useApi.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { AnalyticsSkeleton } from "../components/ui/Skeletons.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import Icon from "../components/ui/Icon.jsx";
import { BarChart, LEVEL_COLORS, Legend, LineChart, TIERS, TierBar } from "../components/charts/Charts.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const speed = (v) => (isNum(v) ? `${v.toFixed(1)} m/s` : "—");
const LEVEL_OPTS = [
  { id: "all", label: "Both levels" },
  { id: "Easy", label: "Level 1" },
  { id: "Medium", label: "Level 2" },
];
const PHASES = ["Start", "2nd fifth", "Middle", "4th fifth", "End"];

// Light to dark along the existing blue scale; white text from --accent (5.2:1) onward.
const HEAT = ["var(--accent-soft)", "var(--blue-100)", "var(--accent-ring)", "var(--blue-300)", "var(--blue-400)", "var(--accent)", "var(--accent-strong)", "var(--blue-800)", "var(--blue-900)"];
const HEAT_METRICS = [
  { id: "speed", label: "Swing speed", get: (p) => p.avgSwingSpeed, fmt: (v) => v.toFixed(1), unit: "m/s avg" },
  { id: "full", label: "Full power", get: (p) => (isNum(p.fullPowerRatio) ? p.fullPowerRatio * 100 : null), fmt: (v) => `${Math.round(v)}%`, unit: "of hits" },
  { id: "hits", label: "Hits", get: (p) => (p.hits ? p.hits : null), fmt: (v) => formatNumber(v), unit: "counted" },
];

function ChartCard({ title, measures, children, note, className = "" }) {
  return (
    <Card className={`overflow-hidden ${className}`}>
      <CardHeader title={title} subtitle={measures} />
      <div className="px-3 pb-4 pt-4 sm:px-5">{children}</div>
      {note && <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)]">{note}</p>}
    </Card>
  );
}

// Levels × five slices of round time, coloured by the chosen measure.
function Heatmap({ effort }) {
  const [metricId, setMetricId] = useState("speed");
  const [focus, setFocus] = useState(null);
  const metric = HEAT_METRICS.find((m) => m.id === metricId);
  const rows = effort.filter((l) => LEVEL_COLORS[l.level]);
  const values = rows.flatMap((l) => l.phases.map(metric.get)).filter(isNum);
  if (!values.length) return <EmptyState icon="gauge" title="Needs finished fights with timing data" description="Each hit's time in the round comes from the live-telemetry build." />;
  const lo = Math.min(...values);
  const hi = Math.max(...values);
  const shade = (v) => (hi === lo ? 5 : Math.round(((v - lo) / (hi - lo)) * (HEAT.length - 1)));
  const detail = focus && rows.find((l) => l.level === focus.level)?.phases[focus.phase];

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <SegmentedControl label="Heatmap measure" options={HEAT_METRICS} value={metricId} onChange={setMetricId} />
        <div className="flex items-center gap-2 text-xs font-semibold text-[var(--muted)]" aria-hidden>
          <span className="tabular-nums">{metric.fmt(lo)}</span>
          <span className="flex h-2.5 w-28 overflow-hidden rounded-full">
            {HEAT.map((c) => (
              <span key={c} className="flex-1" style={{ background: c }} />
            ))}
          </span>
          <span className="tabular-nums">{metric.fmt(hi)}</span>
        </div>
      </div>

      <div className="scrollbar-thin overflow-x-auto">
        <div className="grid min-w-[520px] grid-cols-[6.5rem_repeat(5,minmax(0,1fr))] gap-1.5" onMouseLeave={() => setFocus(null)}>
          <span />
          {PHASES.map((p, i) => (
            <span key={p} className="pb-1 text-center text-xs font-bold text-[var(--ink-secondary)]">
              {p}
              <span className="block font-medium text-[var(--muted)]">
                {i * 20}–{i * 20 + 20}%
              </span>
            </span>
          ))}
          {rows.map((l, r) => (
            <div key={`${l.level}-${metricId}`} className="contents">
              <span className="flex items-center gap-2 text-sm font-bold text-[var(--ink)]">
                <LevelBadge level={l.level} />
                <span className="truncate">{levelName(l.level).replace("Level ", "L")}</span>
              </span>
              {l.phases.map((p) => {
                const v = metric.get(p);
                const s = isNum(v) ? shade(v) : null;
                const on = focus?.level === l.level && focus?.phase === p.phase;
                return (
                  <button
                    key={p.phase}
                    type="button"
                    onMouseEnter={() => setFocus({ level: l.level, phase: p.phase })}
                    onFocus={() => setFocus({ level: l.level, phase: p.phase })}
                    onBlur={() => setFocus(null)}
                    aria-label={`${levelName(l.level)}, ${PHASES[p.phase]} of the round: ${isNum(v) ? `${metric.fmt(v)} ${metric.unit}` : "no data"}, ${p.hits} hits`}
                    className={`cell-in press flex h-16 flex-col items-center justify-center rounded-xl text-center ring-offset-2 transition-shadow ${on ? "ring-2 ring-[var(--ink)]" : ""} ${
                      s === null ? "border border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)]" : ""
                    }`}
                    style={{ "--i": r * 5 + p.phase, background: s === null ? undefined : HEAT[s] }}
                  >
                    <span className={`text-base font-extrabold tabular-nums ${s === null ? "text-[var(--muted)]" : s >= 5 ? "text-white" : "text-[var(--blue-900)]"}`}>
                      {isNum(v) ? metric.fmt(v) : "—"}
                    </span>
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      </div>

      <p className="mt-4 flex min-h-[1.25rem] items-center gap-2 text-xs font-semibold text-[var(--ink-secondary)]" aria-live="polite">
        <Icon name="info" className="h-3.5 w-3.5 shrink-0 text-[var(--muted)]" />
        {detail
          ? `${levelName(focus.level)} · ${PHASES[focus.phase]} (${detail.label} of round time): ${formatNumber(detail.hits)} hits from ${formatNumber(detail.sessions)} session${detail.sessions === 1 ? "" : "s"}, avg ${speed(detail.avgSwingSpeed)}, ${
              isNum(detail.fullPowerRatio) ? `${Math.round(detail.fullPowerRatio * 100)}% full power` : "no tier data"
            }`
          : "Hover or focus a cell for its sample size. Darker blue means a higher value; colours are scaled to this grid."}
      </p>
    </div>
  );
}

function Histogram({ data, level }) {
  const step = data.histogramStep;
  const rows = data.speedHistogram.filter((r) => r.level === level);
  if (!rows.length) return <EmptyState icon="gauge" title={`No measured swings on ${levelName(level)}`} />;
  const s = LEVELS[level]?.swing;
  const top = Math.max(...rows.map((r) => r.to), s ? s.tiers.full + step : 0);
  const buckets = [];
  for (let b = 0; b < top - 1e-9; b += step) buckets.push(Math.round(b * 100) / 100);
  return (
    <div className="min-w-0">
      <p className="mb-2 flex flex-wrap items-center gap-x-2 text-xs font-medium text-[var(--ink-secondary)]">
        <span className="h-2.5 w-2.5 rounded-sm" style={{ background: LEVEL_COLORS[level] }} aria-hidden />
        <span className="font-bold text-[var(--ink)]">{levelName(level)}</span>
        {s && <span className="text-[var(--muted)]">no hit below {s.min} · full power from {s.tiers.full} m/s</span>}
      </p>
      <BarChart
        caption={`Swing speed distribution, ${levelName(level)}`}
        height={180}
        categories={buckets.map((b) => b.toFixed(1))}
        series={[{ key: "n", label: "Hits", color: LEVEL_COLORS[level], values: buckets.map((b) => rows.find((r) => Math.abs(r.from - b) < 1e-6)?.count || 0) }]}
        yFormat={(v) => formatNumber(v)}
        tipTitle={(c) => `${c}–${(Number(c) + step).toFixed(1)} m/s`}
      />
    </div>
  );
}

function LevelCard({ l }) {
  const facts = [
    ["Sessions", formatNumber(l.sessions)],
    ["Win rate", isNum(l.winRate) ? formatPercent(l.winRate) : "—"],
    ["Avg score", isNum(l.avgScore) ? formatNumber(l.avgScore) : "—"],
    ["Avg length", formatDuration(l.avgSessionSec)],
    ["Hits / min", isNum(l.hitsPerMinute) ? l.hitsPerMinute.toFixed(1) : "—"],
    ["Damage / hit", isNum(l.damagePerHit) ? l.damagePerHit.toFixed(1) : "—"],
    ["Avg swing", speed(l.avgSwingSpeed)],
    ["Full power", isNum(l.fullPowerRatio) ? formatPercent(l.fullPowerRatio * 100) : "—"],
  ];
  return (
    <article className="lift rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5">
      <header className="flex items-center justify-between gap-3">
        <span className="flex items-center gap-2 text-base font-extrabold text-[var(--ink)]">
          <span className="h-3 w-3 rounded-full" style={{ background: LEVEL_COLORS[l.level] || "var(--border-strong)" }} aria-hidden />
          {levelName(l.level)}
        </span>
        <span className="text-xs font-semibold text-[var(--muted)]">{formatNumber(l.swingSamples)} measured hits</span>
      </header>
      <dl className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
        {facts.map(([k, v]) => (
          <div key={k} className="rounded-xl bg-[var(--surface-muted)] px-3 py-2 ring-1 ring-inset ring-[var(--border)]">
            <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
            <dd className="mt-0.5 text-sm font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
          </div>
        ))}
      </dl>
    </article>
  );
}

export default function AnalyticsView() {
  const [level, setLevel] = useState("all");
  const [player, setPlayer] = useState("all");
  const players = useApi(() => api.players(), []);
  const { data, error, loading, reload } = useApi(() => api.analytics({ level, player }), [level, player]);
  const refresh = useThrottledCallback(reload, 6000);
  useLiveMessages((type) => type === "session_finished" && refresh());

  const playerOpts = useMemo(() => [{ id: "all", label: "Everyone" }, ...(players.data || []).map((p) => ({ id: p.playerName, label: p.playerName }))], [players.data]);
  const levels = (data?.levelComparison || []).map((l) => l.level).filter((l) => LEVEL_COLORS[l]);
  const effort = (data?.effortOverSession || [])
    .filter((l) => LEVEL_COLORS[l.level] && l.phases.some((p) => isNum(p.avgSwingSpeed)))
    .map((l) => ({ key: l.level, label: levelName(l.level), color: LEVEL_COLORS[l.level], points: l.phases.filter((p) => isNum(p.avgSwingSpeed)).map((p) => ({ x: p.phase, y: p.avgSwingSpeed })) }));
  const tierCounts = useMemo(() => {
    const out = { tap: 0, light: 0, medium: 0, full: 0 };
    for (const l of data?.tiers || []) for (const k in out) out[k] += l.counts[k] || 0;
    return out;
  }, [data]);
  const hasSpeed = data?.sample.hitsWithSpeed > 0;

  if (!data && !error) return <AnalyticsSkeleton />;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Research"
        title="Analytics"
        description="The thesis view: how hard people swing, whether effort holds up through a fight, and how the two levels differ."
        actions={
          <>
            <Select label="Level" value={level} onChange={setLevel} options={LEVEL_OPTS} />
            <Select label="Player" value={player} onChange={setPlayer} options={playerOpts} />
          </>
        }
      />
      {data && (
        <p className="-mt-2 flex items-center gap-2 text-xs font-semibold text-[var(--muted)]">
          <Icon name="database" className="h-3.5 w-3.5" />
          Sample: {formatNumber(data.sample.hitsWithSpeed)} hits with measured speed, from {formatNumber(data.sample.sessionsWithSpeed)} session
          {data.sample.sessionsWithSpeed === 1 ? "" : "s"} · {formatNumber(data.sample.finishedSessions)} finished sessions in total.
        </p>
      )}

      {error && !data ? (
        <Card>
          <EmptyState icon="analytics" title="Analytics will appear here" description="They load as soon as the server answers." />
        </Card>
      ) : (
        <div className={`space-y-6 transition-opacity duration-300 ${loading ? "opacity-60" : ""}`}>
          {!hasSpeed ? (
            <Card>
              <EmptyState
                icon="gauge"
                title="No swing-speed data for this selection yet"
                description="Speed, tier and timing of each hit come from the live-telemetry build of the game. The level cards below still use every finished session."
              />
            </Card>
          ) : (
            <>
              <ChartCard
                title="Swing intensity through the round"
                measures="Each finished fight is cut into five equal slices of round time. A cell shows what happened in that slice across every fight on that level."
              >
                <Heatmap effort={data.effortOverSession || []} />
              </ChartCard>

              <ChartCard title="Swing velocity distribution" measures="How fast the controller was moving when each counted hit landed, in 0.5 m/s bands. Further right means more physical effort.">
                <div className={`grid gap-6 ${!level || level === "all" ? "xl:grid-cols-2" : ""}`}>
                  {(level !== "all" ? [level] : levels).map((l) => (
                    <Histogram key={l} data={data} level={l} />
                  ))}
                </div>
              </ChartCard>

              <div className="grid gap-6 xl:grid-cols-2">
                <ChartCard title="Effort over the course of a session" measures="Average swing speed (m/s) in each fifth of a finished fight. A falling line means intensity drops late in the round.">
                  {effort.length ? (
                    <>
                      <LineChart caption="Average swing speed by fifth of the session" series={effort} xTicks={[0, 1, 2, 3, 4]} xFormat={(x) => PHASES[x] ?? ""} yFormat={(v) => v.toFixed(1)} markers />
                      <Legend items={effort} shape="line" />
                    </>
                  ) : (
                    <EmptyState icon="clock" title="Needs finished fights with timing data" />
                  )}
                </ChartCard>

                <ChartCard
                  title="Damage per swing"
                  measures="Average damage of one hit in each swing tier: what the game pays for putting more speed into a swing."
                  note={`Sample: ${data.damagePerSwing.map((d) => `${TIER_LABEL[d.tier]} ${formatNumber(d.hits)}`).join(" · ")} hits`}
                >
                  <BarChart
                    caption="Average damage per hit by swing tier"
                    categories={data.damagePerSwing.map((d) => TIER_LABEL[d.tier])}
                    series={[{ key: "d", label: "Avg damage", colors: TIERS.map((t) => t.color), values: data.damagePerSwing.map((d) => d.avgDamage) }]}
                    yFormat={(v) => v.toFixed(1)}
                    showValues
                  />
                </ChartCard>
              </div>

              <ChartCard title="Swing tiers" measures="Share of counted hits in each tier. Tap is the slowest swing that still counts; full power is the fastest.">
                <div className="px-2">
                  <TierBar counts={tierCounts} />
                </div>
              </ChartCard>
            </>
          )}

          <section aria-labelledby="levels-title" className="space-y-3">
            <div>
              <h2 id="levels-title" className="text-[0.95rem] font-bold tracking-tight text-[var(--ink)]">
                Level 1 vs Level 2
              </h2>
              <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">
                Every finished session by level. Hits per minute and damage per hit use session totals, so they include fights recorded before per-hit telemetry.
              </p>
            </div>
            {data.levelComparison.length ? (
              <div className="stagger grid gap-4 xl:grid-cols-2">
                {data.levelComparison.map((l) => (
                  <LevelCard key={l.level} l={l} />
                ))}
              </div>
            ) : (
              <Card>
                <EmptyState icon="compare" title="No finished sessions for this selection" />
              </Card>
            )}
          </section>
        </div>
      )}
    </div>
  );
}
