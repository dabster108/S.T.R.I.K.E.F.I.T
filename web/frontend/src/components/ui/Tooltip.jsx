import { cloneElement, isValidElement, useId } from "react";

const SIDES = {
  top: "bottom-full left-1/2 mb-2 -translate-x-1/2",
  bottom: "top-full left-1/2 mt-2 -translate-x-1/2",
  right: "left-full top-1/2 ml-3 -translate-y-1/2",
};

// Hover / keyboard-focus tooltip. The child gets aria-describedby so screen readers hear it too.
export default function Tooltip({ label, side = "top", disabled = false, className = "", children }) {
  const id = useId();
  if (disabled || !label) return children;
  const child = isValidElement(children) ? cloneElement(children, { "aria-describedby": id }) : children;
  return (
    <span className={`has-tip relative inline-flex ${className}`}>
      {child}
      <span role="tooltip" id={id} className={`tooltip absolute z-50 whitespace-nowrap ${SIDES[side]}`}>
        <span className="block rounded-lg bg-[var(--ink)] px-2 py-1 text-xs font-semibold text-white shadow-lg">{label}</span>
      </span>
    </span>
  );
}
