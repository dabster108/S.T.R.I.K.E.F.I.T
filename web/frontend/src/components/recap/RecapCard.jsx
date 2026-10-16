import { forwardRef, useEffect, useMemo, useState } from "react";
import { api } from "../../lib/api.js";
import { levelName } from "../../lib/game.js";
import { formatKcal } from "../../lib/fitness.js";
import { formatDateTime, formatNumber } from "../../lib/format.js";
import { useApi } from "../../hooks/useApi.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import ShareChallenge from "../engage/ShareChallenge.jsx";

// A shareable 1080 x 1350 recap of one session. Everything on it comes from the stored session,
// its events, the player's other sessions and their unlocked badges; a number that doesn't exist is
// left off the card, never filled in. Calories always carry the word "estimated".

export const CARD_W = 1080;
export const CARD_H = 1350;

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const P = (e) => (e.payload && typeof e.payload === "object" && !Array.isArray(e.payload) ? e.payload : {});
const OUTCOME = { victory: "Victory", defeat: "Defeat" };

// Peak swing speed and best combo from the stored events; null when the session has none.
export function sessionPeaks(events) {
  let speed = null;
  let combo = null;
  for (const e of events || []) {
    const p = P(e);
    if (e.type === "hit" && isNum(p.swingSpeed)) speed = Math.max(speed ?? 0, p.swingSpeed);
    if ((e.type === "hit" || e.type === "hp") && isNum(p.combo)) combo = Math.max(combo ?? 0, p.combo);
  }
  return { peakSpeed: speed, bestCombo: combo };
}

// This session against the mean of the player's other finished sessions. Needs 2+ sessions with data.
export function versusAverage(series, sessionId) {
  const done = (series || []).filter((s) => s.status !== "live");
  const me = done.find((s) => s.id === sessionId);
  const others = done.filter((s) => s.id !== sessionId);
  if (!me || !others.length) return null;
  const row = (label, pick, fmt) => {
    const mine = pick(me);
    const rest = others.map(pick).filter(isNum);
    if (!isNum(mine) || !rest.length) return null;
    const avg = rest.reduce((a, v) => a + v, 0) / rest.length;
    return { label, mine: fmt(mine), avg: fmt(avg), diff: mine - avg, count: rest.length };
  };
  const rows = [
    row("Score", (s) => s.score, (v) => formatNumber(v)),
    row("Estimated energy", (s) => s.estimatedCalories, (v) => formatKcal(v)),
  ].filter(Boolean);
  return rows.length ? rows : null;
}

// "Daily goal: 42 / 100 kcal (estimated), 42%" for the local day (Asia/Kathmandu, the API's default) the session started on.
const GOAL_UNIT = { kcal: "kcal (estimated)", fullSwings: "full-power swings", activeMinutes: "active minutes", xp: "XP" };
export function goalRecapLine(days, startedAt) {
  if (!days?.length || !startedAt) return null;
  const day = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Kathmandu", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(startedAt));
  const d = days.find((x) => x.date === day);
  if (!d || !d.goal || !d.progress) return null;
  const v = d.goal.metric === "kcal" ? Math.round(d.progress.value * 10) / 10 : Math.round(d.progress.value);
  return { met: d.met, text: d.met ? `Daily goal reached: ${v} / ${d.goal.target} ${GOAL_UNIT[d.goal.metric]}` : `Daily goal: ${v} / ${d.goal.target} ${GOAL_UNIT[d.goal.metric]} (${d.progress.percent}%)` };
}

const Tile = ({ label, value, sub }) => (
  <div style={{ background: "var(--surface-muted)", border: "2px solid var(--border)", borderRadius: 28, padding: "18px 30px" }}>
    <div style={{ fontSize: 22, fontWeight: 600, color: "var(--muted)" }}>{label}</div>
    <div style={{ fontSize: 48, fontWeight: 800, letterSpacing: "-0.03em", color: "var(--ink)", lineHeight: 1.1, marginTop: 2, fontVariantNumeric: "tabular-nums" }}>{value}</div>
    {sub && <div style={{ fontSize: 20, fontWeight: 600, color: "var(--muted)", marginTop: 4 }}>{sub}</div>}
  </div>
);

// Fixed-size card. No transforms on this element: the preview scales a parent, so exporting this node is pixel-exact.
export const RecapCard = forwardRef(function RecapCard({ session, kcal, peakSpeed, bestCombo, badges = [], versus, goalLine = null }, ref) {
  const tiles = [
    isNum(peakSpeed) && { label: "Peak swing speed", value: peakSpeed.toFixed(1), sub: "metres per second" },
    isNum(bestCombo) && { label: "Best combo", value: formatNumber(bestCombo), sub: "hits in a row" },
    session.xp > 0 && { label: "XP gained", value: `+${formatNumber(session.xp)}` },
    { label: "Score", value: formatNumber(session.score), sub: `${formatNumber(session.hitCount)} hits` },
  ].filter(Boolean);
  const won = session.outcome === "victory";
  return (
    <div
      ref={ref}
      style={{
        width: CARD_W,
        height: CARD_H,
        boxSizing: "border-box",
        padding: "56px 64px",
        background: "var(--surface)",
        color: "var(--ink)",
        fontFamily: "var(--font-ui)",
        display: "flex",
        flexDirection: "column",
        position: "relative",
        overflow: "hidden",
      }}
    >
      <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 18, background: "var(--accent)" }} />
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: 12 }}>
        <div style={{ fontSize: 40, fontWeight: 800, letterSpacing: "0.12em", color: "var(--accent-strong)" }}>STRIKEFIT</div>
        <div style={{ fontSize: 28, fontWeight: 600, color: "var(--muted)" }}>{formatDateTime(session.startedAt)}</div>
      </div>

      <div style={{ marginTop: 40 }}>
        <div style={{ fontSize: 68, fontWeight: 800, letterSpacing: "-0.035em", lineHeight: 1.05, overflowWrap: "anywhere" }}>{session.playerName}</div>
        <div style={{ display: "flex", gap: 16, marginTop: 20, alignItems: "center" }}>
          <span style={{ fontSize: 30, fontWeight: 700, padding: "8px 24px", borderRadius: 999, background: "var(--accent-soft)", color: "var(--accent-strong)", border: "2px solid var(--accent-ring)" }}>
            {levelName(session.level)}
          </span>
          <span
            style={{
              fontSize: 30,
              fontWeight: 700,
              padding: "8px 24px",
              borderRadius: 999,
              background: won ? "var(--good-soft)" : "var(--bad-soft)",
              color: won ? "var(--good-ink)" : "var(--bad)",
              border: `2px solid ${won ? "#a7f3d0" : "#fecaca"}`,
            }}
          >
            {OUTCOME[session.outcome] || session.outcome}
          </span>
        </div>
      </div>

      <div style={{ marginTop: 36, borderRadius: 32, padding: "26px 40px", background: "linear-gradient(135deg, var(--accent) 0%, var(--blue-800) 100%)", color: "#fff" }}>
        <div style={{ fontSize: 28, fontWeight: 600, opacity: 0.9 }}>Estimated energy use</div>
        <div style={{ display: "flex", alignItems: "baseline", gap: 18, marginTop: 6 }}>
          <span style={{ fontSize: 104, fontWeight: 800, letterSpacing: "-0.04em", lineHeight: 1, fontVariantNumeric: "tabular-nums" }}>{isNum(kcal) ? (kcal < 10 ? kcal.toFixed(1) : Math.round(kcal)) : "—"}</span>
          <span style={{ fontSize: 44, fontWeight: 700, opacity: 0.9 }}>kcal (estimated)</span>
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 18, marginTop: 26 }}>
        {tiles.map((t) => (
          <Tile key={t.label} {...t} />
        ))}
      </div>

      {badges.length > 0 && (
        <div style={{ marginTop: 28 }}>
          <div style={{ fontSize: 24, fontWeight: 700, color: "var(--muted)", marginBottom: 12 }}>Badges unlocked</div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 14 }}>
            {badges.slice(0, 4).map((b) => (
              <span key={b.id} style={{ fontSize: 28, fontWeight: 700, padding: "8px 24px", borderRadius: 999, background: "#fffbeb", color: "#92400e", border: "2px solid #fde68a" }}>
                {b.name}
              </span>
            ))}
            {badges.length > 4 && <span style={{ fontSize: 28, fontWeight: 700, padding: "8px 24px", color: "var(--muted)" }}>+{badges.length - 4} more</span>}
          </div>
        </div>
      )}

      {versus && (
        <div style={{ marginTop: 28, fontSize: 26, fontWeight: 600, color: "var(--ink-secondary)", lineHeight: 1.35 }}>
          <span style={{ color: "var(--muted)" }}>vs your average of {versus[0].count} other session{versus[0].count === 1 ? "" : "s"}: </span>
          {versus.map((r, i) => (
            <span key={r.label}>
              {i > 0 && " · "}
              {r.label} {r.mine} <span style={{ color: r.diff >= 0 ? "var(--good-ink)" : "var(--bad)", fontWeight: 700 }}>({r.diff >= 0 ? "+" : "−"}{r.label === "Score" ? formatNumber(Math.abs(r.diff)) : Math.abs(r.diff).toFixed(1)})</span>
            </span>
          ))}
        </div>
      )}

      {goalLine && (
        <div style={{ marginTop: 24, fontSize: 28, fontWeight: 700, color: goalLine.met ? "var(--good-ink)" : "var(--ink-secondary)" }}>
          {goalLine.text}
        </div>
      )}

      <div style={{ marginTop: "auto", fontSize: 22, fontWeight: 500, color: "var(--muted)", lineHeight: 1.4, paddingTop: 20 }}>
        Calories are estimated from fight length and swing strength with an assumed body mass, not measured. · strikefit.vercel.app
      </div>
    </div>
  );
});

// Waits for web fonts, renders the node to a PNG blob.
export async function renderPng(node) {
  if (document.fonts?.ready) await document.fonts.ready;
  const { toBlob } = await import("html-to-image");
  const opts = { width: CARD_W, height: CARD_H, pixelRatio: 1, cacheBust: true };
  // The first pass can miss images/fonts that were still being fetched; the second is the real one.
  await toBlob(node, opts);
  return toBlob(node, opts);
}

// Preview of the card with Export / Share. data: GET /api/sessions/:id
export default function RecapPanel({ data }) {
  const s = data.session;
  const [ref, width] = useElementWidth(CARD_W);
  const [node, setNode] = useState(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState(null);
  const player = useApi(() => api.player(s.playerName), [s.playerName]);
  const ach = useApi(() => api.playerAchievements(s.playerName), [s.playerName]);
  const goals = useApi(() => api.playerGoals(s.playerName, 30), [s.playerName]);

  const { peakSpeed, bestCombo } = useMemo(() => sessionPeaks(data.events), [data.events]);
  const versus = useMemo(() => versusAverage(player.data?.series, s.id), [player.data, s.id]);
  const badges = (ach.data?.badges || []).filter((b) => b.unlocked && b.sessionId === s.id);
  const goalLine = useMemo(() => goalRecapLine(goals.data?.days, s.startedAt), [goals.data, s.startedAt]);
  const scale = Math.min(1, width / CARD_W);
  const ready = !player.loading && !ach.loading && !goals.loading; // export only once the badges and averages have loaded (or failed)

  useEffect(() => setNote(null), [s.id]);

  const file = async () => new File([await renderPng(node)], `strikefit-${s.playerName.replace(/[^a-z0-9]+/gi, "_")}-${s.id.slice(0, 8)}.png`, { type: "image/png" });
  const download = (f) => {
    const url = URL.createObjectURL(f);
    const a = Object.assign(document.createElement("a"), { href: url, download: f.name });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  const run = async (share) => {
    if (!node || busy) return;
    setBusy(true);
    setNote(null);
    try {
      const f = await file();
      if (share && navigator.canShare?.({ files: [f] })) await navigator.share({ files: [f], title: "My STRIKEFIT session" });
      else download(f);
    } catch (err) {
      if (err?.name !== "AbortError") setNote("Couldn't create the image. Try again.");
    } finally {
      setBusy(false);
    }
  };
  const canShare = typeof navigator !== "undefined" && typeof navigator.canShare === "function";

  return (
    <div>
      <div ref={ref} className="mx-auto w-full max-w-[540px]">
        <div className="overflow-hidden rounded-2xl border border-[var(--border)] shadow-[var(--shadow-lift)]" style={{ height: CARD_H * scale }}>
          <div style={{ width: CARD_W, height: CARD_H, transform: `scale(${scale})`, transformOrigin: "top left" }}>
            <RecapCard ref={setNode} session={s} kcal={data.fitness?.estimatedCalories} peakSpeed={peakSpeed} bestCombo={bestCombo} badges={badges} versus={versus} goalLine={goalLine} />
          </div>
        </div>
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-center gap-2">
        <Button variant="primary" size="md" icon="download" onClick={() => run(false)} disabled={!node || busy || !ready}>
          {busy ? "Creating image…" : "Download PNG"}
        </Button>
        {canShare && (
          <Button size="md" icon="external" onClick={() => run(true)} disabled={!node || busy || !ready}>
            Share
          </Button>
        )}
        <ShareChallenge sessionId={s.id} playerName={s.playerName} level={levelName(s.level)} size="md" />
      </div>
      {note && (
        <p role="alert" className="mt-3 flex items-center justify-center gap-2 text-sm font-semibold text-[var(--bad)]">
          <Icon name="info" className="h-4 w-4" /> {note}
        </p>
      )}
      <p className="mt-3 text-center text-xs font-medium text-[var(--muted)]">1080 × 1350 px. Only numbers recorded for this session appear on the card.</p>
    </div>
  );
}
