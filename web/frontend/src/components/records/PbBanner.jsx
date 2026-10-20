import { PB_CONFETTI } from "../../config/records.js";
import { describeRecords, recordsHeadline, recordsSentence } from "../../lib/records.js";
import Confetti from "../ui/Confetti.jsx";
import Icon from "../ui/Icon.jsx";

// "New PB": what this fight beat on its level, each with the value it beat. Light confetti (24 small pieces, 1.5 s) the first time it is
// shown; in Calm mode and under reduced motion there is no confetti, the badge and the numbers are the whole message.
// records: the API's newRecords. fireKey: a value that changes once per fight, so the confetti plays once per fight.
export default function PbBanner({ records, fireKey, className = "" }) {
  const rows = describeRecords(records);
  if (!rows.length) return null;
  return (
    <section className={`relative rounded-2xl border border-[var(--accent-ring)] bg-[var(--accent-soft)] px-5 py-4 ${className}`} role="status" aria-label={recordsHeadline(rows)} data-testid="pb-banner">
      <Confetti fire={fireKey} pieces={PB_CONFETTI.pieces} ms={PB_CONFETTI.ms} />
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--accent)] px-3 py-1 text-sm font-extrabold text-white shadow-[var(--shadow-lift)]">
          <Icon name="sparkles" className="h-4 w-4" strokeWidth={2.4} /> New PB
        </span>
        <p className="text-sm font-bold text-[var(--accent-strong)]">{recordsHeadline(rows)} on this level</p>
      </div>
      <ul className="mt-3 grid gap-x-8 gap-y-2 sm:grid-cols-2">
        {rows.map((r) => (
          <li key={r.metric} className="flex flex-wrap items-baseline gap-x-2">
            <span className="text-xs font-semibold text-[var(--ink-secondary)]">{r.label}</span>
            <span className="text-lg font-extrabold tabular-nums text-[var(--ink)]">{r.value}</span>
            <span className="text-xs font-medium text-[var(--muted)]">was {r.previous}</span>
          </li>
        ))}
      </ul>
      <p className="sr-only">{recordsSentence(records)}</p>
    </section>
  );
}
