import Icon from "../ui/Icon.jsx";
import { LevelBadge, OutcomeBadge } from "../ui/Badge.jsx";
import { formatDateTime, formatDuration, formatNumber, formatRelative } from "../../lib/format.js";

const th = "px-4 py-2.5 text-left text-[0.68rem] font-semibold uppercase tracking-wide text-[var(--muted)] whitespace-nowrap";
const td = "px-4 py-3 text-sm text-[var(--ink)] whitespace-nowrap tabular-nums";

const COLUMNS = [
  { key: "endedAt", label: "Played" },
  { key: "playerName", label: "Player" },
  { key: "level", label: "Level" },
  { key: "outcome", label: "Result" },
  { key: "score", label: "Score", numeric: true },
  { key: "hitCount", label: "Hits", numeric: true },
  { key: "damageDealt", label: "Damage", numeric: true },
  { key: "durationSec", label: "Duration", numeric: true },
];

function SortHeader({ col, sort, onSort }) {
  const active = sort?.key === col.key;
  if (!onSort) return <th className={`${th} ${col.numeric ? "text-right" : ""}`}>{col.label}</th>;
  return (
    <th className={`${th} ${col.numeric ? "text-right" : ""}`} aria-sort={active ? (sort.dir === "asc" ? "ascending" : "descending") : "none"}>
      <button
        type="button"
        onClick={() => onSort(col.key)}
        className={`inline-flex items-center gap-1 uppercase hover:text-[var(--ink)] ${active ? "text-[var(--ink)]" : ""}`}
      >
        {col.label}
        <Icon name={active ? (sort.dir === "asc" ? "arrowUp" : "arrowDown") : "sort"} className="h-3 w-3" strokeWidth={2.2} />
      </button>
    </th>
  );
}

export default function SessionsTable({ rows, sort, onSort, onSelect, compact = false }) {
  const cols = compact ? COLUMNS.filter((c) => ["endedAt", "playerName", "level", "outcome", "score"].includes(c.key)) : COLUMNS;
  return (
    <div className="scrollbar-thin relative overflow-x-auto">
      <table className={`w-full border-collapse ${compact ? "min-w-[520px]" : "min-w-[820px]"}`}>
        <thead>
          <tr className="border-b border-[var(--border)] bg-[var(--surface-muted)]">
            {cols.map((c) => (
              <SortHeader key={c.key} col={c} sort={sort} onSort={onSort} />
            ))}
            <th className={th}>
              <span className="sr-only">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={row.id}
              onClick={() => onSelect?.(row)}
              onKeyDown={(e) => (e.key === "Enter" || e.key === " ") && (e.preventDefault(), onSelect?.(row))}
              tabIndex={onSelect ? 0 : undefined}
              className="cursor-pointer border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--surface-muted)] focus-visible:bg-[var(--accent-soft)]"
            >
              <td className={td}>
                <span className="block text-[var(--ink)]">{formatRelative(row.endedAt || row.startedAt)}</span>
                {!compact && <span className="block text-[0.7rem] text-[var(--muted)]">{formatDateTime(row.endedAt || row.startedAt)}</span>}
              </td>
              <td className={`${td} font-medium`}>{row.playerName}</td>
              <td className={td}>
                <LevelBadge level={row.level} />
              </td>
              <td className={td}>
                <OutcomeBadge outcome={row.outcome} />
              </td>
              <td className={`${td} text-right font-semibold`}>{formatNumber(row.score)}</td>
              {!compact && (
                <>
                  <td className={`${td} text-right`}>{formatNumber(row.hitCount)}</td>
                  <td className={`${td} text-right`}>{formatNumber(row.damageDealt)}</td>
                  <td className={`${td} text-right text-[var(--ink-secondary)]`}>{formatDuration(row.durationSec)}</td>
                </>
              )}
              <td className={`${td} w-8 text-[var(--muted)]`}>
                <Icon name="chevronRight" className="h-4 w-4" />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
