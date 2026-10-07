import { formatNumber } from "../../lib/format.js";
import { levelName } from "../../lib/game.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import { LevelBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";
import { LEVEL_COLORS, Legend, niceTicks } from "../charts/Charts.jsx";
import { useElementWidth } from "../charts/useElementWidth.js";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const dash = (v, d = 1) => (isNum(v) ? v.toFixed(d) : "—");

// Perceived exertion (Borg RPE, 6-20) on x against measured swing intensity (average tier weight,
// 1 = tap to 4 = full power) on y, one dot per rated session.
function Scatter({ points }) {
  const [ref, width] = useElementWidth();
  const height = 260;
  const pad = { t: 14, r: 16, b: 40, l: 44 };
  const iw = Math.max(10, width - pad.l - pad.r);
  const ih = height - pad.t - pad.b;
  const yTicks = [1, 2, 3, 4];
  const xTicks = [6, 9, 11, 13, 15, 17, 19];
  const sx = (v) => pad.l + ((v - 6) / 14) * iw;
  const sy = (v) => pad.t + ih - ((v - 1) / 3) * ih;
  return (
    <div ref={ref} className="relative">
      <svg width={width} height={height} role="img" aria-label="Perceived exertion against measured swing intensity, one dot per rated session">
        {yTicks.map((t) => (
          <g key={t}>
            <line x1={pad.l} x2={width - pad.r} y1={sy(t)} y2={sy(t)} stroke="var(--grid)" />
            <text x={pad.l - 8} y={sy(t) + 3.5} textAnchor="end" fontSize="10.5" fill="var(--muted)">
              {t}
            </text>
          </g>
        ))}
        <line x1={pad.l} x2={width - pad.r} y1={sy(1)} y2={sy(1)} stroke="var(--border-strong)" />
        {xTicks.map((t) => (
          <text key={t} x={sx(t)} y={height - 22} textAnchor="middle" fontSize="10.5" fill="var(--muted)">
            {t}
          </text>
        ))}
        <text x={pad.l + iw / 2} y={height - 5} textAnchor="middle" fontSize="11" fontWeight="600" fill="var(--ink-secondary)">
          Perceived exertion (Borg 6 to 20)
        </text>
        <text transform={`translate(11 ${pad.t + ih / 2}) rotate(-90)`} textAnchor="middle" fontSize="11" fontWeight="600" fill="var(--ink-secondary)">
          Measured swing intensity
        </text>
        {points.map((p) => (
          <circle key={p.sessionId} cx={sx(p.rpe)} cy={sy(p.effort)} r="5" fill="var(--surface)" stroke={LEVEL_COLORS[p.level] || "var(--accent)"} strokeWidth="2.2">
            <title>{`${levelName(p.level)}: perceived ${p.rpe}, measured ${p.effort.toFixed(2)} of 4`}</title>
          </circle>
        ))}
      </svg>
      <table className="sr-only">
        <caption>Perceived exertion and measured swing intensity per rated session</caption>
        <thead>
          <tr>
            <th scope="col">Level</th>
            <th scope="col">Perceived exertion</th>
            <th scope="col">Swing intensity</th>
          </tr>
        </thead>
        <tbody>
          {points.map((p) => (
            <tr key={p.sessionId}>
              <td>{levelName(p.level)}</td>
              <td>{p.rpe}</td>
              <td>{p.effort.toFixed(2)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// data: GET /api/analytics/feedback
export default function PerceivedEffort({ data }) {
  if (!data) return null;
  const c = data.correlation;
  const plotted = data.points.filter((p) => isNum(p.rpe) && isNum(p.effort));
  const levels = [...new Set(plotted.map((p) => p.level))];
  return (
    <Card className="overflow-hidden">
      <CardHeader title="Perceived vs measured effort" subtitle="How hard players said it felt (Borg RPE) next to how hard they actually swung" />
      {data.n === 0 ? (
        <EmptyState
          icon="gauge"
          title="No ratings yet"
          description="Ratings come from the /feedback page after a session. Once there are enough, perceived effort is plotted against measured swing intensity here."
        />
      ) : (
        <div className="grid gap-6 p-5 xl:grid-cols-12">
          <div className="xl:col-span-7">
            {plotted.length ? (
              <>
                <Scatter points={plotted} />
                <Legend items={levels.map((l) => ({ label: levelName(l), color: LEVEL_COLORS[l] || "var(--accent)" }))} />
              </>
            ) : (
              <EmptyState icon="gauge" title="No rated session has swing data yet" description="The scatter needs a rating on a session with recorded swings." />
            )}
          </div>
          <div className="space-y-5 xl:col-span-5">
            <dl className="grid grid-cols-3 gap-3 text-center">
              {[
                ["Ratings", formatNumber(data.n)],
                ["Mean enjoyment", `${dash(data.enjoyment.mean)} / 5`, `n = ${data.enjoyment.n}`],
                ["Mean exertion", dash(data.rpe.mean), `n = ${data.rpe.n}`],
              ].map(([k, v, sub]) => (
                <div key={k} className="rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-2 py-3">
                  <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
                  <dd className="mt-0.5 text-lg font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
                  {sub && <dd className="text-xs font-medium text-[var(--muted)]">{sub}</dd>}
                </div>
              ))}
            </dl>

            <div className="overflow-hidden rounded-xl border border-[var(--border)]">
              <table className="w-full text-left text-xs">
                <caption className="sr-only">Mean ratings per level</caption>
                <thead className="bg-[var(--surface-muted)] text-[var(--muted)]">
                  <tr>
                    <th scope="col" className="px-3 py-2 font-semibold">Level</th>
                    <th scope="col" className="px-3 py-2 text-right font-semibold">n</th>
                    <th scope="col" className="px-3 py-2 text-right font-semibold">Enjoyment</th>
                    <th scope="col" className="px-3 py-2 text-right font-semibold">Exertion</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--border)]">
                  {data.byLevel.map((l) => (
                    <tr key={l.level}>
                      <th scope="row" className="px-3 py-2 font-semibold text-[var(--ink)]"><LevelBadge level={l.level} /> <span className="ml-1">{levelName(l.level)}</span></th>
                      <td className="px-3 py-2 text-right font-bold tabular-nums text-[var(--ink)]">{l.n}</td>
                      <td className="px-3 py-2 text-right font-bold tabular-nums text-[var(--ink)]">{dash(l.meanEnjoyment)}</td>
                      <td className="px-3 py-2 text-right font-bold tabular-nums text-[var(--ink)]">{dash(l.meanRpe)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="rounded-xl border border-[var(--border)] px-4 py-3">
              {c.status === "ok" ? (
                <p className="text-sm font-bold text-[var(--ink)]">
                  Spearman ρ = {c.rho === null ? "undefined" : c.rho.toFixed(2)} <span className="font-semibold text-[var(--muted)]">(n = {c.n}, descriptive)</span>
                </p>
              ) : (
                <p className="flex items-start gap-2 text-sm font-bold text-[var(--ink-secondary)]">
                  <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--muted)]" /> Not enough data for a correlation yet
                </p>
              )}
              <p className="mt-1 text-xs font-medium leading-relaxed text-[var(--muted)]">{c.note}</p>
            </div>
          </div>
        </div>
      )}
      <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)]">
        Ratings are self-reported, have no sign-in, and cover only players who chose to answer. Swing intensity is the average swing tier (1 tap to 4 full power).
      </p>
    </Card>
  );
}
