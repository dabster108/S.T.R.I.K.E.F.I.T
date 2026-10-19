// Words for the records a finished fight set (the API's newRecords: [{ metric, value, previous }]). Pure, so it is tested.
import { RECORD_METRICS, RECORD_ORDER } from "../config/records.js";

/** 7.94 -> "7.94", 8 -> "8", 333.1 -> "333.1", 1003 -> "1,003": up to two decimals, no trailing zeros. */
const TWO_DECIMALS = new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 });
const trimmed = (v) => TWO_DECIMALS.format(v);

function valueText(metric, v) {
  const m = RECORD_METRICS[metric];
  const sign = m?.signed && v > 0 ? "+" : "";
  const unit = m?.unit ? (m.unit === "%" ? "%" : ` ${m.unit}`) : "";
  return `${sign}${trimmed(v)}${unit}`;
}

/** The records in the order the page shows them, each as { metric, label, value, previous } with the numbers already written out. */
export function describeRecords(list) {
  const known = (Array.isArray(list) ? list : []).filter((r) => r && RECORD_METRICS[r.metric] && Number.isFinite(r.value) && Number.isFinite(r.previous));
  return known
    .sort((a, b) => RECORD_ORDER.indexOf(a.metric) - RECORD_ORDER.indexOf(b.metric))
    .map((r) => ({ metric: r.metric, label: RECORD_METRICS[r.metric].label, value: valueText(r.metric, r.value), previous: valueText(r.metric, r.previous) }));
}

/** "New personal best" or "3 new personal bests". */
export const recordsHeadline = (list) => (list.length === 1 ? "New personal best" : `${list.length} new personal bests`);

/** One sentence for a screen reader or a tooltip: "Fastest swing 7.9 m/s (was 6.2 m/s); Score 1,400 pts (was 900 pts)". */
export const recordsSentence = (list) => describeRecords(list).map((r) => `${r.label} ${r.value} (was ${r.previous})`).join("; ");

/** The labels of the metrics a session set records in (for the PB chip's tooltip). */
export const metricLabels = (metrics) => (metrics || []).filter((m) => RECORD_METRICS[m]).sort((a, b) => RECORD_ORDER.indexOf(a) - RECORD_ORDER.indexOf(b)).map((m) => RECORD_METRICS[m].label);
