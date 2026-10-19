import { lazy, Suspense, useCallback, useEffect, useMemo, useReducer, useRef, useState } from "react";
import { useFeed, useLiveIds, useLiveMessages, useLiveReady } from "../live/LiveProvider.jsx";
import { liveStore } from "../live/liveStore.js";
import { RESULT_HOLD_MS } from "../lib/arena.js";
import { arenaLink, effectiveChoice, pickStage, playersOnStage, readChoice, writeChoice } from "../lib/arenaView.js";
import { copyText } from "../lib/toast.js";
import { isTypingTarget } from "../components/layout/ShortcutsOverlay.jsx";
import { anotherModalOpen } from "../components/onboarding/tourState.js";
import ArenaSwitcher from "../components/live/arena/ArenaSwitcher.jsx";
import { CommentaryControls } from "../components/live/arena/Commentary.jsx";
import { Card, SectionRule } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import Button from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";
import EventList, { collapseRepeats } from "../components/sessions/EventList.jsx";
import { LiveChip } from "../components/live/LiveFight.jsx";
import { LiveSkeleton } from "../components/ui/Skeletons.jsx";
import ActivityFeed from "../components/engage/ActivityFeed.jsx";
// The quiet arena is what most visits see first (no fight on): bundled with the page (under 2 kB gzipped) instead of a second
// lazy hop, so its portraits are requested at once. The fight stage stays a lazy chunk.
import QuietArena from "../components/live/arena/QuietArena.jsx";
import MonsterPortrait from "../components/ui/MonsterPortrait.jsx";
import { LEVEL_KEYS } from "../lib/arena.js";

// The Live Arena. While a fight is on, one stage per fight (components/live/arena): who is fighting which monster, both health bars,
// the attack the monster is winding up, the swing gauge, ghost pace and a kill-feed. When a fight ends its result stays for 8 seconds,
// then the page goes back to the quiet arena (the three monsters and the last fight). The stream itself is untouched: this page only
// listens to it. The stage chunk loads after the page, in its own file.

const ArenaStage = lazy(() => import("../components/live/arena/ArenaStage.jsx"));

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

// Full screen for projecting: the browser's own full-screen mode where it is allowed, and an overlay that hides the sidebar, header and
// everything below the fights either way. F toggles it, Esc leaves it.
function useArenaFullscreen() {
  const [fs, setFs] = useState(false);
  const enter = useCallback(() => {
    setFs(true);
    document.documentElement.requestFullscreen?.().catch(() => {}); // refused (iframe, old Safari): the overlay alone still hides the chrome
  }, []);
  const exit = useCallback(() => {
    setFs(false);
    if (document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
  }, []);
  useEffect(() => {
    const onKey = (e) => {
      if (e.metaKey || e.ctrlKey || e.altKey || isTypingTarget(e.target)) return;
      if (e.key === "Escape" && fs) {
        e.preventDefault();
        exit();
      } else if ((e.key === "f" || e.key === "F") && !anotherModalOpen()) {
        e.preventDefault();
        fs ? exit() : enter();
      }
    };
    const onChange = () => !document.fullscreenElement && setFs(false); // the browser's own Esc
    window.addEventListener("keydown", onKey);
    document.addEventListener("fullscreenchange", onChange);
    return () => {
      window.removeEventListener("keydown", onKey);
      document.removeEventListener("fullscreenchange", onChange);
      if (document.fullscreenElement) document.exitFullscreen?.().catch(() => {});
    };
  }, [fs, enter, exit]);
  useEffect(() => {
    if (!fs) return undefined;
    const prev = document.documentElement.style.overflow;
    document.documentElement.style.overflow = "hidden";
    return () => {
      document.documentElement.style.overflow = prev;
    };
  }, [fs]);
  return { fs, enter, exit };
}

export default function LiveView() {
  const ready = useLiveReady();
  const stage = useStageIds();
  const live = useLiveIds().filter((id) => liveStore.getSession(id)?.session.status === "live").length;
  const [logOpen, setLogOpen] = useState(false);
  const [choice, setChoice] = useState(() => readChoice(localStorage, window.location.search));
  const { fs, enter, exit } = useArenaFullscreen();

  const entries = stage
    .map((id) => liveStore.getSession(id))
    .filter(Boolean)
    .map((e) => ({ id: e.session.id, playerName: e.session.playerName, level: e.session.level, live: e.session.status === "live", at: new Date(e.session.startedAt || 0).getTime() }))
    .sort((a, b) => a.at - b.at || a.playerName.localeCompare(b.playerName)); // earliest fight first, so "Player 1" stays Player 1
  const players = playersOnStage(entries);
  const shown = pickStage(entries, choice);
  const active = effectiveChoice(entries, choice);
  const choose = (c) => {
    setChoice(c);
    writeChoice(localStorage, c);
  };
  const copyLink = () => copyText(arenaLink(window.location.origin, active), "Link");

  // Until the first snapshot arrives the page is a skeleton, but the three portraits the quiet arena will show start loading now,
  // off screen, instead of after the snapshot (they were the page's largest paint, found 1.6 s late).
  if (!ready)
    return (
      <>
        <LiveSkeleton />
        <div aria-hidden className="pointer-events-none fixed -left-[9999px] top-0 h-px w-px overflow-hidden">
          {LEVEL_KEYS.map((k) => (
            <MonsterPortrait key={k} level={k} size="sm" glow={false} eager />
          ))}
        </div>
      </>
    );

  const stages = shown.length ? (
    <div className={shown.length > 1 ? "grid gap-12 2xl:grid-cols-2 2xl:gap-8" : ""}>
      {shown.map((id, i) => (
        <Suspense key={id} fallback={<StageSkeleton />}>
          <ArenaStage id={id} primary={i === 0} compact={shown.length > 1} />
        </Suspense>
      ))}
    </div>
  ) : (
    <QuietArena />
  );

  const controls = (
    <div className="flex flex-wrap items-center gap-2">
      <CommentaryControls />
      <Button icon="link" onClick={copyLink}>
        Copy link
      </Button>
      <Button icon={fs ? "minimize" : "maximize"} onClick={fs ? exit : enter} aria-pressed={fs} aria-keyshortcuts="F" title="Full screen for projecting (F)">
        {fs ? "Exit full screen" : "Full screen"}
        <kbd className="ml-1.5 rounded border border-[var(--border)] bg-[var(--surface-muted)] px-1 font-sans text-[0.6875rem] font-bold text-[var(--ink-secondary)]" aria-hidden>
          F
        </kbd>
      </Button>
    </div>
  );
  const status =
    live > 1 ? (
      <LiveChip label={`${live} fights live`} />
    ) : live === 1 ? (
      <LiveChip />
    ) : (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--surface-muted)] px-2.5 py-1 text-xs font-semibold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">
        <span className="h-2 w-2 rounded-full bg-gray-400" aria-hidden />
        No fights right now
      </span>
    );

  if (fs) {
    return (
      <div role="region" aria-label="Live Arena, full screen" className="fixed inset-0 z-[55] overflow-y-auto bg-[var(--bg)] px-5 py-5 sm:px-8 sm:py-6">
        <div className="mx-auto max-w-[1920px] space-y-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-3">
              <p className="t-eyebrow">STRIKEFIT · Live Arena</p>
              {status}
            </div>
            <div className="flex flex-wrap items-center gap-3">
              {players.length > 1 && <ArenaSwitcher players={players} choice={active} onChoose={choose} />}
              {controls}
            </div>
          </div>
          {stages}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <p className="t-eyebrow">Live Arena</p>
          {live > 1 ? status : live === 0 ? status : null /* one live fight already carries its own LIVE chip in the stage header */}
        </div>
        {controls}
      </div>
      {players.length > 1 && <ArenaSwitcher players={players} choice={active} onChoose={choose} />}

      {stages}

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
