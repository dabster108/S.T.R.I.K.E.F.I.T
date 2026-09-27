import { useMemo, useState } from "react";
import { levelRank } from "../lib/derive.js";
import { formatDuration, formatNumber, formatRelative } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { LevelBadge, OutcomeBadge } from "../components/ui/Badge.jsx";
import Icon from "../components/ui/Icon.jsx";

const MODES = [
  { id: "best", label: "Best per player" },
  { id: "all", label: "All runs" },
];

const PODIUM = [
  { place: 2, height: "h-20", ring: "ring-gray-300", medal: "bg-gray-100 text-gray-600" },
  { place: 1, height: "h-28", ring: "ring-amber-300", medal: "bg-amber-100 text-amber-700" },
  { place: 3, height: "h-14", ring: "ring-orange-200", medal: "bg-orange-50 text-orange-700" },
];

const th = "px-4 py-2.5 text-left text-[0.68rem] font-semibold uppercase tracking-wide text-[var(--muted)]";
const td = "px-4 py-3 text-sm whitespace-nowrap tabular-nums";

export default function LeaderboardView({ sessions, openSession }) {
  const [mode, setMode] = useState("best");
  const [level, setLevel] = useState("all");

  const levels = useMemo(
    () => [{ id: "all", label: "All levels" }, ...[...new Set(sessions.map((s) => s.level))].sort((a, b) => levelRank(a) - levelRank(b)).map((l) => ({ id: l, label: l }))],
    [sessions]
  );

  const ranked = useMemo(() => {
    const pool = sessions.filter((s) => level === "all" || s.level === level);
    const sorted = [...pool].sort((a, b) => (Number(b.score) || 0) - (Number(a.score) || 0) || (a.durationSec || 1e9) - (b.durationSec || 1e9));
    if (mode === "all") return sorted.slice(0, 50);
    const seen = new Set();
    return sorted.filter((s) => !seen.has(s.playerName) && seen.add(s.playerName));
  }, [sessions, mode, level]);

  const podium = PODIUM.map((p) => ({ ...p, s: ranked[p.place - 1] })).filter((p) => p.s);

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Rankings"
        title="Leaderboard"
        description="Highest scores across all recorded battles. Ties go to the faster run."
        actions={
          <>
            <SegmentedControl label="Ranking mode" options={MODES} value={mode} onChange={setMode} />
            <Select label="Level" value={level} onChange={setLevel} options={levels} />
          </>
        }
      />

      {ranked.length === 0 ? (
        <Card>
          <EmptyState icon="trophy" title="No scores yet" description="Finish a battle to claim the top spot." />
        </Card>
      ) : (
        <>
          <Card className="px-4 pb-0 pt-8">
            <div className="mx-auto flex max-w-xl items-end justify-center gap-3 sm:gap-6">
              {podium.map(({ place, height, ring, medal, s }) => (
                <button
                  key={place}
                  type="button"
                  onClick={() => openSession(s.id)}
                  className="group flex w-1/3 max-w-[10rem] flex-col items-center text-center"
                >
                  <span className={`flex h-12 w-12 items-center justify-center rounded-full bg-[var(--surface)] text-base font-bold text-[var(--ink)] ring-4 ${ring} transition-transform group-hover:-translate-y-0.5`}>
                    {s.playerName.slice(0, 2).toUpperCase()}
                  </span>
                  <span className="mt-2 w-full truncate text-sm font-semibold text-[var(--ink)]">{s.playerName}</span>
                  <span className="text-lg font-bold tabular-nums text-[var(--accent)]">{formatNumber(s.score)}</span>
                  <span className="text-[0.7rem] text-[var(--muted)]">{s.level}</span>
                  <span className={`mt-3 flex w-full ${height} items-start justify-center rounded-t-xl border border-b-0 border-[var(--border)] bg-[var(--surface-muted)] pt-3`}>
                    <span className={`inline-flex h-7 w-7 items-center justify-center rounded-full text-sm font-bold ${medal}`}>{place}</span>
                  </span>
                </button>
              ))}
            </div>
          </Card>

          <Card className="overflow-hidden">
            <CardHeader title={mode === "best" ? "Best run per player" : "Top 50 runs"} subtitle={`${ranked.length} ranked`} />
            <div className="scrollbar-thin relative overflow-x-auto">
              <table className="w-full min-w-[640px] border-collapse">
                <thead>
                  <tr className="border-b border-[var(--border)] bg-[var(--surface-muted)]">
                    <th className={`${th} w-16`}>Rank</th>
                    <th className={th}>Player</th>
                    <th className={`${th} text-right`}>Score</th>
                    <th className={th}>Result</th>
                    <th className={th}>Level</th>
                    <th className={`${th} text-right`}>Duration</th>
                    <th className={th}>When</th>
                  </tr>
                </thead>
                <tbody>
                  {ranked.map((s, i) => (
                    <tr
                      key={s.id}
                      onClick={() => openSession(s.id)}
                      className="cursor-pointer border-b border-[var(--border)] last:border-0 hover:bg-[var(--surface-muted)]"
                    >
                      <td className={`${td} font-semibold text-[var(--muted)]`}>
                        {i < 3 ? <Icon name="trophy" className={`h-4 w-4 ${["text-amber-500", "text-gray-400", "text-orange-400"][i]}`} /> : `#${i + 1}`}
                      </td>
                      <td className={`${td} font-medium text-[var(--ink)]`}>{s.playerName}</td>
                      <td className={`${td} text-right font-semibold text-[var(--ink)]`}>{formatNumber(s.score)}</td>
                      <td className={td}>
                        <OutcomeBadge outcome={s.outcome} />
                      </td>
                      <td className={td}>
                        <LevelBadge level={s.level} />
                      </td>
                      <td className={`${td} text-right text-[var(--ink-secondary)]`}>{formatDuration(s.durationSec)}</td>
                      <td className={`${td} text-[var(--muted)]`}>{formatRelative(s.endedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  );
}
