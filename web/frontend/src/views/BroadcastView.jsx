import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Brand } from "../components/landing/LandingNav.jsx";
import { buttonClass } from "../components/ui/Button.jsx";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { useDocTitle } from "../hooks/useDocTitle.js";
import Icon from "../components/ui/Icon.jsx";
import { useBroadcast } from "../hooks/useBroadcast.js";
import { LiveOverlay } from "../components/live/LiveHud.jsx";
import { ReactionBar, ReactionLayer } from "../components/live/Reactions.jsx";
import { CaptionBar, CommentaryOverlay, useStageCommentary } from "../components/live/arena/Commentary.jsx";
import CommentaryControls from "../components/live/arena/CommentaryControls.jsx";
import { useLiveIds, useLiveSession } from "../live/LiveProvider.jsx";
import { copyText } from "../lib/toast.js";

// strikefit.vercel.app/broadcast — the headset's view, live. Opening the page is the
// trigger: it connects to the signaling relay and asks the game for video, so nobody
// has to touch the Quest. ?room=<name> picks a room other than the default.

const PHASES = {
  disconnected: { label: "Disconnected", icon: "offline", dot: "bg-[var(--bad)]", cls: "bg-[var(--bad-soft)] text-[var(--bad-ink)] ring-red-200" },
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

function waitingTitle(phase, headsetOnline) {
  if (phase === "disconnected") return "Server unreachable";
  if (phase === "connecting" && headsetOnline === false) return "Waiting for the headset";
  if (phase === "connecting") return "Connecting to the server";
  return "Starting the stream";
}

// The three steps to a picture as a stepper: done steps get a tick, the current one pulses, later ones stay dim.
function Stepper({ step }) {
  return (
    <ol className="flex w-full max-w-md items-start" aria-label="Connection progress">
      {STEPS.slice(1).map((s, i) => {
        const n = i + 1;
        const state = n < step || (n === step && s === "live") ? "done" : n === step ? "current" : "todo";
        return (
          <li key={s} className="flex flex-1 flex-col items-center gap-2 text-center" aria-current={state === "current" ? "step" : undefined}>
            <div className="flex w-full items-center">
              <span className={`h-0.5 flex-1 rounded-full ${i === 0 ? "opacity-0" : n <= step ? "bg-white/80" : "bg-white/15"}`} aria-hidden />
              <span
                className={`relative grid h-9 w-9 shrink-0 place-items-center rounded-full text-sm font-extrabold ${
                  state === "done" ? "bg-white text-[var(--blue-900)]" : state === "current" ? "bg-[var(--accent)] text-white ring-4 ring-white/20" : "bg-white/10 text-[var(--blue-100)] ring-1 ring-inset ring-white/20"
                }`}
              >
                {state === "current" && <span className="bc-ping absolute inset-0 rounded-full bg-[var(--blue-400)]" aria-hidden />}
                <span className="relative">{state === "done" ? <Icon name="check" className="h-4 w-4" /> : n}</span>
              </span>
              <span className={`h-0.5 flex-1 rounded-full ${n === 3 ? "opacity-0" : n < step ? "bg-white/80" : "bg-white/15"}`} aria-hidden />
            </div>
            <span className={`px-1 text-xs font-bold ${state === "todo" ? "text-[var(--blue-300)]" : "text-white"}`}>{PHASES[s].label === "LIVE" ? "Live" : PHASES[s].label}</span>
          </li>
        );
      })}
    </ol>
  );
}

// mm:ss since the picture arrived, for the LIVE badge.
function useElapsed(running) {
  const [sec, setSec] = useState(0);
  useEffect(() => {
    if (!running) return setSec(0);
    const t0 = Date.now();
    const id = setInterval(() => setSec(Math.floor((Date.now() - t0) / 1000)), 1000);
    return () => clearInterval(id);
  }, [running]);
  return `${String(Math.floor(sec / 60)).padStart(2, "0")}:${String(sec % 60).padStart(2, "0")}`;
}

function Stat({ icon, label, value }) {
  return (
    <div className="flex items-center gap-3 bg-[var(--surface)] px-4 py-3.5">
      <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent)]" aria-hidden>
        <Icon name={icon} className="h-4 w-4" />
      </span>
      <div className="min-w-0">
        <div className="t-kicker truncate">{label}</div>
        <div className={`mt-0.5 truncate text-base font-extrabold tabular-nums tracking-tight ${value == null ? "text-[var(--muted)]" : "text-[var(--ink)]"}`}>{value ?? "—"}</div>
      </div>
    </div>
  );
}

// One word for the stream's state, from the numbers the player already reports (shown with its label, never colour alone).
function health(live, stats) {
  if (!live || !stats) return { label: "No video yet", cls: "bg-[var(--surface-muted)] text-[var(--muted)] ring-[var(--border)]", dot: "bg-[var(--border-strong)]" };
  const smooth = (stats.fps == null || stats.fps >= 24) && (stats.rttMs == null || stats.rttMs < 200);
  return smooth
    ? { label: "Smooth", cls: "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200", dot: "bg-[var(--good)]" }
    : { label: "Unstable", cls: "bg-[var(--warn-soft)] text-[var(--warn-ink)] ring-amber-200", dot: "bg-[var(--warn)]" };
}

// Cheers for the fight on the video (the first live fight, the same one LiveOverlay shows): the bar under the player,
// the floating icons and counter over it.
function useFirstFight() {
  const ids = useLiveIds();
  const entry = useLiveSession(ids[0] || "__none__");
  return entry && entry.session.status === "live" ? entry.session : null;
}
function CheerBar() {
  const fight = useFirstFight();
  if (!fight) return null;
  return (
    <div className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2.5">
      <p className="text-sm font-semibold text-[var(--ink-secondary)]">
        Cheering for <span className="font-bold text-[var(--ink)]">{fight.playerName}</span>
        <span className="ml-1 text-xs font-medium text-[var(--muted)]">(shown here and on the dashboard, not in the headset yet)</span>
      </p>
      <ReactionBar sessionId={fight.id} />
    </div>
  );
}
function CheerLayer() {
  const fight = useFirstFight();
  return fight ? <ReactionLayer sessionId={fight.id} tone="overlay" /> : null;
}
// The live commentary of the fight on the video, across the top of the frame (the viewer can hide it or have it read aloud).
function CommentaryLayer() {
  const fight = useFirstFight();
  return fight ? <CommentaryOverlay id={fight.id} /> : null;
}
// With no headset video yet, the same commentary sits as a bar under the player, so anyone watching still follows the fight.
function CommentaryBar({ id }) {
  const entry = useLiveSession(id);
  const commentary = useStageCommentary(id, entry, { speak: true });
  return (
    <div className="mt-3">
      <CaptionBar commentary={commentary} />
    </div>
  );
}
function CommentaryBelow() {
  const fight = useFirstFight();
  return fight ? <CommentaryBar id={fight.id} /> : null;
}

// Player, status and stream health. Shared by the public page and the dashboard page.
function BroadcastPanel({ room, header }) {
  const { phase, headsetOnline, error, stream, stats, retry } = useBroadcast(room);
  const video = useRef(null);
  const frame = useRef(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (video.current && video.current.srcObject !== stream) video.current.srcObject = stream;
  }, [stream]);

  const live = phase === "live";
  const step = STEPS.indexOf(phase);
  const elapsed = useElapsed(live);
  const h = health(live, stats);
  const waitingIcon = phase === "disconnected" ? "offline" : headsetOnline === false ? "headset" : "loader";

  const copyLink = async () => {
    const url = `${window.location.origin}/broadcast${room === "strikefit" ? "" : `?room=${encodeURIComponent(room)}`}`;
    if (await copyText(url, "Broadcast link")) {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const actions = (
    <>
      <PhaseBadge phase={phase} />
      <button type="button" className={buttonClass("secondary", "md")} onClick={copyLink}>
        <Icon name={copied ? "check" : "link"} className="h-4 w-4" /> {copied ? "Copied" : "Copy link"}
      </button>
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

      <div ref={frame} className={`bc-stage relative mt-5 aspect-video w-full overflow-hidden rounded-[var(--radius-xl)] shadow-[0_24px_60px_-24px_var(--accent-glow)] ring-1 ring-[var(--blue-800)] ${live ? "" : "max-sm:aspect-auto max-sm:min-h-[21rem]"}`}
      >
        <video ref={video} autoPlay playsInline muted className={`relative h-full w-full object-contain ${live ? "" : "invisible"}`} />
        {live && <LiveOverlay />}
        {live && <CheerLayer />}
        {live && <CommentaryLayer />}
        {live ? (
          <span className="absolute left-3 top-3 z-[3] inline-flex items-center gap-2 rounded-lg bg-black/45 py-1 pl-1 pr-2.5 text-xs font-extrabold tracking-wider text-white ring-1 ring-inset ring-white/15 backdrop-blur-md">
            <span className="inline-flex items-center gap-1.5 rounded-md bg-[var(--bad)] px-2 py-0.5">
              <span className="h-1.5 w-1.5 rounded-full bg-white status-live" aria-hidden /> LIVE
            </span>
            <span className="tabular-nums" aria-label={`Live for ${elapsed}`}>{elapsed}</span>
          </span>
        ) : (
          <>
            <div className="absolute inset-x-3 top-3 z-[2] flex items-center justify-between gap-2 sm:inset-x-4 sm:top-4">
              <span className="inline-flex items-center gap-1.5 rounded-full bg-white/10 px-3 py-1 text-xs font-bold text-[var(--blue-100)] ring-1 ring-inset ring-white/15 backdrop-blur-md">
                <Icon name="signal" className="h-3.5 w-3.5" /> Room <span className="text-white">{room}</span>
              </span>
              <span className="hidden items-center gap-1.5 rounded-full bg-white/10 px-3 py-1 text-xs font-bold text-[var(--blue-100)] ring-1 ring-inset ring-white/15 backdrop-blur-md sm:inline-flex">
                <Icon name="headset" className="h-3.5 w-3.5" /> Headset {headsetOnline == null ? "…" : headsetOnline ? "online" : "offline"}
              </span>
            </div>

            <div className="absolute inset-0 z-[1] flex flex-col items-center justify-center gap-5 px-6 text-center text-white">
              <div className="relative grid h-20 w-20 place-items-center sm:h-24 sm:w-24">
                {phase !== "disconnected" && (
                  <>
                    <span className="bc-wave absolute inset-0 rounded-full border border-[var(--blue-300)]" aria-hidden />
                    <span className="bc-wave bc-wave-2 absolute inset-0 rounded-full border border-[var(--blue-300)]" aria-hidden />
                  </>
                )}
                <span className="relative grid h-full w-full place-items-center rounded-full bg-white/10 ring-1 ring-inset ring-white/20 backdrop-blur-md">
                  <Icon name={waitingIcon} className={`h-9 w-9 text-[var(--blue-100)] sm:h-10 sm:w-10 ${waitingIcon === "loader" ? "animate-spin" : ""}`} />
                </span>
              </div>
              <div className="space-y-1.5">
                <p className="text-lg font-extrabold tracking-tight sm:text-2xl">{waitingTitle(phase, headsetOnline)}</p>
                <p className="mx-auto hidden max-w-md text-sm font-medium text-[var(--blue-100)] sm:block">{waitingText(phase, headsetOnline)}</p>
              </div>
              <Stepper step={step} />
            </div>

            <p className="absolute inset-x-0 bottom-3 z-[1] hidden items-center justify-center gap-1.5 text-xs font-semibold text-[var(--blue-300)] sm:flex">
              <Icon name="shieldCheck" className="h-3.5 w-3.5" /> Video goes from the headset straight to this browser; the server only introduces them.
            </p>
          </>
        )}
      </div>
      {/* On a phone the stage is short, so the explanation sits under it instead. */}
      {!live && <p className="mt-2 text-sm font-medium text-[var(--muted)] sm:hidden">{waitingText(phase, headsetOnline)}</p>}

      {!live && <CommentaryBelow />}
      <CheerBar />
      <div className="mt-3">
        <CommentaryControls />
      </div>

      {error && (
        <p className="mt-4 flex items-start gap-2 rounded-xl border border-red-200 bg-[var(--bad-soft)] px-4 py-3 text-sm font-semibold text-[var(--bad-ink)]">
          <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </p>
      )}

      <Card as="section" aria-label="Stream health" className="mt-5 overflow-hidden">
        <CardHeader
          title="Stream health"
          subtitle="Measured in this browser every second while the video plays"
          actions={
            <span className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-xs font-bold ring-1 ring-inset ${h.cls}`}>
              <span className={`h-2 w-2 rounded-full ${h.dot} ${live ? "status-live" : ""}`} aria-hidden />
              {h.label}
            </span>
          }
        />
        <div className="grid grid-cols-1 gap-px bg-[var(--border)] min-[420px]:grid-cols-2 md:grid-cols-3 xl:grid-cols-6">
          <Stat icon="headset" label="Headset" value={headsetOnline == null ? null : headsetOnline ? "Online" : "Offline"} />
          <Stat icon="monitor" label="Resolution" value={stats?.width ? `${stats.width}×${stats.height}` : null} />
          <Stat icon="activity" label="Frame rate" value={stats?.fps != null ? `${stats.fps} fps` : null} />
          <Stat icon="waveform" label="Bitrate" value={stats?.kbps != null ? `${(stats.kbps / 1000).toFixed(1)} Mbps` : null} />
          <Stat icon="timer" label="Round trip" value={stats?.rttMs != null ? `${stats.rttMs} ms` : null} />
          <Stat icon="layers" label="Packets lost" value={stats ? stats.packetsLost : null} />
        </div>
      </Card>
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
                  {DESCRIPTION}
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
