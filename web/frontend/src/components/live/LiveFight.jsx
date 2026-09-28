import { memo, useEffect, useState, useSyncExternalStore } from "react";
import { Link } from "react-router-dom";
import { useLiveSession } from "../../live/LiveProvider.jsx";
import { wakeStore } from "../../lib/api.js";
import { levelName } from "../../lib/game.js";
import { formatClock, formatNumber } from "../../lib/format.js";
import { useTween } from "../../hooks/motion.js";
import { LevelBadge, OutcomeBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);

export function LiveChip({ label = "Live" }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--good-soft)] px-2 py-0.5 text-[0.65rem] font-bold uppercase tracking-wide text-[var(--good)] ring-1 ring-inset ring-emerald-200">
      <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--good)]" aria-hidden />
      {label}
    </span>
  );
}

// HP bar: the fill eases on transform; a lighter "damage taken" trail follows behind.
export function HpBar({ label, value, max, tone = "player" }) {
  const ok = isNum(value) && isNum(max) && max > 0;
  const pct = ok ? Math.max(0, Math.min(1, value / max)) : 0;
  const shown = useTween(ok ? value : 0);
  const color = tone === "monster" ? "var(--hp-monster)" : "var(--hp-player)";
  return (
    <div>
      <div className="mb-1.5 flex items-center justify-between text-xs">
        <span className="text-[var(--muted)]">{label}</span>
        <span className="font-semibold tabular-nums text-[var(--ink)]">{ok ? `${Math.round(shown)} / ${Math.round(max)}` : "—"}</span>
      </div>
      <div
        className="relative h-2.5 w-full overflow-hidden rounded-full bg-gray-100"
        role="meter"
        aria-label={`${label} health`}
        aria-valuemin={0}
        aria-valuemax={ok ? max : 100}
        aria-valuenow={ok ? Math.round(value) : 0}
      >
        <div className="absolute inset-0 origin-left rounded-full bg-gray-300 transition-transform delay-200 duration-700" style={{ transform: `scaleX(${pct})` }} />
        <div className="absolute inset-0 origin-left rounded-full transition-transform duration-500" style={{ transform: `scaleX(${pct})`, background: color }} />
      </div>
    </div>
  );
}

function useNow(ms) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), ms);
    return () => clearInterval(t);
  }, [ms]);
  return now;
}

function RoundClock({ session, state }) {
  const now = useNow(500);
  const live = session.status === "live";
  // Unity reports the round timer; count down locally between updates.
  if (isNum(state?.timeLeft) && state.updatedAt) {
    const left = live ? state.timeLeft - (now - new Date(state.updatedAt).getTime()) / 1000 : state.timeLeft;
    return <span title="Round time left">{clock(Math.max(0, left))}</span>;
  }
  const end = live ? now : new Date(session.endedAt).getTime();
  return <span title="Elapsed">{clock((end - new Date(session.startedAt).getTime()) / 1000)}</span>;
}
const clock = (s) => `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(Math.floor(s % 60)).padStart(2, "0")}`;

function Tweened({ value }) {
  const v = useTween(isNum(value) ? value : 0);
  return isNum(value) ? formatNumber(v) : "—";
}

// One card per fight; subscribes to its own session only, so other cards don't re-render.
export const LiveArenaCard = memo(function LiveArenaCard({ id, compact = false }) {
  const entry = useLiveSession(id);
  if (!entry) return null;
  const { session, liveState: st } = entry;
  const live = session.status === "live";
  const last = st?.lastEvent;
  return (
    <article
      className={`flash-in min-w-0 rounded-2xl border bg-[var(--surface)] shadow-[var(--shadow-card)] transition-colors ${live ? "border-blue-200" : "border-[var(--border)] opacity-80"} ${compact ? "p-4" : "p-5"}`}
      aria-label={`${session.playerName}, ${levelName(session.level)}`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-sm font-bold text-[var(--accent)]">
            {session.playerName.slice(0, 2).toUpperCase()}
          </span>
          <div className="min-w-0">
            <Link to={`/dashboard/player/${encodeURIComponent(session.playerName)}`} className="block truncate font-semibold text-[var(--ink)] hover:text-[var(--accent)]">
              {session.playerName}
            </Link>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs text-[var(--muted)]">
              <LevelBadge level={session.level} /> {levelName(session.level)}
            </span>
          </div>
        </div>
        {live ? <LiveChip /> : <OutcomeBadge outcome={session.outcome} />}
      </div>

      <div className="mt-4 space-y-3">
        <HpBar label="Player" tone="player" value={st?.playerHp} max={st?.playerMaxHp} />
        <HpBar label="Monster" tone="monster" value={st?.monsterHp} max={st?.monsterMaxHp} />
      </div>

      <dl className="mt-4 grid grid-cols-4 gap-2 border-t border-[var(--border)] pt-3 text-center">
        {[
          ["Score", <Tweened key="s" value={st?.score ?? session.score} />],
          ["XP", <Tweened key="x" value={st?.xp ?? session.xp} />],
          ["Hits", <Tweened key="h" value={st?.hits ?? session.hitCount} />],
          ["Time", <RoundClock key="t" session={session} state={st} />],
        ].map(([label, value]) => (
          <div key={label}>
            <dt className="text-[0.68rem] text-[var(--muted)]">{label}</dt>
            <dd className="text-sm font-semibold tabular-nums text-[var(--ink)]">{value}</dd>
          </div>
        ))}
      </dl>

      {!compact && (
        <div className="mt-3 flex items-center justify-between gap-3 border-t border-[var(--border)] pt-3 text-xs">
          <p className="min-w-0 truncate text-[var(--muted)]" aria-live="polite">
            {last ? (
              <>
                <span className="tabular-nums">{formatClock(last.at)}</span> · <span className="text-[var(--ink-secondary)]">{last.message}</span>
              </>
            ) : (
              "Waiting for the first swing…"
            )}
          </p>
          <Link to={`/dashboard/session/${encodeURIComponent(session.id)}`} className="shrink-0 font-medium text-[var(--accent)] hover:underline">
            Details
          </Link>
        </div>
      )}
    </article>
  );
});

// Shown while any request is slow: Render's free tier sleeps when idle.
export function WakeNotice() {
  const waking = useSyncExternalStore(wakeStore.subscribe, wakeStore.isWaking);
  if (!waking) return null;
  return (
    <div className="flash-in flex items-start gap-3 rounded-2xl border border-amber-200 bg-[var(--warn-soft)] px-4 py-3 text-sm text-[var(--warn)]" role="status">
      <Icon name="refresh" className="mt-0.5 h-4 w-4 shrink-0 animate-spin [animation-duration:1.6s] motion-reduce:animate-none" />
      <div>
        <p className="font-semibold">Waking the server, this can take up to a minute.</p>
        <p className="mt-0.5 text-xs text-[var(--ink-secondary)]">The backend sleeps when nobody has used it for a while. Everything loads as soon as it answers.</p>
      </div>
    </div>
  );
}
