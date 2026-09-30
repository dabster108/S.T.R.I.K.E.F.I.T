import { isNum } from "../../lib/compare.js";
import { formatNumber } from "../../lib/format.js";
import { Legend, LineChart } from "../charts/Charts.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import { Panel, SIDE_A, SIDE_B } from "./shared.jsx";

const PHASES = ["Start", "2nd fifth", "Middle", "4th fifth", "End"];

/**
 * Levels mode: average swing speed in each fifth of a finished fight, one line per level.
 * a, b: { label, phases } where phases is that level's analytics.effortOverSession entry.
 */
export default function RoundEffort({ a, b, className = "" }) {
  const sides = [
    { ...a, key: "a", color: SIDE_A.color },
    { ...b, key: "b", color: SIDE_B.color },
  ].map((s) => {
    const phases = (s.phases || []).filter((p) => isNum(p.avgSwingSpeed));
    return {
      key: s.key,
      label: s.label,
      color: s.color,
      hits: phases.reduce((n, p) => n + p.hits, 0),
      points: phases.map((p) => ({ x: p.phase, y: p.avgSwingSpeed, note: `${formatNumber(p.hits)} hit${p.hits === 1 ? "" : "s"} · ${p.sessions} fight${p.sessions === 1 ? "" : "s"}` })),
    };
  });
  const drawn = sides.filter((s) => s.points.length);
  return (
    <Panel
      className={className}
      title="Effort through the round"
      subtitle="Average swing speed (m/s) in each fifth of a finished fight. A falling line means intensity drops late in the round."
      note={drawn.length ? `Sample: ${sides.map((s) => `${s.label} ${formatNumber(s.hits)} timed hits`).join(" · ")}` : null}
    >
      {drawn.length ? (
        <>
          <LineChart caption="Average swing speed by fifth of the fight, per level" height={240} series={drawn} xTicks={[0, 1, 2, 3, 4]} xFormat={(x) => PHASES[x] ?? ""} yFormat={(v) => v.toFixed(1)} markers exact />
          <Legend shape="line" items={sides.map((s) => ({ label: s.points.length ? s.label : `${s.label} · no swing data`, color: s.color }))} />
        </>
      ) : (
        <EmptyState icon="clock" title="Needs finished fights with timing data" description="Each hit's time in the round comes from the live-telemetry build." />
      )}
    </Panel>
  );
}
