import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Link } from "react-router-dom";
import { wakeStore } from "../../lib/api.js";
import { useConnection } from "../../live/LiveProvider.jsx";
import Icon from "../ui/Icon.jsx";
import Tooltip from "../ui/Tooltip.jsx";
import { Brand } from "./Sidebar.jsx";

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

const STATES = {
  connecting: { label: "Connecting", dot: "bg-[var(--warn)] status-live", text: "text-[var(--warn-ink)]", tip: "Opening the live stream" },
  connected: { label: "Connected", dot: "bg-[var(--good)]", text: "text-[var(--good-ink)]", tip: "Live updates are streaming" },
  reconnecting: { label: "Reconnecting", dot: "bg-[var(--warn)] status-live", text: "text-[var(--warn-ink)]", tip: "Lost the stream, retrying automatically" },
  offline: { label: "Offline", dot: "bg-[var(--bad)]", text: "text-[var(--bad)]", tip: "The server isn't answering. Data will reload when it's back." },
};

// The only place the app talks about the backend's state.
function ConnectionIndicator() {
  const { connection } = useConnection();
  const waking = useSyncExternalStore(wakeStore.subscribe, wakeStore.isWaking);
  const s = STATES[connection] || STATES.connecting;
  const showWake = waking && connection !== "connected";
  const tip = showWake ? "Waking the server, this can take up to a minute" : s.tip;

  return (
    <div className="flex items-center gap-2" role="status" aria-live="polite">
      {showWake && (
        <span className="fade-in hidden items-center gap-1.5 text-xs font-semibold text-[var(--ink-secondary)] md:inline-flex">
          <Icon name="loader" className="h-3.5 w-3.5 animate-spin text-[var(--accent)] motion-reduce:animate-none" />
          Waking the server, this can take up to a minute
        </span>
      )}
      <Tooltip label={tip} side="bottom">
        <Link
          to="/dashboard/connection"
          className="press inline-flex items-center gap-1.5 rounded-full border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1 text-xs font-semibold text-[var(--ink-secondary)] hover:border-[var(--border-strong)] hover:text-[var(--ink)]"
        >
          <span className={`h-2 w-2 rounded-full ${s.dot}`} aria-hidden />
          <span className={connection === "connected" ? "" : s.text}>{s.label}</span>
          <span className="sr-only">{showWake ? ". Waking the server, this can take up to a minute" : ""}</span>
        </Link>
      </Tooltip>
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
          <ConnectionIndicator />
          <Link
            to="/"
            className="press hidden items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)] xl:inline-flex"
          >
            <Icon name="chevronLeft" className="h-3.5 w-3.5" />
            Landing page
          </Link>
          <button type="button" onClick={onMenu} aria-label="Open menu" className="press rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] lg:hidden">
            <Icon name="menu" className="h-5 w-5" />
          </button>
        </div>
      </header>
    </>
  );
}
