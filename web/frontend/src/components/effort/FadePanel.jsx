import { useMemo, useState } from "react";
import { api } from "../../lib/api.js";
import { useApi } from "../../hooks/useApi.js";
import { clockText, FADE_PLAIN, FADE_TONE, formatFade, movingAverage, nearestByTime, smoothPath, speakFade } from "../../lib/effort.js";
import { SWING_SPEED_CAP } from "../../lib/game.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Button from "../ui/Button.jsx";

// Effort fade for one fight (GET /api/sessions/:id/fade): a smooth line of swing speed over the fight, a dashed line at the
// level's "full power" speed, and a tile that says what the fade was in plain words. The numbers are the backend's
// (backend/effort.js); nothing here recomputes them. No animation, so there is nothing to switch off for reduced motion / Calm mode.

export const CHART_H = 248;
// The skeleton and the loaded panel are the same height, so nothing below them moves when the data arrives.
export const PANEL_MIN_H = 392;
const PAD = { t: 28, r: 16, b: 30, l: 40 };

export function FadeTile({ fade, className = "" }) {
  if (!fade || fade.status !== "ok") {
    return (
      <div className={`rounded-xl border border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)] p-5 ${className}`}>
        <p className="t-kicker">Effort fade</p>
        <p className="mt-3 text-xl font-extrabold tracking-tight text-[var(--ink)]">Not enough swings</p>
        <p className="mt-2 text-sm font-medium leading-relaxed text-[var(--muted)]">
          {fade?.reason ? `${fade.reason}. ` : ""}Fade needs at least 6 landed swings, with 2 or more in both the first and last third of the fight, so one lucky swing can't stand in for a trend.
        </p>
      </div>
    );
  }
  const tone = FADE_TONE[fade.label];
  return (
    <div className={`rounded-xl border border-[var(--border)] bg-[var(--surface)] p-5 ${className}`}>
      <p className="t-kicker">Effort fade</p>
      <p className="mt-3 flex flex-wrap items-baseline gap-x-3 gap-y-2">
        <span className={`text-3xl font-extrabold tracking-tight tabular-nums ${tone.text}`} aria-label={`Fade ${speakFade(fade.fadePct)}`}>
          Fade {formatFade(fade.fadePct)}
        </span>
        <span className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-extrabold ring-1 ring-inset ${tone.chip}`}>{fade.labelText}</span>
      </p>
      <p className="mt-3 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{FADE_PLAIN[fade.label]}</p>
      <dl className="mt-4 grid grid-cols-2 gap-3 border-t border-[var(--border)] pt-3 text-xs">
        <div>
          <dt className="font-semibold text-[var(--muted)]">First third</dt>
          <dd className="mt-0.5 text-base font-extrabold tabular-nums text-[var(--ink)]">{fade.firstAvg.toFixed(1)} m/s</dd>
          <dd className="text-[var(--muted)]">{fade.firstSwings} swings</dd>
        </div>
        <div>
          <dt className="font-semibold text-[var(--muted)]">Last third</dt>
          <dd className="mt-0.5 text-base font-extrabold tabular-nums text-[var(--ink)]">{fade.lastAvg.toFixed(1)} m/s</dd>
          <dd className="text-[var(--muted)]">{fade.lastSwings} swings</dd>
        </div>
      </dl>
    </div>
  );
}

function FadeChart({ data }) {
  const [ref, width] = useElementWidth();
  const [cursor, setCursor] = useState(null); // swing index
  const { points, fade, fullPowerSpeed } = data;
  const length = Math.max(fade.lengthSec || 0, data.durationSec || 0, ...points.map((p) => p.t), 1);
  const iw = Math.max(10, width - PAD.l - PAD.r);
  const ih = CHART_H - PAD.t - PAD.b;
  const sx = (t) => PAD.l + (t / length) * iw;
  const sy = (v) => PAD.t + ih - (Math.min(v, SWING_SPEED_CAP) / SWING_SPEED_CAP) * ih;
  const smooth = useMemo(() => movingAverage(points.map((p) => ({ x: p.t, y: p.speed })), 2), [points]);
  const path = smooth.length > 1 ? smoothPath(smooth.map((p) => ({ x: sx(p.x), y: sy(p.y) }))) : "";
  const ticks = [0, 2, 4, 6, 8];
  const xTicks = Array.from({ length: 5 }, (_, i) => (length * i) / 4);
  const ok = fade.status === "ok";
  const at = cursor === null ? null : points[cursor];

  const move = (e) => {
    const x = e.clientX - e.currentTarget.getBoundingClientRect().left;
    const near = nearestByTime(points, ((x - PAD.l) / iw) * length);
    setCursor(near ? points.indexOf(near) : null);
  };
  const key = (e) => {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key) || !points.length) return;
    e.preventDefault();
    setCursor((c) => (e.key === "Home" ? 0 : e.key === "End" ? points.length - 1 : Math.max(0, Math.min(points.length - 1, (c ?? points.length) + (e.key === "ArrowRight" ? 1 : -1)))));
  };

  return (
    <div ref={ref} className="relative w-full min-w-0 select-none" style={{ height: CHART_H }}>
      {width > 0 && (
        <svg
          width={width}
          height={CHART_H}
          role="img"
          tabIndex={0}
          aria-label={`Swing speed over the fight, ${points.length} swings. ${ok ? `Average ${fade.firstAvg.toFixed(1)} metres per second in the first third and ${fade.lastAvg.toFixed(1)} in the last third.` : "Not enough swings for a fade figure."} Full power starts at ${fullPowerSpeed} metres per second. Use the arrow keys to read each swing; a data table follows.`}
          className="block rounded-lg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent)]"
          onMouseMove={move}
          onMouseLeave={() => setCursor(null)}
          onKeyDown={key}
          onBlur={() => setCursor(null)}
        >
          {/* the two thirds the fade compares */}
          {ok && (
            <>
              <rect x={sx(0)} y={PAD.t} width={sx(fade.firstEndSec) - sx(0)} height={ih} fill="var(--accent-soft)" />
              <rect x={sx(fade.lastStartSec)} y={PAD.t} width={sx(length) - sx(fade.lastStartSec)} height={ih} fill="var(--accent-soft)" />
              <text x={sx(0) + 6} y={PAD.t - 10} fontSize="11" fontWeight="700" fill="var(--ink-secondary)">First third · {fade.firstAvg.toFixed(1)} m/s</text>
              <text x={sx(length) - 6} y={PAD.t - 10} textAnchor="end" fontSize="11" fontWeight="700" fill="var(--ink-secondary)">Last third · {fade.lastAvg.toFixed(1)} m/s</text>
              <line x1={sx(0)} x2={sx(fade.firstEndSec)} y1={sy(fade.firstAvg)} y2={sy(fade.firstAvg)} stroke="var(--accent-strong)" strokeWidth="1.5" opacity="0.55" />
              <line x1={sx(fade.lastStartSec)} x2={sx(length)} y1={sy(fade.lastAvg)} y2={sy(fade.lastAvg)} stroke="var(--accent-strong)" strokeWidth="1.5" opacity="0.55" />
            </>
          )}
          {ticks.map((v) => (
            <g key={v}>
              <line x1={PAD.l} x2={width - PAD.r} y1={sy(v)} y2={sy(v)} stroke={v === 0 ? "var(--border-strong)" : "var(--grid)"} />
              <text x={PAD.l - 8} y={sy(v) + 3.5} textAnchor="end" fontSize="11" fill="var(--muted)" className="tabular-nums">{v}</text>
            </g>
          ))}
          {xTicks.map((t, i) => (
            <text key={i} x={sx(t)} y={CHART_H - 10} textAnchor={i === 0 ? "start" : i === xTicks.length - 1 ? "end" : "middle"} fontSize="11" fill="var(--muted)">{clockText(t)}</text>
          ))}
          <text x={PAD.l - 8} y={12} textAnchor="start" fontSize="11" fill="var(--muted)" className="sr-only">m/s</text>

          {/* full-power reference: the speed from which a hit counts as full power on this level */}
          {Number.isFinite(fullPowerSpeed) && (
            <g>
              <line x1={PAD.l} x2={width - PAD.r} y1={sy(fullPowerSpeed)} y2={sy(fullPowerSpeed)} stroke="var(--accent-strong)" strokeWidth="1.5" strokeDasharray="6 5" />
              <text x={width - PAD.r - 4} y={sy(fullPowerSpeed) - 6} textAnchor="end" fontSize="11" fontWeight="700" fill="var(--accent-strong)">Full power {fullPowerSpeed} m/s</text>
            </g>
          )}

          {points.map((p, i) => (
            <circle key={i} cx={sx(p.t)} cy={sy(p.speed)} r={cursor === i ? 5 : 2.6} fill={cursor === i ? "var(--accent)" : "var(--surface)"} stroke="var(--accent)" strokeWidth={cursor === i ? 2.5 : 1.25} opacity={cursor === i ? 1 : 0.55} />
          ))}
          {path && <path d={path} fill="none" stroke="var(--accent)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />}
          {at && <line x1={sx(at.t)} x2={sx(at.t)} y1={PAD.t} y2={PAD.t + ih} stroke="var(--border-strong)" strokeDasharray="3 3" />}
        </svg>
      )}
      <p className="pointer-events-none absolute right-2 top-0 text-xs font-bold tabular-nums text-[var(--ink)]" aria-live="polite">
        {at ? `${clockText(at.t)} · ${at.speed.toFixed(1)} m/s${at.tier ? ` · ${at.tier}` : ""}` : ""}
      </p>
    </div>
  );
}

function DataTable({ points }) {
  return (
    <details className="mt-3 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2 text-xs">
      <summary className="cursor-pointer font-semibold text-[var(--ink-secondary)]">Show the swings as a table</summary>
      <div className="scrollbar-thin mt-2 max-h-56 overflow-auto">
        <table className="w-full text-left tabular-nums">
          <caption className="sr-only">Every landed swing: fight time, speed in metres per second, and strength tier</caption>
          <thead>
            <tr className="text-[var(--muted)]">
              <th scope="col" className="py-1 pr-4 font-semibold">Time</th>
              <th scope="col" className="py-1 pr-4 font-semibold">Speed (m/s)</th>
              <th scope="col" className="py-1 font-semibold">Tier</th>
            </tr>
          </thead>
          <tbody>
            {points.map((p, i) => (
              <tr key={i} className="border-t border-[var(--border)]">
                <td className="py-1 pr-4">{clockText(p.t)}</td>
                <td className="py-1 pr-4">{p.speed.toFixed(1)}</td>
                <td className="py-1">{p.tier || "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}

export function FadeSkeleton() {
  return (
    <div className="grid content-start gap-5 p-5 lg:grid-cols-[minmax(0,17rem)_minmax(0,1fr)]" style={{ minHeight: PANEL_MIN_H }} aria-hidden>
      <Skeleton className="h-52 w-full rounded-xl" />
      <Skeleton className="w-full rounded-xl" style={{ height: CHART_H }} />
    </div>
  );
}

export default function FadePanel({ sessionId }) {
  const { data, error, reload } = useApi(() => api.sessionFade(sessionId), [sessionId]);
  if (error && !data) {
    return (
      <EmptyState
        icon="clock"
        title="Effort fade is unavailable right now"
        description="It loads from the server as soon as it answers."
        action={<Button onClick={reload}>Try again</Button>}
      />
    );
  }
  if (!data) return <FadeSkeleton />;
  if (!data.points.length) {
    return <EmptyState icon="bolt" title="No swing speeds recorded" description="This fight has no landed swings with a measured speed (it may predate the live-telemetry build), so there is nothing to compare." />;
  }
  return (
    <div className="p-5" style={{ minHeight: PANEL_MIN_H }}>
      <div className="grid gap-5 lg:grid-cols-[minmax(0,17rem)_minmax(0,1fr)]">
        <FadeTile fade={data.fade} />
        <div className="min-w-0">
          <FadeChart data={data} />
          <p className="mt-2 text-xs font-medium text-[var(--muted)]">
            Dots are single swings, the line is their running average, the dashed line is where full power starts on this level. Speeds above the game's {data.speedCap} m/s cap are shown at the cap.
          </p>
        </div>
      </div>
      <p className="sr-only">{data.fade.status === "ok" ? `Fade ${speakFade(data.fade.fadePct)}. ${data.fade.labelText}. ${FADE_PLAIN[data.fade.label]}` : data.fade.reason}</p>
      <DataTable points={data.points} />
    </div>
  );
}
