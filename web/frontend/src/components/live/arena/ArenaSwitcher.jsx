import { useRef } from "react";
import { levelShort } from "../../../lib/game.js";
import { BOTH } from "../../../lib/arenaView.js";

// "Both / Player 1 / Player 2": which fights the arena shows. Tabs (roving focus, arrow keys), each with the player's initials, name,
// level and a pulsing dot while their fight is live. The parent remembers the choice.
const initials = (name) => name.slice(0, 2).toUpperCase();

function Dot({ live }) {
  return (
    <span className="relative flex h-2 w-2 shrink-0" aria-hidden>
      {live && <span className="status-live absolute inset-0 rounded-full bg-[var(--good)]" />}
      <span className={`relative h-2 w-2 rounded-full ${live ? "bg-[var(--good)]" : "bg-[var(--border-strong)]"}`} />
    </span>
  );
}

export default function ArenaSwitcher({ players, choice, onChoose }) {
  const refs = useRef([]);
  const items = [{ id: BOTH, label: "Both", players: players.length }, ...players.map((p) => ({ ...p, id: p.playerName }))];
  const at = Math.max(0, items.findIndex((i) => i.id === choice));
  const onKey = (e) => {
    const step = { ArrowRight: 1, ArrowLeft: -1 }[e.key];
    if (!step && e.key !== "Home" && e.key !== "End") return;
    e.preventDefault();
    const next = e.key === "Home" ? 0 : e.key === "End" ? items.length - 1 : (at + step + items.length) % items.length;
    onChoose(items[next].id);
    refs.current[next]?.focus();
  };
  return (
    <div role="tablist" aria-label="Which fights to show" onKeyDown={onKey} className="flex max-w-full items-stretch gap-1 overflow-x-auto [contain:paint] rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] p-1">
      {items.map((it, i) => {
        const on = i === at;
        return (
          <button
            key={it.id}
            ref={(el) => (refs.current[i] = el)}
            type="button"
            role="tab"
            aria-selected={on}
            tabIndex={on ? 0 : -1}
            onClick={() => onChoose(it.id)}
            className={`press flex shrink-0 items-center gap-2 rounded-lg px-3 py-1.5 text-left text-sm font-bold ${
              on ? "bg-[var(--surface)] text-[var(--ink)] shadow-[var(--shadow-card)] ring-1 ring-[var(--border)]" : "text-[var(--ink-secondary)] hover:text-[var(--ink)]"
            }`}
          >
            {it.id === BOTH ? (
              <>
                <span>Both</span>
                <span className="rounded-full bg-[var(--accent-soft)] px-1.5 text-xs font-extrabold tabular-nums text-[var(--accent-strong)]">{it.players}</span>
              </>
            ) : (
              <>
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent)] text-[0.625rem] font-extrabold text-white" aria-hidden>
                  {initials(it.playerName)}
                </span>
                <span className="max-w-[10rem] truncate">{it.playerName}</span>
                <span className="rounded-md bg-[var(--surface-muted)] px-1 text-xs font-bold text-[var(--muted)] ring-1 ring-inset ring-[var(--border)]">{levelShort(it.level)}</span>
                <Dot live={it.live} />
                <span className="sr-only">{it.live ? "live now" : "finished"}</span>
              </>
            )}
          </button>
        );
      })}
    </div>
  );
}
