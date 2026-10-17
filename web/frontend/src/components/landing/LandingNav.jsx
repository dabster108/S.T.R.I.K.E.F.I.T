import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useScroll } from "../../hooks/motion.js";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

export const SECTIONS = [
  ["problem", "Problem"],
  ["how", "How it works"],
  ["levels", "Levels"],
  ["combat", "Combat"],
  ["numbers", "Numbers"],
  ["thesis", "Thesis"],
];

// Which section is under the middle of the viewport (for aria-current on the nav).
function useActiveSection() {
  const [active, setActive] = useState(null);
  useEffect(() => {
    const io = new IntersectionObserver(
      (items) => {
        for (const it of items) if (it.isIntersecting) setActive(it.target.id);
      },
      { rootMargin: "-45% 0px -50% 0px" }
    );
    // The sections below the first screen mount later (views/LandingRest.jsx): watch them once they exist.
    const watch = () => SECTIONS.forEach(([id]) => { const el = document.getElementById(id); if (el) io.observe(el); });
    watch();
    window.addEventListener("landing:sections", watch);
    return () => {
      window.removeEventListener("landing:sections", watch);
      io.disconnect();
    };
  }, []);
  return active;
}

export function Brand() {
  return (
    <span className="flex items-center gap-2.5">
      <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-extrabold text-white shadow-[var(--shadow-glow)]">SF</span>
      <span className="text-[0.95rem] font-extrabold tracking-tight text-[var(--ink)]">STRIKEFIT</span>
    </span>
  );
}

export default function LandingNav({ live }) {
  const [open, setOpen] = useState(false);
  const header = useRef(null);
  const bar = useRef(null);
  const menuBtn = useRef(null);
  const active = useActiveSection();

  useScroll((y) => {
    header.current?.classList.toggle("is-scrolled", y > 12);
    const max = document.documentElement.scrollHeight - window.innerHeight;
    if (bar.current) bar.current.style.transform = `scaleX(${max > 0 ? Math.min(1, y / max) : 0})`;
  });

  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e) => {
      if (e.key === "Escape") {
        setOpen(false);
        menuBtn.current?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  return (
    <header ref={header} className="site-nav sticky top-0 z-40 isolate">
      <div className="mx-auto flex h-[var(--nav-h)] max-w-6xl items-center gap-3 px-4 sm:gap-6 sm:px-6">
        <a href="#top" className="press rounded-xl" aria-label="STRIKEFIT, back to top">
          <Brand />
        </a>
        <nav aria-label="Sections" className="hidden flex-1 items-center justify-center gap-7 lg:flex">
          {SECTIONS.map(([id, label]) => (
            <a
              key={id}
              href={`#${id}`}
              aria-current={active === id ? "true" : undefined}
              className={`nav-link rounded text-sm font-semibold transition-colors hover:text-[var(--accent)] ${active === id ? "text-[var(--accent)]" : "text-[var(--ink-secondary)]"}`}
            >
              {label}
            </a>
          ))}
          <Link to="/monsters" className="nav-link rounded text-sm font-semibold text-[var(--ink-secondary)] transition-colors hover:text-[var(--accent)]">
            Monsters
          </Link>
        </nav>
        <div className="ml-auto flex items-center gap-2 lg:ml-0">
          <Link to="/dashboard/live" className={buttonClass("secondary", "md", "max-sm:hidden")}>
            <span className={`h-2 w-2 rounded-full ${live ? "status-live bg-[var(--good)]" : "bg-[var(--border-strong)]"}`} aria-hidden />
            Watch Live
          </Link>
          <Link to="/dashboard" className={buttonClass("primary", "md")}>
            <span className="sm:hidden">Dashboard</span>
            <span className="hidden sm:inline">Open Dashboard</span>
          </Link>
          <button
            ref={menuBtn}
            type="button"
            className="press rounded-lg p-2 text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)] lg:hidden"
            aria-expanded={open}
            aria-controls="mobile-sections"
            aria-label={open ? "Close menu" : "Open menu"}
            onClick={() => setOpen((o) => !o)}
          >
            <Icon name={open ? "x" : "menu"} className="h-5 w-5" />
          </button>
        </div>
      </div>
      <div className="absolute inset-x-0 bottom-0 h-[2px] overflow-hidden" aria-hidden>
        <div ref={bar} className="scroll-progress h-full w-full bg-[var(--accent)]" />
      </div>
      {open && (
        <nav id="mobile-sections" aria-label="Sections" className="pop-in border-t border-[var(--border)] bg-[var(--surface)] px-4 py-2 shadow-[var(--shadow-lift)] lg:hidden">
          {SECTIONS.map(([id, label]) => (
            <a
              key={id}
              href={`#${id}`}
              onClick={() => setOpen(false)}
              className="block rounded-lg px-3 py-2.5 text-sm font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
            >
              {label}
            </a>
          ))}
          <Link to="/monsters" onClick={() => setOpen(false)} className="block rounded-lg px-3 py-2.5 text-sm font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]">
            Monsters
          </Link>
          <Link to="/dashboard/live" className="block rounded-lg px-3 py-2.5 text-sm font-semibold text-[var(--ink-secondary)] hover:bg-[var(--surface-muted)] sm:hidden">
            Watch Live
          </Link>
        </nav>
      )}
    </header>
  );
}
