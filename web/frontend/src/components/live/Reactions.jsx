import { memo, useEffect, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { useReducedMotion } from "../../hooks/motion.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import Icon from "../ui/Icon.jsx";

// Audience cheers for a live fight. Viewers tap one of three line icons; the server validates, rate limits and
// broadcasts a "reaction" on the stream (nothing is stored). Every viewer sees floating icons and a counter of the cheers
// that arrived while their page was open. Under reduced motion / calm mode only the counter changes. The player in the
// headset does not see them yet.

export const KINDS = [
  { id: "fire", icon: "flame", label: "Fire" },
  { id: "power", icon: "bolt", label: "Power" },
  { id: "respect", icon: "thumbsUp", label: "Respect" },
];
const ICON = Object.fromEntries(KINDS.map((k) => [k.id, k.icon]));
const MAX_FLOATS = 8;
const FLOAT_MS = 1600;

// One status request per page load: hides the buttons when REACTIONS_ENABLED is off on the server.
let statusPromise = null;
function useReactionsEnabled() {
  const [enabled, setEnabled] = useState(null);
  useEffect(() => {
    if (!statusPromise) statusPromise = api.reactionsStatus().then((d) => Boolean(d?.enabled)).catch(() => false);
    let off = false;
    statusPromise.then((v) => !off && setEnabled(v));
    return () => {
      off = true;
    };
  }, []);
  return enabled;
}

// Floating icons + counter for one session. tone: "card" (white card) or "overlay" (on the video).
export const ReactionLayer = memo(function ReactionLayer({ sessionId, tone = "card" }) {
  const reduced = useReducedMotion();
  const [count, setCount] = useState(0);
  const [floats, setFloats] = useState([]);
  const timers = useRef([]);
  useEffect(() => () => timers.current.forEach(clearTimeout), []);
  useLiveMessages((type, p) => {
    if (type !== "reaction" || p?.sessionId !== sessionId || !ICON[p.kind]) return;
    setCount((c) => c + 1);
    if (reduced) return;
    const key = `${Date.now()}-${Math.random()}`;
    const x = 8 + Math.round(Math.random() * 84);
    setFloats((list) => [...list.slice(-(MAX_FLOATS - 1)), { key, kind: p.kind, x }]);
    timers.current.push(setTimeout(() => setFloats((list) => list.filter((f) => f.key !== key)), FLOAT_MS));
  });
  if (!count) return null;
  const overlay = tone === "overlay";
  return (
    <>
      <div className="pointer-events-none absolute inset-0 z-[2] overflow-hidden" aria-hidden>
        {floats.map((f) => (
          <span key={f.key} className={`reaction-float absolute bottom-6 ${overlay ? "text-white" : "text-[var(--accent)]"}`} style={{ left: `${f.x}%` }}>
            <Icon name={ICON[f.kind]} className="h-6 w-6" strokeWidth={2.2} />
          </span>
        ))}
      </div>
      <span
        className={`pointer-events-none absolute z-[3] inline-flex ${overlay ? "right-3 top-3" : "right-5 top-[3.75rem]"} items-center gap-1 rounded-full px-2.5 py-1 text-xs font-extrabold tabular-nums ${overlay ? "bg-[var(--blue-900)]/80 text-white" : "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]"}`}
        role="status"
        aria-label={`${count} cheer${count === 1 ? "" : "s"} from viewers`}
      >
        <Icon name="thumbsUp" className="h-3.5 w-3.5" /> {count}
      </span>
    </>
  );
});

export function ReactionBar({ sessionId, live = true, className = "" }) {
  const enabled = useReactionsEnabled();
  const [note, setNote] = useState(null);
  const [busy, setBusy] = useState(false);
  const noteTimer = useRef(null);
  useEffect(() => () => clearTimeout(noteTimer.current), []);
  if (!enabled || !live) return null;
  const say = (text) => {
    setNote(text);
    clearTimeout(noteTimer.current);
    noteTimer.current = setTimeout(() => setNote(null), 2500);
  };
  const send = async (kind) => {
    if (busy) return;
    setBusy(true);
    try {
      await api.react(sessionId, kind);
    } catch (err) {
      say(err.status === 429 ? "One cheer every 2 seconds." : err.status === 409 ? "This fight has finished." : "Cheer not sent.");
    } finally {
      setTimeout(() => setBusy(false), 300);
    }
  };
  return (
    <div className={`flex flex-wrap items-center gap-2 ${className}`}>
      <span className="text-xs font-semibold text-[var(--muted)]">Cheer</span>
      {KINDS.map((k) => (
        <button
          key={k.id}
          type="button"
          onClick={() => send(k.id)}
          disabled={busy}
          aria-label={`Cheer: ${k.label}`}
          title={k.label}
          className="press flex h-9 w-9 items-center justify-center rounded-full border border-[var(--border)] bg-[var(--surface)] text-[var(--accent-strong)] shadow-sm hover:border-[var(--accent-ring)] hover:bg-[var(--accent-soft)] disabled:opacity-60"
        >
          <Icon name={k.icon} className="h-4 w-4" />
        </button>
      ))}
      <span className="min-w-0 text-xs font-semibold text-[var(--muted)]" role="status" aria-live="polite">
        {note}
      </span>
    </div>
  );
}
