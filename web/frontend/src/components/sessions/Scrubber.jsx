import { useRef, useState } from "react";

const MARKS = {
  hit: { cls: "bg-amber-500", label: "Hit", lane: 0 },
  clash: { cls: "bg-gray-400", label: "Clash", lane: 0 },
  monster_attack: { cls: "bg-rose-600", label: "Monster attack", lane: 1 },
  player_hurt: { cls: "bg-[var(--bad)]", label: "Player hurt", lane: 1 },
  session_complete: { cls: "bg-emerald-600", label: "Result", lane: 2 },
};
export const SCRUB_LEGEND = [MARKS.hit, MARKS.monster_attack, MARKS.player_hurt, MARKS.session_complete];

const clock = (s) => `${String(Math.floor(Math.max(0, s) / 60)).padStart(2, "0")}:${String(Math.floor(Math.max(0, s) % 60)).padStart(2, "0")}`;

// Timeline scrubber: event markers along the fight, drag or use the keyboard to move.
export default function Scrubber({ t, duration, events, onSeek, onScrubStart }) {
  const track = useRef(null);
  const [hover, setHover] = useState(null);
  const d = Math.max(0.1, duration);
  const pct = Math.min(1, Math.max(0, t / d));

  const at = (clientX) => {
    const r = track.current.getBoundingClientRect();
    return Math.min(1, Math.max(0, (clientX - r.left) / r.width)) * d;
  };

  const onKey = (e) => {
    const step = e.shiftKey ? 5 : 1;
    const next = {
      ArrowRight: t + step,
      ArrowUp: t + step,
      ArrowLeft: t - step,
      ArrowDown: t - step,
      PageUp: t + 10,
      PageDown: t - 10,
      Home: 0,
      End: d,
    }[e.key];
    if (next === undefined) return;
    e.preventDefault();
    onScrubStart?.();
    onSeek(Math.min(d, Math.max(0, next)));
  };

  const marks = events.filter((e) => MARKS[e.type]);

  return (
    <div className="relative select-none pt-7">
      {hover !== null && (
        <span
          className="pointer-events-none absolute top-0 -translate-x-1/2 rounded-md bg-[var(--ink)] px-1.5 py-0.5 text-xs font-bold tabular-nums text-white"
          style={{ left: `${(hover / d) * 100}%` }}
          aria-hidden
        >
          {clock(hover)}
        </span>
      )}
      <div
        ref={track}
        role="slider"
        tabIndex={0}
        aria-label="Replay position"
        aria-valuemin={0}
        aria-valuemax={Math.round(d)}
        aria-valuenow={Math.round(t)}
        aria-valuetext={`${clock(t)} of ${clock(d)}`}
        onKeyDown={onKey}
        onPointerDown={(e) => {
          e.currentTarget.setPointerCapture(e.pointerId);
          onScrubStart?.();
          onSeek(at(e.clientX));
        }}
        onPointerMove={(e) => {
          setHover(at(e.clientX));
          if (e.currentTarget.hasPointerCapture(e.pointerId)) onSeek(at(e.clientX));
        }}
        onPointerLeave={() => setHover(null)}
        className="group relative h-12 cursor-pointer touch-none rounded-xl bg-[var(--surface-muted)] ring-1 ring-inset ring-[var(--border)] outline-none focus-visible:ring-2 focus-visible:ring-[var(--accent)]"
      >
        {/* marker lanes: swings on top, the monster in the middle, the result at the bottom */}
        <div className="absolute inset-x-2 inset-y-2" aria-hidden>
          {marks.map((e) => {
            const m = MARKS[e.type];
            return (
              <span
                key={e.id}
                className={`absolute w-[2px] rounded-full ${m.cls} ${e.t <= t ? "opacity-100" : "opacity-35"}`}
                style={{ left: `${Math.min(100, (e.t / d) * 100)}%`, top: `${m.lane * 34}%`, height: "30%" }}
                title={`${m.label} at ${clock(e.t)}`}
              />
            );
          })}
        </div>
        <div className="absolute inset-x-0 bottom-0 h-1 overflow-hidden rounded-b-xl" aria-hidden>
          <div className="h-full origin-left bg-[var(--accent)]" style={{ transform: `scaleX(${pct})` }} />
        </div>
        <div className="pointer-events-none absolute inset-y-0 left-0 right-0" style={{ transform: `translateX(${pct * 100}%)` }} aria-hidden>
          <span className="absolute -top-1 bottom-[-4px] left-0 w-0.5 -translate-x-1/2 bg-[var(--accent)]" />
          <span className="absolute -top-2 left-0 h-4 w-4 -translate-x-1/2 rounded-full border-2 border-white bg-[var(--accent)] shadow-[var(--shadow-glow)] transition-transform duration-150 group-active:scale-125" />
        </div>
      </div>
      <div className="mt-1.5 flex justify-between text-xs font-semibold tabular-nums text-[var(--muted)]" aria-hidden>
        <span>00:00</span>
        <span>{clock(d / 2)}</span>
        <span>{clock(d)}</span>
      </div>
    </div>
  );
}
