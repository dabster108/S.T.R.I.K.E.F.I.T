import { useState } from "react";
import { isNum } from "../../lib/compare.js";
import { formatDuration, formatNumber, formatShortDate } from "../../lib/format.js";
import { levelName } from "../../lib/game.js";
import { Legend, LineChart } from "../charts/Charts.jsx";
import { SegmentedControl } from "../ui/Controls.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import { Panel, SIDE_A, SIDE_B } from "./shared.jsx";

// What one fight can be plotted by. `needs` is the data a side must have recorded
// for that line to mean anything; without it the side is left off, not drawn at zero.
const METRICS = {
  score: { id: "score", label: "Score", measures: "Score of each fight.", get: (f) => f.score, fmt: formatNumber },
  damage: { id: "damage", label: "Damage", measures: "Total damage dealt in each fight.", get: (f) => f.damage, fmt: formatNumber, needs: "hits" },
  hits: { id: "hits", label: "Hits", measures: "Counted hits in each fight.", get: (f) => f.hits, fmt: formatNumber, needs: "hits" },
  speed: { id: "speed", label: "Swing speed", measures: "Average swing speed (m/s) of each fight's measured hits.", get: (f) => f.speed, fmt: (v) => v.toFixed(1), needs: "speed" },
  length: { id: "length", label: "Length", measures: "How long each fight lasted.", get: (f) => (f.durationSec > 0 ? f.durationSec : null), fmt: (v) => (v > 0 ? formatDuration(v) : "0s") },
};
const MISSING = { hits: "no hits recorded", speed: "no swing data" };
const LEVEL_OPTS = [
  { id: "all", label: "Both levels" },
  { id: "Easy", label: "Level 1" },
  { id: "Medium", label: "Level 2" },
];

// A side's fights numbered 1, 2, 3… in the order played, so two calendars share one axis.
// `missing` says why a side has no line.
function plot(side, metric, level) {
  const fights = side.fights.filter((f) => level === "all" || f.level === level);
  const points =
    metric.needs === "hits" && !side.hasHits
      ? []
      : fights.map((f, i) => ({ x: i + 1, y: metric.get(f), note: `${f.who || levelName(f.level)} · ${formatShortDate(f.startedAt)}` })).filter((p) => isNum(p.y));
  const missing = !fights.length ? `no fights${level === "all" ? "" : ` on ${levelName(level)}`}` : MISSING[metric.needs] || "not recorded";
  return { points, missing };
}

/** metrics: ids from METRICS, in picker order. levelFilter adds a level picker (two-players mode). */
export default function TrendCard({ a, b, metrics = ["score", "damage", "hits", "speed"], levelFilter = false, className = "" }) {
  const [metricId, setMetricId] = useState(metrics[0]);
  const [level, setLevel] = useState("all");
  const metric = METRICS[metricId];
  const sides = [
    { ...a, key: "a", color: SIDE_A.color },
    { ...b, key: "b", color: SIDE_B.color },
  ].map((s) => ({ key: s.key, label: s.label, color: s.color, ...plot(s, metric, level) }));
  const drawn = sides.filter((s) => s.points.length);

  return (
    <Panel
      className={className}
      title="Fight by fight"
      subtitle={`${metric.measures} Each side's own fights in the order played (1st, 2nd, 3rd…), so different calendars still line up.`}
      actions={
        <>
          <SegmentedControl label="Measure" options={metrics.map((id) => METRICS[id])} value={metricId} onChange={setMetricId} />
          {levelFilter && <SegmentedControl label="Level" options={LEVEL_OPTS} value={level} onChange={setLevel} />}
        </>
      }
      note={metric.needs === "speed" ? "Only fights recorded by the live-telemetry build have swing speeds; the others are skipped, not drawn at zero." : null}
    >
      {drawn.length ? (
        <>
          <LineChart
            key={`${metricId}-${level}`}
            caption={`${metric.label} per fight, ${a.label} and ${b.label}`}
            height={260}
            series={drawn}
            xFormat={(x) => (Number.isInteger(x) ? `#${x}` : "")}
            yFormat={metric.fmt}
            exact
          />
          <Legend shape="line" items={sides.map((s) => ({ label: s.points.length ? s.label : `${s.label} · ${s.missing}`, color: s.color }))} />
        </>
      ) : (
        <EmptyState
          icon={metric.needs === "speed" ? "gauge" : "score"}
          title={`No ${metric.label.toLowerCase()} to plot for this selection`}
          description={metric.needs ? "Hits, damage and swing speed are reported by newer builds of the game; older fights only kept the score." : "Neither side has a finished fight here yet."}
        />
      )}
    </Panel>
  );
}
