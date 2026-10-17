import ReactSelect, { components } from "react-select";
import Icon from "./Icon.jsx";

// Single-choice dropdown (react-select). Kept apart from Controls.jsx so only the dashboard pages that use it download react-select.

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
