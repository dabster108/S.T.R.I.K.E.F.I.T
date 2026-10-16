import { useEffect, useMemo, useState } from "react";
import { levelRank } from "../lib/derive.js";
import { MIN_HITS_FOR_EFFORT, MIN_IMPROVED_SESSIONS, bestPerPlayer, boardPool, compareRuns, mostImproved, youVsLastWeek } from "../lib/leaderboard.js";
import { api } from "../lib/api.js";
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

const BOARDS = [
  { id: "weekly", label: "This week" },
  { id: "alltime", label: "All time" },
  { id: "level", label: "Per level" },
  { id: "improved", label: "Most improved" },
];

const MEASURES = [
  { id: "score", label: "By score" },
  { id: "effort", label: "By effort" },
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

// GET /api/leaderboard: the board the headset shows. Rows come back session-shaped so the views below need no changes; null
// while loading or when the server is unreachable (the caller then ranks the loaded sessions with the same rule).
const fromBoardRow = (r) => ({ id: r.sessionId, playerName: r.name, score: r.score, level: r.level, outcome: r.outcome, durationSec: r.durationSec, endedAt: r.date });
function useServerBoard(level, limit, enabled, refreshKey) {
  const [rows, setRows] = useState(null);
  useEffect(() => {
    if (!enabled) return undefined;
    let live = true;
    api
      .leaderboard({ level, limit })
      .then((d) => live && setRows((d.rows || []).map(fromBoardRow)))
      .catch(() => live && setRows(null));
    return () => {
      live = false;
    };
  }, [level, limit, enabled, refreshKey]);
  return enabled ? rows : null;
}

const RULE_NOTE = "Ranked by best score per level";

const signed = (v, digits = 0) => `${v > 0 ? "+" : v < 0 ? "−" : "±"}${Math.abs(v).toFixed(digits)}`;

// One player's week against the week before (finished fights, best and average score).
function YouVsLastWeek({ sessions }) {
  const names = useMemo(() => [...new Set(sessions.map((s) => s.playerName))].sort((a, b) => a.localeCompare(b)), [sessions]);
  const latest = useMemo(() => [...sessions].sort((a, b) => new Date(b.startedAt) - new Date(a.startedAt))[0]?.playerName, [sessions]);
  const [who, setWho] = useState(null);
  const name = who || latest;
  if (!name) return null;
  const { thisWeek: a, lastWeek: b } = youVsLastWeek(sessions, name);
  const line =
    a.fights === 0 && b.fights === 0
      ? "No finished fights this week or last week."
      : `This week: ${a.fights} fight${a.fights === 1 ? "" : "s"}${a.best !== null ? `, best ${formatNumber(a.best)}` : ""}. Last week: ${b.fights} fight${b.fights === 1 ? "" : "s"}${b.best !== null ? `, best ${formatNumber(b.best)}` : ""}.`;
  const delta = a.best !== null && b.best !== null ? a.best - b.best : null;
  return (
    <Card className="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3">
      <Select label="You are" value={name} onChange={setWho} options={names.map((n) => ({ id: n, label: n }))} />
      <p className="min-w-0 flex-1 text-sm font-semibold text-[var(--ink-secondary)]">
        <span className="font-extrabold text-[var(--ink)]">You vs last week · </span>
        {line}
        {delta !== null && (
          <span className={`ml-1 font-extrabold ${delta > 0 ? "text-[var(--good-ink)]" : delta < 0 ? "text-[var(--bad)]" : "text-[var(--ink)]"}`}>
            ({delta > 0 ? "+" : delta < 0 ? "−" : "±"}
            {formatNumber(Math.abs(delta))} on your best)
          </span>
        )}
      </p>
    </Card>
  );
}

// Top runs per level, side by side: the server's level boards (the headset's L1 / L2 / L3 tabs), or the same rule locally.
function PerLevel({ sessions, openSession }) {
  const levels = [...new Set(sessions.map((s) => s.level))].sort((a, b) => levelRank(a) - levelRank(b));
  if (!levels.length) return null;
  return (
    <div className="stagger grid gap-6 lg:grid-cols-3">
      {levels.map((l) => (
        <PerLevelCard key={l} level={l} sessions={sessions} openSession={openSession} />
      ))}
    </div>
  );
}

function PerLevelCard({ level, sessions, openSession }) {
  const server = useServerBoard(level, 5, true, sessions.length);
  const local = useMemo(() => bestPerPlayer(sessions.filter((s) => s.level === level)).slice(0, 5), [sessions, level]);
  const top = server || local;
  return (
    <Card className="overflow-hidden">
      <CardHeader title={levelName(level)} subtitle={`${RULE_NOTE} · ${top.length} shown`} actions={<LevelBadge level={level} />} />
      <ol className="divide-y divide-[var(--border)]">
        {top.map((s, i) => (
          <li key={s.id}>
            <button type="button" onClick={() => openSession(s.id)} className="press flex w-full items-center gap-3 px-5 py-2.5 text-left hover:bg-[var(--surface-muted)]">
              <span className="t-index w-6 text-[var(--muted)]">{String(i + 1).padStart(2, "0")}</span>
              <span className="min-w-0 flex-1 truncate font-bold text-[var(--ink)]">{s.playerName}</span>
              <span className="font-extrabold tabular-nums text-[var(--ink)]">
                <Score value={s.score} />
              </span>
            </button>
          </li>
        ))}
      </ol>
    </Card>
  );
}

// Most improved: the latest third of each player's fights against all their earlier ones (lib/leaderboard.js).
function MostImproved({ sessions }) {
  const [measure, setMeasure] = useState("score");
  const rows = useMemo(() => mostImproved(sessions, measure), [sessions, measure]);
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="Most improved"
        subtitle={
          measure === "score"
            ? `Average of each player's latest third of fights against their earlier ones. Score is shown as % of the level's median score, so levels compare. Players with ${MIN_IMPROVED_SESSIONS}+ won or lost fights (abandoned fights left out).`
            : `Full-power share of hits (fights with ${MIN_HITS_FOR_EFFORT}+ hits), latest third against earlier fights, in percentage points. Players with ${MIN_IMPROVED_SESSIONS}+ such fights.`
        }
        actions={<SegmentedControl label="Improvement measure" options={MEASURES} value={measure} onChange={setMeasure} />}
      />
      {rows.length === 0 ? (
        <EmptyState icon="score" title="Nobody qualifies yet" description={`A player needs at least ${MIN_IMPROVED_SESSIONS} won or lost fights${measure === "effort" ? ` with ${MIN_HITS_FOR_EFFORT}+ hits` : ""} before a change can be measured.`} />
      ) : (
        <div className="scrollbar-thin overflow-x-auto">
          <table className="w-full min-w-[560px] text-left text-sm">
            <thead>
              <tr className="border-b-2 border-[var(--ink)] bg-[var(--surface-muted)]">
                {[["Rank", "w-16 pl-5"], ["Player", ""], ["Fights", "text-right"], ["Earlier", "text-right"], ["Latest third", "text-right"], ["Change", "pr-5 text-right"]].map(([h, cls]) => (
                  <th key={h} scope="col" className={`t-kicker px-3 py-2.5 font-bold ${cls}`}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="stagger divide-y divide-[var(--border)]">
              {rows.map((r, i) => (
                <tr key={r.playerName}>
                  <td className="py-3 pl-5 pr-3">
                    <span className="t-index pl-1.5 text-[var(--muted)]">{String(i + 1).padStart(2, "0")}</span>
                  </td>
                  <td className="max-w-[14rem] truncate px-3 py-3 font-bold text-[var(--ink)]">{r.playerName}</td>
                  <td className="px-3 py-3 text-right tabular-nums">{r.sessions}</td>
                  <td className="px-3 py-3 text-right tabular-nums">{r.earlier.toFixed(0)}%</td>
                  <td className="px-3 py-3 text-right tabular-nums">{r.recent.toFixed(0)}%</td>
                  <td className={`py-3 pl-3 pr-5 text-right font-extrabold tabular-nums ${r.change > 0 ? "text-[var(--good-ink)]" : r.change < 0 ? "text-[var(--bad)]" : "text-[var(--ink)]"}`}>
                    {signed(r.change)} {measure === "score" ? "pts" : "pp"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

export default function LeaderboardView({ sessions, openSession }) {
  const [board, setBoard] = useState("alltime");
  const [mode, setMode] = useState("best");
  const [level, setLevel] = useState("all");
  const [outcome, setOutcome] = useState("all");

  const levels = useMemo(
    () => [{ id: "all", label: "All levels" }, ...[...new Set(sessions.map((s) => s.level))].sort((a, b) => levelRank(a) - levelRank(b)).map((l) => ({ id: l, label: l }))],
    [sessions]
  );

  // All time, best per player, any outcome = exactly the headset's scoreboard (GET /api/leaderboard). Every other filter is
  // ranked here from the loaded sessions with the same rule (lib/leaderboard.js compareRuns).
  const serverBoard = board === "alltime" && mode === "best" && outcome === "all";
  const server = useServerBoard(level, 100, serverBoard, sessions.length);
  const local = useMemo(() => {
    const pool = boardPool(sessions, board === "weekly" ? "weekly" : "alltime").filter((s) => (level === "all" || s.level === level) && (outcome === "all" || s.outcome === outcome));
    if (mode === "all") return [...pool].filter((s) => s.status !== "live").sort(compareRuns).slice(0, 50);
    return bestPerPlayer(pool);
  }, [sessions, mode, level, outcome, board]);
  const ranked = server || local;

  const podium = PODIUM.map((p) => ({ ...p, s: ranked[p.place - 1] })).filter((p) => p.s);
  const top = Math.max(1, Number(ranked[0]?.score) || 0);
  const filterKey = `${board}-${mode}-${level}-${outcome}`;
  const leader = ranked[0];
  const runnerUp = ranked[1];
  const margin = leader && runnerUp ? (Number(leader.score) || 0) - (Number(runnerUp.score) || 0) : null;
  const MEDAL = ["bg-amber-100 text-amber-800 ring-amber-300", "bg-gray-100 text-gray-700 ring-gray-300", "bg-orange-50 text-orange-800 ring-orange-200"];

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Rankings"
        title="Leaderboard"
        description={`${RULE_NOTE}, the same board the headset shows. Ties go to the faster run. This week starts on Monday.`}
        actions={
          board === "weekly" || board === "alltime" ? (
            <>
              <SegmentedControl label="Ranking mode" options={MODES} value={mode} onChange={setMode} />
              <Select label="Level" value={level} onChange={setLevel} options={levels} />
              <Select label="Outcome" value={outcome} onChange={setOutcome} options={OUTCOMES} />
            </>
          ) : null
        }
      />
      <SegmentedControl label="Board" options={BOARDS} value={board} onChange={setBoard} />
      <YouVsLastWeek sessions={sessions} />

      {board === "level" ? (
        <PerLevel sessions={sessions} openSession={openSession} />
      ) : board === "improved" ? (
        <MostImproved sessions={sessions} />
      ) : ranked.length === 0 ? (
        <Card>
          <EmptyState icon="trophy" title={board === "weekly" ? "No scores this week yet" : "No scores yet"} description="Finish a battle to claim the top spot." />
        </Card>
      ) : (
        <>
          {/* podium on the left, the leader's numbers on the right */}
          <Card className="relative grid overflow-hidden lg:grid-cols-12">
            <div className="relative px-4 pb-0 pt-10 lg:col-span-7">
              <div className="pointer-events-none absolute inset-x-0 top-0 h-40 bg-gradient-to-b from-[var(--accent-soft)] to-transparent" aria-hidden />
              <div key={filterKey} className="relative">
                <Podium entries={podium} openSession={openSession} />
              </div>
            </div>
            <div key={`lead-${filterKey}`} className="fade-in flex flex-col justify-between gap-6 border-t border-[var(--border)] p-6 sm:p-7 lg:col-span-5 lg:border-l lg:border-t-0">
              <div>
                <p className="flex items-center gap-2">
                  <span className="t-index">01</span>
                  <span className="t-kicker text-[var(--accent-strong)]">Leader</span>
                </p>
                <p className="mt-3 truncate text-xl font-extrabold tracking-tight text-[var(--ink)]">{leader.playerName}</p>
                <p className="t-figure mt-4 text-[var(--ink)]">
                  <Score value={leader.score} />
                </p>
                <p className="mt-3 text-sm font-semibold text-[var(--ink-secondary)]">
                  {margin === null ? (
                    "No one else on the board yet"
                  ) : margin > 0 ? (
                    <>
                      <span className="font-extrabold text-[var(--good-ink)]">+{formatNumber(margin)}</span> ahead of {runnerUp.playerName}
                    </>
                  ) : (
                    <>Level with {runnerUp.playerName}, faster run</>
                  )}
                </p>
              </div>
              <dl className="readout grid grid-cols-3 divide-x divide-[var(--border)] border-y border-[var(--border)] py-3 text-sm">
                {[
                  ["Level", levelName(leader.level)],
                  ["Time", formatDuration(leader.durationSec)],
                  ["When", formatRelative(leader.endedAt)],
                ].map(([k, v], i) => (
                  <div key={k} className={i ? "pl-3" : ""}>
                    <dt className="t-kicker">{k}</dt>
                    <dd className="mt-1 truncate font-extrabold text-[var(--ink)]">{v}</dd>
                  </div>
                ))}
              </dl>
              <button type="button" onClick={() => openSession(leader.id)} className="press self-start rounded text-xs font-semibold text-[var(--accent)] hover:text-[var(--accent-strong)] hover:underline">
                Open the winning battle
              </button>
            </div>
          </Card>

          <Card className="overflow-hidden">
            <CardHeader title="Full standings" subtitle={`${ranked.length} ranked${mode === "all" ? " (top 50 runs)" : ", one row per player"} · bars are relative to the top score`} />
            <div className="scrollbar-thin overflow-x-auto">
              <table key={filterKey} className="w-full min-w-[640px] text-left text-sm">
                <thead>
                  <tr className="border-b-2 border-[var(--ink)] bg-[var(--surface-muted)]">
                    {[
                      ["Rank", "w-16 pl-5"],
                      ["Player", ""],
                      ["Level", "hidden md:table-cell"],
                      ["Outcome", "hidden lg:table-cell"],
                      ["Time", "hidden md:table-cell text-right"],
                      ["Score", "w-56 pr-5 text-right"],
                    ].map(([h, cls]) => (
                      <th key={h} scope="col" className={`t-kicker px-3 py-2.5 font-bold ${cls}`}>
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="stagger divide-y divide-[var(--border)]">
                  {ranked.map((s, i) => {
                    const rank = i + 1;
                    const share = Math.max(0.02, (Number(s.score) || 0) / top);
                    const first = rank === 1;
                    return (
                      <tr
                        key={s.id}
                        onClick={() => openSession(s.id)}
                        className={`group cursor-pointer transition-colors ${first ? "relative bg-[var(--accent-soft)] hover:bg-[var(--accent-soft)]" : "hover:bg-[var(--surface-muted)]"}`}
                      >
                        <td className={`py-3 pl-5 pr-3 ${first ? "shadow-[inset_3px_0_0_var(--accent)]" : ""}`}>
                          {rank <= 3 ? (
                            <span className={`inline-flex h-7 w-7 items-center justify-center rounded-full text-xs font-extrabold ring-2 ${MEDAL[rank - 1]}`}>{rank}</span>
                          ) : (
                            <span className="t-index pl-1.5 text-[var(--muted)]">{String(rank).padStart(2, "0")}</span>
                          )}
                        </td>
                        <td className="px-3 py-3">
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              openSession(s.id);
                            }}
                            aria-label={`Rank ${rank}: ${s.playerName}, ${formatNumber(s.score)} points, open battle`}
                            className={`press max-w-[14rem] truncate rounded text-left ${first ? "text-base font-extrabold" : "font-bold"} text-[var(--ink)] group-hover:text-[var(--accent)]`}
                          >
                            {s.playerName}
                          </button>
                          <span className="block text-xs font-medium text-[var(--muted)]">{formatRelative(s.endedAt)}</span>
                        </td>
                        <td className="hidden px-3 py-3 md:table-cell">
                          <LevelBadge level={s.level} />
                        </td>
                        <td className="hidden px-3 py-3 lg:table-cell">
                          <OutcomeBadge outcome={s.outcome} />
                        </td>
                        <td className="readout hidden px-3 py-3 text-right font-semibold text-[var(--ink-secondary)] md:table-cell">{formatDuration(s.durationSec)}</td>
                        <td className="py-3 pl-3 pr-5">
                          <span className="flex items-center justify-end gap-3">
                            <span className="h-1.5 w-24 rounded-full bg-gray-100" aria-hidden>
                              <span className="block h-full" style={{ width: `${share * 100}%` }}>
                                <span className={`grow-x block h-full w-full rounded-full ${first ? "bg-[var(--accent)]" : "bg-[var(--blue-400)]"}`} style={{ "--i": Math.min(i, 10) }} />
                              </span>
                            </span>
                            <span className={`w-16 text-right tabular-nums text-[var(--ink)] ${first ? "text-lg font-extrabold" : "text-base font-extrabold"}`}>{formatNumber(s.score)}</span>
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  );
}
