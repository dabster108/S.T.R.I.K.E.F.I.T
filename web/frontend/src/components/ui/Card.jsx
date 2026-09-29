export function Card({ as: Tag = "section", className = "", children, ...rest }) {
  return (
    <Tag className={`min-w-0 rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)] ${className}`} {...rest}>
      {children}
    </Tag>
  );
}

export function CardHeader({ title, subtitle, actions, className = "" }) {
  return (
    <div className={`flex flex-wrap items-center justify-between gap-3 border-b border-[var(--border)] px-5 py-4 ${className}`}>
      <div className="min-w-0">
        <h2 className="text-[0.95rem] font-bold tracking-tight text-[var(--ink)]">{title}</h2>
        {subtitle && <p className="mt-0.5 text-xs font-medium leading-relaxed text-[var(--muted)]">{subtitle}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}

export function PageHeader({ eyebrow, title, description, actions, leading }) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex min-w-0 items-center gap-4">
        {leading}
        <div className="min-w-0">
          {eyebrow && <p className="t-eyebrow">{eyebrow}</p>}
          <h1 className="t-h1 mt-1 text-[var(--ink)]">{title}</h1>
          {description && <p className="mt-1.5 max-w-2xl text-sm font-medium leading-relaxed text-[var(--muted)]">{description}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}

// "02 ── Trend ─────────── aside": numbered section divider for long dashboard pages.
export function SectionRule({ index, title, aside, id, className = "" }) {
  return (
    <div className={`flex items-center gap-3 ${className}`}>
      {index && <span className="t-index">{index}</span>}
      <h2 id={id} className="t-kicker shrink-0 text-[var(--ink-secondary)]">
        {title}
      </h2>
      <span className="h-px min-w-6 flex-1 bg-[var(--border)]" aria-hidden />
      {aside && <div className="flex shrink-0 items-center gap-2">{aside}</div>}
    </div>
  );
}
