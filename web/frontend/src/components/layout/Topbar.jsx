import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api } from "../../lib/api.js";
import Icon from "../ui/Icon.jsx";
import { ConnectionBadge } from "../ui/Badge.jsx";
import { Brand } from "./Sidebar.jsx";

// Global player search: type, pick with arrows/Enter, lands on the player page.
function PlayerSearch() {
  const [names, setNames] = useState(null);
  const [q, setQ] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const navigate = useNavigate();
  const listId = useId();
  const box = useRef(null);

  const matches = useMemo(() => {
    const s = q.trim().toLowerCase();
    return (names || []).filter((n) => !s || n.toLowerCase().includes(s)).slice(0, 8);
  }, [names, q]);

  useEffect(() => {
    const onDoc = (e) => !box.current?.contains(e.target) && setOpen(false);
    document.addEventListener("pointerdown", onDoc);
    return () => document.removeEventListener("pointerdown", onDoc);
  }, []);

  const go = (name) => {
    if (!name) return;
    setOpen(false);
    setQ("");
    navigate(`/dashboard/player/${encodeURIComponent(name)}`);
  };

  return (
    <div ref={box} className="relative min-w-0 flex-1 sm:max-w-xs">
      <label className="relative flex items-center">
        <span className="sr-only">Find a player</span>
        <Icon name="search" className="pointer-events-none absolute left-2.5 h-3.5 w-3.5 text-[var(--muted)]" />
        <input
          type="search"
          role="combobox"
          aria-expanded={open && matches.length > 0}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={open && matches[active] ? `${listId}-${active}` : undefined}
          value={q}
          placeholder="Find a player…"
          onFocus={() => {
            setOpen(true);
            if (!names) api.players().then((p) => setNames(p.map((x) => x.playerName))).catch(() => setNames([]));
          }}
          onChange={(e) => {
            setQ(e.target.value);
            setActive(0);
            setOpen(true);
          }}
          onKeyDown={(e) => {
            if (e.key === "ArrowDown") (e.preventDefault(), setActive((a) => Math.min(matches.length - 1, a + 1)));
            else if (e.key === "ArrowUp") (e.preventDefault(), setActive((a) => Math.max(0, a - 1)));
            else if (e.key === "Enter") (e.preventDefault(), go(matches[active] || q.trim()));
            else if (e.key === "Escape") setOpen(false);
          }}
          className="w-full rounded-lg border border-[var(--border)] bg-[var(--surface)] py-1.5 pl-8 pr-2.5 text-xs text-[var(--ink)] shadow-sm placeholder:text-[var(--muted)] focus:border-[var(--accent-ring)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-soft)]"
        />
      </label>
      {open && (names === null || q || matches.length > 0) && (
        <ul id={listId} role="listbox" className="flash-in absolute left-0 right-0 top-full z-30 mt-1 rounded-xl border border-[var(--border)] bg-[var(--surface)] p-1 text-sm shadow-lg">
          {names === null && <li className="px-3 py-2 text-xs text-[var(--muted)]">Loading players…</li>}
          {names !== null && !matches.length && <li className="px-3 py-2 text-xs text-[var(--muted)]">No player matches “{q}”</li>}
          {matches.map((n, i) => (
            <li
              key={n}
              id={`${listId}-${i}`}
              role="option"
              aria-selected={i === active}
              onPointerDown={(e) => (e.preventDefault(), go(n))}
              onPointerEnter={() => setActive(i)}
              className={`flex cursor-pointer items-center gap-2 rounded-lg px-3 py-2 ${i === active ? "bg-[var(--accent-soft)] text-[var(--accent)]" : "text-[var(--ink)]"}`}
            >
              <Icon name="players" className="h-4 w-4 opacity-70" />
              {n}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default function Topbar({ connection, onMenu }) {
  return (
    <header className="sticky top-0 z-20 flex items-center gap-3 border-b border-[var(--border)] bg-[var(--surface)]/90 px-4 py-3 backdrop-blur-md sm:px-6 lg:px-8">
      <div className="hidden sm:block lg:hidden">
        <Brand />
      </div>
      <PlayerSearch />
      <div className="ml-auto flex items-center gap-2">
        <span className="sm:hidden">
          <ConnectionBadge state={connection} compact />
        </span>
        <span className="hidden sm:inline-flex">
          <ConnectionBadge state={connection} />
        </span>
        <Link to="/" className="hidden items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)] sm:inline-flex">
          <Icon name="chevronLeft" className="h-3.5 w-3.5" />
          Landing page
        </Link>
        <button type="button" onClick={onMenu} aria-label="Open menu" className="rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] lg:hidden">
          <Icon name="menu" className="h-5 w-5" />
        </button>
      </div>
    </header>
  );
}
