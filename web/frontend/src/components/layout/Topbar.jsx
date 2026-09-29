import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Link } from "react-router-dom";
import { wakeStore } from "../../lib/api.js";
import { useConnection } from "../../live/LiveProvider.jsx";
import Icon from "../ui/Icon.jsx";
import { Brand } from "./Sidebar.jsx";

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

// Only shown while something is wrong; a healthy connection needs no badge
// (the sidebar's Connection item carries the steady-state dot).
const PROBLEMS = {
  connecting: { label: "Connecting…", cls: "text-[var(--warn-ink)]" },
  reconnecting: { label: "Reconnecting…", cls: "text-[var(--warn-ink)]" },
  offline: { label: "Server offline", cls: "text-[var(--bad)]" },
};

function ConnectionNotice() {
  const { connection } = useConnection();
  const waking = useSyncExternalStore(wakeStore.subscribe, wakeStore.isWaking);
  const problem = PROBLEMS[connection];
  return (
    <div role="status" aria-live="polite">
      {problem && (
        <Link
          to="/dashboard/connection"
          className={`fade-in inline-flex items-center gap-1.5 text-xs font-semibold hover:underline ${problem.cls}`}
        >
          <Icon name={connection === "offline" ? "offline" : "loader"} className={`h-3.5 w-3.5 ${connection === "offline" ? "" : "animate-spin motion-reduce:animate-none"}`} />
          {waking ? "Waking the server (up to a minute)" : problem.label}
        </Link>
      )}
    </div>
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
          <ConnectionNotice />
          <button type="button" onClick={onMenu} aria-label="Open menu" className="press rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] lg:hidden">
            <Icon name="menu" className="h-5 w-5" />
          </button>
        </div>
      </header>
    </>
  );
}
