import { forwardRef, useEffect, useState } from "react";
import { api } from "../../lib/api.js";
import { levelName } from "../../lib/game.js";
import { formatKcal, formatMinutes } from "../../lib/fitness.js";
import { formatDuration, formatNumber } from "../../lib/format.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import { SegmentedControl } from "../ui/Controls.jsx";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import { WorkoutEquivalent } from "../fitness/Fitness.jsx";
import { CARD_H, CARD_W, renderPng } from "../recap/RecapCard.jsx";

// "Your week": Monday to Sunday in the API's local zone, this week so far or last week. Only recorded numbers; "vs
// previous week" only when that week had a session. The PNG export reuses the session recap card's renderer and size.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const WEEKS = [
  { id: "this", label: "This week" },
  { id: "last", label: "Last week" },
];
const range = (w) => {
  const f = (d) => new Date(`${d}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });
  return `${f(w.weekStart)} to ${f(w.weekEnd)}`;
};
const signed = (v, fmt = (x) => formatNumber(x)) => (v > 0 ? `+${fmt(v)}` : v < 0 ? `−${fmt(Math.abs(v))}` : "same");

function figures(w) {
  return [
    { label: "Sessions", value: formatNumber(w.sessions), sub: `on ${w.activeDays} day${w.activeDays === 1 ? "" : "s"}`, key: "sessions" },
    { label: "Active minutes", value: formatNumber(w.activeMinutes), sub: `${formatDuration(w.fightSeconds)} in fights`, key: "activeMinutes" },
    { label: "Full-power swings", value: formatNumber(w.fullSwings), key: "fullSwings" },
    { label: "Best swing speed", value: isNum(w.bestSwingSpeed) ? `${w.bestSwingSpeed.toFixed(1)} m/s` : "—", key: "bestSwingSpeed" },
    { label: "Levels cleared", value: w.levelsCleared.length ? w.levelsCleared.map(levelName).join(", ") : "None yet", key: null },
  ];
}

const WeekRecapCard = forwardRef(function WeekRecapCard({ name, w }, ref) {
  return (
    <div ref={ref} style={{ width: CARD_W, height: CARD_H, boxSizing: "border-box", padding: "56px 64px", background: "var(--surface)", color: "var(--ink)", fontFamily: "var(--font-ui)", display: "flex", flexDirection: "column", position: "relative", overflow: "hidden" }}>
      <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 18, background: "var(--accent)" }} />
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: 12 }}>
        <div style={{ fontSize: 40, fontWeight: 800, letterSpacing: "0.12em", color: "var(--accent-strong)" }}>STRIKEFIT</div>
        <div style={{ fontSize: 28, fontWeight: 600, color: "var(--muted)" }}>{range(w)}</div>
      </div>
      <div style={{ marginTop: 40 }}>
        <div style={{ fontSize: 30, fontWeight: 700, color: "var(--muted)" }}>My week</div>
        <div style={{ fontSize: 68, fontWeight: 800, letterSpacing: "-0.035em", lineHeight: 1.05, overflowWrap: "anywhere" }}>{name}</div>
      </div>
      <div style={{ marginTop: 36, borderRadius: 32, padding: "26px 40px", background: "linear-gradient(135deg, var(--accent) 0%, var(--blue-800) 100%)", color: "#fff" }}>
        <div style={{ fontSize: 28, fontWeight: 600, opacity: 0.9 }}>Estimated energy use</div>
        <div style={{ display: "flex", alignItems: "baseline", gap: 18, marginTop: 6 }}>
          <span style={{ fontSize: 104, fontWeight: 800, letterSpacing: "-0.04em", lineHeight: 1 }}>{isNum(w.estimatedCalories) ? Math.round(w.estimatedCalories * 10) / 10 : "—"}</span>
          <span style={{ fontSize: 44, fontWeight: 700, opacity: 0.9 }}>kcal (estimated)</span>
        </div>
        {isNum(w.estimatedWalkingMinutes) && <div style={{ fontSize: 26, fontWeight: 600, opacity: 0.9, marginTop: 6 }}>roughly {formatMinutes(w.estimatedWalkingMinutes)} of brisk walking</div>}
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 18, marginTop: 26 }}>
        {figures(w).map((t) => (
          <div key={t.label} style={{ borderRadius: 24, border: "2px solid var(--border)", padding: "22px 28px" }}>
            <div style={{ fontSize: 24, fontWeight: 700, color: "var(--muted)" }}>{t.label}</div>
            <div style={{ fontSize: 48, fontWeight: 800, letterSpacing: "-0.03em", marginTop: 4 }}>{t.value}</div>
          </div>
        ))}
      </div>
      <div style={{ marginTop: "auto", fontSize: 22, fontWeight: 500, color: "var(--muted)", lineHeight: 1.4, paddingTop: 20 }}>
        Calories are estimated from fight length and swing strength with an assumed body mass, not measured. · strikefit.vercel.app
      </div>
    </div>
  );
});

export function WeekCard({ name }) {
  const [which, setWhich] = useState("this");
  const state = useApi(() => api.playerWeek(name, which), [name, which]);
  const refresh = useThrottledCallback(state.reload, 5000);
  useLiveMessages((type, payload) => type === "session_finished" && payload?.session?.playerName === name && refresh());
  const data = state.data && state.data.playerName === name && state.data.offset === (which === "last" ? 1 : 0) ? state.data : null;
  const [node, setNode] = useState(null);
  const [exporting, setExporting] = useState(false);
  const [note, setNote] = useState(null);

  // The 1080 x 1350 card is mounted off screen only while exporting.
  useEffect(() => {
    if (!exporting || !node || !data) return;
    let off = false;
    (async () => {
      try {
        const blob = await renderPng(node);
        if (off) return;
        const file = new File([blob], `strikefit-week-${name.replace(/[^a-z0-9]+/gi, "_")}-${data.week.weekStart}.png`, { type: "image/png" });
        const url = URL.createObjectURL(file);
        const a = Object.assign(document.createElement("a"), { href: url, download: file.name });
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        setNote("Saved the PNG.");
      } catch {
        if (!off) setNote("Couldn't create the image. Try again.");
      } finally {
        if (!off) setExporting(false);
      }
    })();
    return () => {
      off = true;
    };
  }, [exporting, node, data, name]);

  const w = data?.week;
  const vs = data?.vsPrevious;
  return (
    <Card className="overflow-hidden">
      <CardHeader
        title="Your week"
        subtitle={w ? `${range(w)}${data.inProgress ? " · so far" : ""} · Monday to Sunday (${data.timeZone})` : "Monday to Sunday"}
        actions={
          <>
            <SegmentedControl label="Week" options={WEEKS} value={which} onChange={setWhich} />
            <Button icon="download" onClick={() => { setNote(null); setExporting(true); }} disabled={!w || !w.sessions || exporting}>
              {exporting ? "Creating…" : "PNG"}
            </Button>
          </>
        }
      />
      {!data ? (
        state.error ? <EmptyState icon="calendar" title="Your week will appear here" description="It loads as soon as the server answers." /> : <div className="p-5"><Skeleton className="h-40 w-full" /></div>
      ) : !w.sessions ? (
        <EmptyState icon="calendar" title={which === "last" ? "No fights last week" : "No fights yet this week"} description={which === "last" ? "Weeks with a session show their totals here." : "Totals fill in as fights finish. The week resets on Monday."} />
      ) : (
        <div className="p-5">
          <div className="rounded-2xl bg-gradient-to-br from-[var(--accent)] to-[var(--blue-800)] px-5 py-4 text-white">
            <p className="text-xs font-semibold opacity-90">Estimated energy use</p>
            <p className="mt-1 text-3xl font-extrabold tabular-nums tracking-tight">
              {isNum(w.estimatedCalories) ? formatKcal(w.estimatedCalories) : "—"} <span className="text-sm font-bold opacity-90">estimated</span>
            </p>
            {isNum(w.estimatedWalkingMinutes) ? (
              <div className="[&_p]:text-[var(--blue-100)]">
                <WorkoutEquivalent walkingMinutes={w.estimatedWalkingMinutes} />
              </div>
            ) : (
              <p className="mt-0.5 text-xs font-medium text-[var(--blue-100)]">No fight this week recorded swings, so there is nothing to estimate from.</p>
            )}
          </div>
          <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3 lg:grid-cols-5">
            {figures(w).map((f) => (
              <div key={f.label}>
                <dt className="t-kicker">{f.label}</dt>
                <dd className="mt-1 text-lg font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{f.value}</dd>
                {f.sub && <dd className="text-xs font-semibold text-[var(--muted)]">{f.sub}</dd>}
                {vs && f.key && vs[f.key] !== null && vs[f.key] !== undefined && (
                  <dd className="text-xs font-bold text-[var(--ink-secondary)]">{signed(vs[f.key], f.key === "bestSwingSpeed" ? (x) => `${x.toFixed(1)} m/s` : (x) => formatNumber(x))} vs previous week</dd>
                )}
              </div>
            ))}
          </dl>
          {!vs && <p className="mt-3 text-xs font-medium text-[var(--muted)]">No fights the week before, so there is nothing to compare with.</p>}
          {vs && isNum(vs.estimatedCalories) && <p className="mt-3 text-xs font-semibold text-[var(--muted)]">Estimated kcal {signed(vs.estimatedCalories, (x) => `${Math.round(x * 10) / 10}`)} vs previous week (estimate).</p>}
          {note && <p className="mt-3 text-xs font-semibold text-[var(--ink-secondary)]" role="status"><Icon name="info" className="mr-1 inline h-3.5 w-3.5" />{note}</p>}
        </div>
      )}
      {exporting && w && (
        <div aria-hidden style={{ position: "fixed", left: -20000, top: 0, pointerEvents: "none" }}>
          <WeekRecapCard ref={setNode} name={name} w={w} />
        </div>
      )}
    </Card>
  );
}
