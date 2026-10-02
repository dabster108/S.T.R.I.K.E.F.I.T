import { useState } from "react";
import { daysAgo, fillDays, fillWeeks, formatLongDate, shortDay } from "../../lib/fitness.js";
import { formatNumber } from "../../lib/format.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import { SegmentedControl } from "../ui/Controls.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import { FigureStrip } from "../ui/StatTile.jsx";
import { BarChart } from "../charts/Charts.jsx";

const GROUPS = [
  { id: "day", label: "By day" },
  { id: "week", label: "By week" },
];

// Today's date in the zone the backend used, so the chart's right edge matches its day boundaries.
const todayIn = (zone) => new Intl.DateTimeFormat("en-CA", { timeZone: zone, year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());

export function SessionsOverTime({ data, title = "Sessions over time" }) {
  const [group, setGroup] = useState("day");
  const today = todayIn(data.timeZone);
  const rows = group === "day" ? fillDays(data.sessionsByDay, today, 30) : fillWeeks(data.sessionsByWeek, today, 12);
  const label = (r) => shortDay(group === "day" ? r.day : r.weekStart);
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title={title}
        subtitle={group === "day" ? "Sessions per day, last 30 days" : "Sessions per week (weeks start Monday), last 12 weeks"}
        actions={<SegmentedControl label="Group sessions" options={GROUPS} value={group} onChange={setGroup} />}
      />
      <div className="px-3 pb-4 pt-4 sm:px-5">
        {rows.length ? (
          <BarChart
            caption={title}
            categories={rows.map(label)}
            series={[{ key: "n", label: "Sessions", color: "var(--accent)", values: rows.map((r) => r.sessions) }]}
            yFormat={(v) => formatNumber(v)}
            tipTitle={(c) => (group === "week" ? `Week of ${c}` : c)}
          />
        ) : (
          <EmptyState icon="calendar" title="No sessions yet" />
        )}
      </div>
    </Card>
  );
}

// Everything about one player's engagement. Streak claims only appear once there are at least
// two different days of play; with one day it just states what exists.
export default function PlayerEngagement({ data }) {
  const multiDay = data.daysPlayed >= 2;
  const items = [
    { label: "First played", value: formatLongDate(data.firstSessionAt), sub: `${formatNumber(data.totalSessions)} session${data.totalSessions === 1 ? "" : "s"} on ${data.daysPlayed} day${data.daysPlayed === 1 ? "" : "s"}` },
    { label: "Last played", value: daysAgo(data.daysSinceLastSession), sub: formatLongDate(data.lastSessionAt) },
    multiDay
      ? { label: "Current streak", value: `${data.currentStreak} day${data.currentStreak === 1 ? "" : "s"}`, sub: data.currentStreak ? "in a row, counting today or yesterday" : "no session today or yesterday" }
      : { label: "Streak", value: "—", sub: "needs play on 2+ days" },
    multiDay ? { label: "Longest streak", value: `${data.longestStreak} day${data.longestStreak === 1 ? "" : "s"}`, sub: "consecutive days with a session" } : { label: "Longest streak", value: "—", sub: "needs play on 2+ days" },
  ];
  return (
    <div className="space-y-6">
      <FigureStrip items={items} caption={data.definitions.streak} />
      {data.totalSessions === 1 ? (
        <Card>
          <EmptyState icon="calendar" title="One session so far" description="A chart of sessions over time needs more than one session. Until then, the first and last played dates above are everything there is to show." />
        </Card>
      ) : (
        <SessionsOverTime data={data} />
      )}
    </div>
  );
}
