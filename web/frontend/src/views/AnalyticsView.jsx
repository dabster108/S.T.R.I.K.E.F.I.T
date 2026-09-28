import { useMemo, useState } from "react";
import { api } from "../lib/api.js";
import { LEVELS, TIER_LABEL, levelName } from "../lib/game.js";
import { formatDuration, formatNumber, formatPercent } from "../lib/format.js";
import { useApi, useThrottledCallback } from "../hooks/useApi.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { Select } from "../components/ui/Controls.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import { BarChart, LEVEL_COLORS, Legend, LineChart, TIERS, TierBar } from "../components/charts/Charts.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const speed = (v) => (isNum(v) ? `${v.toFixed(1)} m/s` : "—");
const LEVEL_OPTS = [
  { id: "all", label: "Both levels" },
  { id: "Easy", label: "Level 1" },
  { id: "Medium", label: "Level 2" },
];
const PHASES = ["Start", "2nd fifth", "Middle", "4th fifth", "End"];
const th = "px-4 py-2.5 text-right text-[0.68rem] font-semibold uppercase tracking-wide text-[var(--muted)] whitespace-nowrap first:text-left";
const td = "px-4 py-3 text-right text-sm tabular-nums whitespace-nowrap first:text-left";

function ChartCard({ title, measures, children, note }) {
  return (
    <Card className="overflow-hidden">
      <CardHeader title={title} subtitle={measures} />
      <div className="px-3 pb-4 pt-4 sm:px-5">{children}</div>
      {note && <p className="border-t border-[var(--border)] px-5 py-2.5 text-[0.7rem] text-[var(--muted)]">{note}</p>}
    </Card>
  );
}

function Histogram({ data, level }) {
  const step = data.histogramStep;
  const rows = data.speedHistogram.filter((r) => r.level === level);
  if (!rows.length) return <EmptyState icon="bolt" title={`No measured swings on ${levelName(level)}`} />;
  const s = LEVELS[level]?.swing;
  const top = Math.max(...rows.map((r) => r.to), s ? s.tiers.full + step : 0);
  const buckets = [];
  for (let b = 0; b < top - 1e-9; b += step) buckets.push(Math.round(b * 100) / 100);
  return (
    <div className="min-w-0">
      <p className="mb-2 flex flex-wrap items-center gap-x-2 text-xs text-[var(--ink-secondary)]">
        <span className="h-2.5 w-2.5 rounded-sm" style={{ background: LEVEL_COLORS[level] }} aria-hidden />
        <span className="font-semibold text-[var(--ink)]">{levelName(level)}</span>
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
        <p className="text-xs text-[var(--muted)]">
          Sample: {formatNumber(data.sample.hitsWithSpeed)} hits with measured speed, from {formatNumber(data.sample.sessionsWithSpeed)} session
          {data.sample.sessionsWithSpeed === 1 ? "" : "s"} · {formatNumber(data.sample.finishedSessions)} finished sessions in total.
        </p>
      )}

      {error && !data ? (
        <Card>
          <EmptyState icon="xCircle" title="Couldn't load analytics" description={error.message} />
        </Card>
      ) : !data ? (
        <div className="grid gap-6 xl:grid-cols-2" aria-busy="true">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-64 w-full rounded-2xl" />
          ))}
        </div>
      ) : (
        <div className={`space-y-6 transition-opacity ${loading ? "opacity-60" : ""}`}>
          {!hasSpeed ? (
            <Card>
              <EmptyState icon="bolt" title="No swing-speed data for this selection yet" description="Speed, tier and timing of each hit come from the live-telemetry build of the game. The level table below still uses every finished session." />
            </Card>
          ) : (
            <>
              <ChartCard title="Swing velocity distribution" measures="How fast the controller was moving when each counted hit landed, in 0.5 m/s bands. Further right means more physical effort.">
                <div className={`grid gap-6 ${!level || level === "all" ? "xl:grid-cols-2" : ""}`}>
                  {(level !== "all" ? [level] : levels).map((l) => (
                    <Histogram key={l} data={data} level={l} />
                  ))}
                </div>
              </ChartCard>

              <div className="grid gap-6 xl:grid-cols-2">
                <ChartCard
                  title="Effort over the course of a session"
                  measures="Average swing speed (m/s) in each fifth of a finished fight. A falling line means intensity drops late in the round."
                  note={`Hits per fifth: ${(data.effortOverSession || []).filter((l) => LEVEL_COLORS[l.level]).map((l) => `${levelName(l.level)} ${l.phases.map((p) => p.hits).join(" / ")}`).join(" · ")}`}
                >
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

          <Card className="overflow-hidden">
            <CardHeader title="Level 1 vs Level 2" subtitle="Every finished session by level. Hits per minute and damage per hit use session totals, so they include fights recorded before per-hit telemetry." />
            {data.levelComparison.length ? (
              <div className="scrollbar-thin overflow-x-auto">
                <table className="w-full min-w-[820px] border-collapse">
                  <thead>
                    <tr className="border-b border-[var(--border)] bg-[var(--surface-muted)]">
                      {["Level", "Sessions", "Win rate", "Avg score", "Avg length", "Hits / min", "Dmg / hit", "Avg swing", "Full power", "Measured hits"].map((h) => (
                        <th key={h} className={th}>
                          {h}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {data.levelComparison.map((l) => (
                      <tr key={l.level} className="border-b border-[var(--border)] last:border-0">
                        <td className={`${td} font-medium`}>{levelName(l.level)}</td>
                        <td className={td}>{formatNumber(l.sessions)}</td>
                        <td className={td}>{isNum(l.winRate) ? formatPercent(l.winRate) : "—"}</td>
                        <td className={td}>{isNum(l.avgScore) ? formatNumber(l.avgScore) : "—"}</td>
                        <td className={td}>{formatDuration(l.avgSessionSec)}</td>
                        <td className={td}>{isNum(l.hitsPerMinute) ? l.hitsPerMinute.toFixed(1) : "—"}</td>
                        <td className={td}>{isNum(l.damagePerHit) ? l.damagePerHit.toFixed(1) : "—"}</td>
                        <td className={td}>{speed(l.avgSwingSpeed)}</td>
                        <td className={td}>{isNum(l.fullPowerRatio) ? formatPercent(l.fullPowerRatio * 100) : "—"}</td>
                        <td className={td}>{formatNumber(l.swingSamples)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <EmptyState icon="compare" title="No finished sessions for this selection" />
            )}
          </Card>
        </div>
      )}
    </div>
  );
}
