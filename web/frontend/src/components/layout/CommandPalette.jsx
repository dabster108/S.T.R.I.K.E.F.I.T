import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../../lib/api.js";
import { formatNumber } from "../../lib/format.js";
import Icon from "../ui/Icon.jsx";
import { NAV } from "./Sidebar.jsx";
import { useLiveIds } from "../../live/LiveProvider.jsx";
import { liveStore } from "../../live/liveStore.js";
import { levelName } from "../../lib/game.js";

// The matched part of a label in bold, so it is clear why a result is there.
function Highlight({ text, needle }) {
  const i = needle ? text.toLowerCase().indexOf(needle) : -1;
  if (i < 0) return text;
  return (
    <>
      {text.slice(0, i)}
      <mark className="rounded-sm bg-transparent font-extrabold text-[var(--accent-strong)]">{text.slice(i, i + needle.length)}</mark>
      {text.slice(i + needle.length)}
    </>
  );
}

const PAGES = [
  ...NAV.map((n) => ({ kind: "page", id: n.to, label: n.label, icon: n.icon, to: n.to })),
  { kind: "page", id: "/dashboard/connection", label: "Connection and Unity setup", icon: "server", to: "/dashboard/connection", keywords: "settings health api" },
  { kind: "page", id: "/dashboard/guide", label: "User guide", icon: "book", to: "/dashboard/guide", keywords: "help manual docs how" },
  { kind: "page", id: "/", label: "Landing page", icon: "external", to: "/", keywords: "home public" },
];

const score = (text, q) => {
  const t = text.toLowerCase();
  if (t.startsWith(q)) return 0;
  const i = t.indexOf(q);
  if (i >= 0) return 1 + i / 100;
  // Loose subsequence match ("ldb" -> "Leaderboard").
  let at = 0;
  for (const ch of q) {
    at = t.indexOf(ch, at);
    if (at < 0) return null;
    at++;
  }
  return 3;
};

function filter(items, q) {
  if (!q) return items;
  return items
    .map((it) => ({ it, s: score(`${it.label} ${it.keywords || ""}`, q) }))
    .filter((x) => x.s !== null)
    .sort((a, b) => a.s - b.s)
    .map((x) => x.it);
}

export default function CommandPalette({ open, onClose }) {
  const [q, setQ] = useState("");
  const [active, setActive] = useState(0);
  const [players, setPlayers] = useState(null);
  const navigate = useNavigate();
  const input = useRef(null);
  const list = useRef(null);
  const returnFocus = useRef(null);
  const listId = useId();

  useEffect(() => {
    if (!open) return undefined;
    returnFocus.current = document.activeElement;
    setQ("");
    setActive(0);
    requestAnimationFrame(() => input.current?.focus());
    let alive = true;
    api
      .players()
      .then((p) => alive && setPlayers(p))
      .catch(() => alive && setPlayers((prev) => prev || []));
    return () => {
      alive = false;
      returnFocus.current?.focus?.();
      setQ(""); // cleared on close, so a fast reopen-and-type never starts from the old query
      setActive(0);
    };
  }, [open]);

  const needle = q.trim().toLowerCase();
  const liveIds = useLiveIds();
  const groups = useMemo(() => {
    // Fights being played right now come first: the most likely place someone wants to jump to.
    const live = filter(
      liveIds
        .map((id) => liveStore.getSession(id))
        .filter((e) => e && e.session.status === "live")
        .map((e) => ({ kind: "live", id: `live:${e.session.id}`, label: `${e.session.playerName} is fighting`, icon: "live", to: "/dashboard/live", hint: `${levelName(e.session.level)} · open the Live Arena`, keywords: e.session.playerName })),
      needle
    );
    const pages = filter(PAGES, needle);
    const people = filter(
      (players || []).map((p) => ({
        kind: "player",
        id: `player:${p.playerName}`,
        label: p.playerName,
        icon: "player",
        to: `/dashboard/player/${encodeURIComponent(p.playerName)}`,
        hint: `${p.sessions} session${p.sessions === 1 ? "" : "s"} · best ${formatNumber(p.bestScore)}`,
      })),
      needle
    ).slice(0, needle ? 8 : 5);
    return [
      { title: "Live now", items: live },
      { title: "Pages", items: pages },
      { title: "Players", items: people },
    ].filter((g) => g.items.length);
  }, [needle, players, liveIds]);
  const flat = groups.flatMap((g) => g.items);

  useEffect(() => {
    list.current?.querySelector(`[data-index="${active}"]`)?.scrollIntoView({ block: "nearest" });
  }, [active]);

  if (!open) return null;

  const go = (item) => {
    if (!item) return;
    onClose();
    navigate(item.to);
  };

  const onKey = (e) => {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((a) => (flat.length ? (a + 1) % flat.length : 0));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((a) => (flat.length ? (a - 1 + flat.length) % flat.length : 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      go(flat[active]);
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    } else if (e.key === "Tab") {
      e.preventDefault();
    }
  };

  let index = -1;
  return (
    <div className="fade-in fixed inset-0 z-50 flex items-start justify-center bg-gray-900/40 px-4 pt-[12vh]" onMouseDown={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
        className="pop-in w-full max-w-lg overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-2xl"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-3 border-b border-[var(--border)] px-4">
          <Icon name="search" className="h-[18px] w-[18px] text-[var(--accent)]" />
          <input
            ref={input}
            value={q}
            onChange={(e) => {
              setQ(e.target.value);
              setActive(0);
            }}
            onKeyDown={onKey}
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={flat[active] ? `${listId}-${active}` : undefined}
            placeholder="Jump to a page or player…"
            className="h-14 w-full bg-transparent text-[0.95rem] font-semibold text-[var(--ink)] placeholder:font-medium placeholder:text-[var(--muted)] focus:outline-none"
          />
          <kbd className="rounded-md border border-[var(--border)] bg-[var(--surface-muted)] px-1.5 py-0.5 font-sans text-xs font-semibold text-[var(--ink-secondary)]">Esc</kbd>
        </div>

        <div ref={list} id={listId} role="listbox" aria-label="Results" className="max-h-[min(60vh,26rem)] overflow-y-auto p-2">
          {groups.map((g) => (
            <div key={g.title} role="group" aria-label={g.title} className="mb-1">
              <p className="px-3 pb-1 pt-2 text-xs font-bold uppercase tracking-wider text-[var(--muted)]">{g.title}</p>
              {g.items.map((item) => {
                index++;
                const i = index;
                const on = i === active;
                return (
                  <div
                    key={item.id}
                    id={`${listId}-${i}`}
                    data-index={i}
                    role="option"
                    aria-selected={on}
                    onMouseMove={() => active !== i && setActive(i)}
                    onClick={() => go(item)}
                    className={`flex cursor-pointer items-center gap-3 rounded-xl px-3 py-2.5 transition-colors duration-150 ${on ? "bg-[var(--accent-soft)] text-[var(--accent-strong)]" : "text-[var(--ink)]"}`}
                  >
                    {item.kind === "live" ? (
                      <span className="relative flex h-7 w-7 items-center justify-center rounded-full bg-[var(--good-soft)] text-[var(--good-ink)]">
                        <span className="status-live h-2.5 w-2.5 rounded-full bg-[var(--good)]" aria-hidden />
                      </span>
                    ) : item.kind === "player" ? (
                      <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[var(--accent)] text-xs font-extrabold text-white">{item.label.slice(0, 2).toUpperCase()}</span>
                    ) : (
                      <span className={`flex h-7 w-7 items-center justify-center rounded-lg ${on ? "bg-[var(--surface)]" : "bg-[var(--surface-muted)]"}`}>
                        <Icon name={item.icon} className="h-4 w-4" />
                      </span>
                    )}
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-semibold">
                        <Highlight text={item.label} needle={needle} />
                      </span>
                      {item.hint && <span className="block truncate text-xs font-medium text-[var(--muted)]">{item.hint}</span>}
                    </span>
                    {on && <Icon name="enter" className="h-4 w-4 opacity-70" />}
                  </div>
                );
              })}
            </div>
          ))}
          {players === null && (
            <div className="space-y-2 px-3 py-2" aria-hidden>
              {[0, 1, 2].map((i) => (
                <div key={i} className="flex items-center gap-3">
                  <span className="skeleton h-7 w-7 rounded-full" />
                  <span className="skeleton h-3 flex-1 rounded" />
                </div>
              ))}
            </div>
          )}
          {!flat.length && players !== null && (
            <div className="px-3 py-8 text-center">
              <p className="text-sm font-bold text-[var(--ink)]">Nothing matches “{q}”</p>
              <p className="mt-1 text-xs font-medium text-[var(--muted)]">Try a player's name, or a page such as Live Arena, Sessions or Thesis Lab.</p>
            </div>
          )}
        </div>

        <div className="flex items-center gap-4 border-t border-[var(--border)] bg-[var(--surface-muted)] px-4 py-2 text-xs font-semibold text-[var(--ink-secondary)]">
          <span className="inline-flex items-center gap-1">
            <Icon name="arrowUp" className="h-3 w-3" />
            <Icon name="arrowDown" className="h-3 w-3" /> navigate
          </span>
          <span className="inline-flex items-center gap-1">
            <Icon name="enter" className="h-3 w-3" /> open
          </span>
          <span className="ml-auto">{players ? `${players.length} player${players.length === 1 ? "" : "s"}` : ""}</span>
        </div>
      </div>
    </div>
  );
}
