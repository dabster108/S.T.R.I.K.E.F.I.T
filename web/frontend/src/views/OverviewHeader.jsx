import { RANGES } from "../lib/derive.js";
import { formatNumber } from "../lib/format.js";
import { PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";

// The Overview's title, range picker and one-line summary. Rendered by the dashboard shell (DashboardApp), not by the lazily loaded
// DashboardView, and from the first frame: it needs no data, so the page's largest paint (this heading) does not wait for the
// sessions request or the view's chunk, and it is the same element before and after the data arrives.
export default function OverviewHeader({ shown, total, loading, range, onRange }) {
  return (
    <div data-tour="overview">
      <PageHeader
        eyebrow="Dashboard"
        title="Performance overview"
        description={
          loading
            ? "Battles you finish in VR show up here in real time."
            : total
              ? `${formatNumber(shown)} of ${formatNumber(total)} recorded battles in this range.`
              : "Battles you finish in VR show up here in real time."
        }
        actions={<SegmentedControl label="Time range" options={RANGES} value={range} onChange={onRange} />}
      />
    </div>
  );
}
