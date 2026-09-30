import { activity, daysPlayed } from "../../lib/compare.js";
import { formatNumber, formatRelative } from "../../lib/format.js";
import { BarChart, Legend } from "../charts/Charts.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import { Panel, SIDE_A, SIDE_B } from "./shared.jsx";

// Tick labels are unique within a chart (at most 14 days, weeks or months), so a tick finds its bucket.
const monthDay = (d) => `${String(d.getMonth() + 1).padStart(2, "0")}/${String(d.getDate()).padStart(2, "0")}`;
const UNIT = {
  day: { per: "day", tick: monthDay, title: (d) => d.toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" }) },
  week: { per: "week", tick: monthDay, title: (d) => `Week of ${d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}` },
  month: {
    per: "month",
    tick: (d) => `${d.toLocaleDateString(undefined, { month: "short" })} ’${String(d.getFullYear()).slice(2)}`,
    title: (d) => d.toLocaleDateString(undefined, { month: "long", year: "numeric" }),
  },
};
const plural = (n, word) => `${formatNumber(n)} ${word}${n === 1 ? "" : "s"}`;

/** Fights started per day (or week, or month on long histories) by each side, on one calendar. */
export default function ActivityCard({ a, b, className = "" }) {
  const { unit, buckets, clipped } = activity(a, b);
  const u = UNIT[unit];
  const line = (s) => {
    const d = daysPlayed(s);
    return d.count ? `${s.label}: ${plural(s.fights.length, "fight")} on ${plural(d.count, "day")}, last ${formatRelative(d.last)}` : `${s.label}: no fights`;
  };
  return (
    <Panel
      className={className}
      title="Activity"
      subtitle={`Fights started per ${u.per}, from the first fight of either side to the latest${clipped ? " (latest 14 months shown)" : ""}.`}
      note={`${line(a)} · ${line(b)}`}
    >
      {buckets.length ? (
        <>
          <BarChart
            caption={`Fights per ${u.per}, ${a.label} and ${b.label}`}
            height={220}
            categories={buckets.map((x) => u.tick(x.date))}
            series={[
              { key: "a", label: a.label, color: SIDE_A.color, values: buckets.map((x) => x.a) },
              { key: "b", label: b.label, color: SIDE_B.color, values: buckets.map((x) => x.b) },
            ]}
            yFormat={(v) => formatNumber(v)}
            tipTitle={(c) => {
              const bucket = buckets.find((x) => u.tick(x.date) === c);
              return bucket ? u.title(bucket.date) : c;
            }}
          />
          <Legend
            items={[
              { label: a.label, color: SIDE_A.color },
              { label: b.label, color: SIDE_B.color },
            ]}
          />
        </>
      ) : (
        <EmptyState icon="calendar" title="No fights to place on a calendar yet" />
      )}
    </Panel>
  );
}
