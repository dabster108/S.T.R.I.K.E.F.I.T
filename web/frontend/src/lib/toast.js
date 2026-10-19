// One toast store for the whole dashboard, so a copy button or an export deep in a page can say what happened without every
// parent passing `push` down. <ToastStack> (components/ui/Toasts.jsx) renders it; useToasts() reads it.
let items = [];
const listeners = new Set();
const timers = new Map();
const emit = () => listeners.forEach((l) => l());

export const MAX_TOASTS = 4;

export function dismiss(id) {
  clearTimeout(timers.get(id));
  timers.delete(id);
  items = items.filter((t) => t.id !== id);
  emit();
}

export function push({ tone = "info", title, detail, timeout = 4500 }) {
  // The same message twice in a row replaces itself instead of stacking (a double-clicked Copy shows one toast).
  const dup = items.find((t) => t.tone === tone && t.title === title && t.detail === detail);
  if (dup) dismiss(dup.id);
  const id = `${Date.now()}-${Math.random().toString(36).slice(2, 6)}`;
  items = [...items.slice(-(MAX_TOASTS - 1)), { id, tone, title, detail }];
  if (timeout) timers.set(id, setTimeout(() => dismiss(id), timeout));
  emit();
  return id;
}

export const toast = {
  push,
  dismiss,
  good: (title, detail) => push({ tone: "good", title, detail }),
  bad: (title, detail) => push({ tone: "bad", title, detail, timeout: 7000 }),
  info: (title, detail) => push({ tone: "info", title, detail }),
};

export const subscribe = (l) => {
  listeners.add(l);
  return () => listeners.delete(l);
};
export const getToasts = () => items;
export const clearToasts = () => {
  for (const t of timers.values()) clearTimeout(t);
  timers.clear();
  items = [];
  emit();
};

// Copies text and says so. Falls back to a hidden textarea where the async clipboard API is missing or refused (http, old Safari).
export async function copyText(text, what = "Link") {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    try {
      const ta = Object.assign(document.createElement("textarea"), { value: text });
      ta.setAttribute("readonly", "");
      ta.style.cssText = "position:fixed;opacity:0;pointer-events:none";
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand("copy");
      ta.remove();
      if (!ok) throw new Error("copy refused");
    } catch {
      toast.bad(`Couldn't copy the ${what.toLowerCase()}`, "Select it and copy by hand.");
      return false;
    }
  }
  toast.good(`${what} copied`);
  return true;
}
