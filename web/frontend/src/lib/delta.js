// Delta chips on the KPI tiles: "▲ 12% vs previous 7 days". Only ever built from two real numbers; a previous value of zero
// has no percentage, so the chip falls back to the plain difference rather than inventing one.

// Percent change from `prev` to `cur`, rounded to a whole percent. null when there is no usable baseline.
export function percentChange(cur, prev) {
  if (!Number.isFinite(cur) || !Number.isFinite(prev) || prev === 0) return null;
  return Math.round(((cur - prev) / Math.abs(prev)) * 100);
}

// { value, label, suffix } for <TrendChip>. `value` carries the sign (and drives the colour), `label` the magnitude to print.
//   mode "percent" (default): 12 -> "12%"; falls back to `fallbackLabel(absDiff)` when prev is 0
//   mode "points": for numbers that already are percentages (win rate): "5 pts"
export function deltaChip({ cur, prev, suffix, mode = "percent", fallbackLabel = (n) => String(n) }) {
  if (!Number.isFinite(cur) || !Number.isFinite(prev)) return null;
  if (mode === "points") {
    const d = Math.round(cur - prev);
    return { value: d, label: `${Math.abs(d)} pts`, suffix };
  }
  const pct = percentChange(cur, prev);
  if (pct === null) {
    const d = cur - prev;
    return d === 0 ? { value: 0, label: "0", suffix } : { value: d, label: fallbackLabel(Math.abs(d)), suffix };
  }
  // a change that rounds to 0% is a flat line, not an up or down arrow
  return { value: pct, label: `${Math.abs(pct)}%`, suffix };
}

// The last `n` values, so every KPI sparkline has the same seven points however long the series is.
export const lastN = (values, n = 7) => (values.length > n ? values.slice(-n) : values);
