import { useEffect, useMemo, useRef, useState } from "react";
import { clockText } from "../../lib/effort.js";
import { SWING_SPEED_CAP } from "../../lib/game.js";
import { buildReplay, neighbourTime, NOT_LOGGED, readoutAt, replaySummary } from "../../lib/replay.js";
import { useReducedMotion } from "../../hooks/motion.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import { EmptyState } from "../ui/Feedback.jsx";
import { SegmentedControl } from "../ui/Controls.jsx";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

// Fight replay: one horizontal timeline of the whole fight with a play / pause button and a scrubber. Only events the game already
// stores are drawn (lib/replay.js): swings as bars (height = speed, colour = tier), monster attacks as markers, weapon clashes
// as diamonds, damage taken as red ticks. Scrubbing updates the readout (time, swing speed, damage, which attack).
// Reduced motion / Calm mode: playback moves in half-second steps instead of a continuous sweep.

const SPEEDS = [
  { id: 1, label: "1×" },
  { id: 4, label: "4×" },
  { id: 10, label: "10×" },
];
const TIER_COLOR = { tap: "var(--tier-tap)", light: "var(--tier-light)", medium: "var(--tier-medium)", full: "var(--tier-full)" };
const TIER_ORDER = ["tap", "light", "medium", "full"];
const H = 188;
const LANE = { attackY: 16, baseY: 124, barMax: 84, clashY: 138, hurtTop: 152, hurtMax: 26 };
export const REPLAY_MIN_H = 520;

function Diamond({ x, y, r = 5, ...rest }) {
  return <path d={`M${x},${y - r}L${x + r},${y}L${x},${y + r}L${x - r},${y}Z`} {...rest} />;
}

function Timeline({ model, t, duration, onSeek }) {
  const [ref, width] = useElementWidth();
  const gutter = width >= 560 ? 84 : 8;
  const pad = { l: gutter, r: 12 };
  const iw = Math.max(10, width - pad.l - pad.r);
  const sx = (s) => pad.l + (Math.min(duration, s) / Math.max(0.1, duration)) * iw;
  const barW = Math.max(2, Math.min(5, (iw / Math.max(1, model.swings.length)) * 0.6));
  const seekFromPointer = (e) => {
    const x = e.clientX - e.currentTarget.getBoundingClientRect().left;
    onSeek(Math.min(duration, Math.max(0, ((x - pad.l) / iw) * duration)));
  };
  const xTicks = Array.from({ length: 5 }, (_, i) => (duration * i) / 4);
  return (
    <div ref={ref} className="w-full min-w-0 select-none">
      {width > 0 && (
        <svg
          width={width}
          height={H}
          role="img"
          aria-label={`Fight timeline. ${replaySummary(model)} Use the slider below to move through the fight.`}
          className="block cursor-pointer rounded-lg"
          onPointerDown={(e) => {
            e.currentTarget.setPointerCapture?.(e.pointerId);
            seekFromPointer(e);
          }}
          onPointerMove={(e) => e.buttons === 1 && seekFromPointer(e)}
        >
          {/* lane guides */}
          <line x1={pad.l} x2={width - pad.r} y1={LANE.baseY} y2={LANE.baseY} stroke="var(--border-strong)" />
          {[0.5, 1].map((f) => (
            <line key={f} x1={pad.l} x2={width - pad.r} y1={LANE.baseY - LANE.barMax * f} y2={LANE.baseY - LANE.barMax * f} stroke="var(--grid)" />
          ))}
          {gutter > 20 && (
            <g fontSize="11" fontWeight="600" fill="var(--muted)">
              <text x={0} y={LANE.attackY + 4}>Monster</text>
              <text x={0} y={LANE.baseY - LANE.barMax / 2 + 4}>Swings</text>
              <text x={0} y={LANE.baseY - LANE.barMax + 4} className="tabular-nums">{SWING_SPEED_CAP} m/s</text>
              <text x={0} y={LANE.clashY + 4}>Clashes</text>
              <text x={0} y={LANE.hurtTop + 14}>Damage taken</text>
            </g>
          )}
          {xTicks.map((s, i) => (
            <text key={i} x={sx(s)} y={H - 4} textAnchor={i === 0 ? "start" : i === 4 ? "end" : "middle"} fontSize="11" fill="var(--muted)" className="tabular-nums">
              {clockText(s)}
            </text>
          ))}

          {/* what has already happened is solid, what is still to come is faint */}
          {model.attacks.map((a, i) => (
            <path key={`a${i}`} d={`M${sx(a.t) - 5},${LANE.attackY - 6}h10l-5,11z`} fill="var(--hp-monster)" opacity={a.t <= t ? 1 : 0.35}>
              <title>{`${clockText(a.t)} · ${a.attack}`}</title>
            </path>
          ))}
          {model.swings.map((s, i) => {
            const h = Math.max(2, (s.speed / SWING_SPEED_CAP) * LANE.barMax);
            return (
              <rect key={`s${i}`} x={sx(s.t) - barW / 2} y={LANE.baseY - h} width={barW} height={h} rx="1" fill={TIER_COLOR[s.tier] || "var(--blue-400)"} opacity={s.t <= t ? 1 : 0.35}>
                <title>{`${clockText(s.t)} · ${s.speed.toFixed(1)} m/s${s.tier ? ` · ${s.tier}` : ""}`}</title>
              </rect>
            );
          })}
          {model.clashes.map((c, i) => (
            <Diamond key={`c${i}`} x={sx(c.t)} y={LANE.clashY} fill="var(--warn)" opacity={c.t <= t ? 1 : 0.35}>
              <title>{`${clockText(c.t)} · weapon clash`}</title>
            </Diamond>
          ))}
          {model.hurts.map((h, i) => {
            const len = 6 + (h.amount / Math.max(1, model.maxHurt)) * (LANE.hurtMax - 6);
            return (
              <line key={`h${i}`} x1={sx(h.t)} x2={sx(h.t)} y1={LANE.hurtTop} y2={LANE.hurtTop + len} stroke="var(--bad)" strokeWidth="2.5" strokeLinecap="round" opacity={h.t <= t ? 1 : 0.35}>
                <title>{`${clockText(h.t)} · took ${h.amount.toFixed(1)}${h.attack ? ` from ${h.attack}` : ""}`}</title>
              </line>
            );
          })}

          {/* cursor */}
          <line x1={sx(t)} x2={sx(t)} y1={2} y2={H - 18} stroke="var(--accent)" strokeWidth="2" />
          <path d={`M${sx(t) - 5},0h10l-5,7z`} fill="var(--accent)" />
        </svg>
      )}
      {/* The slider is laid over the plot's own x range (the thumb is 18 px wide, so it is 9 px wider on each side), so the thumb sits under the cursor. */}
      <input
        type="range"
        className="replay-range mt-1 block"
        style={{ marginLeft: Math.max(0, pad.l - 9), width: Math.max(0, iw + 18) }}
        min={0}
        max={duration}
        step={0.1}
        value={Math.min(t, duration)}
        onChange={(e) => onSeek(Number(e.target.value))}
        aria-label="Position in the fight"
        aria-valuetext={`${clockText(t)} of ${clockText(duration)}`}
      />
    </div>
  );
}

function Legend() {
  return (
    <ul className="mt-2 flex flex-wrap gap-x-5 gap-y-1.5 text-xs font-semibold text-[var(--ink-secondary)]" aria-label="Legend">
      <li className="inline-flex items-center gap-2">
        <span className="inline-flex items-end gap-0.5" aria-hidden>
          {TIER_ORDER.map((tier, i) => (
            <span key={tier} className="w-[3px] rounded-[1px]" style={{ background: TIER_COLOR[tier], height: 6 + i * 3 }} />
          ))}
        </span>
        Swing: height is speed, colour is tier (tap, light, medium, full)
      </li>
      <li className="inline-flex items-center gap-1.5">
        <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden><path d="M1,2h10l-5,9z" fill="var(--hp-monster)" /></svg>
        Monster attack
      </li>
      <li className="inline-flex items-center gap-1.5">
        <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden><path d="M6,0L12,6L6,12L0,6Z" fill="var(--warn)" /></svg>
        Weapon clash
      </li>
      <li className="inline-flex items-center gap-1.5">
        <span className="h-3 w-[3px] rounded-full bg-[var(--bad)]" aria-hidden />
        Damage taken
      </li>
    </ul>
  );
}

function Readout({ r, playing }) {
  const swing = r.swing;
  const items = [
    ["Time", clockText(r.t)],
    ["Last swing", swing ? `${swing.speed.toFixed(1)} m/s${swing.tier ? ` · ${swing.tier}` : ""}` : "none yet", swing ? `${r.swingAgeSec.toFixed(1)} s ago` : null],
    ["Damage taken", r.ticksTaken ? `${Math.round(r.damageTaken)} so far` : "none yet", r.lastHurt ? `last ${r.lastHurt.amount.toFixed(1)}${r.lastHurt.attack ? ` from ${r.lastHurt.attack}` : ""}` : null],
    ["Monster attack", r.attack ? r.attack.attack : "none right now", r.attack?.category || (r.lastAttack ? `last: ${r.lastAttack.attack}` : null)],
  ];
  return (
    <dl className="mt-4 grid grid-cols-2 gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] p-4 sm:grid-cols-4" aria-live={playing ? "off" : "polite"}>
      {items.map(([k, v, sub]) => (
        <div key={k} className="min-w-0">
          <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
          <dd className="mt-0.5 truncate text-base font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
          {sub && <dd className="truncate text-xs font-medium text-[var(--muted)]">{sub}</dd>}
        </div>
      ))}
    </dl>
  );
}

function EventTable({ model }) {
  const rows = useMemo(
    () =>
      [
        ...model.swings.map((s) => ({ t: s.t, kind: "Swing", detail: `${s.speed.toFixed(1)} m/s${s.tier ? `, ${s.tier}` : ""}${s.damage !== null ? `, ${Math.round(s.damage)} damage dealt` : ""}` })),
        ...model.attacks.map((a) => ({ t: a.t, kind: "Monster attack", detail: `${a.attack}${a.category ? ` (${a.category})` : ""}` })),
        ...model.clashes.map((c) => ({ t: c.t, kind: "Weapon clash", detail: c.speed !== null ? `${c.speed.toFixed(1)} m/s` : "" })),
        ...model.hurts.map((h) => ({ t: h.t, kind: "Damage taken", detail: `${h.amount.toFixed(1)}${h.attack ? ` from ${h.attack}` : ""}` })),
      ].sort((a, b) => a.t - b.t),
    [model]
  );
  return (
    <details className="mt-3 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2 text-xs">
      <summary className="cursor-pointer font-semibold text-[var(--ink-secondary)]">Show the events as a table ({rows.length})</summary>
      <div className="scrollbar-thin mt-2 max-h-64 overflow-auto">
        <table className="w-full text-left tabular-nums">
          <caption className="sr-only">Every swing, monster attack, weapon clash and damage tick in this fight, in time order</caption>
          <thead>
            <tr className="text-[var(--muted)]">
              <th scope="col" className="py-1 pr-4 font-semibold">Time</th>
              <th scope="col" className="py-1 pr-4 font-semibold">Event</th>
              <th scope="col" className="py-1 font-semibold">Detail</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={i} className="border-t border-[var(--border)]">
                <td className="py-1 pr-4">{clockText(r.t)}</td>
                <td className="py-1 pr-4">{r.kind}</td>
                <td className="py-1">{r.detail}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}

export default function FightReplay({ events, durationSec = 0 }) {
  const model = useMemo(() => buildReplay(events, durationSec), [events, durationSec]);
  const duration = Math.max(1, model.duration);
  const reduced = useReducedMotion();
  const [t, setT] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState(4);
  const root = useRef(null);

  useEffect(() => {
    if (!playing) return undefined;
    // Continuous sweep normally; coarse half-second steps under reduced motion / Calm mode.
    let raf;
    let timer;
    let last = performance.now();
    const advance = (dt) =>
      setT((cur) => {
        if (cur + dt >= duration) {
          setPlaying(false);
          return duration;
        }
        return cur + dt;
      });
    if (reduced) timer = setInterval(() => advance(0.5 * speed), 500);
    else {
      const tick = (now) => {
        advance(((now - last) / 1000) * speed);
        last = now;
        raf = requestAnimationFrame(tick);
      };
      raf = requestAnimationFrame(tick);
    }
    return () => {
      cancelAnimationFrame(raf);
      clearInterval(timer);
    };
  }, [playing, speed, duration, reduced]);

  if (model.isEmpty) {
    return <EmptyState icon="clock" title="Nothing to replay" description="No swings, monster attacks, clashes or damage were recorded for this fight (it may predate the live-telemetry build)." />;
  }

  const at = Math.min(t, duration);
  const r = readoutAt(model, at);
  const atEnd = at >= duration - 0.05;
  const toggle = () => {
    if (playing) return setPlaying(false);
    if (atEnd) setT(0);
    setPlaying(true);
  };
  const jump = (dir) => {
    setPlaying(false);
    const n = neighbourTime(model, at, dir);
    setT(n === null ? (dir > 0 ? duration : 0) : n);
  };

  return (
    <div
      ref={root}
      className="p-5"
      style={{ minHeight: REPLAY_MIN_H }}
      onKeyDown={(e) => {
        // Space plays / pauses from anywhere in the panel except on a button, where it already means "press".
        if (e.key === " " && !e.target.closest("button, summary, a")) {
          e.preventDefault();
          toggle();
        }
      }}
    >
      <div className="flex flex-wrap items-center gap-2">
        <Button variant="primary" size="md" icon={playing ? "pause" : "play"} onClick={toggle} aria-label={playing ? "Pause the replay" : atEnd ? "Replay from the start" : "Play the replay"}>
          {playing ? "Pause" : atEnd ? "Replay" : "Play"}
        </Button>
        <Button variant="ghost" icon="chevronLeft" onClick={() => jump(-1)} aria-label="Previous event">
          <span className="hidden sm:inline">Prev event</span>
        </Button>
        <Button variant="ghost" onClick={() => jump(1)} aria-label="Next event">
          <span className="hidden sm:inline">Next event</span>
          <Icon name="chevronRight" className="h-3.5 w-3.5" />
        </Button>
        <span className="ml-auto text-sm font-bold tabular-nums text-[var(--ink)]">
          {clockText(at)} <span className="font-semibold text-[var(--muted)]">/ {clockText(duration)}</span>
        </span>
        <SegmentedControl label="Replay speed" options={SPEEDS} value={speed} onChange={setSpeed} />
      </div>

      <div className="mt-3">
        <Timeline model={model} t={at} duration={duration} onSeek={(v) => { setPlaying(false); setT(v); }} />
        <p className="mt-1 text-xs font-medium text-[var(--muted)]">Arrow keys move 0.1 s (hold for faster), Page Up and Page Down jump further, Space plays or pauses.</p>
      </div>

      <Legend />
      <Readout r={r} playing={playing} />

      <p className="mt-3 text-xs font-medium text-[var(--muted)]">{replaySummary(model)} Swing speeds above the game's {SWING_SPEED_CAP} m/s cap are drawn at the cap.</p>
      <EventTable model={model} />
      <details className="mt-3 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2 text-xs">
        <summary className="cursor-pointer font-semibold text-[var(--ink-secondary)]">What this replay can't show yet</summary>
        <ul className="mt-2 list-disc space-y-1 pl-5 font-medium text-[var(--ink-secondary)]">
          {NOT_LOGGED.map((x) => (
            <li key={x}>{x}</li>
          ))}
        </ul>
        <p className="mt-2 text-[var(--muted)]">The game doesn't log these yet, so they are left out rather than guessed.</p>
      </details>
    </div>
  );
}
