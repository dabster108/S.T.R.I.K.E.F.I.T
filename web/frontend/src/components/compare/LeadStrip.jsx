import { gapText, leadSummary } from "../../lib/compare.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import { SIDE_A, SIDE_B, SideKey } from "./shared.jsx";

const plural = (n, word) => `${n} ${word}${n === 1 ? "" : "s"}`;

function Gap({ title, gap, a, b }) {
  const text = gapText(gap);
  return (
    <div className="bg-[var(--surface)] px-5 py-3.5 sm:px-6">
      <dt className="t-kicker">{title}</dt>
      <dd className="mt-1.5">
        <p className="text-sm font-extrabold text-[var(--ink)]">{gap.row.label}</p>
        <p className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs font-semibold text-[var(--ink-secondary)]">
          <SideKey side={gap.lead === "a" ? SIDE_A : SIDE_B} label={gap.lead === "a" ? a : b} />
          <span className="tabular-nums">
            {gap.row.fmt(gap.hi)} vs {gap.row.fmt(gap.lo)}
          </span>
          {text && <span className="text-[var(--muted)]">· {text}</span>}
        </p>
      </dd>
    </div>
  );
}

// Tally of the head-to-head rows above it: how many each side is higher on, as one
// split bar, then the widest and the narrowest gap.
export default function LeadStrip({ rows, a, b }) {
  const s = leadSummary(rows);
  const count = (n, align) => (
    <p className={`mt-1 flex items-baseline gap-1.5 ${align}`}>
      <span className="t-stat text-[var(--ink)]">{n}</span>
      <span className="text-xs font-semibold text-[var(--muted)]">of {s.compared}</span>
    </p>
  );
  const segments = [
    { key: "a", n: s.a, style: { background: SIDE_A.color } },
    { key: "tied", n: s.tied, className: "bg-gray-300" },
    { key: "b", n: s.b, style: { background: SIDE_B.color } },
  ].filter((x) => x.n > 0);

  return (
    <Card className="overflow-hidden">
      <CardHeader title="Who leads" subtitle={`Which side has the higher value on each of the ${rows.length} metrics above.`} />
      {s.compared ? (
        <>
          <div className="px-5 py-4 sm:px-6">
            <div className="flex items-end justify-between gap-4 text-xs font-bold text-[var(--ink-secondary)]">
              <div className="min-w-0">
                <SideKey side={SIDE_A} label={a} className="max-w-full" />
                {count(s.a, "")}
              </div>
              <div className="min-w-0 text-right">
                <SideKey side={SIDE_B} label={b} className="max-w-full" />
                {count(s.b, "justify-end")}
              </div>
            </div>
            <div
              className="mt-3 flex h-3 w-full gap-[2px] overflow-hidden rounded-full bg-gray-100"
              role="img"
              aria-label={`${a} is higher on ${plural(s.a, "metric")}, ${b} on ${s.b}${s.tied ? `, ${s.tied} level` : ""}`}
            >
              {segments.map((x, i) => (
                <div key={x.key} className="h-full" style={{ flex: x.n }}>
                  <div className={`grow-x h-full w-full ${x.className || ""}`} style={{ ...x.style, "--i": i }} />
                </div>
              ))}
            </div>
            {(s.tied > 0 || s.missing > 0) && (
              <p className="mt-2 text-xs font-medium text-[var(--muted)]">
                {[s.tied > 0 && `${plural(s.tied, "metric")} level (grey)`, s.missing > 0 && `${plural(s.missing, "metric")} left out: no data on one side`].filter(Boolean).join(" · ")}
              </p>
            )}
          </div>
          {s.widest && (
            <dl className={`grid gap-px border-t border-[var(--border)] bg-[var(--border)] ${s.closest ? "sm:grid-cols-2" : ""}`}>
              <Gap title="Biggest gap" gap={s.widest} a={a} b={b} />
              {s.closest && <Gap title="Closest" gap={s.closest} a={a} b={b} />}
            </dl>
          )}
        </>
      ) : (
        <p className="px-5 py-6 text-sm font-medium text-[var(--muted)]">No metric has data on both sides yet.</p>
      )}
    </Card>
  );
}
