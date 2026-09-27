import Icon from "./Icon.jsx";

// Pill-style single choice (range pickers, outcome filters).
export function SegmentedControl({ options, value, onChange, label }) {
  return (
    <div role="radiogroup" aria-label={label} className="inline-flex rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] p-0.5">
      {options.map((o) => {
        const active = o.id === value;
        return (
          <button
            key={o.id}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(o.id)}
            className={`rounded-md px-2.5 py-1 text-xs font-medium transition-all ${
              active ? "bg-[var(--surface)] text-[var(--ink)] shadow-sm" : "text-[var(--muted)] hover:text-[var(--ink)]"
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
        className="w-44 rounded-lg border border-[var(--border)] bg-[var(--surface)] py-1.5 pl-8 pr-2.5 text-xs text-[var(--ink)] shadow-sm placeholder:text-[var(--muted)] focus:border-[var(--accent-ring)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-soft)] sm:w-56"
      />
    </label>
  );
}

export function Select({ value, onChange, options, label }) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      aria-label={label}
      className="rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 text-xs text-[var(--ink)] shadow-sm focus:border-[var(--accent-ring)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-soft)]"
    >
      {options.map((o) => (
        <option key={o.id} value={o.id}>
          {o.label}
        </option>
      ))}
    </select>
  );
}

export function Pagination({ page, pageCount, onPage, total, pageSize }) {
  if (pageCount <= 1) return null;
  const from = page * pageSize + 1;
  const to = Math.min(total, (page + 1) * pageSize);
  return (
    <div className="flex items-center justify-between gap-3 border-t border-[var(--border)] px-5 py-3 text-xs text-[var(--muted)]">
      <span className="tabular-nums">
        {from}–{to} of {total}
      </span>
      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={() => onPage(page - 1)}
          disabled={page === 0}
          aria-label="Previous page"
          className="rounded-md p-1.5 hover:bg-[var(--surface-muted)] disabled:opacity-40"
        >
          <Icon name="chevronLeft" className="h-4 w-4" />
        </button>
        <span className="px-1 tabular-nums">
          {page + 1} / {pageCount}
        </span>
        <button
          type="button"
          onClick={() => onPage(page + 1)}
          disabled={page >= pageCount - 1}
          aria-label="Next page"
          className="rounded-md p-1.5 hover:bg-[var(--surface-muted)] disabled:opacity-40"
        >
          <Icon name="chevronRight" className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}
