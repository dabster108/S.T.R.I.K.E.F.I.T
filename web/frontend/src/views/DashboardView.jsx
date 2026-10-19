import { lazy, Suspense, useMemo } from "react";
import { Link } from "react-router-dom";
import { RANGES, byLevel, records, sessionTime, summarize } from "../lib/derive.js";
import { useLiveIds } from "../live/LiveProvider.jsx";
import { GoalsMetToday } from "../components/fitness/DailyGoal.jsx";
import { LiveArenaCard } from "../components/live/LiveFight.jsx";
import { formatDuration, formatNumber } from "../lib/format.js";
import { deltaChip, lastN } from "../lib/delta.js";
import { SectionRule } from "../components/ui/Card.jsx";
import LazyBlock from "../components/ui/LazyBlock.jsx";
import { OverviewMoreSkeleton } from "../components/ui/Skeletons.jsx";
import StatTile from "../components/ui/StatTile.jsx";
import Highlights from "../components/engage/Highlights.jsx";

// Everything under the headline figures is its own chunk, fetched when the visitor scrolls near it (see OverviewMore.jsx).
const OverviewMore = lazy(() => import("./OverviewMore.jsx"));

const link = "press rounded text-xs font-semibold text-[var(--accent)] hover:text-[var(--accent-strong)] hover:underline";

function PlayingNow() {
  const ids = useLiveIds();
  if (!ids.length) return null;
  return (
    <section aria-label="Playing now" className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="flex items-center gap-2 text-sm font-bold text-[var(--ink)]">
          <span className="status-live h-2 w-2 rounded-full bg-[var(--good)]" aria-hidden />
          Playing now
        </h2>
        <Link to="/dashboard/live" className={link}>
          Open Live Arena
        </Link>
      </div>
      <div className="stagger grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        {ids.map((id) => (
          <LiveArenaCard key={id} id={id} compact />
        ))}
      </div>
    </section>
  );
}

// Seven buckets for the KPI sparklines: seven days for "all" and "7d", seven ~3.4-hour blocks for "24h".
const SPARK_POINTS = 7;
function buckets(sessions, rangeId) {
  const size = rangeId === "24h" ? 864e5 / SPARK_POINTS : 864e5;
  const end = Date.now();
  const out = Array.from({ length: SPARK_POINTS }, () => []);
  for (const s of sessions) {
    const i = SPARK_POINTS - 1 - Math.floor((end - sessionTime(s)) / size);
    if (i >= 0 && i < SPARK_POINTS) out[i].push(s);
  }
  return out;
}

// The same-length window just before the selected one. Null for "all time" or when
// nothing was played then, so no trend badge is ever shown without real data.
function previousWindow(allSessions, rangeId) {
  const ms = RANGES.find((r) => r.id === rangeId)?.ms;
  if (!ms) return null;
  const now = Date.now();
  const prev = allSessions.filter((s) => {
    const t = sessionTime(s);
    return t < now - ms && t >= now - 2 * ms;
  });
  return prev.length ? summarize(prev) : null;
}

const fmtSpeed = (v) => (Number.isFinite(v) ? `${v.toFixed(1)} m/s` : "—");
const fmtShare = (v) => (Number.isFinite(v) ? `${Math.round(v * 100)}%` : "—");

// The page title and the range picker are OverviewHeader, rendered by the dashboard shell so they paint without waiting for this chunk.
export default function DashboardView({ sessions, allSessions, events, eventsReady = true, range, openSession }) {
  const stats = useMemo(() => summarize(sessions), [sessions]);
  const levels = useMemo(() => byLevel(sessions), [sessions]);
  const recs = useMemo(() => records(sessions), [sessions]);
  const chrono = useMemo(() => [...sessions].sort((a, b) => sessionTime(a) - sessionTime(b)), [sessions]);
  const trend = chrono.slice(-30);
  const prev = useMemo(() => previousWindow(allSessions, range), [allSessions, range]);
  const rangeLabel = RANGES.find((r) => r.id === range)?.label.toLowerCase();

  const sparks = useMemo(() => {
    const b = buckets(sessions, range);
    // Win rate as it built up over the decided fights (the shared rule: abandoned skipped), sampled at the end of each bucket;
    // buckets before the first decided fight have no rate and are left out rather than drawn as 0%.
    let wins = 0;
    let decided = 0;
    const winRate = [];
    for (const l of b) {
      for (const s of l) {
        if (s.outcome === "victory" || s.outcome === "defeat") {
          decided += 1;
          wins += s.outcome === "victory" ? 1 : 0;
        }
      }
      if (decided) winRate.push((wins / decided) * 100);
    }
    return {
      battles: b.map((l) => l.length),
      winRate,
      score: lastN(chrono.map((s) => Number(s.score) || 0)),
      time: b.map((l) => l.reduce((t, s) => t + (Number(s.durationSec) || 0), 0)),
    };
  }, [sessions, range, chrono]);

  // Latest vs the battle before it: both are real sessions.
  const [latest, previous] = sessions;
  const scoreDelta = latest && previous ? Number(latest.score) - Number(previous.score) : null;
  const noHitData = sessions.length > 0 && stats.totalHits === 0;
  const vsPrev = `vs previous ${rangeLabel}`;
  const sparkWord = range === "24h" ? "3-hour block" : "day";

  return (
    <div>
      <div className="mt-6">
        <PlayingNow />
      </div>

      {/* Highlights: best run, longest win streak, most played level (all recorded fights, whatever the range above) */}
      <section aria-label="Highlights" className="mt-6">
        <Highlights sessions={allSessions} openSession={openSession} />
      </section>

      {/* 01: four headline figures of equal weight, each with its own small trend line */}
      <SectionRule index="01" title={`Headline · ${rangeLabel}`} className="mt-8" />
      <div className="stagger mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          label="Battles"
          count={stats.sessions}
          icon={null}
          spark={sessions.length ? sparks.battles : null}
          sparkLabel={`Battles per ${sparkWord}`}
          trend={prev ? deltaChip({ cur: stats.sessions, prev: prev.sessions, suffix: vsPrev, fallbackLabel: formatNumber }) : null}
          hint={prev ? null : `${stats.players} player${stats.players === 1 ? "" : "s"}${sessions.length ? ` · battles per ${sparkWord}` : ""}`}
        />
        <StatTile
          label="Win rate"
          count={stats.winRate}
          format={(v) => `${Math.round(v * 10) / 10}%`}
          icon={null}
          spark={sparks.winRate.length > 1 ? sparks.winRate : null}
          sparkLabel="Win rate over the last battles"
          trend={prev && stats.sessions ? deltaChip({ cur: stats.winRate, prev: prev.winRate, mode: "points", suffix: vsPrev }) : null}
          hint={prev && stats.sessions ? null : stats.currentStreak > 1 ? `${stats.currentStreak} wins in a row` : `${stats.victories} won · ${stats.defeats} lost${stats.abandoned ? ` · ${stats.abandoned} abandoned (not counted)` : ""}`}
        />
        <StatTile
          label="Latest score"
          count={latest ? Number(latest.score) || 0 : undefined}
          value="—"
          icon={null}
          spark={sparks.score.length > 1 ? sparks.score : null}
          sparkLabel="Scores of the last battles"
          trend={latest && previous ? deltaChip({ cur: Number(latest.score), prev: Number(previous.score), suffix: "vs previous battle", fallbackLabel: formatNumber }) : null}
          hint={scoreDelta !== null ? null : `best ${formatNumber(stats.bestScore)}`}
        />
        <StatTile
          label="Active time"
          count={stats.totalDurationSec}
          format={(v) => formatDuration(v)}
          icon={null}
          spark={sessions.length ? sparks.time : null}
          sparkLabel={`Active time per ${sparkWord}`}
          trend={prev ? deltaChip({ cur: stats.totalDurationSec, prev: prev.totalDurationSec, suffix: vsPrev, fallbackLabel: (n) => formatDuration(n).replace("—", "0s") }) : null}
          hint={prev ? null : stats.sessions ? `${formatDuration(stats.avgDurationSec)} per battle` : "No battles yet"}
        />
      </div>

      <div className="mt-4">
        <GoalsMetToday />
      </div>

      <LazyBlock minHeight={900} fallback={<OverviewMoreSkeleton />}>
        <Suspense fallback={<OverviewMoreSkeleton />}>
          <OverviewMore sessions={sessions} stats={stats} levels={levels} recs={recs} trend={trend} openSession={openSession} events={events} eventsReady={eventsReady} noHitData={noHitData} />
        </Suspense>
      </LazyBlock>
    </div>
  );
}
