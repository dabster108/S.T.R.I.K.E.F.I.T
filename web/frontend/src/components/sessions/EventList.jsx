import { useRef } from "react";
import Icon from "../ui/Icon.jsx";
import { formatClock } from "../../lib/format.js";
import { clampSpeed, TIER_LABEL } from "../../lib/game.js";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);

// The game writes its own message for each event; for hits and clashes it prints the RAW controller speed (e.g. "13.13 m/s"),
// which the dashboard otherwise clamps to the game's 8 m/s cap. Those two are rebuilt from the payload, everything else is as sent.
export function describeEvent(ev) {
  const p = ev.payload && typeof ev.payload === "object" ? ev.payload : {};
  if (ev.type === "hit" && isNum(p.swingSpeed)) {
    const tier = TIER_LABEL[String(p.tier || "").toLowerCase()];
    return `${tier ? `${tier} hit` : "Hit"}${isNum(p.damage) ? ` for ${Math.round(p.damage * 10) / 10} damage` : ""} · ${clampSpeed(p.swingSpeed).toFixed(1)} m/s${p.defended ? " · into the guard" : ""}`;
  }
  if (ev.type === "clash" && isNum(p.swingSpeed)) return `Weapon clash · ${clampSpeed(p.swingSpeed).toFixed(1)} m/s`;
  return ev.message;
}

export const EVENT_META = {
  session_start: { label: "Session start", icon: "play", cls: "bg-blue-50 text-blue-600", dot: "bg-blue-500" },
  hit: { label: "Hit", icon: "bolt", cls: "bg-amber-50 text-amber-600", dot: "bg-amber-500" },
  session_complete: { label: "Session complete", icon: "check", cls: "bg-emerald-50 text-emerald-600", dot: "bg-emerald-500" },
  weak_swing: { label: "Too slow", icon: "hand", cls: "bg-gray-100 text-gray-600", dot: "bg-gray-300" },
  clash: { label: "Weapon clash", icon: "swords", cls: "bg-gray-100 text-gray-600", dot: "bg-gray-300" },
  monster_attack: { label: "Monster attack", icon: "flame", cls: "bg-rose-50 text-rose-600", dot: "bg-rose-600" },
  monster_defend: { label: "Monster guard", icon: "shield", cls: "bg-rose-50 text-rose-600", dot: "bg-rose-600" },
  monster_guard: { label: "Guard result", icon: "shieldCheck", cls: "bg-blue-50 text-blue-600", dot: "bg-blue-500" },
  opening_hit: { label: "Opening found", icon: "crosshair", cls: "bg-emerald-50 text-emerald-600", dot: "bg-emerald-500" },
  dodge: { label: "Dodged", icon: "check", cls: "bg-blue-50 text-blue-600", dot: "bg-blue-500" },
  block: { label: "Blocked", icon: "shieldCheck", cls: "bg-blue-50 text-blue-600", dot: "bg-blue-500" },
  player_hurt: { label: "Player hurt", icon: "heart", cls: "bg-red-50 text-[var(--bad)]", dot: "bg-[var(--bad)]" },
};

export function eventMeta(type) {
  return EVENT_META[type] || { label: type || "Event", icon: "signal", cls: "bg-gray-100 text-gray-600" };
}

// Resting a sword on the monster (or its axe) reports several "too slow" / "clash" events a
// second; back-to-back repeats from the same fight become one row with a count.
const REPEATABLE = new Set(["weak_swing", "clash"]);
export function collapseRepeats(events) {
  const out = [];
  for (const e of events) {
    const prev = out[out.length - 1];
    if (prev && REPEATABLE.has(e.type) && prev.type === e.type && prev.sessionId === e.sessionId) {
      out[out.length - 1] = { ...prev, repeat: (prev.repeat || 1) + 1 };
    } else {
      out.push(e);
    }
  }
  return out;
}

// Rows that arrive after the list first rendered keep a "new" flag, so their
// slide-in + highlight isn't cut short when later events re-render the list.
function useArrivals(events) {
  const initial = useRef(null);
  const arrived = useRef(new Set());
  if (initial.current === null) initial.current = new Set(events.map((e) => e.id));
  for (const e of events) if (!initial.current.has(e.id)) arrived.current.add(e.id);
  if (arrived.current.size > 600) arrived.current = new Set([...arrived.current].slice(-300));
  return arrived.current;
}

// Vertical timeline of events, rendered in the order given.
// showPlayer: prefix rows with the player's name (feeds that mix several players).
// ev.repeat (Live Arena): identical back-to-back events folded into one row.
export default function EventList({ events, dense = false, showPlayer = false }) {
  const arrived = useArrivals(events);
  return (
    <ol className="relative">
      {events.map((ev, i) => {
        const meta = eventMeta(ev.type);
        const last = i === events.length - 1;
        return (
          <li key={ev.id} className={`relative flex gap-3 ${arrived.has(ev.id) ? "row-new" : ""} ${dense ? "px-4 py-2" : "px-5 py-3"}`}>
            {!last && (
              <span
                className={`absolute bottom-0 w-px bg-[var(--border)] ${dense ? "left-[1.875rem] top-9" : "left-[2.125rem] top-10"}`}
                aria-hidden
              />
            )}
            <span className={`relative z-[1] flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${meta.cls}`}>
              <Icon name={meta.icon} className="h-3.5 w-3.5" />
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex items-baseline justify-between gap-2">
                <p className="min-w-0 truncate text-xs font-bold text-[var(--ink)]">
                  {showPlayer && ev.playerName && <span className="text-[var(--accent-strong)]">{ev.playerName} · </span>}
                  {meta.label}
                  {ev.repeat > 1 && (
                    <span className="ml-1.5 rounded-full bg-[var(--surface-muted)] px-1.5 py-0.5 text-[0.7rem] font-extrabold tabular-nums text-[var(--ink-secondary)]">
                      ×{ev.repeat}
                    </span>
                  )}
                </p>
                <time className="shrink-0 text-xs font-medium tabular-nums text-[var(--muted)]">{formatClock(ev.at)}</time>
              </div>
              <p className="mt-0.5 text-xs font-medium leading-snug text-[var(--muted)]">{describeEvent(ev)}</p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
