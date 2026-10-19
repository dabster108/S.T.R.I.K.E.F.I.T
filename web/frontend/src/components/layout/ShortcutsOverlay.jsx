import { useEffect, useRef } from "react";
import Icon from "../ui/Icon.jsx";

// "?" opens this list of the keyboard shortcuts the dashboard really has. Closes on Escape, on a click outside, or on Close;
// focus goes to the Close button and returns to where it was. It never opens while you are typing in a field.
const KEYS = [
  [["Ctrl", "K"], "Jump to any page or player (Cmd + K on a Mac)"],
  [["G", "letter"], "Go to a page: O Overview, L Live Arena, P Players, S Sessions, A Analytics, T Thesis Lab, E Export"],
  [["F"], "On the Live Arena: full screen for projecting (Esc leaves it)"],
  [["?"], "Show this list"],
  [["Esc"], "Close a menu, a dialog, the tour or this list"],
  [["←", "→"], "Move between the options of a range or filter picker"],
  [["↑", "↓", "Enter"], "Choose in the Ctrl + K search"],
  [["Tab"], "Move to the next control; Shift + Tab goes back"],
];

export function isTypingTarget(el) {
  if (!el) return false;
  const tag = el.tagName;
  return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
}

export default function ShortcutsOverlay({ open, onClose }) {
  const closeRef = useRef(null);
  const back = useRef(null);
  useEffect(() => {
    if (!open) return undefined;
    back.current = document.activeElement;
    closeRef.current?.focus();
    const onKey = (e) => {
      if (e.key === "Escape") { e.preventDefault(); onClose(); }
      else if (e.key === "Tab") { e.preventDefault(); closeRef.current?.focus(); }   // the only control inside
    };
    window.addEventListener("keydown", onKey, true);
    return () => {
      window.removeEventListener("keydown", onKey, true);
      back.current?.focus?.();
    };
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-4" role="presentation">
      <button type="button" aria-label="Close keyboard shortcuts" tabIndex={-1} className="fade-in absolute inset-0 bg-[var(--blue-900)]/40" onClick={onClose} />
      <div role="dialog" aria-modal="true" aria-labelledby="shortcuts-title" className="pop-in relative w-full max-w-md rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-6 shadow-[var(--shadow-lg)]">
        <div className="flex items-start justify-between gap-4">
          <h2 id="shortcuts-title" className="text-lg font-extrabold tracking-tight text-[var(--ink)]">
            Keyboard shortcuts
          </h2>
          <button ref={closeRef} type="button" onClick={onClose} aria-label="Close" className="press -mr-1 -mt-1 rounded-lg p-1.5 text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]">
            <Icon name="x" className="h-4 w-4" />
          </button>
        </div>
        <dl className="mt-4 divide-y divide-[var(--border)]">
          {KEYS.map(([keys, text]) => (
            <div key={text} className="flex items-center justify-between gap-4 py-2.5">
              <dt className="flex shrink-0 items-center gap-1">
                {keys.map((k) => (
                  <kbd key={k} className="min-w-6 rounded border border-[var(--border)] bg-[var(--surface-muted)] px-1.5 py-0.5 text-center font-sans text-xs font-bold text-[var(--ink)]">
                    {k}
                  </kbd>
                ))}
              </dt>
              <dd className="text-right text-sm font-medium text-[var(--ink-secondary)]">{text}</dd>
            </div>
          ))}
        </dl>
      </div>
    </div>
  );
}
