import { useEffect, useState } from "react";
import { api } from "../../lib/api.js";
import { formatDateTime, formatDuration, formatNumber } from "../../lib/format.js";
import { LevelBadge, OutcomeBadge, Pill } from "../ui/Badge.jsx";
import Button from "../ui/Button.jsx";
import { ConfirmDialog, EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";
import EventList from "./EventList.jsx";

function Metric({ label, value, icon }) {
  return (
    <div className="rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2.5">
      <p className="flex items-center gap-1.5 text-[0.68rem] font-medium text-[var(--muted)]">
        <Icon name={icon} className="h-3.5 w-3.5" />
        {label}
      </p>
      <p className="mt-1 text-lg font-bold tabular-nums text-[var(--ink)]">{value}</p>
    </div>
  );
}

// Slide-over with one session's full detail + its event timeline (GET /api/sessions/:id).
export default function SessionDrawer({ sessionId, fallback, onClose, onDeleted }) {
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState(null);
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setDetail(null);
    setError(null);
    api
      .session(sessionId)
      .then((d) => !cancelled && setDetail(d))
      .catch((e) => !cancelled && setError(e.status === 404 ? "This session no longer exists." : e.message));
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  useEffect(() => {
    const onKey = (e) => e.key === "Escape" && !confirm && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, confirm]);

  const s = detail?.session || fallback;

  async function remove() {
    setBusy(true);
    try {
      await api.deleteSession(sessionId);
      onDeleted?.(s);
      onClose();
    } catch (e) {
      setError(e.message);
      setConfirm(false);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fade-in fixed inset-0 z-40 bg-gray-900/25" onClick={onClose}>
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Session details"
        className="drawer-in scrollbar-thin ml-auto flex h-full w-full max-w-lg flex-col overflow-y-auto border-l border-[var(--border)] bg-[var(--surface)] shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-[var(--border)] bg-[var(--surface)]/95 px-5 py-4 backdrop-blur">
          <div>
            <p className="text-xs font-medium text-[var(--muted)]">Session</p>
            <p className="font-mono text-xs text-[var(--ink-secondary)]">{sessionId.slice(0, 12)}…</p>
          </div>
          <button type="button" onClick={onClose} aria-label="Close" className="rounded-lg p-2 text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]">
            <Icon name="x" className="h-5 w-5" />
          </button>
        </div>

        {!s && !error && (
          <div className="space-y-3 p-5">
            <Skeleton className="h-8 w-40" />
            <Skeleton className="h-24 w-full" />
            <Skeleton className="h-40 w-full" />
          </div>
        )}

        {error && !s && <EmptyState icon="xCircle" title="Couldn't load session" description={error} />}

        {s && (
          <div className="flex-1 space-y-6 p-5">
            <div>
              <div className="flex flex-wrap items-center gap-2">
                <OutcomeBadge outcome={s.outcome} />
                <LevelBadge level={s.level} />
                <Pill>{s.source}</Pill>
              </div>
              <h2 className="mt-3 text-xl font-bold text-[var(--ink)]">{s.playerName}</h2>
              <p className="text-sm text-[var(--muted)]">{formatDateTime(s.endedAt || s.startedAt)}</p>
            </div>

            <div className="rounded-2xl bg-[var(--accent-soft)] px-5 py-4">
              <p className="text-xs font-medium text-[var(--accent)]">Score</p>
              <p className="text-4xl font-bold tracking-tight text-[var(--ink)]">{formatNumber(s.score)}</p>
            </div>

            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              <Metric label="Hits" value={formatNumber(s.hitCount)} icon="bolt" />
              <Metric label="Damage" value={formatNumber(s.damageDealt)} icon="flame" />
              <Metric label="Taps" value={formatNumber(s.tapCount)} icon="hand" />
              <Metric label="Duration" value={formatDuration(s.durationSec)} icon="clock" />
              <Metric label="Player HP left" value={formatNumber(s.playerHpRemaining)} icon="heart" />
              <Metric label="Monster HP left" value={formatNumber(s.monsterHpRemaining)} icon="target" />
            </div>

            {s.notes && (
              <div className="rounded-xl border border-[var(--border)] px-4 py-3 text-sm text-[var(--ink-secondary)]">
                <p className="mb-1 text-xs font-medium text-[var(--muted)]">Notes</p>
                {s.notes}
              </div>
            )}

            <div>
              <h3 className="text-sm font-semibold text-[var(--ink)]">Timeline</h3>
              <div className="mt-2 overflow-hidden rounded-xl border border-[var(--border)]">
                {!detail && !error && <Skeleton className="m-4 h-16" />}
                {detail && detail.events.length === 0 && (
                  <p className="px-4 py-6 text-center text-xs text-[var(--muted)]">No events were recorded for this session.</p>
                )}
                {detail && detail.events.length > 0 && <EventList events={detail.events} dense />}
              </div>
            </div>

            {error && s && <p className="text-xs text-[var(--bad)]">{error}</p>}
          </div>
        )}

        {s && (
          <div className="sticky bottom-0 flex justify-end border-t border-[var(--border)] bg-[var(--surface)] px-5 py-3">
            <Button variant="danger" icon="trash" onClick={() => setConfirm(true)}>
              Delete session
            </Button>
          </div>
        )}
      </aside>

      <ConfirmDialog
        open={confirm}
        title="Delete this session?"
        description="It will be removed from db.json along with its events. This can't be undone."
        confirmLabel="Delete"
        busy={busy}
        onConfirm={remove}
        onCancel={() => setConfirm(false)}
      />
    </div>
  );
}
