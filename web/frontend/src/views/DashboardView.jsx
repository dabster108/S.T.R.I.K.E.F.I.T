import { useMemo } from "react";
import { Link } from "react-router-dom";
import { RANGES, byLevel, records, sessionTime, summarize } from "../lib/derive.js";
import { api } from "../lib/api.js";
import { levelName } from "../lib/game.js";
import { useApi, useThrottledCallback } from "../hooks/useApi.js";
import { useLiveIds, useLiveMessages } from "../live/LiveProvider.jsx";
import { LiveArenaCard } from "../components/live/LiveFight.jsx";
import { BarChart } from "../components/charts/Charts.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import { formatDuration, formatNumber, formatPercent, formatRelative } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import StatTile from "../components/ui/StatTile.jsx";
import Icon from "../components/ui/Icon.jsx";
import ScoreTrendChart from "../components/charts/ScoreTrendChart.jsx";
import OutcomeSplit from "../components/charts/OutcomeSplit.jsx";
import LevelBars from "../components/charts/LevelBars.jsx";
import { SessionList } from "../components/sessions/SessionCards.jsx";
import EventList from "../components/sessions/EventList.jsx";

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

// Buckets for the KPI sparklines: daily for "all" (last 14 days) and "7d", two-hourly for "24h".
function buckets(sessions, rangeId) {
  const hourly = rangeId === "24h";
  const n = hourly ? 12 : rangeId === "7d" ? 7 : 14;
  const size = hourly ? 2 * 36e5 : 864e5;
  const end = Date.now();
  const out = Array.from({ length: n }, () => []);
  for (const s of sessions) {
    const i = n - 1 - Math.floor((end - sessionTime(s)) / size);
    if (i >= 0 && i < n) out[i].push(s);
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

// Last 14 days + per-level effort, from /api/overview (independent of the range filter).
function ActivityCards() {
  const { data, error, reload } = useApi(() => api.overview(), []);
  const refresh = useThrottledCallback(reload, 4000);
  useLiveMessages((type) => (type === "session_finished" || type === "session_started" || type === "session_deleted") && refresh());
  const days = data?.sessionsPerDay || [];
  const levels = (data?.byLevel || []).filter((l) => ["Easy", "Medium", "Hard"].includes(l.level));
  return (
    <div className="grid gap-6 xl:grid-cols-12">
      <Card className="overflow-hidden xl:col-span-7">
        <CardHeader title="Sessions per day" subtitle="Fights started each day, last 14 days" />
        <div className="px-3 pb-3 pt-4 sm:px-5">
          {error && !data ? (
            <EmptyState icon="clock" title="Activity will appear here" description="It loads as soon as the server answers." />
          ) : !data ? (
            <div className="flex h-48 items-end gap-2" aria-hidden>
              {[40, 65, 30, 80, 55, 70, 45, 90, 60, 35, 75, 50, 85, 40].map((h, i) => (
                <div key={i} className="skeleton flex-1 rounded-t-md" style={{ height: `${h}%` }} />
              ))}
            </div>
          ) : days.some((d) => d.sessions) ? (
            <BarChart
              caption="Sessions per day, last 14 days"
              categories={days.map((d) => d.day.slice(5).replace("-", "/"))}
              series={[{ key: "s", label: "Sessions", color: "var(--accent)", values: days.map((d) => d.sessions) }]}
              yFormat={(v) => formatNumber(v)}
            />
          ) : (
            <EmptyState icon="calendar" title="No sessions in the last 14 days" description="Older battles still count everywhere else." />
          )}
        </div>
      </Card>
      <Card className="overflow-hidden xl:col-span-5">
        <CardHeader title="Per level" subtitle="All-time results and swing effort per level" />
        {!data ? (
          <div className="space-y-3 p-5" aria-hidden>
            <div className="skeleton h-16 w-full rounded-xl" />
            <div className="skeleton h-16 w-full rounded-xl" />
          </div>
        ) : levels.length ? (
          <ul className="stagger divide-y divide-[var(--border)]">
            {levels.map((l) => (
              <li key={l.level} className="px-5 py-4">
                <div className="flex items-center justify-between gap-2 text-xs">
                  <span className="flex items-center gap-2 text-sm font-bold text-[var(--ink)]">
                    <LevelBadge level={l.level} /> {levelName(l.level)}
                  </span>
                  <span className="font-semibold text-[var(--muted)]">
                    {formatNumber(l.sessions)} session{l.sessions === 1 ? "" : "s"}
                  </span>
                </div>
                <dl className="mt-3 grid grid-cols-2 gap-2 text-center sm:grid-cols-4">
                  {[
                    ["Win rate", l.winRate === null ? "—" : formatPercent(l.winRate)],
                    ["Avg score", formatNumber(l.avgScore)],
                    ["Avg swing", fmtSpeed(l.avgSwingSpeed)],
                    ["Full power", fmtShare(l.fullPowerRatio)],
                  ].map(([k, v]) => (
                    <div key={k} className="rounded-lg bg-[var(--surface-muted)] px-2 py-2 ring-1 ring-inset ring-[var(--border)]">
                      <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
                      <dd className="mt-0.5 text-sm font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
                    </div>
                  ))}
                </dl>
              </li>
            ))}
          </ul>
        ) : (
          <EmptyState icon="target" title="No levels played yet" />
        )}
        {levels.some((l) => !l.swingSamples) && (
          <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)]">
            Swing speed is recorded by the live-telemetry build of the game; older battles show “—”.
          </p>
        )}
      </Card>
    </div>
  );
}

function RecordRow({ icon, label, session, value, onOpen }) {
  return (
    <li>
      <button
        type="button"
        disabled={!session}
        onClick={() => session && onOpen(session.id)}
        className="press group flex w-full items-center gap-3 px-5 py-3 text-left hover:bg-[var(--surface-muted)] disabled:cursor-default disabled:hover:bg-transparent"
      >
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent)] transition-transform duration-200 group-enabled:group-hover:scale-110">
          <Icon name={icon} className="h-4 w-4" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-xs font-semibold text-[var(--muted)]">{label}</span>
          <span className="block truncate text-sm font-bold text-[var(--ink)]">
            {session ? `${session.playerName} · ${formatRelative(session.endedAt)}` : "No qualifying session yet"}
          </span>
        </span>
        {session && <span className="text-base font-extrabold tabular-nums text-[var(--ink)]">{value}</span>}
      </button>
    </li>
  );
}

export default function DashboardView({ sessions, allSessions, events, range, onRange, openSession }) {
  const stats = useMemo(() => summarize(sessions), [sessions]);
  const levels = useMemo(() => byLevel(sessions), [sessions]);
  const recs = useMemo(() => records(sessions), [sessions]);
  const chrono = useMemo(() => [...sessions].sort((a, b) => sessionTime(a) - sessionTime(b)), [sessions]);
  const trend = chrono.slice(-30);
  const prev = useMemo(() => previousWindow(allSessions, range), [allSessions, range]);
  const rangeLabel = RANGES.find((r) => r.id === range)?.label.toLowerCase();

  const sparks = useMemo(() => {
    const b = buckets(sessions, range);
    // Win rate as it built up over the last 20 battles in range.
    const last = chrono.slice(-20);
    let wins = 0;
    const winRate = last.map((s, i) => ((wins += s.outcome === "victory" ? 1 : 0) / (i + 1)) * 100);
    return {
      battles: b.map((l) => l.length),
      winRate,
      score: chrono.slice(-12).map((s) => Number(s.score) || 0),
      time: b.map((l) => l.reduce((a, s) => a + (Number(s.durationSec) || 0), 0)),
    };
  }, [sessions, range, chrono]);

  // Latest vs the battle before it: both are real sessions.
  const [latest, previous] = sessions;
  const scoreDelta = latest && previous ? Number(latest.score) - Number(previous.score) : null;
  const noHitData = sessions.length > 0 && stats.totalHits === 0;
  const vsPrev = `vs previous ${rangeLabel}`;
  const sparkWord = range === "24h" ? "two-hour block" : "day";

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

      <PlayingNow />

      <div className="stagger grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatTile
          label="Battles"
          count={stats.sessions}
          icon="sessions"
          tone="blue"
          spark={sessions.length ? sparks.battles : null}
          sparkLabel={`Battles per ${sparkWord}`}
          trend={prev ? { value: stats.sessions - prev.sessions, label: formatNumber(Math.abs(stats.sessions - prev.sessions)), suffix: vsPrev } : null}
          hint={prev ? null : `${stats.players} player${stats.players === 1 ? "" : "s"}`}
        />
        <StatTile
          label="Win rate"
          count={stats.winRate}
          format={(v) => `${Math.round(v * 10) / 10}%`}
          icon="check"
          tone="green"
          spark={sparks.winRate.length > 1 ? sparks.winRate : null}
          sparkLabel="Win rate over the last battles"
          trend={
            prev && stats.sessions
              ? { value: Math.round(stats.winRate - prev.winRate), label: `${Math.abs(Math.round(stats.winRate - prev.winRate))} pts`, suffix: vsPrev }
              : null
          }
          hint={prev && stats.sessions ? null : stats.currentStreak > 1 ? `${stats.currentStreak} wins in a row` : `${stats.victories} won · ${stats.defeats} lost`}
        />
        <StatTile
          label="Latest score"
          count={latest ? Number(latest.score) || 0 : undefined}
          value="—"
          icon="score"
          tone="indigo"
          spark={sparks.score.length > 1 ? sparks.score : null}
          sparkLabel="Scores of the last battles"
          trend={scoreDelta !== null ? { value: scoreDelta, label: formatNumber(Math.abs(scoreDelta)), suffix: "vs previous battle" } : null}
          hint={scoreDelta !== null ? null : `best ${formatNumber(stats.bestScore)}`}
        />
        <StatTile
          label="Active time"
          count={stats.totalDurationSec}
          format={(v) => formatDuration(v)}
          icon="clock"
          tone="amber"
          spark={sessions.length ? sparks.time : null}
          sparkLabel={`Active time per ${sparkWord}`}
          trend={
            prev
              ? {
                  value: stats.totalDurationSec - prev.totalDurationSec,
                  label: formatDuration(Math.abs(stats.totalDurationSec - prev.totalDurationSec)).replace("—", "0s"),
                  suffix: vsPrev,
                }
              : null
          }
          hint={prev ? null : stats.sessions ? `${formatDuration(stats.avgDurationSec)} per battle` : "No battles yet"}
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
              <p className="text-xl font-extrabold tabular-nums text-[var(--ink)]">{stats.currentStreak}</p>
              <p className="text-xs font-semibold text-[var(--muted)]">Current win streak</p>
            </div>
            <div className="px-3 py-3">
              <p className="text-xl font-extrabold tabular-nums text-[var(--ink)]">{stats.bestStreak}</p>
              <p className="text-xs font-semibold text-[var(--muted)]">Best win streak</p>
            </div>
          </div>
        </Card>
      </div>

      <ActivityCards />

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
                <span className="flex-1 text-sm font-semibold text-[var(--ink-secondary)]">{row.label}</span>
                <span className="text-base font-extrabold tabular-nums text-[var(--ink)]">{formatNumber(row.value)}</span>
              </li>
            ))}
          </ul>
          {noHitData && (
            <p className="border-t border-[var(--border)] bg-[var(--warn-soft)] px-5 py-2.5 text-xs font-semibold text-[var(--warn-ink)]">
              Some battles arrived with 0 hits/damage (recorded before hits were reported).
            </p>
          )}
        </Card>

        <Card className="overflow-hidden lg:col-span-2 xl:col-span-4">
          <CardHeader title="Personal records" subtitle="Click a record to open that battle" />
          <ul className="divide-y divide-[var(--border)]">
            <RecordRow icon="trophy" label="Highest score" session={recs.highScore} value={formatNumber(recs.highScore?.score)} onOpen={openSession} />
            <RecordRow icon="timer" label="Fastest victory" session={recs.fastestWin} value={formatDuration(recs.fastestWin?.durationSec)} onOpen={openSession} />
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
              <Link to="/dashboard/sessions" className={link}>
                View all
              </Link>
            }
          />
          {sessions.length ? (
            <div className="p-4 sm:p-5">
              <SessionList rows={sessions.slice(0, 5)} onSelect={(s) => openSession(s.id)} />
            </div>
          ) : (
            <EmptyState title="No battles yet" description="Play a round in the headset and it lands here within a second." />
          )}
        </Card>
        <Card className="overflow-hidden xl:col-span-4">
          <CardHeader
            title="Latest activity"
            actions={
              <Link to="/dashboard/live" className={link}>
                Live Arena
              </Link>
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
