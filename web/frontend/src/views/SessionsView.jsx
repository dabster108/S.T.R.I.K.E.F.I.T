import { useEffect, useMemo, useState } from "react";
import { RANGES, levelRank, sessionTime, summarize, toCsv } from "../lib/derive.js";
import { formatNumber, formatPercent } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { Pagination, SearchInput, SegmentedControl, Select } from "../components/ui/Controls.jsx";
import { EmptyState } from "../components/ui/Feedback.jsx";
import Button from "../components/ui/Button.jsx";
import SessionsTable from "../components/sessions/SessionsTable.jsx";

const PAGE_SIZE = 15;
const OUTCOMES = [
  { id: "all", label: "All" },
  { id: "victory", label: "Victories" },
  { id: "defeat", label: "Defeats" },
];

function compare(a, b, key) {
  if (key === "endedAt") return sessionTime(a) - sessionTime(b);
  if (key === "level") return levelRank(a.level) - levelRank(b.level);
  const x = a[key];
  const y = b[key];
  return typeof x === "number" || typeof y === "number" ? (Number(x) || 0) - (Number(y) || 0) : String(x ?? "").localeCompare(String(y ?? ""));
}

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
  const [sort, setSort] = useState({ key: "endedAt", dir: "desc" });
  const [page, setPage] = useState(0);

  const levels = useMemo(
    () => [{ id: "all", label: "All levels" }, ...[...new Set(sessions.map((s) => s.level))].sort((a, b) => levelRank(a) - levelRank(b)).map((l) => ({ id: l, label: l }))],
    [sessions]
  );

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase();
    const filtered = sessions.filter(
      (s) =>
        (outcome === "all" || s.outcome === outcome) &&
        (level === "all" || s.level === level) &&
        (!q || s.playerName.toLowerCase().includes(q) || String(s.id).toLowerCase().includes(q))
    );
    const dir = sort.dir === "asc" ? 1 : -1;
    return filtered.sort((a, b) => compare(a, b, sort.key) * dir);
  }, [sessions, query, outcome, level, sort]);

  useEffect(() => setPage(0), [query, outcome, level, range, sort]);

  const pageCount = Math.ceil(rows.length / PAGE_SIZE);
  const pageRows = rows.slice(page * PAGE_SIZE, (page + 1) * PAGE_SIZE);
  const summary = useMemo(() => summarize(rows), [rows]);

  const onSort = (key) =>
    setSort((s) => (s.key === key ? { key, dir: s.dir === "asc" ? "desc" : "asc" } : { key, dir: key === "playerName" || key === "level" ? "asc" : "desc" }));

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="History"
        title="Sessions"
        description="Every battle recorded by the backend. Click a row for its full breakdown and event timeline."
        actions={
          <>
            <SegmentedControl label="Time range" options={RANGES} value={range} onChange={onRange} />
            <Button icon="download" onClick={() => download(`vr-fitness-sessions-${new Date().toISOString().slice(0, 10)}.csv`, toCsv(rows))} disabled={!rows.length}>
              Export CSV
            </Button>
          </>
        }
      />

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        {[
          { label: "Matching battles", value: formatNumber(summary.sessions) },
          { label: "Win rate", value: formatPercent(summary.winRate) },
          { label: "Average score", value: formatNumber(summary.avgScore) },
          { label: "Best score", value: formatNumber(summary.bestScore) },
        ].map((m) => (
          <div key={m.label} className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3">
            <p className="text-[0.7rem] font-medium text-[var(--muted)]">{m.label}</p>
            <p className="mt-0.5 text-xl font-bold tabular-nums text-[var(--ink)]">{m.value}</p>
          </div>
        ))}
      </div>

      <Card className="overflow-hidden">
        <CardHeader
          title="All battles"
          subtitle={`${rows.length} result${rows.length === 1 ? "" : "s"}`}
          actions={
            <>
              <SearchInput value={query} onChange={setQuery} placeholder="Search player or id…" label="Search sessions" />
              <SegmentedControl label="Outcome" options={OUTCOMES} value={outcome} onChange={setOutcome} />
              <Select label="Level" value={level} onChange={setLevel} options={levels} />
            </>
          }
        />
        {pageRows.length ? (
          <>
            <SessionsTable rows={pageRows} sort={sort} onSort={onSort} onSelect={(s) => openSession(s.id)} />
            <Pagination page={page} pageCount={pageCount} onPage={setPage} total={rows.length} pageSize={PAGE_SIZE} />
          </>
        ) : (
          <EmptyState
            icon="search"
            title={sessions.length ? "No battles match these filters" : "No battles recorded yet"}
            description={sessions.length ? "Try a wider time range or clear the search." : "Run the backend and finish a round in Unity."}
          />
        )}
      </Card>
    </div>
  );
}
