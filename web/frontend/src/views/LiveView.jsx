import { useMemo, useRef, useState } from "react";
import { useFeed, useLiveIds, useLiveReady } from "../live/LiveProvider.jsx";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { LiveSkeleton } from "../components/ui/Skeletons.jsx";
import Button from "../components/ui/Button.jsx";
import EventList from "../components/sessions/EventList.jsx";
import { LiveArenaCard, LiveChip } from "../components/live/LiveFight.jsx";

const FILTERS = [
  { id: "all", label: "All" },
  { id: "hits", label: "Hits" },
  { id: "monster", label: "Monster" },
  { id: "hurt", label: "Damage taken" },
  { id: "results", label: "Results" },
];
const MATCH = {
  hits: (e) => e.type === "hit" || e.type === "weak_swing",
  monster: (e) => ["monster_attack", "monster_defend", "clash", "dodge", "block"].includes(e.type),
  hurt: (e) => e.type === "player_hurt",
  results: (e) => e.type === "session_start" || e.type === "session_complete",
};

export default function LiveView() {
  const ids = useLiveIds();
  const ready = useLiveReady();
  const [filter, setFilter] = useState("all");
  const [paused, setPaused] = useState(false);
  const events = useFeed(null);
  const frozen = useRef(events);
  if (!paused) frozen.current = events;
  const source = paused ? frozen.current : events;
  const shown = useMemo(() => (filter === "all" ? source : source.filter(MATCH[filter])), [source, filter]);
  const queued = paused ? events.length - frozen.current.length : 0;

  if (!ready) return <LiveSkeleton />;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Realtime"
        title="Live Arena"
        description="Every fight in progress, with both health bars, XP and score updating the moment the headset reports a hit, an attack or an HP change."
        actions={
          ids.length ? (
            <LiveChip label={`${ids.length} fight${ids.length === 1 ? "" : "s"} live`} />
          ) : (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--surface-muted)] px-2.5 py-1 text-xs font-semibold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">
              <span className="h-2 w-2 rounded-full bg-gray-400" aria-hidden />
              No fights right now
            </span>
          )
        }
      />

      {ids.length ? (
        <div className="stagger grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {ids.map((id) => (
            <LiveArenaCard key={id} id={id} />
          ))}
        </div>
      ) : (
        <Card>
          <EmptyState
            icon="headset"
            title="Nobody is playing right now"
            description="When a fight starts in the headset it appears here instantly, with both health bars moving as it happens."
          />
        </Card>
      )}

      <Card className="overflow-hidden">
        <CardHeader
          title="Event feed"
          subtitle={`${shown.length} event${shown.length === 1 ? "" : "s"} shown · newest first`}
          actions={
            <>
              <SegmentedControl label="Event type" options={FILTERS} value={filter} onChange={setFilter} />
              <Button icon={paused ? "play" : "pause"} onClick={() => setPaused((p) => !p)} aria-pressed={paused}>
                {paused ? `Resume${queued > 0 ? ` (${queued} new)` : ""}` : "Pause"}
              </Button>
            </>
          }
        />
        {shown.length ? (
          <div className="scrollbar-thin max-h-[60vh] overflow-y-auto">
            <EventList events={shown.slice(0, 150)} />
          </div>
        ) : (
          <EmptyState icon="live" title="No events yet" description="Recent events load from the server, then new ones stream in as they happen." />
        )}
      </Card>
    </div>
  );
}
