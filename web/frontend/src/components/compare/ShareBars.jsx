import { useRef, useState } from "react";
import { outcomeCounts } from "../../lib/compare.js";
import { formatNumber } from "../../lib/format.js";
import { TIERS, Tooltip } from "../charts/Charts.jsx";
import { EmptyState } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";
import { Panel, SIDE_A, SIDE_B, SideKey } from "./shared.jsx";

const pct = (n, total) => (total ? `${Math.round((n / total) * 100)}%` : "—");
// unit: [singular, plural]
const counted = (n, unit) => `${formatNumber(n)} ${unit[n === 1 ? 0 : 1]}`;

// One side's 100% bar (2px gaps between segments, like OutcomeSplit and TierBar).
// A side without data gets a dashed, empty track instead of a bar of zeros.
function Bar({ side, row, segments, unit, empty, onTip }) {
  const total = row.counts ? segments.reduce((n, s) => n + (row.counts[s.key] || 0), 0) : 0;
  return (
    <div>
      <div className="flex items-center justify-between gap-3 text-xs font-bold text-[var(--ink-secondary)]">
        <SideKey side={side} label={row.label} />
        <span className="shrink-0 font-semibold text-[var(--muted)]">{total ? `${counted(total, unit)}${row.extra ? ` · ${row.extra}` : ""}` : empty}</span>
      </div>
      {total ? (
        <div className="mt-1.5 flex h-3 w-full gap-[2px] overflow-hidden rounded-full bg-gray-100">
          {segments
            .filter((s) => row.counts[s.key])
            .map((s, i) => {
              const n = row.counts[s.key];
              const show = (e) => onTip(e.currentTarget, { title: row.label, rows: [{ label: s.label, color: s.color, value: `${pct(n, total)} · ${formatNumber(n)}` }] });
              return (
                <div
                  key={s.key}
                  className="h-full outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--ink)]"
                  style={{ width: `${(n / total) * 100}%` }}
                  tabIndex={0}
                  role="img"
                  aria-label={`${row.label}, ${s.label}: ${pct(n, total)} (${formatNumber(n)} of ${counted(total, unit)})`}
                  onMouseEnter={show}
                  onFocus={show}
                  onMouseLeave={() => onTip(null)}
                  onBlur={() => onTip(null)}
                >
                  <div className="grow-x h-full w-full" style={{ background: s.color, "--i": i }} />
                </div>
              );
            })}
        </div>
      ) : (
        <div className="mt-1.5 h-3 w-full rounded-full border border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)]" aria-hidden />
      )}
    </div>
  );
}

/**
 * Two 100% bars, one per side, cut into the same ordered segments, with every share
 * and count in a table underneath (the table is the accessible view of the bars).
 * segments: [{ key, label, color, icon?, iconClass? }]; a, b: { label, counts | null, extra? }; unit: [singular, plural].
 */
export function ShareBars({ caption, head, segments, a, b, unit, empty }) {
  const box = useRef(null);
  const [tip, setTip] = useState(null);
  // Tooltip sits above the hovered segment, in the same box the line and bar charts use.
  const onTip = (el, content) => {
    if (!el || !box.current) return setTip(null);
    const [r, o] = [el.getBoundingClientRect(), box.current.getBoundingClientRect()];
    setTip({ ...content, x: r.left - o.left + r.width / 2, y: r.top - o.top });
  };
  const total = (row) => (row.counts ? segments.reduce((n, s) => n + (row.counts[s.key] || 0), 0) : 0);
  const [ta, tb] = [total(a), total(b)];

  return (
    <div ref={box} className="relative">
      <div className="space-y-4">
        <Bar side={SIDE_A} row={a} segments={segments} unit={unit} empty={empty} onTip={onTip} />
        <Bar side={SIDE_B} row={b} segments={segments} unit={unit} empty={empty} onTip={onTip} />
      </div>
      <Tooltip tip={tip} width={box.current?.clientWidth || 0} />
      <table className="mt-5 w-full text-xs">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-b border-[var(--border)] text-left font-bold text-[var(--ink-secondary)]">
            <th scope="col" className="pb-2 pr-3 font-bold">
              {head}
            </th>
            {[
              [SIDE_A, a],
              [SIDE_B, b],
            ].map(([side, row]) => (
              <th key={row.label} scope="col" className="w-[32%] max-w-0 pb-2 pl-2 text-right font-bold">
                <SideKey side={side} label={row.label} className="max-w-full" />
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--border)]">
          {segments.map((s) => (
            <tr key={s.key}>
              <th scope="row" className="py-2 pr-3 text-left font-semibold text-[var(--ink-secondary)]">
                <span className="inline-flex items-center gap-1.5">
                  {s.icon ? <Icon name={s.icon} className={`h-3.5 w-3.5 ${s.iconClass}`} /> : <span className="h-2.5 w-2.5 rounded-sm" style={{ background: s.color }} aria-hidden />}
                  {s.label}
                </span>
              </th>
              {[
                [a, ta],
                [b, tb],
              ].map(([row, t], i) => (
                <td key={i} className="py-2 pl-2 text-right tabular-nums">
                  {t ? (
                    <>
                      <span className="font-extrabold text-[var(--ink)]">{pct(row.counts[s.key] || 0, t)}</span>
                      <span className="ml-1.5 font-semibold text-[var(--muted)]">{formatNumber(row.counts[s.key] || 0)}</span>
                    </>
                  ) : (
                    <span className="font-semibold text-[var(--muted)]">—</span>
                  )}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Share of measured hits per swing tier, slowest to fastest, for both sides. */
export function TierCompareCard({ a, b, className = "" }) {
  const any = a.tiers || b.tiers;
  return (
    <Panel
      className={className}
      title="Swing power"
      subtitle="Share of measured hits in each swing tier, slowest (tap) to fastest (full power)."
      note={any && (!a.tiers || !b.tiers) ? `${(a.tiers ? b : a).label} has no measured swings: those fights were recorded before the live-telemetry build.` : null}
    >
      {any ? (
        <ShareBars
          caption={`Swing tiers, ${a.label} and ${b.label}`}
          head="Tier"
          segments={TIERS}
          unit={["measured hit", "measured hits"]}
          empty="no measured swings"
          a={{ label: a.label, counts: a.tiers }}
          b={{ label: b.label, counts: b.tiers }}
        />
      ) : (
        <EmptyState icon="gauge" title="No swing-speed data on either side" description="Swing tiers come from the live-telemetry build of the game; older fights only kept totals." />
      )}
    </Panel>
  );
}

const OUTCOMES = [
  { key: "victory", label: "Victories", color: "var(--good)", icon: "check", iconClass: "text-[var(--good-ink)]" },
  { key: "defeat", label: "Defeats", color: "var(--bad)", icon: "xCircle", iconClass: "text-[var(--bad)]" },
  { key: "other", label: "No result", color: "var(--border-strong)", icon: "clock", iconClass: "text-[var(--muted)]" },
];

/** How each side's finished fights ended. Win rate counts decided fights only, as everywhere else. */
export function OutcomeCompareCard({ a, b, className = "" }) {
  const row = (s) => {
    const c = outcomeCounts(s.fights);
    const decided = c.victory + c.defeat;
    return { label: s.label, counts: s.fights.length ? c : null, extra: decided ? `${pct(c.victory, decided)} won` : null };
  };
  return (
    <Panel className={className} title="How fights end" subtitle="Victories, defeats and fights with no result (abandoned or timed out), as a share of each side's fights.">
      <ShareBars caption={`Fight outcomes, ${a.label} and ${b.label}`} head="Outcome" segments={OUTCOMES} unit={["fight", "fights"]} empty="no fights" a={row(a)} b={row(b)} />
    </Panel>
  );
}
