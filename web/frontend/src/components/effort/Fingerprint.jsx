import { useEffect, useId, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { useApi } from "../../hooks/useApi.js";
import { formatFade } from "../../lib/effort.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import RadarChart from "./RadarChart.jsx";

// Effort fingerprint: five axes scored 0-100 from the player's last 10 finished fights that have swing data
// (GET /api/players/:name/fingerprint; the maths is backend/effort.js). How each axis is scored:
//   Power         average swing speed / the game's 8 m/s cap
//   Consistency   100 - (standard deviation of swing speed / mean swing speed x 100), kept within 0-100
//   Stamina       100 + the average effort fade % (see Effort fade), kept within 0-100
//   Dodge         share of monster attacks that did not hurt the player within 1.5 s of the attack starting
//   Guard timing  share of hits that landed while the monster was NOT guarding (dropped when the game sent no guard flag)
// An axis with no data behind it is left off the chart and named underneath; it is never drawn as 0.
// The card carries only the STRIKEFIT wordmark and the player's name (already a pseudonym): no personal data.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const W = 1080; // export size, px
const MIN_H = 560;

// What the exported image looks like. Inline styles on purpose (as RecapCard does): the PNG is rendered from this exact node.
function Poster({ data, axes, nodeRef }) {
  const omitted = data.axes.filter((a) => a.score === null);
  return (
    <div ref={nodeRef} style={{ position: "relative", width: W, height: W, boxSizing: "border-box", padding: "64px 72px 48px", background: "#ffffff", color: "var(--ink)", fontFamily: "var(--font-ui)", display: "flex", flexDirection: "column", overflow: "hidden" }}>
      <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 18, background: "var(--accent)" }} />
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: 8 }}>
        <div style={{ fontSize: 40, fontWeight: 800, letterSpacing: "0.12em", color: "var(--accent-strong)" }}>STRIKEFIT</div>
        <div style={{ fontSize: 28, fontWeight: 600, color: "var(--muted)" }}>Effort fingerprint</div>
      </div>
      <div style={{ marginTop: 28, fontSize: 68, fontWeight: 800, letterSpacing: "-0.035em", lineHeight: 1.05, overflowWrap: "anywhere" }}>{data.playerName}</div>
      <div style={{ marginTop: 8, fontSize: 26, fontWeight: 600, color: "var(--muted)" }}>
        Last {data.fights} fight{data.fights === 1 ? "" : "s"} with swing data · {data.swings} swings
      </div>
      <div style={{ display: "flex", justifyContent: "center", marginTop: 8 }}>
        <RadarChart axes={axes} size={640} scale={1.7} caption="Effort fingerprint" />
      </div>
      <div style={{ marginTop: "auto", fontSize: 21, fontWeight: 500, color: "var(--muted)", lineHeight: 1.4 }}>
        {omitted.length > 0 && <div style={{ marginBottom: 6 }}>Not shown (no data yet): {omitted.map((a) => a.label).join(", ")}.</div>}
        Scores run from 0 to 100 and come from swing speed, fade, hits taken and guard timing. · strikefit.vercel.app
      </div>
    </div>
  );
}

// "How is this scored?": a small, non-modal panel under the button. Escape or a click outside closes it; focus goes back to the button.
function ScoringPopover({ axes }) {
  const [open, setOpen] = useState(false);
  const btn = useRef(null);
  const box = useRef(null);
  const id = useId();
  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e) => {
      if (e.key === "Escape") {
        setOpen(false);
        btn.current?.focus();
      }
    };
    const onDown = (e) => !box.current?.contains(e.target) && !btn.current?.contains(e.target) && setOpen(false);
    document.addEventListener("keydown", onKey);
    document.addEventListener("pointerdown", onDown);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("pointerdown", onDown);
    };
  }, [open]);
  return (
    <span className="relative">
      <Button ref={btn} icon="info" aria-expanded={open} aria-controls={id} onClick={() => setOpen((o) => !o)}>
        How is this scored?
      </Button>
      {open && (
        <div ref={box} id={id} role="region" aria-label="How the fingerprint is scored" className="absolute right-0 z-20 mt-2 w-[min(24rem,calc(100vw-2rem))] rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4 text-left shadow-lg">
          <p className="text-sm font-bold text-[var(--ink)]">Five axes, each 0 to 100</p>
          <dl className="mt-3 space-y-3 text-xs">
            {axes.map((a) => (
              <div key={a.key}>
                <dt className="font-bold text-[var(--ink)]">{a.label}</dt>
                <dd className="mt-0.5 font-medium leading-relaxed text-[var(--ink-secondary)]">{a.formula}.</dd>
              </div>
            ))}
          </dl>
          <p className="mt-3 border-t border-[var(--border)] pt-3 text-xs font-medium leading-relaxed text-[var(--muted)]">
            Built from your last 10 finished fights that recorded swings. Swing speeds above the game's 8 m/s cap count as 8. Dodge needs at least 5 timed monster attacks. An axis with no data is left off, not scored as 0.
          </p>
        </div>
      )}
    </span>
  );
}

async function renderPng(node) {
  if (document.fonts?.ready) await document.fonts.ready;
  const { toBlob } = await import("html-to-image");
  const opts = { width: W, height: W, pixelRatio: 1, cacheBust: true };
  await toBlob(node, opts); // the first pass can miss fonts still loading; the second is the real one
  return toBlob(node, opts);
}

export default function Fingerprint({ name }) {
  const { data, error, reload } = useApi(() => api.playerFingerprint(name), [name]);
  const [wrap, width] = useElementWidth(W);
  const nodeRef = useRef(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState(null);

  if (error && !data) {
    return <EmptyState icon="clock" title="The fingerprint is unavailable right now" description="It loads from the server as soon as it answers." action={<Button onClick={reload}>Try again</Button>} />;
  }
  if (!data || data.playerName !== name) {
    return (
      <div className="grid gap-5 p-5 lg:grid-cols-2" style={{ minHeight: MIN_H }} aria-hidden>
        <Skeleton className="h-[26rem] w-full rounded-xl" />
        <Skeleton className="h-[26rem] w-full rounded-xl" />
      </div>
    );
  }
  if (data.status !== "ok") {
    return <EmptyState icon="bolt" title="Not enough swings yet" description={`${data.reason || "Play a few more fights"}. The fingerprint appears once there are enough landed swings and monster attacks to score at least three axes.`} />;
  }

  const axes = data.axes.filter((a) => a.score !== null);
  const omitted = data.axes.filter((a) => a.score === null);
  const scale = Math.min(1, width / W);

  const exportPng = async () => {
    if (!nodeRef.current || busy) return;
    setBusy(true);
    setNote(null);
    try {
      const blob = await renderPng(nodeRef.current);
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement("a"), { href: url, download: `strikefit-fingerprint-${name.replace(/[^a-z0-9]+/gi, "_")}.png` });
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch {
      setNote("Couldn't create the image. Try again.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="p-5" style={{ minHeight: MIN_H }}>
      <div className="mb-4 flex flex-wrap items-center justify-end gap-2">
        <ScoringPopover axes={data.axes} />
        <Button variant="primary" icon="download" onClick={exportPng} disabled={busy}>
          {busy ? "Creating image…" : "Export PNG"}
        </Button>
      </div>
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
        <div ref={wrap} className="mx-auto w-full max-w-[26rem]">
          <div className="overflow-hidden rounded-2xl border border-[var(--border)] shadow-[var(--shadow-lift)]" style={{ height: W * scale }}>
            <div style={{ width: W, height: W, transform: `scale(${scale})`, transformOrigin: "top left" }}>
              <Poster data={data} axes={axes} nodeRef={nodeRef} />
            </div>
          </div>
        </div>

        <div className="min-w-0">
          <table className="w-full text-left text-sm">
            <caption className="sr-only">Effort fingerprint scores and how each is calculated</caption>
            <thead>
              <tr className="text-xs text-[var(--muted)]">
                <th scope="col" className="pb-2 pr-3 font-semibold">Axis</th>
                <th scope="col" className="pb-2 pr-3 text-right font-semibold">Score</th>
                <th scope="col" className="hidden pb-2 font-semibold sm:table-cell">How it is scored</th>
              </tr>
            </thead>
            <tbody>
              {data.axes.map((a) => (
                <tr key={a.key} className="border-t border-[var(--border)] align-top">
                  <th scope="row" className="py-2.5 pr-3 font-bold text-[var(--ink)]">{a.label}</th>
                  <td className="py-2.5 pr-3 text-right text-base font-extrabold tabular-nums text-[var(--ink)]">{a.score === null ? "—" : a.score}</td>
                  <td className="hidden py-2.5 text-xs font-medium leading-relaxed text-[var(--ink-secondary)] sm:table-cell">{a.score === null ? "Left off: not enough data behind it yet." : `${a.formula}.`}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <dl className="mt-4 grid grid-cols-2 gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] p-4 text-xs sm:grid-cols-4">
            {[
              ["Fights", data.fights],
              ["Swings", data.swings],
              ["Monster attacks", data.attacks],
              ["Attacks avoided", isNum(data.attacksAvoided) ? data.attacksAvoided : "—"],
            ].map(([k, v]) => (
              <div key={k}>
                <dt className="font-semibold text-[var(--muted)]">{k}</dt>
                <dd className="mt-0.5 text-base font-extrabold tabular-nums text-[var(--ink)]">{v}</dd>
              </div>
            ))}
          </dl>
          {data.averageFade?.status === "ok" && (
            <p className="mt-3 text-xs font-medium text-[var(--muted)]">Stamina uses the average fade of these fights: {formatFade(data.averageFade.fadePct)} ({data.averageFade.labelText.toLowerCase()}).</p>
          )}
          {omitted.length > 0 && (
            <p className="mt-2 flex items-start gap-2 text-xs font-medium text-[var(--muted)]">
              <Icon name="info" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              {omitted.map((a) => a.label).join(" and ")} {omitted.length === 1 ? "is" : "are"} left off: {omitted.length === 1 ? "it has" : "they have"} no data behind {omitted.length === 1 ? "it" : "them"} yet.
            </p>
          )}
          {note && <p role="alert" className="mt-3 text-sm font-semibold text-[var(--bad)]">{note}</p>}
        </div>
      </div>
    </div>
  );
}
