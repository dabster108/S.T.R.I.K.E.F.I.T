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
        description="Per-player progress, built from every recorded battle. Set the name in Unity on SessionReporter → Player Name."
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
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {players.map((p) => (
            <Card key={p.playerName} as="article" className="p-5">
              <div className="flex items-start justify-between gap-3">
                <div className="flex min-w-0 items-center gap-3">
                  <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-sm font-bold text-[var(--accent)]">
                    {p.playerName.slice(0, 2).toUpperCase()}
                  </span>
                  <div className="min-w-0">
                    <Link to={`/dashboard/player/${encodeURIComponent(p.playerName)}`} className="block truncate font-semibold text-[var(--ink)] hover:text-[var(--accent)] hover:underline">
                      {p.playerName}
                    </Link>
                    <p className="text-xs text-[var(--muted)]">Last played {formatRelative(p.lastPlayedAt)}</p>
                  </div>
                </div>
                {playing.has(p.playerName) ? (
                  <LiveChip label="Playing" />
                ) : (
                  <span className="rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-[0.7rem] font-semibold text-[var(--muted)] ring-1 ring-inset ring-[var(--border)]">
                    #{all.indexOf(p) + 1}
                  </span>
                )}
              </div>

              <div className="mt-5 flex items-end justify-between gap-3">
                <div>
                  <p className="text-[0.7rem] font-medium text-[var(--muted)]">Best score</p>
                  <p className="text-2xl font-bold tabular-nums text-[var(--ink)]">{formatNumber(p.bestScore)}</p>
                </div>
                <Sparkline values={p.scores.slice(-15)} label={`${p.playerName} score trend`} />
              </div>

              <div className="mt-4">
                <div className="mb-1.5 flex justify-between text-xs">
                  <span className="text-[var(--muted)]">Win rate</span>
                  <span className="font-semibold tabular-nums text-[var(--ink)]">
                    {formatPercent(p.winRate)} <span className="font-normal text-[var(--muted)]">({p.victories}W · {p.defeats}L)</span>
                  </span>
                </div>
                <ProgressBar value={p.winRate} tone="good" label={`${p.playerName} win rate`} />
              </div>

              <dl className="mt-5 grid grid-cols-3 gap-2 border-t border-[var(--border)] pt-4 text-center">
                {[
                  { label: "Battles", value: formatNumber(p.sessions) },
                  { label: "Avg score", value: formatNumber(p.avgScore) },
                  { label: "Play time", value: formatDuration(p.totalDurationSec) },
                ].map((m) => (
                  <div key={m.label}>
                    <dt className="text-[0.68rem] text-[var(--muted)]">{m.label}</dt>
                    <dd className="text-sm font-semibold tabular-nums text-[var(--ink)]">{m.value}</dd>
                  </div>
                ))}
              </dl>

              <p className="mt-4 flex items-center gap-1.5 text-xs text-[var(--muted)]">
                Most played: <LevelBadge level={p.favouriteLevel} />
                {p.bestStreak > 1 && <span className="ml-auto">Best streak {p.bestStreak}</span>}
              </p>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
