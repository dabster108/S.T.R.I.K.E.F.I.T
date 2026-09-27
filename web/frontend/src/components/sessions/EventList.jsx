import Icon from "../ui/Icon.jsx";
import { formatClock } from "../../lib/format.js";

export const EVENT_META = {
  session_start: { label: "Session start", icon: "play", cls: "bg-blue-50 text-blue-600", dot: "bg-blue-500" },
  hit: { label: "Hit", icon: "bolt", cls: "bg-amber-50 text-amber-600", dot: "bg-amber-500" },
  session_complete: { label: "Session complete", icon: "check", cls: "bg-emerald-50 text-emerald-600", dot: "bg-emerald-500" },
};

export function eventMeta(type) {
  return EVENT_META[type] || { label: type || "Event", icon: "signal", cls: "bg-gray-100 text-gray-600" };
}

// Vertical timeline of events, rendered in the order given.
export default function EventList({ events, dense = false }) {
  return (
    <ol className="relative">
      {events.map((ev, i) => {
        const meta = eventMeta(ev.type);
        const last = i === events.length - 1;
        return (
          <li key={ev.id} className={`flash-in relative flex gap-3 ${dense ? "px-4 py-2" : "px-5 py-3"}`}>
            {!last && (
              <span
                className={`absolute bottom-0 w-px bg-[var(--border)] ${dense ? "left-[1.875rem] top-9" : "left-[2.125rem] top-10"}`}
                aria-hidden
              />
            )}
            <span className={`relative z-[1] flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${meta.cls}`}>
              <Icon name={meta.icon} className="h-3.5 w-3.5" strokeWidth={2} />
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex items-baseline justify-between gap-2">
                <p className="text-xs font-semibold text-[var(--ink)]">{meta.label}</p>
                <time className="shrink-0 text-[0.68rem] tabular-nums text-[var(--muted)]">{formatClock(ev.at)}</time>
              </div>
              <p className="mt-0.5 text-xs leading-snug text-[var(--muted)]">{ev.message}</p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
