import { useMemo } from "react";
import { byPlayer } from "../lib/derive.js";
import { formatDuration, formatNumber, formatPercent, formatRelative } from "../lib/format.js";
import { Card, PageHeader } from "../components/ui/Card.jsx";
import { EmptyState, ProgressBar } from "../components/ui/Feedback.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import Sparkline from "../components/charts/Sparkline.jsx";

export default function PlayersView({ sessions }) {
  const players = useMemo(() => byPlayer(sessions), [sessions]);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Profiles"
        title="Players"
        description="Per-player progress, built from every recorded battle. Set the name in Unity on SessionReporter → Player Name."
      />

      {players.length === 0 ? (
        <Card>
          <EmptyState icon="players" title="No players yet" description="Players appear after their first finished battle." />
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {players.map((p, i) => (
            <Card key={p.playerName} as="article" className="p-5">
              <div className="flex items-start justify-between gap-3">
                <div className="flex min-w-0 items-center gap-3">
                  <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-sm font-bold text-[var(--accent)]">
                    {p.playerName.slice(0, 2).toUpperCase()}
                  </span>
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-[var(--ink)]">{p.playerName}</p>
                    <p className="text-xs text-[var(--muted)]">Last played {formatRelative(p.lastPlayedAt)}</p>
                  </div>
                </div>
                <span className="rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-[0.7rem] font-semibold text-[var(--muted)] ring-1 ring-inset ring-[var(--border)]">
                  #{i + 1}
                </span>
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
