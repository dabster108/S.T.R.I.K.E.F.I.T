import { useEffect, useRef } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Brand } from "../components/landing/LandingNav.jsx";
import { buttonClass } from "../components/ui/Button.jsx";
import { PageHeader } from "../components/ui/Card.jsx";
import { useDocTitle } from "../hooks/useDocTitle.js";
import Icon from "../components/ui/Icon.jsx";
import { useBroadcast } from "../hooks/useBroadcast.js";
import { LiveOverlay } from "../components/live/LiveHud.jsx";

// strikefit.vercel.app/broadcast — the headset's view, live. Opening the page is the
// trigger: it connects to the signaling relay and asks the game for video, so nobody
// has to touch the Quest. ?room=<name> picks a room other than the default.

const PHASES = {
  disconnected: { label: "Disconnected", icon: "offline", dot: "bg-[var(--bad)]", cls: "bg-[var(--bad-soft)] text-[var(--bad)] ring-red-200" },
  connecting: { label: "Connecting", icon: "loader", dot: "bg-[var(--warn)] status-live", cls: "bg-[var(--warn-soft)] text-[var(--warn-ink)] ring-amber-200" },
  negotiating: { label: "Negotiating Handshake", icon: "loader", dot: "bg-[var(--accent)] status-live", cls: "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent-ring)]" },
  live: { label: "LIVE", icon: "radio", dot: "bg-[var(--good)] status-live", cls: "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200" },
};

const STEPS = ["disconnected", "connecting", "negotiating", "live"];

function PhaseBadge({ phase }) {
  const p = PHASES[phase];
  return (
    <span role="status" aria-live="polite" className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-bold ring-1 ring-inset ${p.cls}`}>
      <span className={`h-2 w-2 rounded-full ${p.dot}`} aria-hidden />
      {p.label}
    </span>
  );
}

function waitingText(phase, headsetOnline) {
  if (phase === "disconnected") return "Can't reach the STRIKEFIT server. Retrying automatically…";
  if (phase === "connecting" && headsetOnline === false) return "Server connected. Waiting for the headset: start the STRIKEFIT game on the Quest and the stream begins on its own.";
  if (phase === "connecting") return "Connecting to the STRIKEFIT server… (a sleeping server can take up to a minute to wake)";
  return "Headset found. Exchanging the WebRTC handshake…";
}

function Stat({ label, value }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3">
      <div className="text-xs font-semibold uppercase tracking-wide text-[var(--muted)]">{label}</div>
      <div className="mt-1 text-lg font-bold tabular-nums text-[var(--ink)]">{value ?? "—"}</div>
    </div>
  );
}

// Player, status and stream health. Shared by the public page and the dashboard page.
function BroadcastPanel({ room, header }) {
  const { phase, headsetOnline, error, stream, stats, retry } = useBroadcast(room);
  const video = useRef(null);
  const frame = useRef(null);

  useEffect(() => {
    if (video.current && video.current.srcObject !== stream) video.current.srcObject = stream;
  }, [stream]);

  const live = phase === "live";
  const step = STEPS.indexOf(phase);
  const actions = (
    <>
      <PhaseBadge phase={phase} />
      <button type="button" className={buttonClass("secondary", "md")} onClick={retry} disabled={phase === "disconnected"}>
        <Icon name="refresh" className="h-4 w-4" /> Reconnect
      </button>
      <button type="button" className={buttonClass("primary", "md")} onClick={() => frame.current?.requestFullscreen?.()} disabled={!live}>
        <Icon name="maximize" className="h-4 w-4" /> Fullscreen
      </button>
    </>
  );

  return (
    <>
      {header(actions)}

      <div ref={frame} className="relative mt-5 aspect-video w-full overflow-hidden rounded-[var(--radius-xl)] bg-[var(--blue-900)] shadow-[var(--shadow-card)]">
        <video ref={video} autoPlay playsInline muted className={`h-full w-full object-contain ${live ? "" : "invisible"}`} />
        {live && <LiveOverlay />}
        {live ? (
          <span className="absolute left-3 top-3 inline-flex items-center gap-1.5 rounded-md bg-[var(--bad)] px-2 py-1 text-xs font-extrabold tracking-wider text-white">
            <span className="h-1.5 w-1.5 rounded-full bg-white status-live" aria-hidden /> LIVE
          </span>
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-4 px-6 text-center text-white">
            <Icon name={phase === "disconnected" ? "offline" : headsetOnline === false ? "headset" : "loader"} className={`h-10 w-10 text-[var(--blue-300)] ${phase !== "disconnected" && headsetOnline !== false ? "animate-spin" : ""}`} />
            <p className="max-w-md text-[0.95rem] font-semibold">{waitingText(phase, headsetOnline)}</p>
            <ol className="flex flex-wrap justify-center gap-2" aria-label="Connection progress">
              {STEPS.slice(1).map((s, i) => (
                <li key={s} className={`rounded-full px-2.5 py-1 text-xs font-bold ${i + 1 <= step ? "bg-white text-[var(--blue-900)]" : "bg-white/10 text-[var(--blue-100)]"}`}>
                  {i + 1}. {PHASES[s].label}
                </li>
              ))}
            </ol>
          </div>
        )}
      </div>

      {error && (
        <p className="mt-4 flex items-start gap-2 rounded-xl border border-red-200 bg-[var(--bad-soft)] px-4 py-3 text-sm font-semibold text-[var(--bad)]">
          <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </p>
      )}

      <section aria-label="Stream health" className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <Stat label="Headset" value={headsetOnline == null ? null : headsetOnline ? "Online" : "Offline"} />
        <Stat label="Resolution" value={stats?.width ? `${stats.width}×${stats.height}` : null} />
        <Stat label="Frame rate" value={stats?.fps != null ? `${stats.fps} fps` : null} />
        <Stat label="Bitrate" value={stats?.kbps != null ? `${(stats.kbps / 1000).toFixed(1)} Mbps` : null} />
        <Stat label="Round trip" value={stats?.rttMs != null ? `${stats.rttMs} ms` : null} />
        <Stat label="Packets lost" value={stats ? stats.packetsLost : null} />
      </section>
    </>
  );
}

function useRoom() {
  const [params] = useSearchParams();
  return (params.get("room") || "strikefit").trim() || "strikefit";
}

const DESCRIPTION = "The player's headset view, streamed straight from the Quest over WebRTC. Opening this page starts the stream; nothing needs to be pressed on the headset.";

// /dashboard/broadcast: same player inside the dashboard layout (sidebar + topbar).
export function DashboardBroadcastView() {
  const room = useRoom();
  useDocTitle("Broadcast");
  return <BroadcastPanel room={room} header={(actions) => <PageHeader eyebrow="Realtime" title="Broadcast" description={DESCRIPTION} actions={actions} />} />;
}

// /broadcast: standalone public page.
export default function BroadcastView() {
  const room = useRoom();
  useEffect(() => {
    document.title = "Broadcast · STRIKEFIT";
  }, []);

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <header className="border-b border-[var(--border)] bg-[var(--surface)]">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3 sm:px-6">
          <Link to="/" aria-label="STRIKEFIT home">
            <Brand />
          </Link>
          <Link to="/dashboard" className={buttonClass("secondary", "sm")}>
            <Icon name="dashboard" className="h-3.5 w-3.5" /> Dashboard
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-6 sm:px-6 sm:py-8">
        <BroadcastPanel
          room={room}
          header={(actions) => (
            <div className="flex flex-wrap items-end justify-between gap-3">
              <div>
                <h1 className="text-[length:var(--fs-h1)] font-extrabold tracking-tight">Live broadcast</h1>
                <p className="mt-1 max-w-2xl text-sm text-[var(--muted)]">
                  {DESCRIPTION} Room <span className="font-semibold text-[var(--ink-secondary)]">{room}</span>
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">{actions}</div>
            </div>
          )}
        />
      </main>
    </div>
  );
}
