import { useMemo, useRef, useState } from "react";
import { liveSession } from "../lib/derive.js";
import { formatRelative } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import Button from "../components/ui/Button.jsx";
import { ConnectionBadge } from "../components/ui/Badge.jsx";
import EventList, { EVENT_META } from "../components/sessions/EventList.jsx";
import LiveSessionCard from "../components/sessions/LiveSessionCard.jsx";

const FILTERS = [
  { id: "all", label: "All" },
  { id: "session_start", label: "Starts" },
  { id: "hit", label: "Hits" },
  { id: "session_complete", label: "Results" },
];

export default function LiveView({ events, connection, lastUpdate }) {
  const [filter, setFilter] = useState("all");
  const [paused, setPaused] = useState(false);
  const frozen = useRef(events);
  if (!paused) frozen.current = events;
  const source = paused ? frozen.current : events;

  const shown = useMemo(() => (filter === "all" ? source : source.filter((e) => e.type === filter)), [source, filter]);
  const live = useMemo(() => liveSession(events), [events]);
  const counts = useMemo(() => {
    const c = {};
    for (const e of events) c[e.type] = (c[e.type] || 0) + 1;
    return c;
  }, [events]);
  const queued = paused ? events.length - frozen.current.length : 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Realtime"
        title="Live feed"
        description="Every session start, hit and result from the headset, pushed over Socket.io as it happens."
        actions={
          <>
            <ConnectionBadge state={connection} />
            <span className="text-xs text-[var(--muted)]">Updated {lastUpdate ? formatRelative(lastUpdate) : "—"}</span>
          </>
        }
      />

      {live ? (
        <LiveSessionCard live={live} />
      ) : (
        <div className="rounded-2xl border border-dashed border-[var(--border-strong)] bg-[var(--surface)] px-5 py-4 text-sm text-[var(--muted)]">
          No battle in progress. Start a round in the headset and it appears here instantly.
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        {Object.entries(EVENT_META).map(([type, meta]) => (
          <div key={type} className="flex items-center gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3">
            <span className={`h-2.5 w-2.5 rounded-full ${meta.dot}`} aria-hidden />
            <span className="flex-1 text-sm text-[var(--ink-secondary)]">{meta.label}</span>
            <span className="text-lg font-bold tabular-nums text-[var(--ink)]">{counts[type] || 0}</span>
          </div>
        ))}
      </div>

      <Card className="overflow-hidden">
        <CardHeader
          title="Event stream"
          subtitle={`${shown.length} event${shown.length === 1 ? "" : "s"} shown · newest first`}
          actions={
            <>
              <SegmentedControl label="Event type" options={FILTERS} value={filter} onChange={setFilter} />
              <Button icon={paused ? "play" : "pause"} onClick={() => setPaused((p) => !p)}>
                {paused ? `Resume${queued > 0 ? ` (${queued} new)` : ""}` : "Pause"}
              </Button>
            </>
          }
        />
        {shown.length ? (
          <div className="scrollbar-thin max-h-[60vh] overflow-y-auto">
            <EventList events={shown} />
          </div>
        ) : (
          <EmptyState icon="live" title="No events yet" description="Events appear here as soon as Unity posts them to the backend." />
        )}
      </Card>
    </div>
  );
}
