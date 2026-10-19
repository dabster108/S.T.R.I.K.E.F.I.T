import { useEffect, useId, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../../lib/api.js";
import { formatNumber } from "../../lib/format.js";
import Icon from "../ui/Icon.jsx";
import { NAV } from "./Sidebar.jsx";
import { useLiveIds } from "../../live/LiveProvider.jsx";
import { liveStore } from "../../live/liveStore.js";
import { LEVELS, levelName } from "../../lib/game.js";
import { fuzzyFilter } from "../../lib/fuzzy.js";
import { addRecent, goHint, readRecent, writeRecent } from "../../lib/shortcuts.js";
import { exportCsv } from "../../lib/exportCsv.js";
import { copyText } from "../../lib/toast.js";
import { useCalmMode } from "../../hooks/motion.js";
import { openTour } from "../onboarding/tourState.js";

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
  ...NAV.map((n) => ({ kind: "page", id: n.to, label: n.label, icon: n.icon, to: n.to, shortcut: goHint(n.to) })),
  { kind: "page", id: "/dashboard/connection", label: "Connection and Unity setup", icon: "server", to: "/dashboard/connection", keywords: "settings health api" },
  { kind: "page", id: "/dashboard/guide", label: "User guide", icon: "book", to: "/dashboard/guide", keywords: "help manual docs how" },
  { kind: "page", id: "/", label: "Landing page", icon: "external", to: "/", keywords: "home public" },
];

// "Jump to level": the Sessions page filtered to that level.
const LEVEL_ITEMS = Object.values(LEVELS).map((l) => ({
  kind: "level",
  id: `level:${l.key}`,
  label: `${l.name}: ${l.difficulty} sessions`,
  icon: "swords",
  to: `/dashboard/sessions?level=${l.key}`,
  hint: l.monster,
  keywords: `level ${l.number} ${l.key} ${l.difficulty} ${l.monster}`,
}));

const filter = (items, q) => fuzzyFilter(items, q);

function Keys({ keys }) {
  if (!keys) return null;
  return (
    <span className="flex shrink-0 items-center gap-1" aria-hidden>
      {keys.map((k, i) => (
        <kbd key={i} className="min-w-5 rounded border border-[var(--border)] bg-[var(--surface)] px-1 py-px text-center font-sans text-[0.6875rem] font-bold text-[var(--ink-secondary)]">
          {k}
        </kbd>
      ))}
    </span>
  );
}

export default function CommandPalette({ open, onClose, onShortcuts }) {
  const [q, setQ] = useState("");
  const [active, setActive] = useState(0);
  const [players, setPlayers] = useState(null);
  const navigate = useNavigate();
  const input = useRef(null);
  const list = useRef(null);
  const returnFocus = useRef(null);
  const listId = useId();
  const [calm, setCalm] = useCalmMode();
  const [recent, setRecent] = useState(() => readRecent(localStorage));

  useEffect(() => {
    if (!open) return undefined;
    returnFocus.current = document.activeElement;
    setQ("");
    setActive(0);
    setRecent(readRecent(localStorage));
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
    const levels = filter(LEVEL_ITEMS, needle);
    const actions = filter(
      [
        { kind: "action", id: "act:calm", label: calm ? "Turn Calm mode off" : "Turn Calm mode on", icon: "moon", hint: "Stops confetti, tilt, embers and cheers", keywords: "calm motion reduce animation accessibility toggle", run: () => setCalm(!calm) },
        { kind: "action", id: "act:csv-sessions", label: "Export sessions as CSV", icon: "download", hint: "Every session, one row each", keywords: "download csv export sessions data", run: () => exportCsv("sessions") },
        { kind: "action", id: "act:csv-events", label: "Export events as CSV", icon: "download", hint: "Every hit and monster attack", keywords: "download csv export events hits data", run: () => exportCsv("events") },
        { kind: "action", id: "act:copy-link", label: "Copy link to this page", icon: "link", keywords: "share url copy", run: () => copyText(window.location.href, "Link") },
        { kind: "action", id: "act:shortcuts", label: "Show keyboard shortcuts", icon: "command", shortcut: ["?"], keywords: "keys help hotkeys", run: () => onShortcuts?.() },
        { kind: "action", id: "act:tour", label: "Take the dashboard tour", icon: "compass", keywords: "guide onboarding walkthrough help", run: () => openTour() },
      ],
      needle
    );
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
    const known = new Map([...PAGES, ...LEVEL_ITEMS, ...people].map((i) => [i.id, i]));
    // Recents come back as they were saved, but a page that no longer exists is dropped and a stale hint is refreshed.
    const recents = needle ? [] : recent.map((r) => known.get(r.id) || (r.kind === "player" ? r : null)).filter(Boolean);
    const seen = new Set(recents.map((r) => r.id));
    const dedupe = (items) => (needle ? items : items.filter((i) => !seen.has(i.id)));
    return [
      { title: "Recent", items: recents },
      { title: "Live now", items: live },
      { title: "Actions", items: needle ? actions : actions.slice(0, 4) },
      { title: "Pages", items: dedupe(pages) },
      { title: "Levels", items: dedupe(levels) },
      { title: "Players", items: dedupe(people) },
    ].filter((g) => g.items.length);
  }, [needle, players, liveIds, recent, calm]);
  const flat = groups.flatMap((g) => g.items);

  useEffect(() => {
    list.current?.querySelector(`[data-index="${active}"]`)?.scrollIntoView({ block: "nearest" });
  }, [active]);

  if (!open) return null;

  const go = (item) => {
    if (!item) return;
    onClose();
    if (item.run) {
      item.run();
      return;
    }
    if (item.kind !== "live") {
      const next = addRecent(recent, item);
      setRecent(next);
      writeRecent(localStorage, next);
    }
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
            placeholder="Jump to a page, player or level, or run an action…"
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
                    <Keys keys={item.shortcut} />
                    {on && <Icon name="enter" className="h-4 w-4 shrink-0 opacity-70" />}
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
          <span className="inline-flex items-center gap-1" title="Press G, then a letter, to jump straight to a page">
            <Keys keys={["G"]} /> then a letter
          </span>
          <span className="ml-auto">{players ? `${players.length} player${players.length === 1 ? "" : "s"}` : ""}</span>
        </div>
      </div>
    </div>
  );
}
