import { useState } from "react";
import { api } from "../../lib/api.js";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

// Perceived exertion uses the Borg RPE scale, 6 to 20.
// Borg GA. Psychophysical bases of perceived exertion. Medicine & Science in Sports & Exercise.
// 1982;14(5):377-381.  (Written from memory: verify volume, issue and pages before citing it.)
// Only the odd numbers carry a standard verbal anchor, plus 6 and 20.
export const BORG_ANCHORS = [
  { value: 6, label: "No exertion" },
  { value: 9, label: "Very light" },
  { value: 11, label: "Light" },
  { value: 13, label: "Somewhat hard" },
  { value: 15, label: "Hard" },
  { value: 17, label: "Very hard" },
  { value: 19, label: "Extremely hard" },
  { value: 20, label: "Maximal" },
];
const BORG_WORDS = { 6: "No exertion", 7: "Extremely light", 9: "Very light", 11: "Light", 13: "Somewhat hard", 15: "Hard", 17: "Very hard", 19: "Extremely hard", 20: "Maximal exertion" };
export const borgWord = (v) => BORG_WORDS[v] || (v ? `Between ${BORG_WORDS[v - 1] || BORG_WORDS[v - 2]} and ${BORG_WORDS[v + 1] || BORG_WORDS[v + 2]}`.toLowerCase().replace(/^b/, "B") : "");

const ENJOYMENT = [
  { value: 1, label: "1" },
  { value: 2, label: "2" },
  { value: 3, label: "3" },
  { value: 4, label: "4" },
  { value: 5, label: "5" },
];

const choice = (on) =>
  `press flex min-h-12 items-center justify-center rounded-xl border px-3 text-sm font-bold ${
    on
      ? "border-transparent bg-[var(--accent)] text-white shadow-sm"
      : "border-[var(--border)] bg-[var(--surface)] text-[var(--ink-secondary)] hover:border-[var(--border-strong)] hover:bg-[var(--surface-muted)]"
  }`;

// The two questions plus comment. onSaved(feedback) after a successful save; onSkip is optional.
export default function RatingForm({ sessionId, onSaved, onSkip }) {
  const [enjoyment, setEnjoyment] = useState(null);
  const [rpe, setRpe] = useState(null);
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const submit = async (e) => {
    e.preventDefault();
    if (busy || (enjoyment === null && rpe === null)) return;
    setBusy(true);
    setError(null);
    try {
      const res = await api.submitFeedback(sessionId, { enjoyment, rpe, comment: comment.trim() || undefined });
      onSaved?.(res.feedback);
    } catch (err) {
      setError(err.status === 409 ? err.message : err.status === 429 ? err.message : err.status === 0 ? "Couldn't reach the server. Check your connection and try again." : err.message || "Something went wrong. Try again.");
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-7">
      <fieldset>
        <legend className="text-sm font-bold text-[var(--ink)]">How much did you enjoy it?</legend>
        <div className="mt-3 grid grid-cols-5 gap-2" role="radiogroup" aria-label="Enjoyment, 1 to 5">
          {ENJOYMENT.map((o) => (
            <button key={o.value} type="button" role="radio" aria-checked={enjoyment === o.value} onClick={() => setEnjoyment(o.value)} className={choice(enjoyment === o.value)}>
              {o.label}
            </button>
          ))}
        </div>
        <p className="mt-2 flex justify-between text-xs font-semibold text-[var(--muted)]">
          <span>1 · Not at all</span>
          <span>5 · Loved it</span>
        </p>
      </fieldset>

      <fieldset>
        <legend className="text-sm font-bold text-[var(--ink)]">How hard did it feel?</legend>
        <p className="mt-1 text-xs font-medium text-[var(--muted)]">Borg scale, 6 to 20. Pick the closest description, then fine-tune with − and +.</p>
        <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4" role="radiogroup" aria-label="Perceived exertion description">
          {BORG_ANCHORS.map((a) => (
            <button key={a.value} type="button" role="radio" aria-checked={rpe === a.value} onClick={() => setRpe(a.value)} className={`${choice(rpe === a.value)} flex-col gap-0.5 py-2`}>
              <span className="text-base font-extrabold tabular-nums">{a.value}</span>
              <span className="text-[0.7rem] font-semibold leading-tight opacity-90">{a.label}</span>
            </button>
          ))}
        </div>
        <div className="mt-3 flex items-center justify-between gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2" aria-live="polite">
          <button type="button" className={`${choice(false)} w-12 shrink-0 text-lg`} onClick={() => setRpe((v) => (v === null ? v : Math.max(6, v - 1)))} disabled={rpe === null || rpe <= 6} aria-label="One step lighter">
            −
          </button>
          <p className="min-w-0 text-center">
            <span className="block text-2xl font-extrabold tabular-nums text-[var(--ink)]">{rpe ?? "—"}</span>
            <span className="block truncate text-xs font-semibold text-[var(--muted)]">{rpe === null ? "Not set" : borgWord(rpe)}</span>
          </p>
          <button type="button" className={`${choice(false)} w-12 shrink-0 text-lg`} onClick={() => setRpe((v) => (v === null ? v : Math.min(20, v + 1)))} disabled={rpe === null || rpe >= 20} aria-label="One step harder">
            +
          </button>
        </div>
      </fieldset>

      <div>
        <label htmlFor="fb-comment" className="text-sm font-bold text-[var(--ink)]">
          Anything to add? <span className="font-medium text-[var(--muted)]">(optional)</span>
        </label>
        <textarea
          id="fb-comment"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          maxLength={500}
          rows={3}
          className="mt-2 w-full rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm font-medium text-[var(--ink)] shadow-sm placeholder:text-[var(--muted)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-ring)]"
          placeholder="What made it fun, or tiring?"
        />
      </div>

      {error && (
        <p role="alert" className="flex items-start gap-2 rounded-xl border border-red-200 bg-[var(--bad-soft)] px-3 py-2.5 text-sm font-semibold text-[var(--bad)]">
          <Icon name="info" className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </p>
      )}

      <p className="text-xs font-medium text-[var(--muted)]">Your answers are stored with this session for research. There is no sign-in, so please only rate your own session.</p>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" variant="primary" size="lg" disabled={busy || (enjoyment === null && rpe === null)} className="min-h-12 flex-1 sm:flex-none">
          {busy ? "Saving…" : "Send rating"}
        </Button>
        {onSkip && (
          <Button variant="ghost" size="md" onClick={onSkip} disabled={busy}>
            Skip
          </Button>
        )}
      </div>
    </form>
  );
}

// Saved ratings, read-only.
export function RatingSummary({ feedback }) {
  return (
    <dl className="grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3">
      <div>
        <dt className="text-xs font-semibold text-[var(--muted)]">Enjoyment</dt>
        <dd className="mt-0.5 text-base font-extrabold tabular-nums text-[var(--ink)]">{feedback.enjoyment ?? "—"}{feedback.enjoyment ? <span className="text-sm font-semibold text-[var(--muted)]"> / 5</span> : null}</dd>
      </div>
      <div>
        <dt className="text-xs font-semibold text-[var(--muted)]">Perceived exertion (Borg)</dt>
        <dd className="mt-0.5 text-base font-extrabold tabular-nums text-[var(--ink)]">
          {feedback.rpe ?? "—"}
          {feedback.rpe ? <span className="text-sm font-semibold text-[var(--muted)]"> · {borgWord(feedback.rpe)}</span> : null}
        </dd>
      </div>
      {feedback.comment && (
        <div className="col-span-2 sm:col-span-3">
          <dt className="text-xs font-semibold text-[var(--muted)]">Comment</dt>
          <dd className="mt-0.5 text-sm font-medium text-[var(--ink-secondary)]">{feedback.comment}</dd>
        </div>
      )}
    </dl>
  );
}
