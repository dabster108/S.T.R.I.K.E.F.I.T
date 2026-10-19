import { memo, useMemo } from "react";
import { useFeed } from "../../../live/LiveProvider.jsx";
import { killFeedItem } from "../../../lib/arenaApp.js";
import { formatClock } from "../../../lib/format.js";
import Icon from "../../ui/Icon.jsx";

// A compact kill-feed for one fight: icon, one line of plain text, and the time; newest on top, at most MAX lines. Each new line
// slides in once (opacity and an 8 px move; off under reduced motion and in Calm mode). Noise (HP snapshots, too-slow swings) never
// appears. The full event log is still one click away on the page.

const MAX = 8;
const TONE = {
  accent: "bg-[var(--accent-soft)] text-[var(--accent)]",
  good: "bg-[var(--good-soft)] text-[var(--good-ink)]",
  bad: "bg-[var(--bad-soft)] text-[var(--bad)]",
  warn: "bg-[var(--warn-soft)] text-[var(--warn-ink)]",
  muted: "bg-gray-100 text-[var(--muted)]",
};

const KillFeed = memo(function KillFeed({ sessionId, monsterName }) {
  const inFight = useMemo(() => (e) => e.sessionId === sessionId, [sessionId]);
  const feed = useFeed(inFight);
  const lines = useMemo(() => {
    const out = [];
    for (const e of feed) {
      const item = killFeedItem(e, { monster: monsterName });
      if (item) out.push({ id: e.id, at: e.at, ...item });
      if (out.length === MAX) break;
    }
    return out;
  }, [feed, monsterName]);

  return (
    <section className="flex h-full min-h-[14rem] flex-col rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]" aria-label="Fight events, newest first">
      <div className="flex items-center justify-between border-b border-[var(--border)] px-4 py-3">
        <h2 className="text-sm font-bold tracking-tight text-[var(--ink)]">What just happened</h2>
        <span className="text-xs font-semibold text-[var(--muted)]">newest first</span>
      </div>
      {lines.length ? (
        <ul className="flex-1 divide-y divide-[var(--border)] overflow-hidden px-2" role="log" aria-live="off">
          {lines.map((l) => (
            <li key={l.id} className="arena-feed-in flex items-center gap-2.5 px-2 py-2">
              <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${TONE[l.tone] || TONE.muted}`} aria-hidden>
                <Icon name={l.icon} className="h-3.5 w-3.5" strokeWidth={2.2} />
              </span>
              <span className="min-w-0 flex-1 text-sm font-medium leading-snug text-[var(--ink-secondary)]">{l.text}</span>
              <time dateTime={l.at} className="shrink-0 text-xs font-semibold tabular-nums text-[var(--muted)]">
                {formatClock(l.at)}
              </time>
            </li>
          ))}
        </ul>
      ) : (
        <p className="flex flex-1 items-center justify-center px-4 py-8 text-center text-sm font-medium text-[var(--muted)]">The first swing will show up here.</p>
      )}
    </section>
  );
});

export default KillFeed;
