import { Card, CardHeader } from "../ui/Card.jsx";

// Side A is blue (--level-1) and side B amber (--level-2) on every Compare panel,
// in both modes, so the reader learns the pairing once.
export const SIDE_A = { color: "var(--level-1)", soft: "bg-[var(--accent-soft)]", ring: "ring-[var(--accent-ring)]", text: "text-[var(--accent-strong)]" };
export const SIDE_B = { color: "var(--level-2)", soft: "bg-amber-50", ring: "ring-amber-200", text: "text-amber-800" };

// Colour swatch + name: how a side is named inside a panel. The name stays in ink.
export function SideKey({ side, label, className = "" }) {
  return (
    <span className={`inline-flex min-w-0 items-center gap-1.5 ${className}`}>
      <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: side.color }} aria-hidden />
      <span className="truncate">{label}</span>
    </span>
  );
}

// A titled card around one chart, with an optional footnote for sample sizes and caveats.
export function Panel({ title, subtitle, actions, note, className = "", children }) {
  return (
    <Card className={`flex flex-col overflow-hidden ${className}`}>
      <CardHeader title={title} subtitle={subtitle} actions={actions} />
      <div className="flex-1 px-4 pb-4 pt-4 sm:px-5">{children}</div>
      {note && <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)]">{note}</p>}
    </Card>
  );
}
