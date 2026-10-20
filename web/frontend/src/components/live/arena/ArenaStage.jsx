import { memo, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { useLiveSession } from "../../../live/LiveProvider.jsx";
import { useReducedMotion, useTween } from "../../../hooks/motion.js";
import { isClashing, isGuarding, phaseLabel, phaseOf, resultLabel, visibleAttack } from "../../../lib/arena.js";
import { monsterFor } from "../../../lib/arenaApp.js";
import { SWING_SPEED_CAP, swingBands } from "../../../lib/game.js";
import { MONSTER_GLOW } from "../../../lib/monsters.js";
import { formatDuration, formatNumber } from "../../../lib/format.js";
import MonsterPortrait from "../../ui/MonsterPortrait.jsx";
import Icon from "../../ui/Icon.jsx";
import { LiveChip } from "../LiveFight.jsx";
import GhostPace from "../GhostPace.jsx";
import KillFeed from "./KillFeed.jsx";
import { DamagePops, usePunch, useRing } from "./Impact.jsx";
import { useArena } from "./useArena.js";
import { useRank } from "../../../hooks/useRank.js";
import { useApi } from "../../../hooks/useApi.js";
import { api } from "../../../lib/api.js";
import RankBadge from "../../rank/RankBadge.jsx";
import RankUpBanner from "../../rank/RankUpBanner.jsx";
import PbBanner from "../../records/PbBanner.jsx";
import { CaptionBar, CommentaryLog, useStageCommentary } from "./Commentary.jsx";

// One live fight, laid out so that someone across the room can read it in two seconds: WHO is fighting WHICH monster (the headline),
// how it is going (the duel bar), what the monster is doing right now (the attack strip) and how hard the player is swinging (the
// gauge). Every number comes from the live stream's state; a figure the game did not send is shown as "—", never invented.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const fraction = (v, max) => (isNum(v) && isNum(max) && max > 0 ? Math.max(0, Math.min(1, v / max)) : null);
const clock = (s) => `${String(Math.floor(Math.max(0, s) / 60)).padStart(2, "0")}:${String(Math.floor(Math.max(0, s) % 60)).padStart(2, "0")}`;
const TIER_COLOR = { none: "var(--border-strong)", tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };

function useNow(ms) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), ms);
    return () => clearInterval(t);
  }, [ms]);
  return now;
}

const Kicker = ({ children, className = "" }) => <p className={`text-[0.68rem] font-extrabold uppercase tracking-wider ${className}`}>{children}</p>;

// ------------------------------------------------------------------------------------------------------------------ monster card

function HpFill({ pct, color, trail, origin = "origin-left" }) {
  return (
    <>
      <div className={`arena-trail absolute inset-0 ${origin}`} style={{ transform: `scaleX(${pct ?? 0})`, background: trail }} />
      <div className={`arena-fill absolute inset-0 ${origin}`} style={{ transform: `scaleX(${pct ?? 0})`, background: color }} />
    </>
  );
}

const MonsterCard = memo(function MonsterCard({ monster, hp, maxHp, guarding, clashing, phase, clashAt, impact, compact = false }) {
  const reduced = useReducedMotion();
  const ring = useRing(clashAt);
  const frac = fraction(hp, maxHp);
  const glow = MONSTER_GLOW[monster.levelKey];
  const enraged = phase === 3;
  return (
    <article className={`grid min-w-0 grid-cols-[8.5rem_minmax(0,1fr)] overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] ${compact ? "" : "lg:grid-cols-1"}`} aria-label={`${monster.name}, the monster in this fight`}>
      <div className={`relative flex items-end justify-center overflow-hidden px-3 pt-4 ${compact ? "" : "lg:px-6 lg:pt-6"}`} style={{ background: `linear-gradient(180deg, color-mix(in srgb, ${glow.aura} 14%, white) 0%, var(--surface) 100%)` }}>
        <MonsterPortrait level={monster.levelKey} size="md" glow={!reduced} intensity={frac ?? 1} className="!w-full max-w-[17.5rem]" />
        {/* a ring that expands from the monster when weapons clash */}
        <span ref={ring} className="pointer-events-none absolute left-1/2 top-1/2 h-36 w-36 -translate-x-1/2 -translate-y-1/2 rounded-full border-4 border-[var(--warn)] opacity-0" aria-hidden />
        {/* state badges sit on the portrait; they appear and disappear, nothing larger moves */}
        <div className="pointer-events-none absolute left-2 top-2 flex flex-col items-start gap-1.5 lg:left-3 lg:top-3">
          {guarding && (
            <span className="arena-rise inline-flex items-center gap-1.5 rounded-full bg-[var(--accent)] px-2.5 py-1 text-xs font-extrabold text-white shadow-[var(--shadow-lift)]">
              <Icon name="shield" className="h-3.5 w-3.5" strokeWidth={2.4} /> Guarding
            </span>
          )}
          {enraged && (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--bad)] px-2.5 py-1 text-xs font-extrabold text-white shadow-[var(--shadow-lift)]">
              <Icon name="flame" className="h-3.5 w-3.5" strokeWidth={2.4} /> Enraged
            </span>
          )}
        </div>
        {clashing && (
          <span className="arena-clash pointer-events-none absolute right-2 top-2 inline-flex items-center gap-1.5 rounded-full bg-[var(--warn)] px-3 py-1 text-sm font-extrabold text-white shadow-[var(--shadow-lift)] lg:right-3 lg:top-3">
            <Icon name="swords" className="h-4 w-4" strokeWidth={2.4} /> Clash!
          </span>
        )}
      </div>

      <div className={`min-w-0 p-4 ${compact ? "" : "lg:p-5"}`}>
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="rounded-md bg-[var(--surface-muted)] px-1.5 py-0.5 text-xs font-bold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">{monster.levelName}</span>
          <span className="rounded-md bg-[var(--accent-soft)] px-1.5 py-0.5 text-xs font-bold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">{monster.difficulty}</span>
        </div>
        <h2 className="mt-2 text-xl font-extrabold leading-tight tracking-tight text-[var(--ink)] lg:text-2xl">{monster.name}</h2>
        {monster.weapon && (
          <p className="mt-1 flex items-center gap-1.5 text-sm font-semibold text-[var(--ink-secondary)]">
            <Icon name="swords" className="h-3.5 w-3.5 text-[var(--muted)]" /> {monster.weapon}
          </p>
        )}

        <div className="relative mt-3">
          <DamagePops impact={impact} />
          <div className="flex items-baseline justify-between gap-2">
            <Kicker className="text-[var(--hp-monster)]">Monster HP</Kicker>
            <p className="text-sm font-extrabold tabular-nums text-[var(--ink)]">
              {isNum(hp) ? formatNumber(hp) : "—"}
              <span className="font-semibold text-[var(--muted)]"> / {isNum(maxHp) ? formatNumber(maxHp) : "—"}</span>
            </p>
          </div>
          <div className="relative mt-1.5 h-3 overflow-hidden rounded-full bg-gray-100" role="meter" aria-label="Monster health" aria-valuemin={0} aria-valuemax={isNum(maxHp) ? maxHp : 100} aria-valuenow={isNum(hp) ? Math.round(hp) : 0}>
            <HpFill pct={frac} color={enraged ? "var(--bad)" : "var(--hp-monster)"} trail="var(--hp-monster-trail)" />
            {/* the boss's phase breaks, where it changes behaviour */}
            {monster.hasPhases && [0.33, 0.66].map((b) => <span key={b} className="absolute inset-y-0 w-px bg-white/90" style={{ left: `${b * 100}%` }} aria-hidden />)}
          </div>
        </div>

        {monster.hasPhases && (
          <div className="mt-3" aria-label={phase ? `Boss ${phaseLabel(phase)}` : "Boss phase unknown"}>
            <ol className="grid grid-cols-3 gap-1.5 text-center text-[0.68rem] font-extrabold uppercase tracking-wide">
              {[1, 2, 3].map((n) => {
                const state = !phase ? "idle" : n < phase ? "done" : n === phase ? "now" : "next";
                const cls = {
                  idle: "bg-gray-100 text-[var(--muted)]",
                  done: "bg-[var(--accent-soft)] text-[var(--accent-strong)]",
                  now: n === 3 ? "bg-[var(--bad)] text-white" : "bg-[var(--accent)] text-white",
                  next: "bg-gray-100 text-[var(--muted)]",
                }[state];
                return (
                  <li key={n} className={`rounded-md px-1 py-1.5 ${cls}`} aria-current={state === "now" ? "step" : undefined}>
                    {n === 3 ? "Enraged" : `Phase ${n}`}
                  </li>
                );
              })}
            </ol>
          </div>
        )}
      </div>
    </article>
  );
});

// ------------------------------------------------------------------------------------------------------------------ duel bar

const Stat = ({ label, value }) => (
  <div className="min-w-0 text-center">
    <Kicker className="text-[var(--muted)]">{label}</Kicker>
    <p className="mt-0.5 truncate text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)] sm:text-3xl">{value}</p>
  </div>
);

const DuelBar = memo(function DuelBar({ playerName, monsterName, st, session, live }) {
  const p = fraction(st?.playerHp, st?.playerMaxHp);
  const m = fraction(st?.monsterHp, st?.monsterMaxHp);
  const score = useTween(isNum(st?.score) ? st.score : session.score || 0);
  const now = useNow(1000);
  const since = live && st?.updatedAt ? (now - new Date(st.updatedAt).getTime()) / 1000 : 0;
  const roundLeft = isNum(st?.timeLeft) ? Math.max(0, st.timeLeft - since) : null;
  const fightTime = isNum(st?.t) ? st.t + since : null;
  const lead = p === null || m === null ? null : p > m ? "player" : m > p ? "monster" : "even";
  return (
    <section className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] sm:p-5" aria-label="Health: player against monster">
      <div className="flex items-end justify-between gap-4">
        <div className="min-w-0">
          <Kicker className="text-[var(--hp-player)]">Player</Kicker>
          <p className="truncate text-lg font-extrabold tracking-tight text-[var(--ink)] sm:text-xl">{playerName}</p>
        </div>
        <div className="min-w-0 text-right">
          <Kicker className="text-[var(--hp-monster)]">Monster</Kicker>
          <p className="truncate text-lg font-extrabold tracking-tight text-[var(--ink)] sm:text-xl">{monsterName}</p>
        </div>
      </div>

      {/* one bar: the player's health runs in from the left edge, the monster's from the right edge; the gap between them is damage taken */}
      <div className="relative mt-3 flex h-7 overflow-hidden rounded-full bg-gray-100 sm:h-8" role="group" aria-label="Both health bars">
        <div className="relative w-1/2 overflow-hidden" role="meter" aria-label="Player health" aria-valuemin={0} aria-valuemax={isNum(st?.playerMaxHp) ? st.playerMaxHp : 100} aria-valuenow={isNum(st?.playerHp) ? Math.round(st.playerHp) : 0}>
          <HpFill pct={p} color="var(--hp-player)" trail="var(--hp-player-trail)" />
        </div>
        <div className="relative w-1/2 overflow-hidden" role="meter" aria-label="Monster health" aria-valuemin={0} aria-valuemax={isNum(st?.monsterMaxHp) ? st.monsterMaxHp : 100} aria-valuenow={isNum(st?.monsterHp) ? Math.round(st.monsterHp) : 0}>
          <HpFill pct={m} color="var(--hp-monster)" trail="var(--hp-monster-trail)" origin="origin-right" />
        </div>
        <span className="pointer-events-none absolute inset-y-0 left-1/2 w-[3px] -translate-x-1/2 bg-[var(--surface)]" aria-hidden />
      </div>
      <div className="mt-2 flex items-baseline justify-between gap-4 tabular-nums">
        <p className="text-sm font-extrabold text-[var(--ink)]">
          {isNum(st?.playerHp) ? formatNumber(st.playerHp) : "—"}
          <span className="font-semibold text-[var(--muted)]"> / {isNum(st?.playerMaxHp) ? formatNumber(st.playerMaxHp) : "—"}</span>
          {p !== null && <span className="ml-2 text-xs font-bold text-[var(--ink-secondary)]">{Math.round(p * 100)}%</span>}
        </p>
        <p className="hidden text-xs font-bold text-[var(--ink-secondary)] sm:block" aria-live="polite">
          {lead === "player" ? "Player ahead" : lead === "monster" ? "Monster ahead" : lead === "even" ? "Dead even" : ""}
        </p>
        <p className="text-sm font-extrabold text-[var(--ink)]">
          {m !== null && <span className="mr-2 text-xs font-bold text-[var(--ink-secondary)]">{Math.round(m * 100)}%</span>}
          {isNum(st?.monsterHp) ? formatNumber(st.monsterHp) : "—"}
          <span className="font-semibold text-[var(--muted)]"> / {isNum(st?.monsterMaxHp) ? formatNumber(st.monsterMaxHp) : "—"}</span>
        </p>
      </div>

      <dl className="mt-4 grid grid-cols-3 gap-2 border-t border-[var(--border)] pt-4">
        <Stat label="Score" value={formatNumber(score)} />
        <Stat label="Round left" value={roundLeft === null ? "—" : clock(roundLeft)} />
        <Stat label="Fight time" value={fightTime === null ? "—" : clock(fightTime)} />
      </dl>
    </section>
  );
});

// ------------------------------------------------------------------------------------------------------------------ attack strip

const CATEGORY_TONE = {
  Standard: "bg-gray-100 text-[var(--ink-secondary)] ring-[var(--border)]",
  Combo: "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent-ring)]",
  Heavy: "bg-[var(--warn-soft)] text-[var(--warn-ink)] ring-amber-200",
  IdleSlam: "bg-[var(--bad-soft)] text-[var(--bad-ink)] ring-red-200",
  Special: "bg-[var(--bad-soft)] text-[var(--bad-ink)] ring-red-200",
};

// A fixed-height strip, so an attack arriving never moves what is below it.
const AttackStrip = memo(function AttackStrip({ attack }) {
  const info = attack?.info;
  const resolved = attack?.result;
  const wind = info?.windUp;
  return (
    <section className="flex min-h-[5.75rem] flex-col justify-center rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)] sm:px-5" aria-label="The monster's current attack" aria-live="polite">
      {!attack ? (
        <>
          <Kicker className="text-[var(--muted)]">Now attacking</Kicker>
          <p className="mt-1 text-sm font-semibold text-[var(--muted)]">Waiting for the next attack…</p>
        </>
      ) : (
        <div key={attack.key} className="arena-rise">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Kicker className="text-[var(--hp-monster)]">Now attacking</Kicker>
            <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-extrabold ring-1 ring-inset ${CATEGORY_TONE[info.category] || CATEGORY_TONE.Standard}`}>{info.categoryLabel}</span>
            {resolved && (
              <span className={`ml-auto inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-extrabold ${resolved === "hit" ? "bg-[var(--bad)] text-white" : "bg-[var(--good)] text-white"}`}>
                <Icon name={resolved === "hit" ? "heart" : "shieldCheck"} className="h-3 w-3" strokeWidth={2.4} /> {resolved === "hit" ? "It hit" : "Dodged"}
              </span>
            )}
          </div>
          <p className="mt-0.5 truncate text-xl font-extrabold tracking-tight text-[var(--ink)] sm:text-2xl">{info.name}</p>
          {wind !== null && (
            <div className="mt-2 flex items-center gap-3">
              <div className="relative h-2.5 flex-1 overflow-hidden rounded-full bg-gray-100" aria-hidden>
                <div
                  className={`${resolved ? "" : "arena-telegraph"} absolute inset-0 rounded-full`}
                  style={{ "--wind": `${wind}s`, background: resolved === "dodged" ? "var(--good)" : "var(--hp-monster)", transform: resolved ? "scaleX(1)" : undefined }}
                />
              </div>
              <span className="w-24 shrink-0 text-right text-xs font-bold tabular-nums text-[var(--muted)]">{wind.toFixed(2)} s wind-up</span>
            </div>
          )}
        </div>
      )}
    </section>
  );
});

// ------------------------------------------------------------------------------------------------------------------ swing gauge

const CX = 110;
const CY = 108;
const R = 86;
const polar = (speed, r) => {
  const a = Math.PI - (Math.min(Math.max(speed, 0), SWING_SPEED_CAP) / SWING_SPEED_CAP) * Math.PI; // 0 m/s on the left, 8 m/s on the right
  return [CX + r * Math.cos(a), CY - r * Math.sin(a)];
};
const arc = (from, to, r) => {
  const [x1, y1] = polar(from, r);
  const [x2, y2] = polar(to, r);
  return `M${x1.toFixed(2)},${y1.toFixed(2)} A${r},${r} 0 0 1 ${x2.toFixed(2)},${y2.toFixed(2)}`;
};

const SwingGauge = memo(function SwingGauge({ levelKey, swing, combo, impact }) {
  const { card, flash } = usePunch(impact);
  const bands = useMemo(() => swingBands(levelKey, SWING_SPEED_CAP), [levelKey]);
  const speed = swing ? swing.speed : 0;
  const band = swing ? bands.find((b) => speed >= b.from && speed < b.to) || bands[bands.length - 1] : bands[0];
  const tierId = swing ? swing.tier || band.id : "none";
  const label = swing ? ({ tap: "Tap", light: "Light", medium: "Medium", full: "Full power" }[tierId] || band.label) : "No hit";
  const color = TIER_COLOR[tierId] || TIER_COLOR.none;
  const deg = (Math.min(speed, SWING_SPEED_CAP) / SWING_SPEED_CAP) * 180;
  return (
    <section ref={card} className="relative flex flex-col rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] sm:p-5" aria-label="Latest swing speed and combo">
      {/* the accent flash of a full-power hit (opacity only) */}
      <span ref={flash} className="pointer-events-none absolute inset-0 rounded-2xl bg-[var(--accent)] opacity-0" aria-hidden />
      <div className="flex items-start justify-between gap-3">
        <Kicker className="text-[var(--muted)]">Swing speed</Kicker>
        <div className="text-right" aria-label={`Combo ${combo ?? 0}`}>
          <Kicker className="text-[var(--muted)]">Combo</Kicker>
          <p className="text-3xl font-extrabold leading-none tabular-nums text-[var(--ink)] sm:text-4xl">
            {combo ?? 0}
            <span className="text-xl">×</span>
          </p>
        </div>
      </div>
      <svg viewBox="0 0 220 128" className="mx-auto -mt-2 block w-full max-w-[22rem]" role="meter" aria-label="Swing speed in metres per second" aria-valuemin={0} aria-valuemax={SWING_SPEED_CAP} aria-valuenow={Number(speed.toFixed(1))} aria-valuetext={swing ? `${speed.toFixed(1)} metres per second, ${label}` : "No swing yet"}>
        {bands.map((b) => (
          <path key={b.id} d={arc(b.from + 0.04, b.to - 0.04, R)} fill="none" stroke={TIER_COLOR[b.id]} strokeWidth="14" strokeLinecap="butt" opacity={b.id === tierId ? 1 : 0.28} />
        ))}
        {[0, 2, 4, 6, 8].map((v) => {
          const [x, y] = polar(v, R + 15);
          return (
            <text key={v} x={x} y={y + 3.5} textAnchor="middle" fontSize="10" fontWeight="600" fill="var(--muted)" className="tabular-nums">
              {v}
            </text>
          );
        })}
        <g className="arena-needle" style={{ transform: `rotate(${deg}deg)`, transformOrigin: `${CX}px ${CY}px` }}>
          <line x1={CX} y1={CY} x2={CX - (R - 14)} y2={CY} stroke="var(--ink)" strokeWidth="3.5" strokeLinecap="round" />
        </g>
        <circle cx={CX} cy={CY} r="7" fill="var(--ink)" />
        <circle cx={CX} cy={CY} r="2.5" fill="var(--surface)" />
      </svg>
      <div className="-mt-1 flex items-baseline justify-center gap-2">
        <p className="text-3xl font-extrabold tabular-nums leading-none tracking-tight text-[var(--ink)]">{speed.toFixed(1)}</p>
        <p className="text-sm font-semibold text-[var(--muted)]">m/s</p>
      </div>
      <p className="mt-1.5 flex items-center justify-center gap-2 text-sm font-extrabold text-[var(--ink)]">
        <span className="h-2.5 w-2.5 rounded-full" style={{ background: color }} aria-hidden /> {label}
      </p>
    </section>
  );
});

// ------------------------------------------------------------------------------------------------------------------ result moment

const TONE = {
  good: "border-emerald-200 bg-[var(--good-soft)] text-[var(--good-ink)]",
  bad: "border-red-200 bg-[var(--bad-soft)] text-[var(--bad-ink)]",
  muted: "border-[var(--border)] bg-[var(--surface-muted)] text-[var(--ink-secondary)]",
};

function ResultMoment({ session, monster, records, rankUp }) {
  const r = resultLabel(session.outcome);
  const facts = [
    ["Score", formatNumber(session.score)],
    ["Fight time", formatDuration(session.durationSec)],
    ["Hits", formatNumber(session.hitCount)],
    ["Damage dealt", formatNumber(session.damageDealt)],
  ];
  return (
    <section className={`arena-rise rounded-2xl border-2 p-6 text-center sm:p-8 ${TONE[r.tone]}`} role="status" aria-label={`Fight finished: ${r.title}`}>
      <Icon name={r.icon} className="mx-auto h-10 w-10" strokeWidth={2} />
      <p className="mt-3 text-5xl font-extrabold tracking-tight sm:text-6xl">{r.title}</p>
      <p className="mt-2 text-base font-semibold text-[var(--ink-secondary)]">
        {session.playerName} against {monster.name}. {r.line}
      </p>
      {/* what this fight beat on its level, and a rank it reached (the stream's session_finished message carries both) */}
      <div className="mx-auto mt-5 max-w-2xl space-y-3 text-left">
        <PbBanner records={records} fireKey={session.id} />
        <RankUpBanner rankUp={rankUp} />
      </div>
      <dl className="mx-auto mt-6 grid max-w-2xl grid-cols-2 gap-3 sm:grid-cols-4">
        {facts.map(([k, v]) => (
          <div key={k} className="rounded-xl bg-[var(--surface)] px-3 py-3 shadow-[var(--shadow-card)]">
            <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
            <dd className="mt-0.5 text-xl font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
          </div>
        ))}
      </dl>
      <Link to={`/dashboard/session/${encodeURIComponent(session.id)}`} className="press mt-5 inline-flex items-center gap-1.5 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3.5 py-2 text-sm font-semibold text-[var(--accent)] hover:underline">
        Open the battle <Icon name="arrowRight" className="h-3.5 w-3.5" />
      </Link>
    </section>
  );
}

// ------------------------------------------------------------------------------------------------------------------ the stage

const initials = (name) => name.slice(0, 2).toUpperCase();

export default memo(function ArenaStage({ id, primary = true, compact = false }) {
  const entry = useLiveSession(id);
  const session = entry?.session;
  const st = entry?.liveState;
  const levelKey = session?.level;
  const monster = useMemo(() => monsterFor(levelKey), [levelKey]);
  const monsterFrac = fraction(st?.monsterHp, st?.monsterMaxHp);
  const phase = monster?.hasPhases ? phaseOf(levelKey, monsterFrac) : null;
  const { state, swing, now, impact } = useArena(id, levelKey, phase === 3);
  // The commentary of this fight: its own stream of lines, read aloud (when the viewer turned that on) only for the first fight on stage.
  const commentary = useStageCommentary(id, entry, { speak: primary });
  const rank = useRank(session?.playerName); // the player's effort rank, shown next to their name
  // The records a finished fight set come with the stream's message; if this page only learned of the end by polling, ask the server.
  const finishedWithoutExtras = Boolean(entry) && entry.session.status !== "live" && entry.newRecords === undefined;
  const fetchedExtras = useApi(() => (finishedWithoutExtras ? api.session(id) : Promise.resolve(null)), [id, finishedWithoutExtras]);
  if (!entry || !monster) return null;

  const Heading = primary ? "h1" : "h2";
  const live = session.status === "live";
  const attack = live ? visibleAttack(state, now) : null;
  const combo = isNum(st?.combo) ? Math.max(0, Math.round(st.combo)) : null;

  return (
    <div className="space-y-4" data-testid="arena-stage">
      {/* who is fighting which monster */}
      <header className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-[var(--accent)] text-sm font-extrabold text-white shadow-[var(--shadow-glow)]" aria-hidden>
          {initials(session.playerName)}
        </span>
        <Heading className="min-w-0 text-2xl font-extrabold tracking-tight text-[var(--ink)] sm:text-3xl">
          <Link to={`/dashboard/player/${encodeURIComponent(session.playerName)}`} className="hover:text-[var(--accent)]">
            {session.playerName}
          </Link>
          <span className="mx-2 text-[var(--muted)]">vs</span>
          <span>{monster.name}</span>
        </Heading>
        {rank.data && <RankBadge rank={rank.data.rank} size="md" />}
        <div className="ml-auto flex items-center gap-2">{live ? <LiveChip /> : null}</div>
      </header>

      {!live ? (
        <ResultMoment session={session} monster={monster} records={entry.newRecords ?? fetchedExtras.data?.newRecords} rankUp={entry.rankUp} />
      ) : (
        <div className={compact ? "grid gap-4" : "grid gap-4 lg:grid-cols-[19rem_minmax(0,1fr)] 2xl:grid-cols-[20rem_minmax(0,1fr)_25rem]"}>
          <MonsterCard monster={monster} hp={st?.monsterHp} maxHp={st?.monsterMaxHp} guarding={isGuarding(state, now)} clashing={isClashing(state, now)} phase={phase} clashAt={state.clashAt} impact={impact} compact={compact} />
          <div className="min-w-0 space-y-4">
            <DuelBar playerName={session.playerName} monsterName={monster.name} st={st} session={session} live={live} />
            <AttackStrip attack={attack} />
            <div className={compact ? "grid gap-4" : "grid gap-4 md:grid-cols-[minmax(0,19rem)_minmax(0,1fr)]"}>
              <SwingGauge levelKey={levelKey} swing={swing} combo={combo} impact={impact} />
              <div className="min-w-0 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] sm:p-5">
                <GhostPace id={id} session={session} state={st} flush />
              </div>
            </div>
          </div>
          <div className={`min-w-0 ${compact ? "" : "lg:col-span-2 2xl:col-span-1"}`}>
            <KillFeed sessionId={id} monsterName={monster.name} />
          </div>
        </div>
      )}

      {/* what is happening, in words: the caption stays through the result moment, the log keeps the last 20 lines */}
      <CaptionBar commentary={commentary} />
      <CommentaryLog commentary={commentary} />
    </div>
  );
});
