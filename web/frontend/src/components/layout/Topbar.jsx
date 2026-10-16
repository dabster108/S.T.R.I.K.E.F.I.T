import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Link } from "react-router-dom";
import { wakeStore } from "../../lib/api.js";
import { useCalmMode } from "../../hooks/motion.js";
import { openTour } from "../onboarding/tourState.js";
import { useConnection } from "../../live/LiveProvider.jsx";
import Icon from "../ui/Icon.jsx";
import Tooltip from "../ui/Tooltip.jsx";
import { Brand } from "./Sidebar.jsx";

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

// The one place backend reachability shows at all: a small dot, always present, never text on
// its own. Hover or focus for the word; nothing interrupts the page while it changes state.
// useConnection() reports the raw stream state (connected | connecting | reconnecting | offline).
const DOT = {
  connected: { cls: "bg-[var(--good)]", label: "Connected" },
  connecting: { cls: "bg-[var(--warn)] status-live", label: "Connecting…" },
  reconnecting: { cls: "bg-[var(--warn)] status-live", label: "Reconnecting…" },
  offline: { cls: "bg-[var(--bad)]", label: "Server offline" },
};

function ConnectionDot() {
  const { connection } = useConnection();
  const waking = useSyncExternalStore(wakeStore.subscribe, wakeStore.isWaking);
  const d = DOT[connection] || DOT.offline;
  const label = waking ? "Waking the server (up to a minute)" : d.label;
  return (
    <Tooltip label={label} side="bottom">
      <Link to="/dashboard/connection" aria-label={`Backend: ${label}`} className="press flex h-8 w-8 items-center justify-center rounded-lg hover:bg-[var(--surface-muted)]">
        <span className={`h-2 w-2 rounded-full ${d.cls}`} aria-hidden />
      </Link>
    </Tooltip>
  );
}

function PaletteTrigger({ onSearch }) {
  return (
    <button
      type="button"
      onClick={onSearch}
      className="press group flex min-w-0 flex-1 items-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2 text-left text-sm font-medium text-[var(--muted)] hover:border-[var(--border-strong)] hover:bg-[var(--surface)] sm:max-w-sm"
      aria-label="Search pages and players"
      aria-keyshortcuts={isMac ? "Meta+K" : "Control+K"}
    >
      <Icon name="search" className="h-4 w-4 shrink-0 transition-colors group-hover:text-[var(--accent)]" />
      <span className="truncate">Search pages and players…</span>
      <kbd className="ml-auto hidden shrink-0 items-center gap-0.5 rounded-md border border-[var(--border)] bg-[var(--surface)] px-1.5 py-0.5 font-sans text-xs font-semibold text-[var(--ink-secondary)] sm:inline-flex">
        {isMac ? <Icon name="command" className="h-3 w-3" /> : "Ctrl"}
        <span>K</span>
      </kbd>
    </button>
  );
}

// Header menu: Calm mode (confetti, tilt, embers and floating reactions off; on by default when the OS asks for reduced
// motion) and the Guide (the first-visit tour, reopened). Esc or a click outside closes it.
function HeaderMenu() {
  const [open, setOpen] = useState(false);
  const [calm, setCalm] = useCalmMode();
  const wrap = useRef(null);
  const button = useRef(null);
  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e) => wrap.current && !wrap.current.contains(e.target) && setOpen(false);
    const onKey = (e) => {
      if (e.key === "Escape") {
        setOpen(false);
        button.current?.focus();
      }
    };
    document.addEventListener("pointerdown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("pointerdown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);
  return (
    <div ref={wrap} className="relative">
      <button
        ref={button}
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-haspopup="true"
        aria-controls="header-menu"
        className="press inline-flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-sm font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)]"
      >
        <Icon name="compass" className="h-4 w-4" />
        <span className="hidden sm:inline">Guide</span>
      </button>
      {open && (
        <div id="header-menu" className="fade-in absolute right-0 top-full z-30 mt-2 w-72 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-2 shadow-[var(--shadow-lift)]">
          <button
            type="button"
            className="press flex w-full items-start gap-3 rounded-xl px-3 py-2.5 text-left hover:bg-[var(--surface-muted)]"
            onClick={() => {
              setOpen(false);
              openTour();
            }}
          >
            <Icon name="compass" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--accent-strong)]" />
            <span>
              <span className="block text-sm font-bold text-[var(--ink)]">Take the tour</span>
              <span className="block text-xs font-medium text-[var(--muted)]">Five steps through the dashboard</span>
            </span>
          </button>
          <Link to="/dashboard/guide" onClick={() => setOpen(false)} className="press flex items-start gap-3 rounded-xl px-3 py-2.5 hover:bg-[var(--surface-muted)]">
            <Icon name="book" className="mt-0.5 h-4 w-4 shrink-0 text-[var(--accent-strong)]" />
            <span>
              <span className="block text-sm font-bold text-[var(--ink)]">User guide</span>
              <span className="block text-xs font-medium text-[var(--muted)]">Every page explained</span>
            </span>
          </Link>
          <div className="mt-1 border-t border-[var(--border)] px-3 pb-1 pt-3">
            <label className="flex cursor-pointer items-start justify-between gap-3">
              <span>
                <span className="flex items-center gap-1.5 text-sm font-bold text-[var(--ink)]">
                  <Icon name="moon" className="h-4 w-4 text-[var(--accent-strong)]" /> Calm mode
                </span>
                <span className="mt-0.5 block text-xs font-medium text-[var(--muted)]">No confetti, tilt, embers or floating cheers.</span>
              </span>
              <button
                type="button"
                role="switch"
                aria-checked={calm}
                aria-label="Calm mode"
                onClick={() => setCalm(!calm)}
                className={`relative mt-0.5 h-6 w-11 shrink-0 rounded-full transition-colors ${calm ? "bg-[var(--accent)]" : "bg-[var(--border-strong)]"}`}
              >
                <span className="absolute left-0.5 top-0.5 h-5 w-5 rounded-full bg-white shadow-sm transition-transform" style={{ transform: calm ? "translateX(20px)" : "translateX(0)" }} />
              </button>
            </label>
          </div>
        </div>
      )}
    </div>
  );
}

export default function Topbar({ onMenu, onSearch }) {
  const [raised, setRaised] = useState(false);
  const sentinel = useRef(null);
  useEffect(() => {
    const el = sentinel.current;
    if (!el || typeof IntersectionObserver === "undefined") return undefined;
    const io = new IntersectionObserver(([e]) => setRaised(!e.isIntersecting));
    io.observe(el);
    return () => io.disconnect();
  }, []);

  return (
    <>
      <span ref={sentinel} className="block h-px" aria-hidden />
      <header
        className={`sticky top-0 z-20 flex items-center gap-3 border-b bg-[var(--surface)]/90 px-4 py-3 backdrop-blur-md transition-[border-color,box-shadow] duration-300 sm:px-6 lg:px-8 ${
          raised ? "border-[var(--border)] shadow-[0_1px_12px_rgb(15_23_42/0.06)]" : "border-transparent"
        }`}
      >
        <div className="hidden sm:block lg:hidden">
          <Brand />
        </div>
        <PaletteTrigger onSearch={onSearch} />
        <div className="ml-auto flex items-center gap-2">
          <HeaderMenu />
          <ConnectionDot />
          <button type="button" onClick={onMenu} aria-label="Open menu" className="press rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] lg:hidden">
            <Icon name="menu" className="h-5 w-5" />
          </button>
        </div>
      </header>
    </>
  );
}
