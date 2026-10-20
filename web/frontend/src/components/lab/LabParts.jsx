import Icon from "../ui/Icon.jsx";

// The three pieces every Thesis Lab chart carries: how many it rests on, a loud small-sample flag, and the plain-language reading.

export function SampleBadge({ n, small }) {
  return small ? (
    <span className="inline-flex rounded-full bg-[var(--warn-soft)] px-2 py-0.5 text-xs font-extrabold text-[var(--warn-ink)] ring-1 ring-inset ring-amber-200">small sample · n = {n}</span>
  ) : (
    <span className="inline-flex rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-xs font-bold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">n = {n}</span>
  );
}

// The plain-language reading of a chart, right under it.
export function WhatThis({ children }) {
  return (
    <div className="flex gap-3 border-t border-[var(--border)] bg-[var(--accent-soft)] px-5 py-3.5">
      <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--accent-strong)]" />
      <p className="text-sm font-medium leading-relaxed text-[var(--ink)]">
        <span className="font-bold">What this tells us: </span>
        {children}
      </p>
    </div>
  );
}

// A loud, unmissable flag in the card header when the number rests on fewer than 10 players or fights.
// always: the playtest study is at most 20 people, so its charts are flagged however many have answered.
export function SmallSampleFlag({ n, unit, always = false }) {
  const u = n === 1 ? unit.replace(/s$/, "") : unit; // "1 rated fight", "1 player"
  if (n >= 10 && !always) return <span className="text-xs font-bold tabular-nums text-[var(--muted)]">n = {n} {u}</span>;
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--warn-soft)] px-2.5 py-1 text-xs font-extrabold text-[var(--warn-ink)] ring-1 ring-inset ring-amber-300">
      <Icon name="info" className="h-3.5 w-3.5" /> Small sample · {n} {u}
    </span>
  );
}

