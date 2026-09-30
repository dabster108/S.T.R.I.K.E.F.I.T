import { Link } from "react-router-dom";
import { bests } from "../../lib/compare.js";
import { formatDuration, formatNumber, formatShortDate } from "../../lib/format.js";
import { levelName } from "../../lib/game.js";
import Icon from "../ui/Icon.jsx";
import { Panel, SIDE_A, SIDE_B, SideKey } from "./shared.jsx";

// `lower` marks the one record where the smaller value is the better one.
const RECORDS = {
  score: { label: "Best score", fmt: formatNumber },
  damage: { label: "Most damage", hint: "one fight", fmt: formatNumber },
  hits: { label: "Most hits", hint: "one fight", fmt: formatNumber },
  peak: { label: "Fastest swing", fmt: (v) => `${v.toFixed(1)} m/s` },
  longest: { label: "Longest fight", fmt: formatDuration },
  fastestWin: { label: "Fastest win", fmt: formatDuration, lower: true },
  streak: { label: "Best win streak", fmt: (v) => `${v} win${v === 1 ? "" : "s"}` },
};

function Best({ rec, best, leads, side, align }) {
  if (!best) return <span className="text-sm font-semibold text-[var(--muted)]">—</span>;
  const f = best.fight;
  const value = rec.fmt(best.value);
  return (
    <>
      <span className={`inline-flex items-center gap-1.5 ${align === "right" ? "flex-row-reverse" : ""}`}>
        <span className={`tabular-nums ${leads ? "text-base font-extrabold text-[var(--ink)]" : "text-sm font-semibold text-[var(--ink-secondary)]"}`}>
          {f ? (
            <Link to={`/dashboard/session/${encodeURIComponent(f.id)}`} className="rounded hover:underline" title="Open this fight">
              {value}
            </Link>
          ) : (
            value
          )}
        </span>
        {leads && (
          <>
            <span className="shrink-0" style={{ color: side.color }}>
              <Icon name="trophy" className="h-3.5 w-3.5" />
            </span>
            <span className="sr-only">(better)</span>
          </>
        )}
      </span>
      {f && (
        <span className="mt-0.5 block text-xs font-medium text-[var(--muted)]">
          {f.who || levelName(f.level)} · {formatShortDate(f.startedAt)}
        </span>
      )}
    </>
  );
}

/** Each side's records, side by side. A record links to the fight it was set in. records: keys of RECORDS, in row order. */
export default function BestsCard({ a, b, title = "Personal bests", records = ["score", "damage", "hits", "peak", "longest", "fastestWin", "streak"], className = "" }) {
  const [ba, bb] = [bests(a), bests(b)];
  return (
    <Panel className={className} title={title} subtitle="The single best fight on each measure. The trophy marks the better record; click a figure to open that fight.">
      <table className="w-full table-fixed">
        <caption className="sr-only">
          {title}, {a.label} and {b.label}
        </caption>
        <thead>
          <tr className="border-b border-[var(--border)] text-xs font-bold text-[var(--ink-secondary)]">
            <th scope="col" className="w-[36%] pb-2 text-left font-bold">
              <SideKey side={SIDE_A} label={a.label} className="max-w-full" />
            </th>
            <th scope="col" className="pb-2">
              <span className="sr-only">Record</span>
            </th>
            <th scope="col" className="w-[36%] pb-2 text-right font-bold">
              <SideKey side={SIDE_B} label={b.label} className="max-w-full" />
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border)]">
          {records.map((key) => {
            const rec = RECORDS[key];
            const [x, y] = [ba[key], bb[key]];
            const lead = x && y && x.value !== y.value ? ((rec.lower ? x.value < y.value : x.value > y.value) ? "a" : "b") : null;
            return (
              <tr key={key}>
                <td className="py-2.5 pr-2 align-middle">
                  <Best rec={rec} best={x} leads={lead === "a"} side={SIDE_A} align="left" />
                </td>
                <th scope="row" className="px-1 py-2.5 text-center align-middle text-xs font-bold text-[var(--ink-secondary)]">
                  {rec.label}
                  {rec.hint && <span className="block font-medium text-[var(--muted)]">{rec.hint}</span>}
                </th>
                <td className="py-2.5 pl-2 text-right align-middle">
                  <Best rec={rec} best={y} leads={lead === "b"} side={SIDE_B} align="right" />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </Panel>
  );
}
