import { useState } from "react";
import { formatNumber, formatPercent } from "../../lib/format.js";
import { levelName } from "../../lib/game.js";

// Outcome colours do a status job (victory / defeat), always with a text label beside them; "abandoned" is the neutral
// non-result (gray on purpose, so it never reads as a third category). Validated with the dataviz palette script (light
// surface): CVD separation victory/defeat ΔE 8.6, every mark >= 3:1 against the card.
export const OUTCOMES = [
  { key: "victories", label: "Victories", color: "var(--good)" },
  { key: "defeats", label: "Defeats", color: "var(--bad)" },
  { key: "abandoned", label: "Abandoned", color: "#6b7280" },
];

const share = (v, total) => (total ? formatPercent((v / total) * 100) : "—");
const winRateText = (s) => (s.winRateKnown ? `${Math.round(s.winRate * 10) / 10}%` : "—");

// One floating readout for the segment under the pointer (or the focused row). Every number it shows is also printed on the card.
function Readout({ tip }) {
  if (!tip) return null;
  return (
    <div
      role="presentation"
      className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full whitespace-nowrap rounded-lg bg-[var(--ink)] px-2.5 py-1.5 text-xs text-white shadow-lg"
      style={{ left: tip.x, top: tip.y - 8 }}
    >
      <span className="font-extrabold tabular-nums">{tip.value}</span> <span className="font-semibold text-white/75">{tip.label}</span>
    </div>
  );
}

// A segmented bar: 2px surface gaps between fills, rounded data-ends; each segment carries its own hover readout.
function Segments({ counts, total, scale = 1, height = "h-3", onTip, name }) {
  const shown = OUTCOMES.filter((o) => counts[o.key] > 0);
  return (
    <div className="flex w-full gap-[2px]" style={{ width: `${scale * 100}%` }} aria-hidden>
      {shown.map((o, i) => (
        <span
          key={o.key}
          className={`${height} block min-w-[4px] first:rounded-l-full last:rounded-r-full`}
          style={{ flexGrow: counts[o.key], flexBasis: 0 }}
          onPointerMove={(e) => {
            const box = e.currentTarget.closest("[data-tip-root]").getBoundingClientRect();
            onTip({ x: e.clientX - box.left, y: e.currentTarget.getBoundingClientRect().top - box.top, value: formatNumber(counts[o.key]), label: `${o.label}${name ? ` · ${name}` : ""} · ${share(counts[o.key], total)}` });
          }}
          onPointerLeave={() => onTip(null)}
        >
          <span className="grow-x block h-full w-full rounded-[inherit]" style={{ background: o.color, "--i": i }} />
        </span>
      ))}
    </div>
  );
}

/** Win rate as the headline, the outcome split as a meter, and the counts as labelled rows. */
export function OutcomeMeter({ stats }) {
  const [tip, setTip] = useState(null);
  const total = OUTCOMES.reduce((a, o) => a + (stats[o.key] || 0), 0);
  const decided = (stats.victories || 0) + (stats.defeats || 0);
  return (
    <div data-tip-root className="@container relative">
      <p className="flex items-baseline gap-2">
        <span className="text-5xl font-extrabold leading-none tracking-tight tabular-nums text-[var(--ink)]">{winRateText(stats)}</span>
        <span className="text-sm font-semibold text-[var(--muted)]">win rate</span>
      </p>
      <p className="mt-1.5 text-xs font-medium text-[var(--muted)]">
        of {formatNumber(decided)} decided {decided === 1 ? "battle" : "battles"}
        {stats.abandoned ? `, ${formatNumber(stats.abandoned)} abandoned not counted` : ""}
      </p>
      <div className="mt-5" role="img" aria-label={`Battle outcomes: ${OUTCOMES.map((o) => `${o.label} ${stats[o.key] || 0}`).join(", ")}`}>
        <Segments counts={stats} total={total} height="h-3.5" onTip={setTip} />
      </div>
      {/* labelled rows in a narrow panel, three cells once the panel has room for the full labels */}
      <ul className="mt-4 grid gap-1.5 @xs:grid-cols-3 @xs:gap-2">
        {OUTCOMES.map((o) => (
          <li key={o.key} className="flex min-w-0 items-baseline justify-between gap-3 rounded-lg px-1 @xs:block @xs:rounded-xl @xs:border @xs:border-[var(--border)] @xs:px-3 @xs:py-2">
            <p className="flex items-center gap-1.5 text-sm font-semibold text-[var(--muted)] @xs:text-xs">
              <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: o.color }} aria-hidden />
              {o.label}
            </p>
            <p className="shrink-0 text-base font-extrabold tabular-nums text-[var(--ink)] @xs:mt-0.5 @xs:text-lg">
              {formatNumber(stats[o.key] || 0)} <span className="text-xs font-semibold text-[var(--muted)]">{share(stats[o.key] || 0, total)}</span>
            </p>
          </li>
        ))}
      </ul>
      <Readout tip={tip} />
    </div>
  );
}

/** Battles per level as horizontal stacked bars (outcome segments), scaled to the busiest level, with the count and win rate. */
export function LevelBars({ levels }) {
  const [tip, setTip] = useState(null);
  const max = Math.max(1, ...levels.map((l) => l.sessions));
  const ticks = [0, Math.round(max / 2), max].filter((v, i, a) => a.indexOf(v) === i);
  return (
    <div data-tip-root className="@container relative">
      <div className="mb-2 hidden grid-cols-[5.5rem_1fr_6.5rem] gap-3 @sm:grid text-[0.68rem] font-bold uppercase tracking-wider text-[var(--muted)]" aria-hidden>
        <span>Level</span>
        <span>Battles by outcome</span>
        <span className="text-right">Battles · win</span>
      </div>
      <ul className="space-y-1">
        {levels.map((l) => {
          const name = levelName(l.level);
          const summary = `${name}: ${l.sessions} battles, ${OUTCOMES.map((o) => `${o.label.toLowerCase()} ${l[o.key] || 0}`).join(", ")}, win rate ${winRateText(l)}`;
          return (
            <li
              key={l.level}
              tabIndex={0}
              aria-label={summary}
              onFocus={(e) => {
                const box = e.currentTarget.closest("[data-tip-root]").getBoundingClientRect();
                const r = e.currentTarget.getBoundingClientRect();
                setTip({ x: r.left - box.left + r.width / 2, y: r.top - box.top, value: formatNumber(l.sessions), label: `battles · ${name} · ${OUTCOMES.filter((o) => l[o.key]).map((o) => `${l[o.key]} ${o.label.toLowerCase()}`).join(", ")}` });
              }}
              onBlur={() => setTip(null)}
              className="grid grid-cols-[1fr_auto] items-center gap-x-3 gap-y-1.5 rounded-lg px-1 py-2 outline-none @sm:grid-cols-[5.5rem_1fr_6.5rem] transition-colors hover:bg-[var(--surface-muted)] focus-visible:ring-2 focus-visible:ring-[var(--accent-ring)]"
            >
              <span className="truncate text-sm font-bold text-[var(--ink)]">{name}</span>
              <span className="order-last col-span-2 @sm:order-none @sm:col-span-1">
                <Segments counts={l} total={l.sessions} scale={l.sessions / max} height="h-4" onTip={setTip} name={name} />
              </span>
              <span className="text-right text-sm tabular-nums">
                <span className="font-extrabold text-[var(--ink)]">{formatNumber(l.sessions)}</span>
                <span className="ml-2 text-xs font-semibold text-[var(--muted)]">{winRateText(l)}</span>
              </span>
            </li>
          );
        })}
      </ul>
      {/* recessive axis under the bar column */}
      <div className="mt-1 grid grid-cols-[1fr] gap-3 @sm:grid-cols-[5.5rem_1fr_6.5rem]" aria-hidden>
        <span className="hidden @sm:block" />
        <span className="relative h-4 border-t border-[var(--border)] text-[0.68rem] font-semibold tabular-nums text-[var(--muted)]">
          {ticks.map((t) => (
            <span key={t} className="absolute top-1 -translate-x-1/2 first:translate-x-0 last:-translate-x-full" style={{ left: `${(t / max) * 100}%` }}>
              {t}
            </span>
          ))}
        </span>
        <span className="hidden @sm:block" />
      </div>
      <ul className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--muted)]" aria-hidden>
        {OUTCOMES.map((o) => (
          <li key={o.key} className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-sm" style={{ background: o.color }} />
            {o.label}
          </li>
        ))}
      </ul>
      <Readout tip={tip} />
    </div>
  );
}
