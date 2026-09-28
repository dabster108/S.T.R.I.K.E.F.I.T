import { useMemo, useState } from "react";
import { levelRank } from "../lib/derive.js";
import { formatDuration, formatNumber, formatRelative } from "../lib/format.js";
import { levelName } from "../lib/game.js";
import { useCountUp } from "../hooks/motion.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import { LevelBadge, OutcomeBadge } from "../components/ui/Badge.jsx";
import Icon from "../components/ui/Icon.jsx";

const MODES = [
  { id: "best", label: "Best per player" },
  { id: "all", label: "All runs" },
];

const OUTCOMES = [
  { id: "all", label: "Any outcome" },
  { id: "victory", label: "Victories" },
  { id: "defeat", label: "Defeats" },
];

// Visual order 2-1-3; blocks rise 3rd, 2nd, then 1st.
const PODIUM = [
  { place: 2, height: "h-24 sm:h-28", block: "bg-gradient-to-b from-[var(--blue-100)] to-[var(--accent-soft)] text-[var(--accent-strong)]", medal: "bg-gray-100 text-gray-700 ring-gray-300", delay: 150 },
  { place: 1, height: "h-32 sm:h-40", block: "bg-gradient-to-b from-[var(--accent)] to-[var(--accent-strong)] text-white", medal: "bg-amber-100 text-amber-800 ring-amber-300", delay: 300 },
  { place: 3, height: "h-16 sm:h-20", block: "bg-gradient-to-b from-[var(--blue-100)] to-[var(--accent-soft)] text-[var(--accent-strong)]", medal: "bg-orange-50 text-orange-800 ring-orange-200", delay: 0 },
];

function Score({ value }) {
  return formatNumber(useCountUp(Number(value) || 0, true, 1100));
}

function Podium({ entries, openSession }) {
  return (
    <div className="mx-auto flex max-w-2xl items-end justify-center gap-2 sm:gap-4">
      {entries.map(({ place, height, block, medal, delay, s }) => (
        <button
          key={place}
          type="button"
          onClick={() => openSession(s.id)}
          aria-label={`Rank ${place}: ${s.playerName}, ${formatNumber(s.score)} points on ${levelName(s.level)}`}
          className="press group flex w-1/3 max-w-[11rem] flex-col items-center rounded-t-2xl text-center"
        >
          <span className="fade-up relative flex flex-col items-center" style={{ "--d": `${delay + 450}ms` }}>
            {place === 1 && <Icon name="crown" className="float-y mb-1 h-6 w-6 text-amber-500" />}
            <span
              className={`flex items-center justify-center rounded-full bg-[var(--surface)] font-extrabold text-[var(--ink)] shadow-[var(--shadow-lift)] ring-4 transition-transform duration-300 group-hover:-translate-y-1 ${
                place === 1 ? "h-16 w-16 text-lg ring-[var(--accent)]" : "h-12 w-12 text-base ring-[var(--accent-ring)]"
              }`}
            >
              {s.playerName.slice(0, 2).toUpperCase()}
            </span>
            <span className="mt-2 w-full max-w-[9rem] truncate text-sm font-bold text-[var(--ink)]">{s.playerName}</span>
            <span className={`font-extrabold tabular-nums text-[var(--accent-strong)] ${place === 1 ? "text-2xl" : "text-lg"}`}>
              <Score value={s.score} />
            </span>
            <span className="text-xs font-semibold text-[var(--muted)]">{levelName(s.level)}</span>
          </span>
          <span className={`rise mt-3 flex w-full ${height} items-start justify-center rounded-t-2xl pt-3 ${block}`} style={{ "--d": `${delay}ms` }}>
            <span className={`inline-flex h-8 w-8 items-center justify-center rounded-full text-sm font-extrabold ring-2 ${medal}`}>{place}</span>
          </span>
        </button>
      ))}
    </div>
  );
}

export default function LeaderboardView({ sessions, openSession }) {
  const [mode, setMode] = useState("best");
  const [level, setLevel] = useState("all");
  const [outcome, setOutcome] = useState("all");

  const levels = useMemo(
    () => [{ id: "all", label: "All levels" }, ...[...new Set(sessions.map((s) => s.level))].sort((a, b) => levelRank(a) - levelRank(b)).map((l) => ({ id: l, label: l }))],
    [sessions]
  );

  const ranked = useMemo(() => {
    const pool = sessions.filter((s) => (level === "all" || s.level === level) && (outcome === "all" || s.outcome === outcome));
    const sorted = [...pool].sort((a, b) => (Number(b.score) || 0) - (Number(a.score) || 0) || (a.durationSec || 1e9) - (b.durationSec || 1e9));
    if (mode === "all") return sorted.slice(0, 50);
    const seen = new Set();
    return sorted.filter((s) => !seen.has(s.playerName) && seen.add(s.playerName));
  }, [sessions, mode, level, outcome]);

  const podium = PODIUM.map((p) => ({ ...p, s: ranked[p.place - 1] })).filter((p) => p.s);
  const rest = ranked.slice(3);
  const top = Math.max(1, Number(ranked[0]?.score) || 0);
  const filterKey = `${mode}-${level}-${outcome}`;

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
            <Select label="Outcome" value={outcome} onChange={setOutcome} options={OUTCOMES} />
          </>
        }
      />

      {ranked.length === 0 ? (
        <Card>
          <EmptyState icon="trophy" title="No scores yet" description="Finish a battle to claim the top spot." />
        </Card>
      ) : (
        <>
          <Card className="relative overflow-hidden px-4 pb-0 pt-10">
            <div className="pointer-events-none absolute inset-x-0 top-0 h-40 bg-gradient-to-b from-[var(--accent-soft)] to-transparent" aria-hidden />
            <div key={filterKey} className="relative">
              <Podium entries={podium} openSession={openSession} />
            </div>
          </Card>

          {rest.length > 0 && (
            <Card className="overflow-hidden">
              <CardHeader title={mode === "best" ? "Everyone else" : "The rest of the top 50"} subtitle={`${ranked.length} ranked · bars are relative to the top score`} />
              <ol key={filterKey} className="stagger space-y-2 p-4 sm:p-5">
                {rest.map((s, i) => {
                  const rank = i + 4;
                  const share = Math.max(0.02, (Number(s.score) || 0) / top);
                  return (
                    <li key={s.id}>
                      <button
                        type="button"
                        onClick={() => openSession(s.id)}
                        className="press group flex w-full items-center gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2.5 text-left hover:border-[var(--accent-ring)] hover:bg-[var(--surface-muted)] sm:gap-4 sm:px-4"
                        aria-label={`Rank ${rank}: ${s.playerName}, ${formatNumber(s.score)} points`}
                      >
                        <span className="w-8 shrink-0 text-center text-sm font-extrabold tabular-nums text-[var(--muted)]">#{rank}</span>
                        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-extrabold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">
                          {s.playerName.slice(0, 2).toUpperCase()}
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="flex flex-wrap items-center gap-2">
                            <span className="truncate text-sm font-bold text-[var(--ink)]">{s.playerName}</span>
                            <LevelBadge level={s.level} />
                            <span className="hidden sm:inline-flex">
                              <OutcomeBadge outcome={s.outcome} />
                            </span>
                          </span>
                          <span className="mt-1.5 block h-1.5 w-full rounded-full bg-gray-100">
                            <span className="block h-full" style={{ width: `${share * 100}%` }}>
                              <span className="grow-x block h-full w-full rounded-full bg-[var(--accent)]" style={{ "--i": Math.min(i, 10) }} />
                            </span>
                          </span>
                        </span>
                        <span className="hidden shrink-0 text-right text-xs font-semibold text-[var(--muted)] md:block">
                          <span className="block">{formatDuration(s.durationSec)}</span>
                          <span className="block font-medium">{formatRelative(s.endedAt)}</span>
                        </span>
                        <span className="w-16 shrink-0 text-right text-base font-extrabold tabular-nums text-[var(--ink)]">{formatNumber(s.score)}</span>
                      </button>
                    </li>
                  );
                })}
              </ol>
            </Card>
          )}
        </>
      )}
    </div>
  );
}
