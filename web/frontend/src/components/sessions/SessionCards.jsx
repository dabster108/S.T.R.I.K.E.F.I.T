import { formatDuration, formatNumber, formatRelative } from "../../lib/format.js";
import { levelName } from "../../lib/game.js";
import { sessionTime } from "../../lib/derive.js";
import { LevelBadge, OutcomeBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";

const STRIPE = { victory: "bg-[var(--good)]", defeat: "bg-[var(--bad)]" };

const timeOfDay = (iso) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
};

function Metric({ icon, label, value }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-[var(--ink-secondary)]" title={label}>
      <Icon name={icon} className="h-3.5 w-3.5 text-[var(--muted)]" />
      <span className="tabular-nums">{value}</span>
      <span className="sr-only">{label}</span>
    </span>
  );
}

// One battle as a clickable card row: result stripe, who / which level / when, key numbers.
export function SessionRow({ session: s, onSelect, compact = false, showTime = false }) {
  const at = s.endedAt || s.startedAt;
  return (
    <button
      type="button"
      onClick={() => onSelect?.(s)}
      className="press group relative flex w-full items-center gap-3 overflow-hidden rounded-xl border border-[var(--border)] bg-[var(--surface)] py-3 pl-4 pr-3 text-left hover:border-[var(--accent-ring)] hover:bg-[var(--surface-muted)] focus-visible:border-[var(--accent)]"
      aria-label={`${s.playerName}, ${levelName(s.level)}, ${s.outcome || "unknown result"}, ${formatNumber(s.score)} points, ${formatRelative(at)}`}
    >
      <span className={`absolute inset-y-0 left-0 w-1 ${STRIPE[s.outcome] || "bg-gray-300"}`} aria-hidden />
      <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-extrabold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">
        {s.playerName.slice(0, 2).toUpperCase()}
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="truncate text-sm font-bold text-[var(--ink)]">{s.playerName}</span>
          <LevelBadge level={s.level} />
          <OutcomeBadge outcome={s.outcome} />
        </span>
        <span className="mt-1 flex flex-wrap items-center gap-x-4 gap-y-1">
          <span className="text-xs font-medium text-[var(--muted)]">{showTime ? timeOfDay(at) : formatRelative(at)}</span>
          {!compact && (
            <>
              <Metric icon="bolt" label="hits" value={`${formatNumber(s.hitCount)} hits`} />
              <Metric icon="flame" label="damage dealt" value={`${formatNumber(s.damageDealt)} dmg`} />
              <Metric icon="clock" label="duration" value={formatDuration(s.durationSec)} />
            </>
          )}
        </span>
      </span>
      <span className="shrink-0 text-right">
        <span className="block text-lg font-extrabold leading-none tabular-nums text-[var(--ink)]">{formatNumber(s.score)}</span>
        <span className="text-xs font-semibold text-[var(--muted)]">pts</span>
      </span>
      <Icon name="chevronRight" className="h-4 w-4 shrink-0 text-[var(--muted)] transition-transform duration-200 group-hover:translate-x-0.5 group-hover:text-[var(--accent)]" />
    </button>
  );
}

const dayKey = (t) => {
  const d = new Date(t);
  return `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
};
function dayLabel(t) {
  const today = new Date();
  const yesterday = new Date(Date.now() - 864e5);
  if (dayKey(t) === dayKey(today)) return "Today";
  if (dayKey(t) === dayKey(yesterday)) return "Yesterday";
  return new Date(t).toLocaleDateString(undefined, { weekday: "long", month: "short", day: "numeric" });
}

// Day-grouped timeline: a rail with a node per day, the day's battles as cards.
export function SessionTimeline({ rows, onSelect }) {
  const groups = [];
  for (const s of rows) {
    const t = sessionTime(s);
    const key = dayKey(t);
    if (groups[groups.length - 1]?.key !== key) groups.push({ key, t, items: [] });
    groups[groups.length - 1].items.push(s);
  }
  return (
    <ol className="relative space-y-6 before:absolute before:bottom-2 before:left-[0.4375rem] before:top-2 before:w-px before:bg-[var(--border)]">
      {groups.map((g) => {
        const wins = g.items.filter((s) => s.outcome === "victory").length;
        return (
          <li key={g.key} className="relative pl-7">
            <span className="absolute left-0 top-1 h-3.5 w-3.5 rounded-full border-[3px] border-[var(--surface)] bg-[var(--accent)] ring-1 ring-[var(--accent-ring)]" aria-hidden />
            <h3 className="flex flex-wrap items-baseline gap-x-2 text-sm font-bold text-[var(--ink)]">
              {dayLabel(g.t)}
              <span className="text-xs font-semibold text-[var(--muted)]">
                {g.items.length} battle{g.items.length === 1 ? "" : "s"} · {wins} won
              </span>
            </h3>
            <ul className="stagger mt-3 space-y-2">
              {g.items.map((s) => (
                <li key={s.id}>
                  <SessionRow session={s} onSelect={onSelect} showTime />
                </li>
              ))}
            </ul>
          </li>
        );
      })}
    </ol>
  );
}

// Flat list for non-chronological sorts (by score, hits, …).
export function SessionList({ rows, onSelect, compact = false }) {
  return (
    <ul className="stagger space-y-2">
      {rows.map((s) => (
        <li key={s.id}>
          <SessionRow session={s} onSelect={onSelect} compact={compact} />
        </li>
      ))}
    </ul>
  );
}
