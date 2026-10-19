import { useEffect, useRef } from "react";
import Button from "./Button.jsx";
import Icon from "./Icon.jsx";

// Small illustration: a soft blue disc, a slowly orbiting dashed ring, and the icon on a card.
function EmptyArt({ icon, tone = "accent" }) {
  const bad = tone === "bad";
  return (
    <div className="relative h-20 w-20" aria-hidden>
      <svg viewBox="0 0 80 80" className="absolute inset-0 h-full w-full">
        <circle cx="40" cy="40" r="38" fill={bad ? "var(--bad-soft)" : "var(--accent-soft)"} />
        <g className="empty-orbit">
          <circle cx="40" cy="40" r="30" fill="none" stroke={bad ? "#fecaca" : "var(--accent-ring)"} strokeWidth="1.5" strokeDasharray="3 5" />
          <circle cx="70" cy="40" r="3.2" fill={bad ? "#fca5a5" : "var(--blue-300)"} />
          <circle cx="16" cy="22" r="2" fill={bad ? "#fecaca" : "var(--accent-ring)"} />
        </g>
      </svg>
      <span
        className={`absolute inset-0 m-auto flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--surface)] shadow-[var(--shadow-lift)] ring-1 ring-[var(--border)] ${bad ? "text-[var(--bad)]" : "text-[var(--accent)]"}`}
      >
        <Icon name={icon} className="h-5 w-5" />
      </span>
    </div>
  );
}

export function EmptyState({ icon = "headset", title, description, action, tone }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-12 text-center">
      <EmptyArt icon={icon} tone={tone || (icon === "xCircle" ? "bad" : "accent")} />
      <p className="mt-4 text-[0.95rem] font-bold tracking-tight text-[var(--ink)]">{title}</p>
      {description && <p className="mt-1 max-w-sm text-sm font-medium leading-relaxed text-[var(--muted)]">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}

export function Skeleton({ className = "h-4 w-full" }) {
  return <div className={`skeleton rounded-lg ${className}`} aria-hidden />;
}

export function Banner({ tone = "bad", title, children, action }) {
  const cls =
    tone === "bad"
      ? "border-red-200 bg-[var(--bad-soft)] text-[var(--bad)]"
      : tone === "warn"
        ? "border-amber-200 bg-[var(--warn-soft)] text-amber-700"
        : "border-blue-200 bg-[var(--accent-soft)] text-[var(--accent-strong)]";
  return (
    <div className={`flash-in flex flex-wrap items-start gap-3 rounded-2xl border px-4 py-3 text-sm ${cls}`} role={tone === "bad" ? "alert" : "status"}>
      <Icon name={tone === "bad" ? "xCircle" : tone === "warn" ? "info" : "check"} className="mt-0.5 h-4 w-4 shrink-0" />
      <div className="min-w-0 flex-1">
        <p className="font-bold">{title}</p>
        {children && <div className="mt-0.5 text-xs font-medium text-[var(--ink-secondary)]">{children}</div>}
      </div>
      {action}
    </div>
  );
}

export function ConfirmDialog({ open, title, description, confirmLabel = "Confirm", onConfirm, onCancel, busy }) {
  const cancelRef = useRef(null);
  const boxRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    const prev = document.activeElement;
    cancelRef.current?.focus();
    const onKey = (e) => {
      if (e.key === "Escape") onCancel();
      if (e.key === "Tab") {
        const f = boxRef.current?.querySelectorAll("button:not([disabled])");
        if (!f?.length) return;
        const [first, last] = [f[0], f[f.length - 1]];
        if (e.shiftKey && document.activeElement === first) (e.preventDefault(), last.focus());
        else if (!e.shiftKey && document.activeElement === last) (e.preventDefault(), first.focus());
      }
    };
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      prev?.focus?.();
    };
  }, [open, onCancel]);

  if (!open) return null;
  return (
    <div
      className="fade-in fixed inset-0 z-50 flex items-center justify-center bg-gray-900/30 p-4 backdrop-blur-[2px]"
      onClick={(e) => {
        e.stopPropagation();
        onCancel();
      }}
    >
      <div
        ref={boxRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        className="pop-in w-full max-w-sm rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <span className="flex h-10 w-10 items-center justify-center rounded-full bg-[var(--bad-soft)] text-[var(--bad)]">
          <Icon name="trash" className="h-5 w-5" />
        </span>
        <h2 id="confirm-title" className="mt-4 text-base font-bold text-[var(--ink)]">
          {title}
        </h2>
        {description && <p className="mt-1.5 text-sm font-medium text-[var(--muted)]">{description}</p>}
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
      {/* scaleX, not width: only transform animates (the grow-x entrance, then any later change) */}
      <div className="h-full origin-left transition-transform duration-500" style={{ transform: `scaleX(${pct / 100})` }}>
        <div className={`grow-x h-full w-full rounded-full ${bar}`} />
      </div>
    </div>
  );
}
