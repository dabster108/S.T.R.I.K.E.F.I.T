import { memo, useEffect, useState } from "react";
import { COMMENTARY, RESTING_TEXT } from "../../../config/commentary.js";
import { useReducedMotion } from "../../../hooks/motion.js";
import { useCommentary, useCommentaryPrefs } from "../../../hooks/useCommentary.js";
import { commentaryPrefs, speechSupported } from "../../../lib/commentaryPrefs.js";
import { monsterFor } from "../../../lib/arenaApp.js";
import { formatClock } from "../../../lib/format.js";
import { useLiveSession } from "../../../live/LiveProvider.jsx";
import { usePauseOffscreen } from "../../../motion/index.js";
import Button from "../../ui/Button.jsx";
import Icon from "../../ui/Icon.jsx";

// Live Commentary for a fight: a large caption bar (readable across a room), a log of the last 20 lines and the viewer's two choices
// (hide it; read it aloud). The words come from lib/commentary.js; this file only shows them. A new caption types in one character at a
// time with an opacity-only animation, and simply appears under reduced motion and in Calm mode.

const TONE = {
  accent: "bg-[var(--accent-soft)] text-[var(--accent)]",
  good: "bg-[var(--good-soft)] text-[var(--good-ink)]",
  bad: "bg-[var(--bad-soft)] text-[var(--bad-ink)]",
  warn: "bg-[var(--warn-soft)] text-[var(--warn-ink)]",
  muted: "bg-gray-100 text-[var(--muted)]",
};
const OVERLAY_TONE = { accent: "bg-[var(--accent)]", good: "bg-[var(--good)]", bad: "bg-[var(--bad)]", warn: "bg-[var(--warn)]", muted: "bg-white/25" };

/** The commentary for one fight stage: pass this to CaptionBar and CommentaryLog. `speak`: read this fight's lines aloud when speech is on. */
export function useStageCommentary(id, entry, { speak = false } = {}) {
  const session = entry?.session;
  const monster = session ? monsterFor(session.level) : null;
  return useCommentary({
    id,
    levelKey: session?.level,
    playerName: session?.playerName,
    monsterName: monster?.name,
    liveState: entry?.liveState,
    status: session?.status,
    speak,
  });
}

// Each character fades in a few milliseconds after the one before it (opacity only; see .cm-char in styles.css).
function TypedText({ text, animate }) {
  if (!animate || text.length > COMMENTARY.typeMaxChars) return text;
  return (
    <>
      {Array.from(text).map((ch, i) => (
        <span key={i} className="cm-char" style={{ "--cm-delay": `${i * COMMENTARY.typeStepMs}ms` }} aria-hidden>
          {ch}
        </span>
      ))}
      <span className="sr-only">{text}</span>
    </>
  );
}

// After a while the bar goes back to its resting text, so an old line is never mistaken for the present.
function useStale(current) {
  const [staleKey, setStaleKey] = useState(0);
  useEffect(() => {
    if (!current) return undefined;
    const t = setTimeout(() => setStaleKey(current.key), COMMENTARY.captionHoldMs);
    return () => clearTimeout(t);
  }, [current]);
  return !current || staleKey === current.key;
}

export const CaptionBar = memo(function CaptionBar({ commentary, variant = "stage" }) {
  const reduced = useReducedMotion();
  const watch = usePauseOffscreen();
  const stale = useStale(commentary.current);
  if (!commentary.enabled) return null;
  const line = commentary.current;
  const showing = line && !stale;
  const overlay = variant === "overlay";
  return (
    <section
      ref={watch}
      aria-label="Live commentary"
      data-testid="commentary-caption"
      className={
        overlay
          ? "flex min-h-[3.25rem] items-center gap-3 rounded-xl bg-[var(--blue-900)]/90 px-4 py-2.5 text-white shadow-[var(--shadow-lift)] sm:min-h-[4rem]"
          : "flex min-h-[4.75rem] items-center gap-4 rounded-2xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-[var(--shadow-card)] sm:min-h-[6rem] sm:px-6"
      }
    >
      <span
        className={`flex shrink-0 items-center justify-center rounded-full ${overlay ? "h-8 w-8 text-white" : "h-10 w-10 sm:h-12 sm:w-12"} ${
          showing ? (overlay ? OVERLAY_TONE[line.tone] || OVERLAY_TONE.muted : TONE[line.tone] || TONE.muted) : overlay ? "bg-white/20" : "bg-gray-100 text-[var(--muted)]"
        }`}
        aria-hidden
      >
        <Icon name={showing ? line.icon : "captions"} className={overlay ? "h-4 w-4" : "h-5 w-5 sm:h-6 sm:w-6"} strokeWidth={2.2} />
      </span>
      <p
        key={showing ? line.key : "rest"}
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className={
          overlay
            ? `min-w-0 flex-1 text-base font-extrabold leading-snug tracking-tight sm:text-xl ${showing ? "text-white" : "text-[var(--blue-100)]"}`
            : `min-w-0 flex-1 text-xl font-extrabold leading-snug tracking-tight sm:text-3xl 2xl:text-4xl ${showing ? "text-[var(--ink)]" : "text-[var(--muted)]"}`
        }
      >
        {showing ? <TypedText text={line.text} animate={!reduced} /> : RESTING_TEXT}
      </p>
    </section>
  );
});

export const CommentaryLog = memo(function CommentaryLog({ commentary }) {
  if (!commentary.enabled) return null;
  const { log } = commentary;
  return (
    <section className="rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]" aria-label="Commentary log">
      <div className="flex items-center justify-between border-b border-[var(--border)] px-4 py-3">
        <h2 className="text-sm font-bold tracking-tight text-[var(--ink)]">Commentary</h2>
        <span className="text-xs font-semibold text-[var(--muted)]">last {COMMENTARY.logMax} lines, newest first</span>
      </div>
      {log.length ? (
        <ol className="scrollbar-thin max-h-60 divide-y divide-[var(--border)] overflow-y-auto px-2" role="log" aria-live="off">
          {log.map((l) => (
            <li key={l.key} className="flex items-center gap-2.5 px-2 py-2">
              <span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${TONE[l.tone] || TONE.muted}`} aria-hidden>
                <Icon name={l.icon} className="h-3.5 w-3.5" strokeWidth={2.2} />
              </span>
              <span className="min-w-0 flex-1 text-sm font-medium leading-snug text-[var(--ink-secondary)]">{l.text}</span>
              <time dateTime={new Date(l.shownAt).toISOString()} className="shrink-0 text-xs font-semibold tabular-nums text-[var(--muted)]">
                {formatClock(new Date(l.shownAt).toISOString())}
              </time>
            </li>
          ))}
        </ol>
      ) : (
        <p className="px-4 py-6 text-center text-sm font-medium text-[var(--muted)]">Lines appear here as the fight moves.</p>
      )}
    </section>
  );
});

/** The viewer's two choices. The voice is the browser's own (SpeechSynthesis), off until it is switched on. */
export function CommentaryControls() {
  const prefs = useCommentaryPrefs();
  const canSpeak = speechSupported();
  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Commentary settings">
      <Button icon="captions" onClick={() => commentaryPrefs.setHidden(!prefs.hidden)} aria-pressed={!prefs.hidden} title={prefs.hidden ? "Show the live commentary" : "Hide the live commentary"}>
        Commentary: {prefs.hidden ? "off" : "on"}
      </Button>
      <Button
        icon={prefs.speech && canSpeak ? "volume" : "volumeOff"}
        onClick={() => commentaryPrefs.setSpeech(!prefs.speech)}
        aria-pressed={prefs.speech && canSpeak}
        disabled={!canSpeak || prefs.hidden}
        title={canSpeak ? "Read the commentary of the first fight aloud, with this browser's own voice" : "This browser has no speech synthesis"}
      >
        Spoken commentary: {prefs.speech && canSpeak ? "on" : "off"}
      </Button>
    </div>
  );
}

/** Over the broadcast video (/watch): the commentary of the fight the video shows, at the top of the frame. */
export function CommentaryOverlay({ id }) {
  const entry = useLiveSession(id);
  const commentary = useStageCommentary(id, entry, { speak: true });
  if (!entry || entry.session.status !== "live") return null;
  return (
    <div className="pointer-events-none absolute inset-x-3 top-12 z-10 sm:inset-x-5">
      <CaptionBar commentary={commentary} variant="overlay" />
    </div>
  );
}
