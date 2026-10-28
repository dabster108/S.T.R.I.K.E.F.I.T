import { useMemo, useState } from "react";
import { smoothPath } from "../../lib/effort.js";
import { useElementWidth } from "./useElementWidth.js";

// Multi-series charts in the same style as ScoreTrendChart: 10.5px muted tick
// labels, --grid lines with a --border-strong baseline, 2px lines, white markers
// ringed in the series colour, dashed crosshair, rounded-xl tooltip. Every chart
// also renders a screen-reader table of its data.

export function niceTicks(min, max, count = 4, minStep = 0) {
  if (!(max > min)) max = min + 1;
  const span = max - min;
  const mag = 10 ** Math.floor(Math.log10(span / count));
  const nice = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((s) => span / s <= count) || 10 * mag;
  const step = minStep ? Math.max(minStep, Math.ceil(nice)) : nice;
  const ticks = [];
  for (let v = Math.floor(min / step) * step; v <= max + step * 0.001; v += step) ticks.push(Math.round(v * 1e6) / 1e6);
  if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
  return ticks;
}

const fmtDefault = (v) => (Math.abs(v) >= 1000 ? `${Math.round(v / 100) / 10}k` : String(Math.round(v * 10) / 10));

export function Legend({ items, shape = "rect" }) {
  if (!items || items.length < 2) return null;
  return (
    <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--ink-secondary)]">
      {items.map((it) => (
        <li key={it.label} className="inline-flex items-center gap-1.5">
          <span
            className={shape === "line" ? "h-0.5 w-3.5 rounded-full" : "h-2.5 w-2.5 rounded-sm"}
            style={{ background: it.color }}
            aria-hidden
          />
          {it.label}
        </li>
      ))}
    </ul>
  );
}

// tip: { x, y, title, rows: [{ label, color, value, note }] } in the container's own pixels.
export function Tooltip({ tip, width }) {
  if (!tip) return null;
  return (
    <div
      className="pointer-events-none absolute z-10 w-48 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-3.5 py-3 text-xs shadow-lg"
      style={{ left: Math.min(Math.max(tip.x - 96, 0), Math.max(0, width - 192)), top: Math.max(0, tip.y - 12), transform: "translateY(-100%)" }}
      role="status"
    >
      {tip.title && <p className="mb-1.5 font-semibold text-[var(--muted)]">{tip.title}</p>}
      {tip.rows.map((r) => (
        <div key={r.label} className="last:mb-0 mb-1">
          <p className="flex items-center gap-2">
            <span className="h-0.5 w-3 shrink-0 rounded-full" style={{ background: r.color }} aria-hidden />
            <span className="font-bold tabular-nums text-[var(--ink)]">{r.value}</span>
            <span className="truncate text-[var(--ink-secondary)]">{r.label}</span>
          </p>
          {r.note && <p className="truncate pl-5 text-[var(--muted)]">{r.note}</p>}
        </div>
      ))}
    </div>
  );
}

function SrTable({ caption, head, rows }) {
  return (
    <table className="sr-only">
      <caption>{caption}</caption>
      <thead>
        <tr>
          {head.map((h, i) => (
            <th key={i} scope="col">{h}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((r, i) => (
          <tr key={i}>
            {r.map((c, j) => (
              <td key={j}>{c}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * series: [{ key, label, color, points: [{ x, y, note }] }] with numeric x. A point's `note` is a second
 * tooltip line. `exact` lists only the points at the hovered x ("—" for a series without one) instead of
 * each series' nearest point, for series that share an x scale but not a length.
 */
export function LineChart({
  series,
  height = 220,
  xFormat = fmtDefault,
  yFormat = fmtDefault,
  xTicks: xTicksProp,
  yDomain,
  caption = "Line chart",
  markers = "auto",
  step = false,
  exact = false,
  curve = false,
  dashedGrid = false,
  filledMarkers = false,
}) {
  const [ref, width] = useElementWidth();
  const [tip, setTip] = useState(null);
  const pad = { t: 14, r: 16, b: 28, l: 44 };
  const all = series.flatMap((s) => s.points);
  const xs = all.map((p) => p.x);
  const ys = all.map((p) => p.y);
  const xMin = Math.min(...xs);
  const xMax = Math.max(...xs);
  const yTicks = niceTicks(yDomain ? yDomain[0] : Math.min(0, ...ys), yDomain ? yDomain[1] : Math.max(1, ...ys), 4);
  const y0 = yTicks[0];
  const y1 = yTicks[yTicks.length - 1];
  const iw = Math.max(10, width - pad.l - pad.r);
  const ih = height - pad.t - pad.b;
  const sx = (x) => pad.l + (xMax === xMin ? iw / 2 : ((x - xMin) / (xMax - xMin)) * iw);
  const sy = (y) => pad.t + ih - ((y - y0) / (y1 - y0 || 1)) * ih;
  const xTicks = xTicksProp || (xMax === xMin ? [xMin] : niceTicks(xMin, xMax, Math.max(2, Math.floor(iw / 90))).filter((t) => t >= xMin && t <= xMax));
  const showMarkers = markers === true || (markers === "auto" && Math.max(...series.map((s) => s.points.length)) <= 30);
  const allX = useMemo(() => [...new Set(xs)].sort((a, b) => a - b), [xs.join(",")]); // eslint-disable-line react-hooks/exhaustive-deps

  const path = (pts) =>
    curve && !step
      ? smoothPath(pts.map((p) => ({ x: Number(sx(p.x).toFixed(1)), y: Number(sy(p.y).toFixed(1)) })))
      : pts.map((p, i) => (i === 0 ? `M${sx(p.x)},${sy(p.y)}` : step ? `H${sx(p.x)}V${sy(p.y)}` : `L${sx(p.x)},${sy(p.y)}`)).join("");

  const showAt = (x) => {
    let nearest = null;
    const rows = [];
    for (const s of series) {
      if (!s.points.length) continue;
      const p = s.points.reduce((a, b) => (Math.abs(b.x - x) < Math.abs(a.x - x) ? b : a));
      if (nearest === null || Math.abs(p.x - x) < Math.abs(nearest - x)) nearest = p.x;
      rows.push({ label: s.label, color: s.color, value: yFormat(p.y), note: p.note, p });
    }
    if (nearest === null) return;
    const shown = exact ? rows.map((r) => (r.p.x === nearest ? r : { label: r.label, color: r.color, value: "—", p: null })) : rows;
    setTip({ x: sx(nearest), y: pad.t, title: xFormat(nearest), rows: shown });
  };

  return (
    <div ref={ref} className="relative w-full min-w-0 select-none">
      {width > 0 && (
        <svg
          width={width}
          height={height}
          className="block rounded-lg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent)]"
          role="img"
          aria-label={caption}
          tabIndex={0}
          onMouseMove={(e) => showAt(xMin + ((e.clientX - e.currentTarget.getBoundingClientRect().left - pad.l) / iw) * (xMax - xMin))}
          onMouseLeave={() => setTip(null)}
          onFocus={() => allX.length && showAt(allX[allX.length - 1])}
          onBlur={() => setTip(null)}
          onKeyDown={(e) => {
            if (!["ArrowLeft", "ArrowRight"].includes(e.key) || !allX.length) return;
            e.preventDefault();
            const cur = tip ? allX.findIndex((v) => sx(v) >= tip.x - 0.5) : allX.length - 1;
            showAt(allX[Math.max(0, Math.min(allX.length - 1, cur + (e.key === "ArrowRight" ? 1 : -1)))]);
          }}
        >
          {yTicks.map((t) => (
            <g key={t}>
              <line
                x1={pad.l}
                x2={width - pad.r}
                y1={sy(t)}
                y2={sy(t)}
                stroke={dashedGrid || t === y0 ? "var(--border-strong)" : "var(--grid)"}
                strokeDasharray={dashedGrid ? "4 5" : undefined}
              />
              <text x={pad.l - 8} y={sy(t) + 3.5} textAnchor="end" fontSize="11" fill="var(--muted)" className="tabular-nums">
                {yFormat(t)}
              </text>
            </g>
          ))}
          {dashedGrid &&
            xTicks.map((t) => (
              <line key={`grid-${t}`} x1={sx(t)} x2={sx(t)} y1={pad.t} y2={pad.t + ih} stroke="var(--border-strong)" strokeDasharray="4 5" />
            ))}
          {xTicks.map((t) => (
            <text key={t} x={sx(t)} y={height - 10} textAnchor="middle" fontSize="11" fill="var(--muted)">
              {xFormat(t)}
            </text>
          ))}
          {tip && <line x1={tip.x} x2={tip.x} y1={pad.t} y2={pad.t + ih} stroke="var(--border-strong)" strokeDasharray="3 3" />}
          {series.map((s) => (
            <g key={s.key}>
              {s.points.length > 1 && (
                <path d={path(s.points)} pathLength="1" fill="none" stroke={s.color} strokeWidth="2.25" strokeLinejoin="round" strokeLinecap="round" className="chart-line" />
              )}
              {(showMarkers || s.points.length === 1) &&
                s.points.map((p, i) => {
                  const hot = tip?.rows.some((r) => r.p === p);
                  const last = i === s.points.length - 1;
                  const halo = hot || (filledMarkers && !tip && last);
                  return (
                    <g key={i}>
                      {halo && <circle cx={sx(p.x)} cy={sy(p.y)} r="11" fill={s.color} opacity="0.16" />}
                      <circle
                        cx={sx(p.x)}
                        cy={sy(p.y)}
                        r={halo ? 5 : 4}
                        fill={filledMarkers || hot ? s.color : "var(--surface)"}
                        stroke={filledMarkers ? "none" : s.color}
                        strokeWidth={filledMarkers ? 0 : hot ? 2.5 : 2}
                        className="dot-in"
                        style={{ "--i": Math.min(i, 30) }}
                      />
                    </g>
                  );
                })}
              {!showMarkers && tip?.rows.filter((r) => r.label === s.label && r.p).map((r) => (
                <circle key="hot" cx={sx(r.p.x)} cy={sy(r.p.y)} r="5" fill="var(--surface)" stroke={s.color} strokeWidth="2.5" />
              ))}
            </g>
          ))}
        </svg>
      )}
      <Tooltip tip={tip} width={width} />
      <SrTable
        caption={caption}
        head={["", ...series.map((s) => s.label)]}
        rows={allX.map((x) => [xFormat(x), ...series.map((s) => {
          const p = s.points.find((q) => q.x === x);
          return p ? yFormat(p.y) : "";
        })])}
      />
    </div>
  );
}

/** Vertical columns. series: [{ key, label, color | colors[], values: [] }] (grouped when > 1). */
export function BarChart({ categories, series, height = 200, yFormat = fmtDefault, caption = "Bar chart", showValues = false, tipTitle }) {
  const [ref, width] = useElementWidth();
  const [hot, setHot] = useState(null);
  const pad = { t: showValues ? 22 : 12, r: 8, b: 28, l: 44 };
  const vals = series.flatMap((s) => s.values).filter(Number.isFinite);
  const counts = vals.every(Number.isInteger);
  const yTicks = niceTicks(0, Math.max(1, ...vals), 4, counts ? 1 : 0);
  const top = yTicks[yTicks.length - 1];
  const iw = Math.max(10, width - pad.l - pad.r);
  const ih = height - pad.t - pad.b;
  const band = iw / Math.max(1, categories.length);
  const gap = 2;
  const barW = Math.max(3, Math.min(24, (band * 0.7 - gap * (series.length - 1)) / series.length));
  const groupW = barW * series.length + gap * (series.length - 1);
  const sy = (v) => pad.t + ih - (v / (top || 1)) * ih;
  const labelEvery = Math.ceil(categories.length / Math.max(1, Math.floor(iw / 44)));
  const xOf = (ci, si) => pad.l + band * ci + (band - groupW) / 2 + si * (barW + gap);
  const colorOf = (s, ci) => (s.colors ? s.colors[ci] : s.color);

  const bar = (x, v) => {
    const y = sy(v);
    const h = pad.t + ih - y;
    if (h <= 0) return "";
    const r = Math.min(4, h, barW / 2);
    return `M${x},${pad.t + ih}V${y + r}Q${x},${y} ${x + r},${y}H${x + barW - r}Q${x + barW},${y} ${x + barW},${y + r}V${pad.t + ih}Z`;
  };

  const tip = hot && {
    x: xOf(hot.ci, hot.si) + barW / 2,
    y: sy(series[hot.si].values[hot.ci] || 0),
    title: tipTitle ? tipTitle(categories[hot.ci]) : categories[hot.ci],
    rows: series.map((s) => ({ label: s.label, color: colorOf(s, hot.ci), value: Number.isFinite(s.values[hot.ci]) ? yFormat(s.values[hot.ci]) : "—" })),
  };

  return (
    <div ref={ref} className="relative w-full min-w-0 select-none">
      {width > 0 && (
        <svg width={width} height={height} className="block" role="img" aria-label={caption} onMouseLeave={() => setHot(null)}>
          {yTicks.map((t) => (
            <g key={t}>
              <line x1={pad.l} x2={width - pad.r} y1={sy(t)} y2={sy(t)} stroke={t === 0 ? "var(--border-strong)" : "var(--grid)"} />
              <text x={pad.l - 8} y={sy(t) + 3.5} textAnchor="end" fontSize="11" fill="var(--muted)" className="tabular-nums">
                {yFormat(t)}
              </text>
            </g>
          ))}
          {categories.map((c, ci) => (
            <g key={`${c}-${ci}`}>
              {ci % labelEvery === 0 && (
                <text x={pad.l + band * ci + band / 2} y={height - 10} textAnchor="middle" fontSize="11" fill="var(--muted)">
                  {c}
                </text>
              )}
              {series.map((s, si) => {
                const v = s.values[ci];
                if (!Number.isFinite(v)) return null;
                const x = xOf(ci, si);
                const isHot = hot && hot.ci === ci && hot.si === si;
                return (
                  <g key={s.key}>
                    <path d={bar(x, v)} fill={colorOf(s, ci)} opacity={hot && !isHot ? 0.55 : 1} className="bar-grow transition-opacity" style={{ "--i": ci }} />
                    {showValues && v > 0 && (
                      <text x={x + barW / 2} y={sy(v) - 6} textAnchor="middle" fontSize="11" fill="var(--ink-secondary)" className="tabular-nums">
                        {yFormat(v)}
                      </text>
                    )}
                    <rect
                      x={x - gap}
                      y={pad.t}
                      width={barW + gap * 2}
                      height={ih}
                      fill="transparent"
                      tabIndex={0}
                      role="img"
                      className="outline-none"
                      aria-label={`${c}, ${s.label}: ${yFormat(v)}`}
                      onMouseEnter={() => setHot({ ci, si })}
                      onFocus={() => setHot({ ci, si })}
                      onBlur={() => setHot(null)}
                    />
                  </g>
                );
              })}
            </g>
          ))}
        </svg>
      )}
      <Tooltip tip={tip} width={width} />
      <SrTable caption={caption} head={["", ...series.map((s) => s.label)]} rows={categories.map((c, i) => [c, ...series.map((s) => (Number.isFinite(s.values[i]) ? yFormat(s.values[i]) : ""))])} />
    </div>
  );
}

export const TIERS = [
  { key: "tap", label: "Tap", color: "var(--tier-tap)" },
  { key: "light", label: "Light", color: "var(--tier-light)" },
  { key: "medium", label: "Medium", color: "var(--tier-medium)" },
  { key: "full", label: "Full power", color: "var(--tier-full)" },
];
export const LEVEL_COLORS = { Easy: "var(--level-1)", Medium: "var(--level-2)", Hard: "var(--level-3)" };

// Share of hits per swing tier: one stacked bar (2px gaps, like OutcomeSplit) plus a counted legend.
// columns={2} keeps the legend in two columns for narrow panels.
export function TierBar({ counts, columns = 4 }) {
  const total = TIERS.reduce((a, t) => a + (counts?.[t.key] || 0), 0);
  if (!total) return null;
  return (
    <div>
      <div
        className="flex h-3 w-full gap-[2px] overflow-hidden rounded-full bg-gray-100"
        role="img"
        aria-label={TIERS.map((t) => `${t.label} ${counts[t.key] || 0}`).join(", ")}
      >
        {TIERS.filter((t) => counts[t.key]).map((t, i) => (
          <div key={t.key} className="h-full first:rounded-l-full last:rounded-r-full" style={{ width: `${(counts[t.key] / total) * 100}%` }}>
            <div className="grow-x h-full w-full" style={{ background: t.color, "--i": i }} />
          </div>
        ))}
      </div>
      <ul className={`mt-4 grid grid-cols-2 gap-2 ${columns === 4 ? "sm:grid-cols-4" : ""}`}>
        {TIERS.map((t) => (
          <li key={t.key} className="rounded-xl border border-[var(--border)] px-3 py-2">
            <p className="flex items-center gap-1.5 text-xs font-semibold text-[var(--muted)]">
              <span className="h-2.5 w-2.5 rounded-sm" style={{ background: t.color }} aria-hidden />
              {t.label}
            </p>
            <p className="mt-0.5 text-lg font-extrabold tabular-nums text-[var(--ink)]">
              {counts[t.key] || 0} <span className="text-xs font-semibold text-[var(--muted)]">{Math.round(((counts[t.key] || 0) / total) * 100)}%</span>
            </p>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Side-by-side comparison, one row per metric, each row scaled on its own (units differ, so no shared axis). */
export function CompareBars({ rows, a, b }) {
  return (
    <div>
      <Legend items={[a, b]} />
      <table className="mt-3 w-full">
        <caption className="sr-only">
          {a.label} compared with {b.label}
        </caption>
        <thead className="sr-only">
          <tr>
            <th scope="col">Metric</th>
            <th scope="col">{a.label}</th>
            <th scope="col">{b.label}</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border)]">
          {rows.map((r) => {
            const top = Math.max(Number.isFinite(r.a) ? r.a : 0, Number.isFinite(r.b) ? r.b : 0) || 1;
            return (
              <tr key={r.label}>
                <th scope="row" className="w-[30%] py-2.5 pr-3 text-left align-middle text-xs font-semibold text-[var(--ink-secondary)]">
                  {r.label}
                  {r.hint && <span className="block text-xs font-medium text-[var(--muted)]">{r.hint}</span>}
                </th>
                {[
                  [r.a, a.color],
                  [r.b, b.color],
                ].map(([v, color], i) => (
                  <td key={i} className="w-[35%] py-2.5 pr-3 align-middle last:pr-0">
                    <div className="h-2 w-full rounded-full bg-gray-100">
                      <div className="h-full" style={{ width: `${Number.isFinite(v) ? Math.max(2, (v / top) * 100) : 0}%` }}>
                        <div className="grow-x h-full w-full rounded-full" style={{ background: color, "--i": i }} />
                      </div>
                    </div>
                    <p className="mt-1 text-xs font-semibold tabular-nums text-[var(--ink)]">{Number.isFinite(v) ? r.fmt(v) : "—"}</p>
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
