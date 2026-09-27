import { useMemo } from "react";
import { RANGES, byLevel, liveSession, records, sessionTime, summarize } from "../lib/derive.js";
import { formatDuration, formatNumber, formatPercent, formatRelative } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import StatTile from "../components/ui/StatTile.jsx";
import Icon from "../components/ui/Icon.jsx";
import ScoreTrendChart from "../components/charts/ScoreTrendChart.jsx";
import OutcomeSplit from "../components/charts/OutcomeSplit.jsx";
import LevelBars from "../components/charts/LevelBars.jsx";
import SessionsTable from "../components/sessions/SessionsTable.jsx";
import EventList from "../components/sessions/EventList.jsx";
import LiveSessionCard from "../components/sessions/LiveSessionCard.jsx";

function RecordRow({ icon, label, session, value, onOpen }) {
  return (
    <li>
      <button
        type="button"
        disabled={!session}
        onClick={() => session && onOpen(session.id)}
        className="flex w-full items-center gap-3 px-5 py-3 text-left transition-colors hover:bg-[var(--surface-muted)] disabled:cursor-default disabled:hover:bg-transparent"
      >
        <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-[var(--accent-soft)] text-[var(--accent)]">
          <Icon name={icon} className="h-4 w-4" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-xs text-[var(--muted)]">{label}</span>
          <span className="block truncate text-sm font-medium text-[var(--ink)]">
            {session ? `${session.playerName} · ${formatRelative(session.endedAt)}` : "No qualifying session yet"}
          </span>
        </span>
        {session && <span className="text-sm font-semibold tabular-nums text-[var(--ink)]">{value}</span>}
      </button>
    </li>
  );
}

export default function DashboardView({ sessions, allSessions, events, range, onRange, openSession }) {
  const stats = useMemo(() => summarize(sessions), [sessions]);
  const levels = useMemo(() => byLevel(sessions), [sessions]);
  const recs = useMemo(() => records(sessions), [sessions]);
  const live = useMemo(() => liveSession(events), [events]);
  const trend = useMemo(() => [...sessions].sort((a, b) => sessionTime(a) - sessionTime(b)).slice(-30), [sessions]);

  // Last session vs the one before, for the score tile's delta chip.
  const [latest, previous] = sessions;
  const scoreDelta = latest && previous ? Number(latest.score) - Number(previous.score) : null;
  const noHitData = sessions.length > 0 && stats.totalHits === 0;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Dashboard"
        title="Performance overview"
        description={
          allSessions.length
            ? `${formatNumber(stats.sessions)} of ${formatNumber(allSessions.length)} recorded battles in this range.`
            : "Battles you finish in VR show up here in real time."
        }
        actions={<SegmentedControl label="Time range" options={RANGES} value={range} onChange={onRange} />}
      />

      {live && <LiveSessionCard live={live} />}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          label="Battles"
          value={formatNumber(stats.sessions)}
          icon="sessions"
          tone="blue"
          hint={`${stats.players} player${stats.players === 1 ? "" : "s"}`}
        />
        <StatTile
          label="Win rate"
          value={formatPercent(stats.winRate)}
          icon="check"
          tone="green"
          hint={stats.currentStreak > 1 ? `${stats.currentStreak} wins in a row` : `${stats.victories}W · ${stats.defeats}L`}
        />
        <StatTile
          label="Latest score"
          value={latest ? formatNumber(latest.score) : "—"}
          icon="score"
          tone="indigo"
          trend={scoreDelta !== null ? { value: scoreDelta, label: formatNumber(Math.abs(scoreDelta)) } : null}
          hint={scoreDelta !== null ? "vs previous battle" : `best ${formatNumber(stats.bestScore)}`}
        />
        <StatTile
          label="Active time"
          value={formatDuration(stats.totalDurationSec)}
          icon="clock"
          tone="amber"
          hint={stats.sessions ? `${formatDuration(stats.avgDurationSec)} per battle` : "No battles yet"}
        />
      </div>

      <div className="grid gap-6 xl:grid-cols-12">
        <Card className="overflow-hidden xl:col-span-8">
          <CardHeader
            title="Score trend"
            subtitle={trend.length ? `Last ${trend.length} battle${trend.length === 1 ? "" : "s"} · click a point for details` : "Per battle, oldest to newest"}
          />
          <div className="px-3 pb-3 pt-4 sm:px-5">
            <ScoreTrendChart sessions={trend} onSelect={(s) => openSession(s.id)} />
          </div>
        </Card>
        <Card className="overflow-hidden xl:col-span-4">
          <CardHeader title="Outcomes" subtitle="Victories vs defeats" />
          <OutcomeSplit victories={stats.victories} defeats={stats.defeats} other={stats.other} />
          <div className="grid grid-cols-2 border-t border-[var(--border)] text-center">
            <div className="border-r border-[var(--border)] px-3 py-3">
              <p className="text-lg font-bold tabular-nums text-[var(--ink)]">{stats.currentStreak}</p>
              <p className="text-[0.7rem] text-[var(--muted)]">Current win streak</p>
            </div>
            <div className="px-3 py-3">
              <p className="text-lg font-bold tabular-nums text-[var(--ink)]">{stats.bestStreak}</p>
              <p className="text-[0.7rem] text-[var(--muted)]">Best win streak</p>
            </div>
          </div>
        </Card>
      </div>

      <div className="grid gap-6 lg:grid-cols-2 xl:grid-cols-12">
        <Card className="overflow-hidden xl:col-span-4">
          <CardHeader title="By level" subtitle="Average score per difficulty" />
          <LevelBars rows={levels} />
        </Card>

        <Card className="overflow-hidden xl:col-span-4">
          <CardHeader title="Combat totals" subtitle="Across battles in this range" />
          <ul className="divide-y divide-[var(--border)]">
            {[
              { icon: "bolt", label: "Hits landed", value: stats.totalHits },
              { icon: "flame", label: "Damage dealt", value: stats.totalDamage },
              { icon: "hand", label: "Taps / swings", value: stats.totalTaps },
              { icon: "score", label: "Points earned", value: stats.totalScore },
            ].map((row) => (
              <li key={row.label} className="flex items-center gap-3 px-5 py-3">
                <Icon name={row.icon} className="h-4 w-4 text-[var(--muted)]" />
                <span className="flex-1 text-sm text-[var(--ink-secondary)]">{row.label}</span>
                <span className="text-sm font-semibold tabular-nums text-[var(--ink)]">{formatNumber(row.value)}</span>
              </li>
            ))}
          </ul>
          {noHitData && (
            <p className="border-t border-[var(--border)] bg-[var(--warn-soft)] px-5 py-2.5 text-[0.7rem] text-[var(--warn)]">
              Battles are arriving with 0 hits/damage — Unity isn't calling SessionReporter.RegisterHit yet.
            </p>
          )}
        </Card>

        <Card className="overflow-hidden lg:col-span-2 xl:col-span-4">
          <CardHeader title="Personal records" subtitle="Click a record to open that battle" />
          <ul className="divide-y divide-[var(--border)]">
            <RecordRow icon="trophy" label="Highest score" session={recs.highScore} value={formatNumber(recs.highScore?.score)} onOpen={openSession} />
            <RecordRow icon="clock" label="Fastest victory" session={recs.fastestWin} value={formatDuration(recs.fastestWin?.durationSec)} onOpen={openSession} />
            <RecordRow icon="bolt" label="Most hits" session={recs.mostHits} value={formatNumber(recs.mostHits?.hitCount)} onOpen={openSession} />
            <RecordRow icon="flame" label="Most damage" session={recs.mostDamage} value={formatNumber(recs.mostDamage?.damageDealt)} onOpen={openSession} />
          </ul>
        </Card>
      </div>

      <div className="grid gap-6 xl:grid-cols-12">
        <Card className="overflow-hidden xl:col-span-8">
          <CardHeader
            title="Recent battles"
            subtitle="Newest first"
            actions={
              <a href="#/sessions" className="text-xs font-medium text-[var(--accent)] hover:underline">
                View all
              </a>
            }
          />
          {sessions.length ? (
            <SessionsTable rows={sessions.slice(0, 6)} onSelect={(s) => openSession(s.id)} compact />
          ) : (
            <EmptyState title="No battles yet" description="Start the backend, play a round in Unity, and it lands here within a second." />
          )}
        </Card>
        <Card className="overflow-hidden xl:col-span-4">
          <CardHeader
            title="Latest activity"
            actions={
              <a href="#/live" className="text-xs font-medium text-[var(--accent)] hover:underline">
                Live feed
              </a>
            }
          />
          {events.length ? (
            <EventList events={events.slice(0, 6)} dense />
          ) : (
            <EmptyState icon="live" title="Quiet for now" description="Session starts, hits and results stream in here." />
          )}
        </Card>
      </div>

    </div>
  );
}
