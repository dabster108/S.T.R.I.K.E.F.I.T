import { useCallback, useState } from "react";
import Icon from "./Icon.jsx";

const TONES = {
  good: { icon: "check", cls: "text-[var(--good-ink)]" },
  bad: { icon: "xCircle", cls: "text-[var(--bad)]" },
  info: { icon: "signal", cls: "text-[var(--accent)]" },
};

export function useToasts() {
  const [toasts, setToasts] = useState([]);

  const dismiss = useCallback((id) => setToasts((t) => t.filter((x) => x.id !== id)), []);

  const push = useCallback(
    ({ tone = "info", title, detail, timeout = 4500 }) => {
      const id = `${Date.now()}-${Math.random().toString(36).slice(2, 6)}`;
      setToasts((t) => [...t.slice(-3), { id, tone, title, detail }]);
      if (timeout) setTimeout(() => dismiss(id), timeout);
    },
    [dismiss]
  );

  return { toasts, push, dismiss };
}

export function ToastStack({ toasts, onDismiss }) {
  return (
    <div className="pointer-events-none fixed bottom-4 right-4 z-[60] flex w-[min(22rem,calc(100vw-2rem))] flex-col gap-2" aria-live="polite">
      {toasts.map((t) => {
        const tone = TONES[t.tone] || TONES.info;
        return (
          <div
            key={t.id}
            className="pop-in pointer-events-auto flex items-start gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-lg"
          >
            <Icon name={tone.icon} className={`mt-0.5 h-4 w-4 shrink-0 ${tone.cls}`} />
            <div className="min-w-0 flex-1">
              <p className="text-sm font-bold text-[var(--ink)]">{t.title}</p>
              {t.detail && <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">{t.detail}</p>}
            </div>
            <button
              type="button"
              onClick={() => onDismiss(t.id)}
              aria-label="Dismiss notification"
              className="press rounded-md p-0.5 text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
            >
              <Icon name="x" className="h-3.5 w-3.5" />
            </button>
          </div>
        );
      })}
    </div>
  );
}
