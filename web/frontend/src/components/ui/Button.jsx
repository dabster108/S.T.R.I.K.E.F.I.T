import { forwardRef } from "react";
import Icon from "./Icon.jsx";

const VARIANTS = {
  primary:
    "border-transparent bg-[var(--accent)] text-white shadow-sm hover:bg-[var(--accent-strong)] disabled:bg-[var(--accent)]/60",
  secondary:
    "border-[var(--border)] bg-[var(--surface)] text-[var(--ink-secondary)] shadow-sm hover:border-[var(--border-strong)] hover:text-[var(--ink)]",
  ghost: "border-transparent bg-transparent text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]",
  danger:
    "border-[var(--border)] bg-[var(--surface)] text-[var(--bad)] shadow-sm hover:border-red-200 hover:bg-[var(--bad-soft)]",
  dangerSolid: "border-transparent bg-[var(--bad)] text-white shadow-sm hover:bg-red-700",
};

const SIZES = {
  sm: "gap-1.5 rounded-lg px-2.5 py-1.5 text-xs",
  md: "gap-2 rounded-lg px-3.5 py-2 text-sm",
};

const Button = forwardRef(function Button(
  { variant = "secondary", size = "sm", icon, children, className = "", ...rest },
  ref
) {
  return (
    <button
      ref={ref}
      type="button"
      className={`inline-flex items-center justify-center border font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${VARIANTS[variant]} ${SIZES[size]} ${className}`}
      {...rest}
    >
      {icon && <Icon name={icon} className={size === "sm" ? "h-3.5 w-3.5" : "h-4 w-4"} />}
      {children}
    </button>
  );
});

export default Button;
