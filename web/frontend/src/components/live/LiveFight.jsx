import { memo, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useLiveMessages, useLiveSession } from "../../live/LiveProvider.jsx";
import { LEVELS, levelName } from "../../lib/game.js";
import { formatClock, formatNumber } from "../../lib/format.js";
import { useTween } from "../../hooks/motion.js";
import { LevelBadge, OutcomeBadge } from "../ui/Badge.jsx";
import Icon from "../ui/Icon.jsx";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const TIER_TONE = { full: "text-[var(--accent-strong)]", medium: "text-[var(--accent)]", light: "text-[var(--ink-secondary)]", tap: "text-[var(--muted)]" };

export function LiveChip({ label = "Live" }) {
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--good-soft)] px-2 py-0.5 text-xs font-extrabold uppercase tracking-wide text-[var(--good-ink)] ring-1 ring-inset ring-emerald-200">
      <span className="relative flex h-2 w-2" aria-hidden>
        <span className="absolute inset-0 animate-ping rounded-full bg-[var(--good)] opacity-60 motion-reduce:animate-none" />
        <span className="relative h-2 w-2 rounded-full bg-[var(--good)]" />
      </span>
      {label}
    </span>
  );
}

// Fill eases on transform; a pale trail lags behind, and a flash marks the chunk just lost.
// size "lg" (Live Arena): taller bar, side icon + name, percentage — readable from across a room.
export function HpBar({ label, value, max, tone = "player", popups = [], size = "md", sub }) {
  const ok = isNum(value) && isNum(max) && max > 0;
  const pct = ok ? Math.max(0, Math.min(1, value / max)) : 0;
  const shown = useTween(ok ? value : 0);
  const prev = useRef(pct);
  const [flash, setFlash] = useState(null);

  useEffect(() => {
    if (pct < prev.current - 0.001) setFlash({ from: pct, to: prev.current, key: Date.now() });
    prev.current = pct;
  }, [pct]);

  const monster = tone === "monster";
  const lg = size === "lg";
  return (
    <div className="relative">
      {lg ? (
        <div className="mb-2 flex items-end justify-between gap-3">
          <span className="flex min-w-0 items-center gap-2">
            <span
              className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-white"
              style={{ background: monster ? "var(--hp-monster)" : "var(--hp-player)" }}
              aria-hidden
            >
              <Icon name={monster ? "flame" : "player"} className="h-4 w-4" />
            </span>
            <span className="min-w-0">
              <span className="block text-[0.7rem] font-extrabold uppercase tracking-wider" style={{ color: monster ? "var(--hp-monster)" : "var(--hp-player)" }}>
                {label}
              </span>
              {sub && <span className="block truncate text-sm font-bold text-[var(--ink)]">{sub}</span>}
            </span>
          </span>
          <span className="shrink-0 text-right tabular-nums">
            <span className="text-lg font-extrabold text-[var(--ink)]">{ok ? Math.round(shown) : "—"}</span>
            <span className="text-sm font-semibold text-[var(--muted)]">{ok ? ` / ${Math.round(max)}` : ""}</span>
            {ok && <span className="ml-2 text-sm font-bold text-[var(--ink-secondary)]">{Math.round(pct * 100)}%</span>}
          </span>
        </div>
      ) : (
        <div className="mb-1.5 flex items-center justify-between text-xs">
          <span className="font-semibold text-[var(--ink-secondary)]">{label}</span>
          <span className="font-bold tabular-nums text-[var(--ink)]">{ok ? `${Math.round(shown)} / ${Math.round(max)}` : "—"}</span>
        </div>
      )}
      <div
        className={`relative w-full overflow-hidden rounded-full bg-gray-100 ${lg ? "h-5" : "h-3"}`}
        role="meter"
        aria-label={`${label} health`}
        aria-valuemin={0}
        aria-valuemax={ok ? max : 100}
        aria-valuenow={ok ? Math.round(value) : 0}
      >
        <div
          className="absolute inset-0 origin-left rounded-full transition-transform delay-300 duration-700 ease-out"
          style={{ transform: `scaleX(${pct})`, background: monster ? "var(--hp-monster-trail)" : "var(--hp-player-trail)" }}
        />
        <div
          className="absolute inset-0 origin-left rounded-full transition-transform duration-300 ease-out"
          style={{ transform: `scaleX(${pct})`, background: monster ? "var(--hp-monster)" : "var(--hp-player)" }}
        />
        {flash && <div key={flash.key} className="hp-flash absolute inset-y-0 bg-white" style={{ left: `${flash.from * 100}%`, width: `${(flash.to - flash.from) * 100}%` }} aria-hidden />}
      </div>
      <div className={`pointer-events-none absolute right-12 ${lg ? "-top-3" : "-top-1"}`} aria-hidden>
        {popups.map((p) => (
          <span key={p.key} className={`popup-float absolute right-0 whitespace-nowrap font-extrabold tabular-nums ${lg ? "text-lg" : "text-sm"} ${p.className}`}>
            {p.text}
          </span>
        ))}
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

// Counts toward the new value; each increase re-keys so it ticks up into place.
function Tweened({ value }) {
  const v = useTween(isNum(value) ? value : 0);
  const [bump, setBump] = useState(0);
  const prev = useRef(value);
  useEffect(() => {
    if (isNum(value) && isNum(prev.current) && value > prev.current) setBump((b) => b + 1);
    prev.current = value;
  }, [value]);
  return (
    <span key={bump} className={bump ? "tick-up inline-block" : "inline-block"}>
      {isNum(value) ? formatNumber(v) : "—"}
    </span>
  );
}

// Floating "-12" numbers from the stream's hit / player_hurt events for this session.
function usePopups(id, live) {
  const [items, setItems] = useState([]);
  const timers = useRef([]);
  useEffect(() => () => timers.current.forEach(clearTimeout), []);
  useLiveMessages((type, e) => {
    if (!live || type !== "event_created" || e.sessionId !== id) return;
    const p = e.payload || {};
    let item = null;
    if (e.type === "hit" && isNum(p.damage) && p.damage > 0) item = { bar: "monster", text: `−${formatNumber(p.damage)}`, className: TIER_TONE[p.tier] || TIER_TONE.medium };
    else if (e.type === "player_hurt" && isNum(p.amount) && p.amount > 0) item = { bar: "player", text: `−${formatNumber(p.amount)}`, className: "text-[var(--bad)]" };
    if (!item) return;
    const key = `${e.id}-${Date.now()}`;
    setItems((list) => [...list.slice(-5), { ...item, key }]);
    timers.current.push(setTimeout(() => setItems((list) => list.filter((x) => x.key !== key)), 1300));
  });
  return items;
}

// One card per fight; subscribes to its own session only, so other cards don't re-render.
export const LiveArenaCard = memo(function LiveArenaCard({ id, compact = false }) {
  const entry = useLiveSession(id);
  const live = entry?.session.status === "live";
  const popups = usePopups(id, live);
  if (!entry) return null;
  const { session, liveState: st } = entry;
  const last = st?.lastEvent;
  const inBar = (bar) => popups.filter((p) => p.bar === bar);

  return (
    <article
      className={`flash-in min-w-0 overflow-hidden rounded-2xl border bg-[var(--surface)] shadow-[var(--shadow-card)] transition-[opacity,border-color] duration-500 ${
        live ? "live-pulse border-[var(--accent-ring)]" : "border-[var(--border)] opacity-80"
      } ${compact ? "p-4" : "p-5"}`}
      aria-label={`${session.playerName}, ${levelName(session.level)}`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[var(--accent)] text-sm font-extrabold text-white shadow-[var(--shadow-glow)]">
            {session.playerName.slice(0, 2).toUpperCase()}
          </span>
          <div className="min-w-0">
            <Link to={`/dashboard/player/${encodeURIComponent(session.playerName)}`} className="block truncate font-bold text-[var(--ink)] hover:text-[var(--accent)]">
              {session.playerName}
            </Link>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs font-medium text-[var(--muted)]">
              <LevelBadge level={session.level} /> {levelName(session.level)}
            </span>
          </div>
        </div>
        {live ? <LiveChip /> : <OutcomeBadge outcome={session.outcome} />}
      </div>

      {compact ? (
        <div className="mt-5 space-y-3.5">
          <HpBar label="Player" tone="player" value={st?.playerHp} max={st?.playerMaxHp} popups={inBar("player")} />
          <HpBar label="Monster" tone="monster" value={st?.monsterHp} max={st?.monsterMaxHp} popups={inBar("monster")} />
        </div>
      ) : (
        <div className="mt-5 space-y-5">
          <HpBar label="Player" sub={session.playerName} size="lg" tone="player" value={st?.playerHp} max={st?.playerMaxHp} popups={inBar("player")} />
          <HpBar label="Monster" sub={LEVELS[session.level]?.monster} size="lg" tone="monster" value={st?.monsterHp} max={st?.monsterMaxHp} popups={inBar("monster")} />
        </div>
      )}

      <dl className="mt-4 grid grid-cols-4 gap-2 border-t border-[var(--border)] pt-3 text-center">
        {[
          ["Score", <Tweened key="s" value={st?.score ?? session.score} />],
          ["XP", <Tweened key="x" value={st?.xp ?? session.xp} />],
          ["Hits", <Tweened key="h" value={st?.hits ?? session.hitCount} />],
          ["Time", <RoundClock key="t" session={session} state={st} />],
        ].map(([label, value]) => (
          <div key={label} className="overflow-hidden">
            <dt className="text-xs font-semibold text-[var(--muted)]">{label}</dt>
            <dd className="text-base font-extrabold tabular-nums text-[var(--ink)]">{value}</dd>
          </div>
        ))}
      </dl>

      {!compact && (
        <div className="mt-3 flex items-center justify-between gap-3 border-t border-[var(--border)] pt-3 text-xs">
          <p className="min-w-0 truncate font-medium text-[var(--muted)]" aria-live="polite">
            {last ? (
              <span key={last.at} className="fade-in">
                <span className="tabular-nums">{formatClock(last.at)}</span> · <span className="font-semibold text-[var(--ink-secondary)]">{last.message}</span>
              </span>
            ) : (
              "Waiting for the first swing…"
            )}
          </p>
          <Link to={`/dashboard/session/${encodeURIComponent(session.id)}`} className="press shrink-0 rounded font-semibold text-[var(--accent)] hover:underline">
            Details
          </Link>
        </div>
      )}
    </article>
  );
});
