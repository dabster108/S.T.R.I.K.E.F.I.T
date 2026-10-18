import { LEVEL_COLORS } from "../charts/Charts.jsx";
import { useElementWidth } from "../charts/useElementWidth.js";
import { levelName, SWING_SPEED_CAP } from "../../lib/game.js";

// Borg RPE (6 to 20, the player's own rating after the fight) against that fight's average swing speed. One dot per rated fight,
// coloured by level (the legend names each level, so colour is never the only signal). Only drawn once the backend has reported
// a Pearson r, i.e. from 8 paired points; before that the page shows an empty state instead of a plot.
const H = 280;
const PAD = { t: 14, r: 16, b: 40, l: 46 };

export default function RpeScatter({ points, r }) {
  const [ref, width] = useElementWidth();
  const iw = Math.max(10, width - PAD.l - PAD.r);
  const ih = H - PAD.t - PAD.b;
  const sx = (v) => PAD.l + (Math.min(v, SWING_SPEED_CAP) / SWING_SPEED_CAP) * iw;
  const sy = (v) => PAD.t + ih - ((v - 6) / 14) * ih;
  const levels = [...new Set(points.map((p) => p.level))];
  return (
    <div>
      <div ref={ref} className="w-full min-w-0" style={{ height: H }}>
        {width > 0 && (
          <svg width={width} height={H} role="img" aria-label={`Scatter plot of ${points.length} rated fights: average swing speed (left to right, 0 to ${SWING_SPEED_CAP} metres per second) against Borg RPE (bottom to top, 6 to 20). Pearson r is ${r}. A data table follows.`} className="block">
            {[6, 9, 12, 15, 18, 20].map((v) => (
              <g key={v}>
                <line x1={PAD.l} x2={width - PAD.r} y1={sy(v)} y2={sy(v)} stroke={v === 6 ? "var(--border-strong)" : "var(--grid)"} />
                <text x={PAD.l - 8} y={sy(v) + 3.5} textAnchor="end" fontSize="11" fill="var(--muted)" className="tabular-nums">{v}</text>
              </g>
            ))}
            {[0, 2, 4, 6, 8].map((v) => (
              <text key={v} x={sx(v)} y={H - 20} textAnchor="middle" fontSize="11" fill="var(--muted)" className="tabular-nums">{v}</text>
            ))}
            <text x={PAD.l + iw / 2} y={H - 4} textAnchor="middle" fontSize="11" fontWeight="600" fill="var(--ink-secondary)">Average swing speed (m/s)</text>
            <text transform={`translate(12 ${PAD.t + ih / 2}) rotate(-90)`} textAnchor="middle" fontSize="11" fontWeight="600" fill="var(--ink-secondary)">Borg RPE (6 to 20)</text>
            {points.map((p, i) => (
              <circle key={p.sessionId || i} cx={sx(p.avgSpeed)} cy={sy(p.rpe)} r="5.5" fill={LEVEL_COLORS[p.level] || "var(--accent)"} fillOpacity="0.8" stroke="var(--surface)" strokeWidth="1.5">
                <title>{`${levelName(p.level)}: ${p.avgSpeed.toFixed(1)} m/s average swing, RPE ${p.rpe}`}</title>
              </circle>
            ))}
          </svg>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--ink-secondary)]" aria-label="Levels">
        {levels.map((l) => (
          <li key={l} className="inline-flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full" style={{ background: LEVEL_COLORS[l] || "var(--accent)" }} aria-hidden />
            {levelName(l)}
          </li>
        ))}
      </ul>
      <details className="mt-3 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2 text-xs">
        <summary className="cursor-pointer font-semibold text-[var(--ink-secondary)]">Show the {points.length} points as a table</summary>
        <div className="scrollbar-thin mt-2 max-h-56 overflow-auto">
          <table className="w-full text-left tabular-nums">
            <caption className="sr-only">Rated fights: level, average swing speed and Borg RPE</caption>
            <thead>
              <tr className="text-[var(--muted)]">
                <th scope="col" className="py-1 pr-4 font-semibold">Level</th>
                <th scope="col" className="py-1 pr-4 font-semibold">Avg swing (m/s)</th>
                <th scope="col" className="py-1 font-semibold">RPE</th>
              </tr>
            </thead>
            <tbody>
              {points.map((p, i) => (
                <tr key={p.sessionId || i} className="border-t border-[var(--border)]">
                  <td className="py-1 pr-4">{levelName(p.level)}</td>
                  <td className="py-1 pr-4">{p.avgSpeed.toFixed(2)}</td>
                  <td className="py-1">{p.rpe}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}
