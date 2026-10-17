import { useEffect, useMemo, useState } from "react";
import { RANGES, levelRank, sessionTime, summarize, toCsv } from "../lib/derive.js";
import { formatNumber } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { Pagination, SearchInput, SegmentedControl } from "../components/ui/Controls.jsx";
import { Select } from "../components/ui/Select.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import Button from "../components/ui/Button.jsx";
import { FigureStrip } from "../components/ui/StatTile.jsx";
import { SessionList, SessionTimeline } from "../components/sessions/SessionCards.jsx";

const PAGE_SIZE = 20;
const OUTCOMES = [
  { id: "all", label: "All" },
  { id: "victory", label: "Victories" },
  { id: "defeat", label: "Defeats" },
];
const SORTS = [
  { id: "newest", label: "Newest first", key: "endedAt", dir: -1 },
  { id: "oldest", label: "Oldest first", key: "endedAt", dir: 1 },
  { id: "score", label: "Highest score", key: "score", dir: -1 },
  { id: "hits", label: "Most hits", key: "hitCount", dir: -1 },
  { id: "damage", label: "Most damage", key: "damageDealt", dir: -1 },
  { id: "duration", label: "Longest", key: "durationSec", dir: -1 },
];

const value = (s, key) => (key === "endedAt" ? sessionTime(s) : Number(s[key]) || 0);

function download(name, text) {
  const url = URL.createObjectURL(new Blob([text], { type: "text/csv" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}

export default function SessionsView({ sessions, range, onRange, openSession }) {
  const [query, setQuery] = useState("");
  const [outcome, setOutcome] = useState("all");
  const [level, setLevel] = useState("all");
  const [sortId, setSortId] = useState("newest");
  const [page, setPage] = useState(0);
  const sort = SORTS.find((s) => s.id === sortId);

  const levels = useMemo(
    () => [{ id: "all", label: "All levels" }, ...[...new Set(sessions.map((s) => s.level))].sort((a, b) => levelRank(a) - levelRank(b)).map((l) => ({ id: l, label: l }))],
    [sessions]
  );

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    return sessions
      .filter(
        (s) =>
          (outcome === "all" || s.outcome === outcome) &&
          (level === "all" || s.level === level) &&
          (!q || s.playerName.toLowerCase().includes(q) || String(s.id).toLowerCase().includes(q))
      )
      .sort((a, b) => (value(a, sort.key) - value(b, sort.key)) * sort.dir);
  }, [sessions, query, outcome, level, sort]);

  useEffect(() => setPage(0), [query, outcome, level, range, sortId]);

  const pageCount = Math.ceil(rows.length / PAGE_SIZE);
  const pageRows = rows.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE);
  const summary = useMemo(() => summarize(rows), [rows]);
  const chronological = sort.key === "endedAt";

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="History"
        title="Sessions"
        description="Every battle recorded by the backend, grouped by day. Open one for its full breakdown and a scrubbable replay."
        actions={
          <>
            <SegmentedControl label="Time range" options={RANGES} value={range} onChange={onRange} />
            <Button icon="download" onClick={() => download(`vr-fitness-sessions-${new Date().toISOString().slice(0, 10)}.csv`, toCsv(rows))} disabled={!rows.length}>
              Export CSV
            </Button>
          </>
        }
      />

      <FigureStrip
        items={[
          { label: "Matching battles", value: formatNumber(summary.sessions), sub: `${summary.victories} won · ${summary.defeats} lost`, lead: true },
          { label: "Win rate", value: `${Math.round(summary.winRate)}%` },
          { label: "Average score", value: formatNumber(summary.avgScore) },
          { label: "Best score", value: formatNumber(summary.bestScore) },
        ]}
      />

      <Card className="overflow-hidden">
        <CardHeader
          title={chronological ? "Battle timeline" : `Ranked by ${sort.label.toLowerCase()}`}
          subtitle={`${formatNumber(rows.length)} result${rows.length === 1 ? "" : "s"}`}
          actions={
            <>
              <SearchInput value={query} onChange={setQuery} placeholder="Search player or id…" label="Search sessions" />
              <SegmentedControl label="Outcome" options={OUTCOMES} value={outcome} onChange={setOutcome} />
              <Select label="Level" value={level} onChange={setLevel} options={levels} />
              <Select label="Sort" value={sortId} onChange={setSortId} options={SORTS} />
            </>
          }
        />
        {pageRows.length ? (
          <>
            <div key={`${sortId}-${page}`} className="p-4 sm:p-5">
              {chronological ? <SessionTimeline rows={pageRows} onSelect={(s) => openSession(s.id)} /> : <SessionList rows={pageRows} onSelect={(s) => openSession(s.id)} />}
            </div>
            <Pagination page={page} pageCount={pageCount} onPage={setPage} total={rows.length} pageSize={PAGE_SIZE} />
          </>
        ) : (
          <EmptyState
            icon="search"
            title={sessions.length ? "No battles match these filters" : "No battles recorded yet"}
            description={sessions.length ? "Try a wider time range or clear the search." : "Finish a round in the headset and it lands here within a second."}
          />
        )}
      </Card>
    </div>
  );
}
