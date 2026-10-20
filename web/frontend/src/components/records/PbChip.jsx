import { metricLabels } from "../../lib/records.js";
import Icon from "../ui/Icon.jsx";

// The small "PB" chip on a battle in a list: this fight set a record on its level. metrics: the metric keys it set (GET /api/players/:name/records).
export default function PbChip({ metrics }) {
  const labels = metricLabels(metrics);
  if (!labels.length) return null;
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--accent)] px-2 py-0.5 text-[0.65rem] font-extrabold uppercase tracking-wide text-white" title={`Personal best: ${labels.join(", ")}`} data-testid="pb-chip">
      <Icon name="sparkles" className="h-3 w-3" strokeWidth={2.6} />
      PB<span className="sr-only"> (personal best: {labels.join(", ")})</span>
    </span>
  );
}
