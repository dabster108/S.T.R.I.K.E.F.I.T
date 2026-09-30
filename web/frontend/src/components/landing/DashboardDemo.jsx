import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { LEVELS, TIER_LABEL, levelName, tierFor } from "../../lib/game.js";
import { useOnScreen, useReducedMotion } from "../../hooks/motion.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import { Card, CardHeader } from "../ui/Card.jsx";
import StatTile from "../ui/StatTile.jsx";
import { SegmentedControl } from "../ui/Controls.jsx";
import Icon from "../ui/Icon.jsx";

// ─── Demo data: invented for the preview, never real players ─────────────────
// `speeds` are the last ten counted hits in m/s; tiers come from the game's real
// thresholds (lib/game.js), so a 5.0 m/s swing on Level 1 really is full power.
const DEMO_PLAYERS = {
  p1: { name: "Player 1", level: "Easy", score: 1240, hits: 23, speeds: [3.1, 4.8, 5.6, 2.4, 6.1, 4.2, 5.3, 3.7, 6.4, 5.0] },
  p2: { name: "Player 2", level: "Medium", score: 780, hits: 14, speeds: [2.2, 3.5, 4.9, 1.8, 3.0, 4.1, 2.7, 5.2, 3.3, 2.9] },
};
const DEMO_TABS = [
  { id: "all", label: "Both fights" },
  { id: "p1", label: "Player 1" },
  { id: "p2", label: "Player 2" },
];

// ─── The animation script: edit the loop here ────────────────────────────────
// Steps run top to bottom, then start over. Each one does its action, then waits
// `ms` (how long the move takes) + `hold` (pause after it) before the next.
//   rest  → put the cursor at `at` instantly and show `view` (only used to open the loop)
//   move  → glide the cursor to `to`
//   click → press + ripple where the cursor is, then show `view`
//   hover → show the chart tooltip for `point` (0 = oldest of the ten hits)
// `to` / `at` is a tab id (all, p1, p2), a data-demo marker in the mock UI
// ("point-7" = chart dot 7), or { x, y } as fractions of the mock screen.
const PARK = { x: 0.9, y: 0.93 };
export const DEMO_SEQUENCE = [
  { action: "rest", at: PARK, view: "all", hold: 1500 }, // 1. Live Arena, both fights
  { action: "move", to: "p1", ms: 1150, hold: 120 }, //     2. glide to the Player 1 tab
  { action: "click", view: "p1", hold: 1400 }, //           3. click: Player 1's numbers and chart
  { action: "move", to: "point-8", ms: 950, hold: 80 }, //  4. glide onto the chart…
  { action: "hover", point: 8, hold: 1900 }, //                …and hover a hit
  { action: "move", to: "p2", ms: 1100, hold: 120 }, //     5. glide to the Player 2 tab
  { action: "click", view: "p2", hold: 1700 }, //           …click: Player 2's data swaps in
  { action: "move", to: "all", ms: 950, hold: 120 }, //     6. back to both fights
  { action: "click", view: "all", hold: 700 },
  { action: "move", to: PARK, ms: 1200, hold: 300 }, //        park, and the loop starts again
];
// Reduced motion shows this one frame instead of the loop.
const STATIC_FRAME = { view: "p1", tip: 8 };
// ─────────────────────────────────────────────────────────────────────────────

const STAGE_W = 600; // mock screen width in px before scaling to fit the hero column
const MAX_V = 8;
const COLOR = { p1: "var(--level-1)", p2: "var(--level-2)" };

const avg = (a) => a.reduce((s, v) => s + v, 0) / a.length;

function statsFor(view) {
  const players = view === "all" ? Object.values(DEMO_PLAYERS) : [DEMO_PLAYERS[view]];
  return {
    score: players.reduce((s, p) => s + p.score, 0),
    hits: players.reduce((s, p) => s + p.hits, 0),
    speed: avg(players.flatMap((p) => p.speeds)),
    hint: view === "all" ? "both fights" : `${levelName(players[0].level)} · ${LEVELS[players[0].level].weapons.toLowerCase()}`,
  };
}

// ─── mock UI ─────────────────────────────────────────────────────────────────

// Swing speed of the last ten hits, on the same 0–8 m/s scale the landing page uses.
const CW = 520;
const CH = 170;
const PAD = { l: 26, r: 12, t: 12, b: 12 };
const cx = (i, n) => PAD.l + (i / (n - 1)) * (CW - PAD.l - PAD.r);
const cy = (v) => PAD.t + (1 - v / MAX_V) * (CH - PAD.t - PAD.b);

function SwingChart({ view, tip }) {
  const ids = view === "all" ? ["p1", "p2"] : [view];
  const single = ids.length === 1 ? DEMO_PLAYERS[ids[0]] : null;
  const full = single ? LEVELS[single.level].swing.tiers.full : null;
  const tipSpeed = single && tip !== null ? single.speeds[tip] : null;
  return (
    <div className="relative">
      <svg viewBox={`0 0 ${CW} ${CH}`} className="block h-auto w-full overflow-visible">
        {[0, 4, 8].map((v) => (
          <g key={v}>
            <line x1={PAD.l} x2={CW - PAD.r} y1={cy(v)} y2={cy(v)} stroke="var(--grid)" strokeWidth="1" />
            <text x={PAD.l - 8} y={cy(v) + 3.5} textAnchor="end" className="fill-[var(--muted)] text-[11px] font-semibold tabular-nums">
              {v}
            </text>
          </g>
        ))}
        {full && (
          <line key={`t${view}`} x1={PAD.l} x2={CW - PAD.r} y1={cy(full)} y2={cy(full)} stroke="var(--tier-full)" strokeWidth="1" strokeDasharray="4 4" opacity="0.55" className="fade-in" />
        )}
        <g key={view}>
          {ids.map((id) => {
            const s = DEMO_PLAYERS[id].speeds;
            const d = s.map((v, i) => `${i ? "L" : "M"}${cx(i, s.length).toFixed(1)},${cy(v).toFixed(1)}`).join(" ");
            return (
              <g key={id}>
                <path d={d} pathLength="1" fill="none" stroke={COLOR[id]} strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" className="chart-line" />
                {s.map((v, i) => {
                  const on = single && tip === i;
                  return (
                    <circle
                      key={i}
                      data-demo={single ? `point-${i}` : undefined}
                      cx={cx(i, s.length)}
                      cy={cy(v)}
                      r={on ? 6.5 : 4}
                      fill={on ? COLOR[id] : "var(--surface)"}
                      stroke={COLOR[id]}
                      strokeWidth="2"
                      className="dot-in demo-dot"
                    />
                  );
                })}
              </g>
            );
          })}
        </g>
      </svg>
      {tipSpeed !== null && (
        <div
          className="pop-in pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full"
          style={{ left: `${(cx(tip, single.speeds.length) / CW) * 100}%`, top: `calc(${(cy(tipSpeed) / CH) * 100}% - 12px)` }}
        >
          <div className="whitespace-nowrap rounded-lg bg-[var(--ink)] px-2.5 py-1.5 text-xs font-semibold text-white shadow-lg">
            <span className="block text-[0.7rem] font-bold uppercase tracking-wider text-[var(--blue-300)]">
              Hit {single.hits - (single.speeds.length - 1 - tip)} of {single.hits}
            </span>
            <span className="tabular-nums">{tipSpeed.toFixed(1)} m/s</span> · {TIER_LABEL[tierFor(tipSpeed, single.level)]}
          </div>
        </div>
      )}
    </div>
  );
}

function MockDashboard({ view, tip }) {
  const s = statsFor(view);
  const ids = view === "all" ? ["p1", "p2"] : [view];
  return (
    <div className="bg-[var(--bg)] px-6 pb-6 pt-5">
      <div className="flex items-end justify-between gap-4">
        <div>
          <p className="t-eyebrow">Realtime</p>
          <p className="t-h1 mt-1 text-[var(--ink)]">Live Arena</p>
        </div>
        <span className="mb-1 inline-flex items-center gap-1.5 rounded-full bg-[var(--good-soft)] px-2 py-0.5 text-xs font-extrabold uppercase tracking-wide text-[var(--good-ink)] ring-1 ring-inset ring-emerald-200">
          <span className="h-2 w-2 rounded-full bg-[var(--good)]" />2 fights live
        </span>
      </div>

      <div className="mt-4 flex items-center justify-between gap-3">
        <SegmentedControl label="Fight" options={DEMO_TABS} value={view} onChange={() => {}} />
        <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--surface)] px-2.5 py-1 text-xs font-bold uppercase tracking-wide text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">
          <Icon name="info" className="h-3.5 w-3.5" /> Demo data
        </span>
      </div>

      <div className="mt-4 grid grid-cols-3 gap-3">
        <StatTile label="Score" count={s.score} icon="trophy" tone="blue" hint={s.hint} />
        <StatTile label="Hits landed" count={s.hits} icon="target" tone="green" hint="counted swings" />
        <StatTile label="Avg swing" count={s.speed} format={(v) => v.toFixed(1)} icon="gauge" tone="indigo" hint="m/s, last 10 hits" />
      </div>

      <Card className="mt-4 overflow-hidden">
        <CardHeader
          title="Swing speed"
          subtitle="Last 10 hits, m/s"
          actions={
            <span className="flex items-center gap-3 text-xs font-semibold text-[var(--ink-secondary)]">
              {ids.map((id) => (
                <span key={id} className="flex items-center gap-1.5">
                  <span className="h-2 w-2 rounded-full" style={{ background: COLOR[id] }} />
                  {DEMO_PLAYERS[id].name}
                </span>
              ))}
              {view !== "all" && (
                <span className="flex items-center gap-1.5">
                  <span className="w-3.5 border-t-2 border-dashed border-[var(--tier-full)] opacity-60" />
                  Full power
                </span>
              )}
            </span>
          }
        />
        <div className="px-4 pb-3 pt-4">
          <SwingChart view={view} tip={tip} />
        </div>
      </Card>
    </div>
  );
}

// The pointer's tip sits at (0, 0) of its box. X and Y ease on different curves,
// so the path bends slightly the way a hand would move a mouse.
function Cursor({ pos, clicks, trail = false }) {
  return (
    <div className={`demo-cursor-x ${trail ? "demo-trail" : ""}`} style={{ transform: `translateX(${pos.x}px)`, transitionDuration: `${pos.ms}ms` }}>
      <div className="demo-cursor-y" style={{ transform: `translateY(${pos.y}px)`, transitionDuration: `${pos.ms}ms` }}>
        {!trail && clicks > 0 && <span key={`r${clicks}`} className="demo-ripple" />}
        <svg key={`c${clicks}`} width="26" height="30" viewBox="0 0 26 30" className={`absolute -left-[3px] -top-[2px] ${!trail && clicks > 0 ? "demo-press" : ""}`}>
          <path
            d="M3 2.5v21.2l5.6-5.3 3.7 8.2 3.6-1.6-3.7-8.1 7.7-.3z"
            fill={trail ? "var(--accent-ring)" : "var(--accent)"}
            stroke="#fff"
            strokeWidth="2"
            strokeLinejoin="round"
          />
        </svg>
      </div>
    </div>
  );
}

// The hero's dashboard preview: a scaled mock of the Live Arena driven by a fake cursor.
export default function DashboardDemo() {
  const reduced = useReducedMotion();
  const [boxRef, boxWidth] = useElementWidth(0);
  const [viewRef, inView] = useOnScreen();
  const stage = useRef(null);
  const frame = useRef(null);
  const [stageH, setStageH] = useState(0);
  const [view, setView] = useState("all");
  const [tip, setTip] = useState(null);
  const [cursor, setCursor] = useState(null);
  const [clicks, setClicks] = useState(0);
  const step = useRef(0);

  const scale = boxWidth ? Math.min(1, boxWidth / STAGE_W) : 0;

  // The frame is a picture of the dashboard: no pointer, no focus, no screen-reader noise.
  useEffect(() => {
    frame.current?.setAttribute("inert", "");
  }, []);

  useLayoutEffect(() => {
    const el = stage.current;
    if (!el) return undefined;
    const ro = new ResizeObserver(() => setStageH(el.offsetHeight));
    ro.observe(el);
    setStageH(el.offsetHeight);
    return () => ro.disconnect();
  }, []);

  // The loop. Pauses off screen (and resumes on the same step) to save CPU.
  useEffect(() => {
    if (reduced || !inView || !scale) return undefined;
    const locate = (to) => {
      const el = stage.current;
      if (!el) return null;
      if (typeof to === "object") return { x: to.x * el.offsetWidth, y: to.y * el.offsetHeight };
      const target = el.querySelector(`[data-demo="${to}"]`) || el.querySelector(`[data-id="${to}"]`);
      if (!target) return null;
      const s = el.getBoundingClientRect();
      const r = target.getBoundingClientRect();
      const k = s.width / el.offsetWidth; // undo the fit-to-width scale
      return { x: (r.left - s.left + r.width * 0.5) / k, y: (r.top - s.top + r.height * 0.55) / k };
    };
    const perform = (st) => {
      if (st.action === "rest") {
        setView(st.view);
        setTip(null);
        const p = locate(st.at);
        if (p) setCursor({ ...p, ms: 0 });
      } else if (st.action === "move") {
        setTip(null);
        const p = locate(st.to);
        if (p) setCursor({ ...p, ms: st.ms });
      } else if (st.action === "click") {
        setClicks((c) => c + 1);
        setView(st.view);
      } else if (st.action === "hover") {
        setTip(st.point);
      }
    };
    let timer;
    const run = () => {
      const st = DEMO_SEQUENCE[step.current];
      perform(st);
      timer = setTimeout(() => {
        step.current = (step.current + 1) % DEMO_SEQUENCE.length;
        run();
      }, (st.ms || 0) + (st.hold || 0));
    };
    run();
    return () => clearTimeout(timer);
  }, [reduced, inView, scale]);

  const shownView = reduced ? STATIC_FRAME.view : view;
  const shownTip = reduced ? STATIC_FRAME.tip : tip;

  return (
    <figure ref={viewRef} aria-labelledby="dashboard-demo-caption" className="min-w-0">
      {/* grid-cols-1 = minmax(0, 1fr): the address bar's nowrap text must not set a minimum width for the hero column */}
      <div
        ref={frame}
        aria-hidden
        className="pointer-events-none grid select-none grid-cols-1 overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[0_30px_60px_-25px_rgb(30_58_138/0.35),0_12px_24px_-12px_rgb(16_24_40/0.12)]"
      >
        {/* window chrome */}
        <div className="flex items-center gap-3 border-b border-[var(--border)] bg-[var(--surface-muted)] px-4 py-2.5">
          <span className="flex shrink-0 gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full bg-[var(--border-strong)]" />
            <span className="h-2.5 w-2.5 rounded-full bg-[var(--border-strong)]" />
            <span className="h-2.5 w-2.5 rounded-full bg-[var(--border-strong)]" />
          </span>
          <span className="mx-auto flex min-w-0 max-w-xs flex-1 items-center justify-center gap-1.5 rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-1 text-xs font-semibold text-[var(--muted)]">
            <Icon name="live" className="h-3 w-3 shrink-0 text-[var(--accent)]" />
            <span className="truncate">strikefit / dashboard / live</span>
          </span>
          <span className="w-[2.625rem] shrink-0" />
        </div>

        {/* the stage is absolute so its unscaled 600px never widens the column; the box takes its scaled height */}
        <div ref={boxRef} className="relative min-w-0 overflow-hidden bg-[var(--bg)]" style={{ height: stageH * scale }}>
          <div ref={stage} className="absolute left-0 top-0 origin-top-left" style={{ width: STAGE_W, transform: `scale(${scale || 1})`, visibility: scale ? "visible" : "hidden" }}>
            <MockDashboard view={shownView} tip={shownTip} />
            {!reduced && cursor && (
              <div className="pointer-events-none absolute inset-0 z-20">
                <Cursor pos={cursor} clicks={clicks} trail />
                <Cursor pos={cursor} clicks={clicks} />
              </div>
            )}
          </div>
        </div>
      </div>

      <figcaption id="dashboard-demo-caption" className="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-1">
        <span className="t-kicker text-[var(--ink-secondary)]">Live dashboard preview</span>
        <span className="text-xs font-semibold text-[var(--muted)]">
          Scripted demo data, not real players ·{" "}
          <Link to="/dashboard/guide" className="press rounded text-[var(--accent)] hover:text-[var(--accent-strong)] hover:underline">
            User guide
          </Link>
        </span>
      </figcaption>
    </figure>
  );
}
