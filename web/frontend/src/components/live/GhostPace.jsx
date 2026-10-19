import { useMemo } from "react";
import { api } from "../../lib/api.js";
import { useApi } from "../../hooks/useApi.js";
import { clockText } from "../../lib/effort.js";
import { cumulativeDamage, damageAt, liveNow, paceChip, paceDelta } from "../../lib/pace.js";
import { formatDateTime, formatNumber } from "../../lib/format.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import { useLiveHits } from "./useLiveHits.js";

// Ghost pace for a live fight: cumulative damage against fight time, with the player's personal-best finished fight on the same
// level drawn as a faint line behind this one, and a chip saying how far ahead or behind it this fight is.
// The best fight comes from GET /api/players/:name/best-pace (cached 60 s on the server). This fight's line is built from the hit
// events already in the live stream (seeded once from the session so far); the stream itself is only listened to, never touched.
// No previous finished fight on this level: no ghost, no chip, just this fight's own line and a note. No animation.

const H = 150;
const PAD = { t: 10, r: 12, b: 24, l: 40 };
const BLOCK_MIN_H = 238; // every state is this tall, so the card doesn't jump when the best fight arrives (or doesn't)

const CHIP_TONE = {
  ahead: "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200",
  behind: "bg-[var(--warn-soft)] text-[var(--warn-ink)] ring-amber-200",
  level: "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent-ring)]",
};

function PaceChart({ live, ghost, now, ghostBest }) {
  const [ref, width] = useElementWidth();
  const maxT = Math.max(30, now, ghost?.length ? ghost[ghost.length - 1].t : 0);
  const maxD = Math.max(50, live[live.length - 1]?.damage || 0, ghost?.length ? ghost[ghost.length - 1].damage : 0) * 1.08;
  const iw = Math.max(10, width - PAD.l - PAD.r);
  const ih = H - PAD.t - PAD.b;
  const sx = (t) => PAD.l + (t / maxT) * iw;
  const sy = (d) => PAD.t + ih - (d / maxD) * ih;
  const line = (pts) => pts.map((p, i) => `${i ? "L" : "M"}${sx(p.t)},${sy(p.damage)}`).join("");
  // this fight's line runs on to "now", flat after the last hit
  const livePts = live.length && now > live[live.length - 1].t ? [...live, { t: now, damage: live[live.length - 1].damage }] : live;
  const end = livePts[livePts.length - 1];
  return (
    <div ref={ref} className="w-full min-w-0 select-none" style={{ height: H }}>
      {width > 0 && (
        <svg width={width} height={H} role="img" aria-label={`Damage dealt over the fight so far${ghostBest ? ", against your best fight on this level" : ""}`} className="block">
          {[0, 0.5, 1].map((f) => (
            <g key={f}>
              <line x1={PAD.l} x2={width - PAD.r} y1={sy(maxD * f)} y2={sy(maxD * f)} stroke={f === 0 ? "var(--border-strong)" : "var(--grid)"} />
              <text x={PAD.l - 6} y={sy(maxD * f) + 3.5} textAnchor="end" fontSize="10.5" fill="var(--muted)" className="tabular-nums">{formatNumber(maxD * f)}</text>
            </g>
          ))}
          <text x={PAD.l} y={H - 6} fontSize="10.5" fill="var(--muted)">00:00</text>
          <text x={width - PAD.r} y={H - 6} textAnchor="end" fontSize="10.5" fill="var(--muted)">{clockText(maxT)}</text>
          {ghost && ghost.length > 1 && <path d={line(ghost)} fill="none" stroke="var(--blue-300)" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" opacity="0.55" />}
          {livePts.length > 1 && <path d={line(livePts)} fill="none" stroke="var(--accent)" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />}
          {end && <circle cx={sx(end.t)} cy={sy(end.damage)} r="4.5" fill="var(--surface)" stroke="var(--accent)" strokeWidth="2.5" />}
        </svg>
      )}
    </div>
  );
}

export default function GhostPace({ id, session, state, flush = false }) {
  const level = ["Easy", "Medium", "Hard"].includes(session.level) ? session.level : null;
  const best = useApi(() => (level ? api.playerBestPace(session.playerName, level) : Promise.resolve(null)), [session.playerName, level]);
  const hits = useLiveHits(id);
  const live = useMemo(() => cumulativeDamage([...hits.values()]), [hits]);
  const data = best.data && best.data.playerName === session.playerName && best.data.level === level ? best.data : null;
  const hasGhost = Boolean(data?.best && !data.reason && data.points.length > 1);
  const now = liveNow(state?.t, live);
  const chip = hasGhost ? paceChip(paceDelta(live, data.points, now)) : null;
  const liveDamage = live[live.length - 1]?.damage || 0;

  let note = null;
  if (!level) note = "Waiting for the level to be reported.";
  else if (best.error && !data) note = "Your best fight on this level isn't available right now; this fight's own line is shown.";
  else if (!data && best.loading) note = "Looking for your best fight on this level…";
  else if (data && !data.best) note = "No earlier finished fight on this level yet: your pace line appears once you have one.";
  else if (data && data.reason === "no_events") note = "Your best fight on this level has no swing data, so there is no pace line to compare.";

  return (
    <section className={flush ? "" : "mt-4 border-t border-[var(--border)] pt-3"} style={{ minHeight: BLOCK_MIN_H }} aria-label="Pace against your best fight">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xs font-bold text-[var(--ink)]">Damage pace</h3>
        {chip && <span className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-extrabold ring-1 ring-inset ${CHIP_TONE[chip.tone]}`}>{chip.text}</span>}
      </div>
      <div className="mt-2">
        <PaceChart live={live} ghost={hasGhost ? data.points : null} now={now} ghostBest={hasGhost} />
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs font-semibold text-[var(--ink-secondary)]">
        <li className="inline-flex items-center gap-1.5">
          <span className="h-0.5 w-4 rounded-full bg-[var(--accent)]" aria-hidden /> This fight · {formatNumber(liveDamage)} dmg
        </li>
        {hasGhost && (
          <li className="inline-flex items-center gap-1.5">
            <span className="h-1 w-4 rounded-full bg-[var(--blue-300)] opacity-60" aria-hidden /> Your best · {formatNumber(data.best.totalDamage)} dmg, {formatDateTime(data.best.startedAt)}
          </li>
        )}
      </ul>
      {note && <p className="mt-2 text-xs font-medium text-[var(--muted)]">{note}</p>}
      <p className="sr-only" aria-live="off">
        {`This fight: ${formatNumber(liveDamage)} damage by ${clockText(now)}.`}
        {hasGhost ? ` Your best fight on this level had ${formatNumber(damageAt(data.points, now))} damage by then. ${chip?.text || ""}.` : ""}
      </p>
    </section>
  );
}
