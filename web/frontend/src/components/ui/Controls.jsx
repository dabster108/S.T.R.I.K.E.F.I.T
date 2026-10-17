import Icon from "./Icon.jsx";

// The react-select based Select lives in Select.jsx: this file is imported by the landing page, which must not pull react-select
// (and its emotion / floating-ui dependencies, about a third of the bundle) into the first load.
const field =
  "rounded-lg border border-[var(--border)] bg-[var(--surface)] text-xs font-semibold text-[var(--ink)] shadow-sm transition-colors hover:border-[var(--border-strong)] focus:border-[var(--accent)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-ring)]";

// Pill-style single choice (range pickers, outcome filters). Arrow keys move between options.
export function SegmentedControl({ options, value, onChange, label }) {
  const onKey = (e) => {
    if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
    e.preventDefault();
    const i = options.findIndex((o) => o.id === value);
    const next = options[(i + (e.key === "ArrowRight" ? 1 : options.length - 1)) % options.length];
    onChange(next.id);
    e.currentTarget.querySelector(`[data-id="${CSS.escape(String(next.id))}"]`)?.focus();
  };
  return (
    <div role="radiogroup" aria-label={label} onKeyDown={onKey} className="inline-flex rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] p-0.5">
      {options.map((o) => {
        const active = o.id === value;
        return (
          <button
            key={o.id}
            data-id={o.id}
            type="button"
            role="radio"
            aria-checked={active}
            tabIndex={active ? 0 : -1}
            onClick={() => onChange(o.id)}
            className={`press rounded-md px-2.5 py-1 text-xs font-semibold ${
              active ? "bg-[var(--surface)] text-[var(--ink)] shadow-sm ring-1 ring-[var(--border)]" : "text-[var(--muted)] hover:bg-[var(--surface)]/60 hover:text-[var(--ink)]"
            }`}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}

export function SearchInput({ value, onChange, placeholder = "Search…", label = "Search" }) {
  return (
    <label className="relative inline-flex items-center">
      <span className="sr-only">{label}</span>
      <Icon name="search" className="pointer-events-none absolute left-2.5 h-3.5 w-3.5 text-[var(--muted)]" />
      <input
        type="search"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className={`w-44 py-1.5 pl-8 pr-2.5 placeholder:font-medium placeholder:text-[var(--muted)] sm:w-56 ${field}`}
      />
    </label>
  );
}

export const inputClass = `px-2.5 py-1.5 ${field}`;

export function Pagination({ page, pageCount, onPage, total, pageSize }) {
  if (pageCount <= 1) return null;
  const from = page * pageSize + 1;
  const to = Math.min(total, (page + 1) * pageSize);
  const btn = "press rounded-md p-1.5 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)] disabled:opacity-40 disabled:hover:bg-transparent";
  return (
    <div className="flex items-center justify-between gap-3 border-t border-[var(--border)] px-5 py-3 text-xs font-semibold text-[var(--muted)]">
      <span className="tabular-nums">
        {from}–{to} of {total}
      </span>
      <div className="flex items-center gap-1">
        <button type="button" onClick={() => onPage(page - 1)} disabled={page === 0} aria-label="Previous page" className={btn}>
          <Icon name="chevronLeft" className="h-4 w-4" />
        </button>
        <span className="px-1 tabular-nums text-[var(--ink-secondary)]">
          {page + 1} / {pageCount}
        </span>
        <button type="button" onClick={() => onPage(page + 1)} disabled={page >= pageCount - 1} aria-label="Next page" className={btn}>
          <Icon name="chevronRight" className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}
