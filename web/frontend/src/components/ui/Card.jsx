export function Card({ as: Tag = "section", className = "", children, ...rest }) {
  return (
    <Tag
      className={`min-w-0 rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[0_1px_2px_rgba(16,24,40,0.04)] ${className}`}
      {...rest}
    >
      {children}
    </Tag>
  );
}

export function CardHeader({ title, subtitle, actions, className = "" }) {
  return (
    <div className={`flex flex-wrap items-center justify-between gap-3 border-b border-[var(--border)] px-5 py-4 ${className}`}>
      <div className="min-w-0">
        <h2 className="text-sm font-semibold text-[var(--ink)]">{title}</h2>
        {subtitle && <p className="mt-0.5 text-xs text-[var(--muted)]">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}

export function PageHeader({ eyebrow, title, description, actions }) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        {eyebrow && <p className="text-xs font-semibold uppercase tracking-wider text-[var(--accent)]">{eyebrow}</p>}
        <h1 className="mt-1 text-2xl font-bold tracking-tight text-[var(--ink)] sm:text-[1.7rem]">{title}</h1>
        {description && <p className="mt-1 max-w-2xl text-sm text-[var(--muted)]">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
