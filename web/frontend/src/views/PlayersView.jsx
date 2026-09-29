import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { byPlayer } from "../lib/derive.js";
import { useLiveIds } from "../live/LiveProvider.jsx";
import { liveStore } from "../live/liveStore.js";
import { LiveChip } from "../components/live/LiveFight.jsx";
import { SearchInput } from "../components/ui/Controls.jsx";
import { formatDuration, formatNumber, formatPercent, formatRelative } from "../lib/format.js";
import { Card, PageHeader } from "../components/ui/Card.jsx";
import { EmptyState, ProgressBar } from "../components/ui/Feedback.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import Sparkline from "../components/charts/Sparkline.jsx";
import PlayerName from "../components/players/PlayerName.jsx";

const nameLink =
  "block truncate text-[var(--ink)] after:absolute after:inset-0 after:rounded-2xl after:content-[''] hover:text-[var(--accent)] focus-visible:outline-none focus-visible:after:ring-2 focus-visible:after:ring-[var(--accent)]";

function Name({ p, className = "font-bold" }) {
  return (
    <PlayerName name={p.playerName}>
      <Link to={`/dashboard/player/${encodeURIComponent(p.playerName)}`} className={`${nameLink} ${className}`}>
        {p.playerName}
      </Link>
    </PlayerName>
  );
}

const rankLabel = (i) => String(i + 1).padStart(2, "0");

// #1 by best score: wider, the score set large, a full sparkline.
function LeaderCard({ p, rank, playing }) {
  return (
    <Card as="article" className="group/card lift relative overflow-hidden hover:border-[var(--accent-ring)] md:col-span-2">
      <span className="absolute inset-y-0 left-0 w-1 bg-[var(--accent)]" aria-hidden />
      <div className="grid gap-6 p-6 pl-7 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
        <div className="min-w-0">
          <div className="flex items-center gap-3">
            <span className="t-index">{rankLabel(rank)}</span>
            <span className="t-kicker text-[var(--accent-strong)]">Top player · best score</span>
            {playing && <LiveChip label="Playing" />}
          </div>
          <div className="mt-3 text-2xl tracking-tight">
            <Name p={p} className="font-extrabold" />
          </div>
          <p className="mt-1 text-xs font-medium text-[var(--muted)]">
            Last played {formatRelative(p.lastPlayedAt)} · most on <LevelBadge level={p.favouriteLevel} />
          </p>
          <p className="t-figure mt-6 text-[var(--ink)]">{formatNumber(p.bestScore)}</p>
        </div>
        <div className="min-w-0 sm:w-64">
          <Sparkline values={p.scores.slice(-20)} width={256} height={64} label={`${p.playerName} score trend`} />
          <dl className="mt-4 grid grid-cols-3 gap-px overflow-hidden rounded-lg bg-[var(--border)] text-center ring-1 ring-[var(--border)]">
            {[
              ["Battles", formatNumber(p.sessions)],
              ["Win rate", formatPercent(p.winRate)],
              ["Play time", formatDuration(p.totalDurationSec)],
            ].map(([k, v]) => (
              <div key={k} className="bg-[var(--surface)] px-2 py-2">
                <dt className="text-[0.6875rem] font-semibold text-[var(--muted)]">{k}</dt>
                <dd className="text-sm font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
              </div>
            ))}
          </dl>
        </div>
      </div>
    </Card>
  );
}

function PlayerCard({ p, rank, playing }) {
  return (
    <Card as="article" className="group/card lift p-5 hover:border-[var(--accent-ring)]">
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-sm font-extrabold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">
            {p.playerName.slice(0, 2).toUpperCase()}
          </span>
          <div className="min-w-0">
            <Name p={p} />
            <p className="text-xs font-medium text-[var(--muted)]">Last played {formatRelative(p.lastPlayedAt)}</p>
          </div>
        </div>
        {playing ? <LiveChip label="Playing" /> : <span className="t-index pt-1 text-[var(--muted)]">{rankLabel(rank)}</span>}
      </div>

      <div className="mt-5 flex items-end justify-between gap-3">
        <div>
          <p className="t-kicker">Best score</p>
          <p className="mt-1 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{formatNumber(p.bestScore)}</p>
        </div>
        <Sparkline values={p.scores.slice(-15)} label={`${p.playerName} score trend`} />
      </div>

      <div className="mt-4">
        <div className="mb-1.5 flex justify-between text-xs">
          <span className="font-semibold text-[var(--muted)]">Win rate</span>
          <span className="font-semibold tabular-nums text-[var(--ink)]">
            {formatPercent(p.winRate)} <span className="font-medium text-[var(--muted)]">({p.victories}W · {p.defeats}L)</span>
          </span>
        </div>
        <ProgressBar value={p.winRate} tone="good" label={`${p.playerName} win rate`} />
      </div>

      <dl className="mt-5 grid grid-cols-3 gap-2 border-t border-[var(--border)] pt-4">
        {[
          { label: "Battles", value: formatNumber(p.sessions) },
          { label: "Avg score", value: formatNumber(p.avgScore) },
          { label: "Play time", value: formatDuration(p.totalDurationSec) },
        ].map((m) => (
          <div key={m.label}>
            <dt className="text-xs font-semibold text-[var(--muted)]">{m.label}</dt>
            <dd className="text-sm font-extrabold tabular-nums text-[var(--ink)]">{m.value}</dd>
          </div>
        ))}
      </dl>

      <p className="mt-4 flex items-center gap-1.5 text-xs font-medium text-[var(--muted)]">
        Most played: <LevelBadge level={p.favouriteLevel} />
        {p.bestStreak > 1 && <span className="ml-auto">Best streak {p.bestStreak}</span>}
      </p>
    </Card>
  );
}

export default function PlayersView({ sessions }) {
  const all = useMemo(() => byPlayer(sessions), [sessions]);
  const [query, setQuery] = useState("");
  const ids = useLiveIds();
  const playing = useMemo(
    () => new Set(ids.map((id) => liveStore.getSession(id)).filter((e) => e && !e.finishedAt).map((e) => e.session.playerName)),
    [ids]
  );
  const players = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? all.filter((p) => p.playerName.toLowerCase().includes(q)) : all;
  }, [all, query]);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Profiles"
        title="Players"
        description="Per-player progress, built from every recorded battle. Hover a name and use the pencil to rename a player everywhere; new fights still arrive under the name set in Unity (SessionReporter → Player Name)."
        actions={<SearchInput value={query} onChange={setQuery} placeholder="Search players…" label="Search players" />}
      />

      {players.length === 0 ? (
        <Card>
          <EmptyState
            icon="players"
            title={all.length ? `No player matches “${query}”` : "No players yet"}
            description={all.length ? "Try part of the name." : "Players appear after their first finished battle."}
          />
        </Card>
      ) : (
        <div className="stagger grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {players.map((p) => {
            const rank = all.indexOf(p);
            const Cmp = rank === 0 && all.length > 1 ? LeaderCard : PlayerCard;
            return <Cmp key={p.playerName} p={p} rank={rank} playing={playing.has(p.playerName)} />;
          })}
        </div>
      )}
    </div>
  );
}
