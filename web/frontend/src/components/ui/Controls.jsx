import ReactSelect, { components } from "react-select";
import Icon from "./Icon.jsx";

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

// react-select dressed as the `field` above (same border, radius, focus ring, text size).
const selectStyles = {
  container: (base) => ({ ...base, fontSize: "0.75rem", fontWeight: 600, lineHeight: "1rem" }),
  control: (base, s) => ({
    ...base,
    minHeight: 30,
    cursor: "pointer",
    flexWrap: "nowrap",
    borderRadius: "var(--radius)",
    borderColor: s.isFocused ? "var(--accent)" : "var(--border)",
    backgroundColor: "var(--surface)",
    boxShadow: s.isFocused ? "0 0 0 2px var(--accent-ring)" : "var(--shadow-sm)",
    transition: "border-color 150ms, box-shadow 150ms",
    "&:hover": { borderColor: s.isFocused ? "var(--accent)" : "var(--border-strong)" },
  }),
  valueContainer: (base) => ({ ...base, padding: "0 0 0 0.625rem", flexWrap: "nowrap" }),
  singleValue: (base) => ({ ...base, margin: 0, color: "var(--ink)" }),
  placeholder: (base) => ({ ...base, margin: 0, color: "var(--muted)", fontWeight: 500 }),
  input: (base) => ({ ...base, margin: 0, padding: 0, color: "var(--ink)" }),
  indicatorSeparator: () => ({ display: "none" }),
  dropdownIndicator: (base, s) => ({
    ...base,
    padding: "0 0.5rem 0 0.25rem",
    color: "var(--muted)",
    transition: "transform 150ms",
    transform: s.selectProps.menuIsOpen ? "rotate(180deg)" : "none",
    "&:hover": { color: "var(--ink)" },
  }),
  // Portalled to <body>, so it sets its own type and sits above the sticky topbar and sidebar.
  menuPortal: (base) => ({ ...base, zIndex: 45 }),
  menu: (base) => ({
    ...base,
    margin: "4px 0",
    overflow: "hidden",
    fontSize: "0.75rem",
    fontWeight: 600,
    lineHeight: "1rem",
    border: "1px solid var(--border)",
    borderRadius: "var(--radius-lg)",
    backgroundColor: "var(--surface)",
    boxShadow: "var(--shadow-lg)",
  }),
  menuList: (base) => ({ ...base, padding: 4, maxHeight: 280 }),
  option: (base, s) => ({
    ...base,
    cursor: "pointer",
    padding: "0.4375rem 0.5rem",
    borderRadius: "var(--radius-sm)",
    overflowWrap: "anywhere",
    color: s.isSelected ? "var(--accent-strong)" : "var(--ink)",
    backgroundColor: s.isSelected ? "var(--accent-soft)" : s.isFocused ? "var(--surface-muted)" : "transparent",
    "&:active": { backgroundColor: "var(--accent-soft)" },
  }),
  noOptionsMessage: (base) => ({ ...base, padding: "0.625rem 0.5rem", color: "var(--muted)", fontWeight: 500 }),
};

function DropdownIndicator(props) {
  return (
    <components.DropdownIndicator {...props}>
      <Icon name="chevronDown" className="h-3.5 w-3.5" />
    </components.DropdownIndicator>
  );
}

// Lists longer than this get type-to-filter (player pickers); short ones stay tap-only.
const SEARCH_FROM = 7;

// Single choice from a list. Same contract as a native <select>: options are {id, label}, onChange gets the id.
export function Select({ value, onChange, options, label, searchable = options.length >= SEARCH_FROM, className = "" }) {
  const selected = options.find((o) => String(o.id) === String(value)) ?? null;
  // Wide enough for the longest label, so the control doesn't resize as the choice changes.
  const longest = options.reduce((n, o) => Math.max(n, String(o.label).length), 0);
  return (
    <ReactSelect
      aria-label={label}
      className={className}
      classNamePrefix="sf-select"
      value={selected}
      onChange={(o) => onChange(o.id)}
      options={options}
      getOptionValue={(o) => String(o.id)}
      getOptionLabel={(o) => String(o.label)}
      isSearchable={searchable}
      styles={{ ...selectStyles, container: (base) => ({ ...selectStyles.container(base), minWidth: `calc(${Math.min(longest, 26)}ch + 2.5rem)` }) }}
      components={{ DropdownIndicator }}
      menuPortalTarget={typeof document === "undefined" ? null : document.body}
      menuPlacement="auto"
      noOptionsMessage={() => "No matches"}
    />
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
