import { useEffect, useRef } from "react";
import Button from "./Button.jsx";
import Icon from "./Icon.jsx";

export function EmptyState({ icon = "headset", title, description, action }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-12 text-center">
      <span className="flex h-11 w-11 items-center justify-center rounded-full bg-[var(--accent-soft)] text-[var(--accent)]">
        <Icon name={icon} className="h-5 w-5" />
      </span>
      <p className="mt-3 text-sm font-semibold text-[var(--ink)]">{title}</p>
      {description && <p className="mt-1 max-w-sm text-xs leading-relaxed text-[var(--muted)]">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

export function Skeleton({ className = "h-4 w-full" }) {
  return <div className={`skeleton ${className}`} aria-hidden />;
}

export function DashboardSkeleton() {
  return (
    <div className="space-y-6" aria-busy="true" aria-label="Loading telemetry">
      <Skeleton className="h-10 w-72" />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-32 w-full rounded-2xl" />
        ))}
      </div>
      <Skeleton className="h-80 w-full rounded-2xl" />
    </div>
  );
}

export function Banner({ tone = "bad", title, children, action }) {
  const cls =
    tone === "bad"
      ? "border-red-200 bg-[var(--bad-soft)] text-[var(--bad)]"
      : tone === "warn"
        ? "border-amber-200 bg-[var(--warn-soft)] text-[var(--warn)]"
        : "border-blue-200 bg-[var(--accent-soft)] text-[var(--accent)]";
  return (
    <div className={`flash-in flex flex-wrap items-start gap-3 rounded-2xl border px-4 py-3 text-sm ${cls}`} role="alert">
      <Icon name={tone === "bad" ? "xCircle" : "signal"} className="mt-0.5 h-4 w-4 shrink-0" />
      <div className="min-w-0 flex-1">
        <p className="font-semibold">{title}</p>
        {children && <div className="mt-0.5 text-xs text-[var(--ink-secondary)]">{children}</div>}
      </div>
      {action}
    </div>
  );
}

export function ConfirmDialog({ open, title, description, confirmLabel = "Confirm", onConfirm, onCancel, busy }) {
  const cancelRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    cancelRef.current?.focus();
    const onKey = (e) => e.key === "Escape" && onCancel();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onCancel]);

  if (!open) return null;
  return (
    <div className="fade-in fixed inset-0 z-50 flex items-center justify-center bg-gray-900/30 p-4 backdrop-blur-[2px]"
      onClick={(e) => {
        e.stopPropagation();
        onCancel();
      }}
    >
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        className="flash-in w-full max-w-sm rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <span className="flex h-10 w-10 items-center justify-center rounded-full bg-[var(--bad-soft)] text-[var(--bad)]">
          <Icon name="trash" className="h-5 w-5" />
        </span>
        <h2 id="confirm-title" className="mt-4 text-base font-semibold text-[var(--ink)]">
          {title}
        </h2>
        {description && <p className="mt-1.5 text-sm text-[var(--muted)]">{description}</p>}
        <div className="mt-6 flex justify-end gap-2">
          <Button ref={cancelRef} size="md" onClick={onCancel}>
            Cancel
          </Button>
          <Button size="md" variant="dangerSolid" onClick={onConfirm} disabled={busy}>
            {busy ? "Working…" : confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}

export function ProgressBar({ value, max = 100, tone = "accent", label }) {
  const pct = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0;
  const bar = tone === "good" ? "bg-[var(--good)]" : tone === "bad" ? "bg-[var(--bad)]" : "bg-[var(--accent)]";
  return (
    <div
      className="h-2 w-full overflow-hidden rounded-full bg-gray-100"
      role="progressbar"
      aria-valuenow={Math.round(pct)}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label={label}
    >
      <div className={`h-full rounded-full ${bar} transition-[width] duration-500`} style={{ width: `${pct}%` }} />
    </div>
  );
}
