import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { DEMO_CLIP } from "../../content/showcaseShots.js";
import TIMELINE from "../../content/level2DemoTimeline.json";
import { demoCredit } from "../../lib/demoCredit.js";
import DemoVideo from "../ui/DemoVideo.jsx";
import Icon from "../ui/Icon.jsx";
import { Reveal, useLiveStatus } from "./shared.jsx";

// ─── type colour + icon for timeline markers ───────────────────────────────
const MARKER_META = {
  start:      { color: "var(--accent)",           icon: "play"      },
  warning:    { color: "var(--tier-medium)",       icon: "zap"       },
  dodge:      { color: "var(--blue-300)",          icon: "arrowRight"},
  hit:        { color: "var(--tier-full)",         icon: "swords"    },
  guardedHit: { color: "var(--tier-tap)",          icon: "shield"    },
  clash:      { color: "var(--tier-light)",        icon: "swords"    },
  guard:      { color: "var(--border-strong)",     icon: "shield"    },
  low:        { color: "var(--good)",              icon: "star"      },
};

// ─── Lightbox: the clip larger, as a modal dialog (focus kept inside, Escape or a click outside closes, focus goes back) ──
function Lightbox({ onClose }) {
  const box = useRef(null);
  const close = useRef(null);
  useEffect(() => {
    const opener = document.activeElement;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    close.current?.focus();
    const onKey = (e) => {
      if (e.key === "Escape") onClose();
      if (e.key !== "Tab" || !box.current) return;
      const f = [...box.current.querySelectorAll("button:not([disabled])")];
      if (!f.length) return;
      const [first, last] = [f[0], f[f.length - 1]];
      if (e.shiftKey && document.activeElement === first) (e.preventDefault(), last.focus());
      else if (!e.shiftKey && document.activeElement === last) (e.preventDefault(), first.focus());
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = overflow;
      opener?.focus?.();
    };
  }, [onClose]);
  return (
    <div
      ref={box}
      role="dialog"
      aria-modal="true"
      aria-label="Level 2 demo clip, larger"
      className="fixed inset-0 z-[200] flex items-center justify-center bg-black/90 p-4 backdrop-blur-sm sm:p-8"
      onClick={onClose}
    >
      <button
        ref={close}
        type="button"
        aria-label="Close the larger clip"
        onClick={onClose}
        className="press absolute right-4 top-4 flex h-11 w-11 items-center justify-center rounded-full bg-white/10 text-white ring-1 ring-white/30 hover:bg-white/20"
      >
        <Icon name="x" className="h-5 w-5" />
      </button>
      <div
        className="relative w-full max-w-5xl overflow-hidden rounded-2xl shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="relative aspect-video bg-black">
          <DemoVideo
            mp4={DEMO_CLIP.mp4}
            webm={DEMO_CLIP.webm}
            poster={DEMO_CLIP.poster}
            label={DEMO_CLIP.alt}
            active
          />
        </div>
        <p className="border-t border-white/10 bg-black/80 px-4 py-2.5 text-xs font-medium text-white/60">
          {demoCredit()}
        </p>
      </div>
    </div>
  );
}

// ─── caption chips: the timeline moments that carry a `chip` text, each shown for 2.4 s while the clip plays ──
const CHIPS = TIMELINE.markers.filter((m) => m.chip);
const CHIP_SECONDS = 2.4;

// The clip's playhead (seconds), from timeupdate; null when there is no video.
function useClipTime(video) {
  const [t, setT] = useState(0);
  useEffect(() => {
    if (!video) return undefined;
    const on = () => setT(video.currentTime);
    video.addEventListener("timeupdate", on);
    video.addEventListener("seeked", on);
    return () => {
      video.removeEventListener("timeupdate", on);
      video.removeEventListener("seeked", on);
    };
  }, [video]);
  return t;
}

function CaptionChips({ time }) {
  const chip = [...CHIPS].reverse().find((c) => time >= c.t && time < c.t + CHIP_SECONDS);
  if (!chip) return null;
  // aria-hidden: the same moments are listed (and announced) in the timeline below; a live region here would chatter
  return (
    <div className="pointer-events-none absolute bottom-14 left-3 sm:bottom-16 sm:left-4" aria-hidden>
      <span key={chip.t} className="chip-pop inline-flex items-center gap-1.5 rounded-full bg-black/70 px-3 py-1.5 text-xs font-bold text-white ring-1 ring-white/20 backdrop-blur">
        <span className="h-1.5 w-1.5 rounded-full" style={{ background: (MARKER_META[chip.type] ?? MARKER_META.hit).color }} />
        {chip.chip}
      </span>
    </div>
  );
}

// ─── "Fight at a glance" timeline strip ────────────────────────────────────
function FightTimeline({ video, time }) {
  const dur = TIMELINE.durationSeconds;
  const progress = Math.min(1, time / dur);
  let active = -1;
  TIMELINE.markers.forEach((m, i) => {
    if (time >= m.t) active = i;
  });
  const btns = useRef([]);
  const [focusIdx, setFocusIdx] = useState(null);
  const n = TIMELINE.markers.length;
  // the Tab stop is the moment last moved to, else the one playing
  const stop = focusIdx ?? Math.max(0, active);
  const onKey = (e) => {
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
    if (!step && e.key !== "Home" && e.key !== "End") return;
    e.preventDefault();
    const next = e.key === "Home" ? 0 : e.key === "End" ? n - 1 : (stop + step + n) % n;
    setFocusIdx(next);
    btns.current[next]?.focus();
  };
  const seek = (t) => {
    if (!video) return;
    video.currentTime = t;
    video.play().catch(() => {});
  };

  return (
    <div className="mt-3 sm:mt-4">
      {/* progress rail */}
      <div className="relative h-1 rounded-full bg-[var(--surface-muted)]" aria-hidden>
        <div
          className="h-full rounded-full bg-[var(--accent)]"
          style={{ width: `${progress * 100}%` }}
        />
        {TIMELINE.markers.map((m, i) => {
          const meta = MARKER_META[m.type] ?? MARKER_META.hit;
          const pct = (m.t / dur) * 100;
          return (
            // decorative: the label buttons below are the keyboard / screen-reader way to seek
            <span
              key={i}
              aria-hidden
              onClick={() => seek(m.t)}
              style={{ left: `${pct}%`, background: meta.color }}
              className={`absolute top-1/2 h-3 w-3 -translate-x-1/2 -translate-y-1/2 cursor-pointer rounded-full ring-2 transition-transform hover:scale-125 ${i === active ? "scale-125 ring-[var(--accent)]" : "ring-[var(--surface)]"}`}
            />
          );
        })}
      </div>
      {/* one Tab stop; the arrow keys (and Home / End) move between moments, Enter or Space jumps the clip there */}
      <div role="toolbar" aria-label="Jump to a moment in the clip" onKeyDown={onKey} className="mt-2 flex flex-wrap gap-x-1.5 gap-y-1">
        {TIMELINE.markers.map((m, i) => {
          const meta = MARKER_META[m.type] ?? MARKER_META.hit;
          return (
            <button
              key={i}
              ref={(el) => (btns.current[i] = el)}
              type="button"
              tabIndex={i === stop ? 0 : -1}
              onClick={() => (setFocusIdx(i), seek(m.t))}
              disabled={!video}
              aria-label={`${m.label}, jump to ${m.t.toFixed(1)} s`}
              aria-current={i === active ? "true" : undefined}
              className={`press flex min-h-6 items-center gap-1 rounded-md px-2 py-1 text-[0.7rem] font-bold tabular-nums transition-colors ${i === active ? "bg-[var(--accent-soft)] text-[var(--accent-strong)]" : "text-[var(--muted)] hover:text-[var(--ink-secondary)]"}`}
            >
              <span className="h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: meta.color }} aria-hidden />
              {m.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

// ─── extra boxes: "Try it" + "Watch live" + "Hardware" ─────────────────────
function ExtraBoxes({ liveStatus }) {
  const live = liveStatus?.state === "live";
  const count = liveStatus?.count ?? 0;
  return (
    <div className="mt-4 grid gap-3 sm:grid-cols-3">
      {/* Try it */}
      <Link to="/practice" className="group press flex flex-col gap-2 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] hover:border-[var(--accent)] sm:p-5">
        <div className="flex items-center gap-2">
          <span className="grid h-8 w-8 place-items-center rounded-[10px] bg-[var(--accent-soft)] text-[var(--accent-strong)]">
            <Icon name="pointer" className="h-4 w-4" />
          </span>
          <span className="text-sm font-bold text-[var(--ink)] group-hover:text-[var(--accent)]">Try it yourself</span>
        </div>
        <p className="text-xs font-medium leading-relaxed text-[var(--muted)]">
          Practice swings with your phone or mouse. No headset needed.
        </p>
        <p className="mt-auto flex items-center gap-1 text-xs font-bold text-[var(--accent)]">
          Open practice <Icon name="arrowRight" className="h-3 w-3" strokeWidth={2.4} />
        </p>
      </Link>

      {/* Watch live */}
      <Link
        to="/dashboard/live"
        className="group press flex flex-col gap-2 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] hover:border-[var(--accent)] sm:p-5"
      >
        <div className="flex items-center gap-2">
          <span className={`grid h-8 w-8 place-items-center rounded-[10px] ${live ? "bg-[var(--good-soft)] text-[var(--good-ink)]" : "bg-[var(--accent-soft)] text-[var(--accent-strong)]"}`}>
            <Icon name="play" className="h-4 w-4" />
          </span>
          <span className="text-sm font-bold text-[var(--ink)] group-hover:text-[var(--accent)]">Watch live</span>
          {live && (
            <span className="ml-auto flex items-center gap-1.5 rounded-full bg-[var(--good-soft)] px-2 py-0.5 text-[0.65rem] font-bold text-[var(--good-ink)] ring-1 ring-inset ring-emerald-200">
              <span className="status-live h-1.5 w-1.5 rounded-full bg-[var(--good)]" aria-hidden /> LIVE
            </span>
          )}
        </div>
        <p className="text-xs font-medium leading-relaxed text-[var(--muted)]">
          {live
            ? count === 1
              ? "A fight is being played right now."
              : `${count} fights are being played right now.`
            : "The arena is open. No fight right now — check back soon."}
        </p>
        <p className="mt-auto flex items-center gap-1 text-xs font-bold text-[var(--accent)]">
          Live arena <Icon name="arrowRight" className="h-3 w-3" strokeWidth={2.4} />
        </p>
      </Link>

      {/* Hardware */}
      <div className="flex flex-col gap-2 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] sm:p-5">
        <div className="flex items-center gap-2">
          <span className="grid h-8 w-8 place-items-center rounded-[10px] bg-[var(--accent-soft)] text-[var(--accent-strong)]">
            <Icon name="headset" className="h-4 w-4" />
          </span>
          <span className="text-sm font-bold text-[var(--ink)]">What you need</span>
        </div>
        <dl className="mt-1 grid gap-y-1 text-xs">
          {[
            ["Headset", "Meta Quest 2 (Quest 3 likely works, untested)"],
            ["Movement", "Stationary — no walking, just swing"],
            ["Session", "A few minutes per fight"],
          ].map(([k, v]) => (
            <div key={k} className="flex gap-2">
              <dt className="shrink-0 font-bold text-[var(--muted)]">{k}</dt>
              <dd className="font-medium text-[var(--ink-secondary)]">{v}</dd>
            </div>
          ))}
        </dl>
      </div>
    </div>
  );
}

// ─── main export ──────────────────────────────────────────────────────────
export default function DemoShowcase() {
  const liveStatus = useLiveStatus();
  const [lightbox, setLightbox] = useState(false);
  const [status, setStatus] = useState("checking");
  const [video, setVideo] = useState(null);
  const time = useClipTime(video);
  const hasClip = status === "ok";

  return (
    <section aria-labelledby="demo-title" className="pb-16 pt-8 lg:pb-20 lg:pt-10">
      <div className="mx-auto max-w-[1200px] px-4 sm:px-6">
        <h2 id="demo-title" className="sr-only">
          A Level 2 fight, start to finish
        </h2>

        <Reveal className="demo-hero">
          <div className="relative overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface-muted)] shadow-[var(--shadow-lift)]">
            <div className="relative aspect-video">
              <DemoVideo
                mp4={DEMO_CLIP.mp4}
                webm={DEMO_CLIP.webm}
                poster={DEMO_CLIP.poster}
                label={DEMO_CLIP.alt}
                active={!lightbox}
                fallback={<ClipFallback />}
                onStatus={setStatus}
                onVideoRef={setVideo}
              />
              {hasClip && <CaptionChips time={time} />}
              {hasClip && (
                <button
                  type="button"
                  onClick={() => setLightbox(true)}
                  aria-label="Open the demo clip larger"
                  aria-haspopup="dialog"
                  className="press absolute right-3 top-3 flex h-11 items-center gap-1.5 rounded-full bg-black/60 px-3.5 text-xs font-bold text-white ring-1 ring-white/30 backdrop-blur hover:bg-black/75 sm:right-4 sm:top-4"
                >
                  <Icon name="maximize" className="h-3.5 w-3.5" /> Expand
                </button>
              )}
            </div>
          </div>
          {/* the honesty line describes a real clip: space kept (nothing shifts) while there is none */}
          <p className={`mt-2.5 flex items-start gap-1.5 text-xs font-medium leading-relaxed text-[var(--muted)] ${hasClip ? "" : "invisible"}`} aria-hidden={!hasClip}>
            <Icon name="info" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            {demoCredit()}
          </p>
        </Reveal>

        {/* space reserved from the first frame; shown once the clip loads */}
        <div className={`mt-4 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3.5 shadow-[var(--shadow-card)] sm:px-5 sm:py-4 ${hasClip ? "" : "invisible"}`} aria-hidden={!hasClip}>
          <h3 className="t-kicker mb-2">Fight at a glance</h3>
          <FightTimeline video={hasClip ? video : null} time={time} />
        </div>

        <ExtraBoxes liveStatus={liveStatus} />
      </div>

      {lightbox && <Lightbox onClose={() => setLightbox(false)} />}
    </section>
  );
}

function ClipFallback() {
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-gradient-to-br from-[var(--accent-soft)] via-[var(--surface)] to-[var(--surface-muted)] px-4 text-center" data-empty-shot>
      <Icon name="headset" className="h-7 w-7 text-[var(--accent)]" />
      <p className="text-sm font-semibold text-[var(--muted)]">The Level 2 demo clip goes here.</p>
    </div>
  );
}
