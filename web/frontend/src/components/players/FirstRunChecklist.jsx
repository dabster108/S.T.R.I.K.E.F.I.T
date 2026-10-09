import { useState } from "react";
import { api } from "../../lib/api.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card } from "../ui/Card.jsx";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

// First-run checklist (GET /api/players/:name/checklist). Every tick comes from the database; only "dismissed" is kept,
// per player, in this browser's localStorage.
const keyFor = (name) => `strikefit.checklist.dismissed.${name}`;
const isDismissed = (name) => {
  try {
    return localStorage.getItem(keyFor(name)) === "1";
  } catch {
    return false;
  }
};

export default function FirstRunChecklist({ name }) {
  const [dismissed, setDismissed] = useState(() => isDismissed(name));
  const { data, reload } = useApi(() => (dismissed ? Promise.resolve(null) : api.playerChecklist(name)), [name, dismissed]);
  const refresh = useThrottledCallback(reload, 4000);
  useLiveMessages((type, payload) => {
    if (dismissed) return;
    if ((type === "session_finished" || type === "feedback_saved") && (payload?.session?.playerName === name || payload?.playerName === name)) refresh();
  });

  if (dismissed || !data || data.playerName !== name) return null;
  const dismiss = () => {
    try {
      localStorage.setItem(keyFor(name), "1");
    } catch {
      /* private mode: hidden for this visit only */
    }
    setDismissed(true);
  };
  const all = data.done === data.total;
  return (
    <Card className="mt-6 overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 py-4">
        <div className="min-w-0">
          <p className="text-[0.95rem] font-bold tracking-tight text-[var(--ink)]">{all ? "All first steps done" : "First steps"}</p>
          <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">
            {data.done} of {data.total} done · checked from this player's sessions, ratings and trophies
          </p>
        </div>
        <Button variant="ghost" icon="x" onClick={dismiss} aria-label="Dismiss the first-steps checklist">
          Dismiss
        </Button>
      </div>
      <ol className="grid gap-px border-t border-[var(--border)] bg-[var(--border)] sm:grid-cols-2 lg:grid-cols-4">
        {data.steps.map((s) => (
          <li key={s.id} className="flex items-center gap-3 bg-[var(--surface)] px-5 py-3">
            <span
              className={`inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-full ${s.done ? "bg-[var(--good-soft)] text-[var(--good-ink)]" : "border border-[var(--border-strong)] text-[var(--muted)]"}`}
              aria-hidden
            >
              {s.done && <Icon name="check" className="h-3.5 w-3.5" strokeWidth={2.6} />}
            </span>
            <span className="min-w-0">
              <span className={`block text-sm font-bold ${s.done ? "text-[var(--ink-secondary)]" : "text-[var(--ink)]"}`}>
                {s.label}
                <span className="sr-only">{s.done ? " (done)" : " (not yet)"}</span>
              </span>
              <span className="block truncate text-xs font-medium text-[var(--muted)]">{s.detail}</span>
            </span>
          </li>
        ))}
      </ol>
    </Card>
  );
}
