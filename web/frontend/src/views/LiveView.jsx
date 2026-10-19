import { lazy, Suspense, useEffect, useMemo, useReducer, useRef, useState } from "react";
import { useFeed, useLiveIds, useLiveMessages, useLiveReady } from "../live/LiveProvider.jsx";
import { liveStore } from "../live/liveStore.js";
import { RESULT_HOLD_MS } from "../lib/arena.js";
import { Card, SectionRule } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import { LiveSkeleton } from "../components/ui/Skeletons.jsx";
import Button from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import EventList, { collapseRepeats } from "../components/sessions/EventList.jsx";
import { LiveChip } from "../components/live/LiveFight.jsx";
import ActivityFeed from "../components/engage/ActivityFeed.jsx";

// The Live Arena. While a fight is on, one stage per fight (components/live/arena): who is fighting which monster, both health bars,
// the attack the monster is winding up, the swing gauge, ghost pace and a kill-feed. When a fight ends its result stays for 8 seconds,
// then the page goes back to the quiet arena (the three monsters and the last fight). The stream itself is untouched: this page only
// listens to it. The stage chunk loads after the page, in its own file.

const ArenaStage = lazy(() => import("../components/live/arena/ArenaStage.jsx"));
const QuietArena = lazy(() => import("../components/live/arena/QuietArena.jsx"));

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

// Fights to show: the ones live, plus the ones that finished less than RESULT_HOLD_MS ago (their result moment).
function useStageIds() {
  const ids = useLiveIds();
  const [, redraw] = useReducer((n) => n + 1, 0);
  useLiveMessages((type) => {
    if (type === "session_started" || type === "session_finished" || type === "session_deleted") redraw();
  });
  const now = Date.now();
  const stage = [];
  let nextEnd = null;
  for (const id of ids) {
    const e = liveStore.getSession(id);
    if (!e) continue;
    if (e.session.status === "live") stage.push(id);
    else if (e.finishedAt && now - e.finishedAt < RESULT_HOLD_MS) {
      stage.push(id);
      const end = e.finishedAt + RESULT_HOLD_MS;
      nextEnd = nextEnd === null ? end : Math.min(nextEnd, end);
    }
  }
  useEffect(() => {
    if (nextEnd === null) return undefined;
    const t = setTimeout(redraw, Math.max(50, nextEnd - Date.now() + 50));
    return () => clearTimeout(t);
  }, [nextEnd]);
  return stage;
}

function FullEventLog() {
  const [filter, setFilter] = useState("all");
  const [paused, setPaused] = useState(false);
  const events = useFeed(null);
  const frozen = useRef(events);
  if (!paused) frozen.current = events;
  const source = paused ? frozen.current : events;
  const shown = useMemo(() => collapseRepeats(filter === "all" ? source : source.filter(MATCH[filter])), [source, filter]);
  const manyPlayers = useMemo(() => new Set(source.map((e) => e.playerName)).size > 1, [source]);
  const queued = paused ? events.length - frozen.current.length : 0;
  return (
    <Card className="mt-4 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--border)] px-4 py-3">
        <SegmentedControl label="Event type" options={FILTERS} value={filter} onChange={setFilter} />
        <Button icon={paused ? "play" : "pause"} onClick={() => setPaused((p) => !p)} aria-pressed={paused}>
          {paused ? `Resume${queued > 0 ? ` (${queued} new)` : ""}` : "Pause"}
        </Button>
      </div>
      {shown.length ? (
        <div className="scrollbar-thin max-h-[28rem] overflow-y-auto">
          <EventList events={shown.slice(0, 150)} dense showPlayer={manyPlayers} />
        </div>
      ) : (
        <EmptyState icon="live" title="No events yet" description="Recent events load from the server, then new ones stream in as they happen." />
      )}
      <p className="border-t border-[var(--border)] px-4 py-2 text-xs font-medium text-[var(--muted)]">Newest first</p>
    </Card>
  );
}

function StageSkeleton() {
  return (
    <div className="space-y-4" aria-hidden>
      <Skeleton className="h-11 w-96 max-w-full" />
      <div className="grid gap-4 lg:grid-cols-[19rem_minmax(0,1fr)]">
        <Skeleton className="h-[34rem] w-full rounded-2xl" />
        <div className="space-y-4">
          <Skeleton className="h-44 w-full rounded-2xl" />
          <Skeleton className="h-24 w-full rounded-2xl" />
          <Skeleton className="h-72 w-full rounded-2xl" />
        </div>
      </div>
    </div>
  );
}

export default function LiveView() {
  const ready = useLiveReady();
  const stage = useStageIds();
  const live = useLiveIds().filter((id) => liveStore.getSession(id)?.session.status === "live").length;
  const [logOpen, setLogOpen] = useState(false);

  if (!ready) return <LiveSkeleton />;

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="t-eyebrow">Live Arena</p>
        {live > 1 ? (
          <LiveChip label={`${live} fights live`} />
        ) : live === 1 ? null : ( // one live fight already carries its own LIVE chip in the stage header
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--surface-muted)] px-2.5 py-1 text-xs font-semibold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">
            <span className="h-2 w-2 rounded-full bg-gray-400" aria-hidden />
            No fights right now
          </span>
        )}
      </div>

      {stage.length ? (
        <div className="space-y-12">
          {stage.map((id, i) => (
            <Suspense key={id} fallback={<StageSkeleton />}>
              <ArenaStage id={id} primary={i === 0} />
            </Suspense>
          ))}
        </div>
      ) : (
        <Suspense fallback={<StageSkeleton />}>
          <QuietArena />
        </Suspense>
      )}

      <section aria-labelledby="milestones-title">
        <SectionRule id="milestones-title" index="01" title="Milestones · all players" />
        <ActivityFeed className="mt-4" limit={10} title="Activity" />
      </section>

      <section aria-labelledby="log-title">
        <SectionRule id="log-title" index="02" title="Full event log" aside={<span className="readout text-xs font-bold text-[var(--muted)]">every event, with filters</span>} />
        <details className="group mt-4" onToggle={(e) => setLogOpen(e.currentTarget.open)}>
          <summary className="press inline-flex cursor-pointer list-none items-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3.5 py-2 text-sm font-semibold text-[var(--ink-secondary)] hover:text-[var(--ink)]">
            <Icon name="list" className="h-4 w-4" /> {logOpen ? "Hide" : "Show"} the full event log
          </summary>
          {logOpen && <FullEventLog />}
        </details>
      </section>
    </div>
  );
}
