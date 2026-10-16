import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import Icon from "../ui/Icon.jsx";
import Button from "../ui/Button.jsx";
import { markTourDone } from "./tourState.js";

// First-visit guided tour: five steps, a spotlight on each target, skippable at any point, fully keyboard driven
// (Enter / Right = next, Left = back, Esc = skip; focus stays in the dialog). Only the "done" flag is stored
// (localStorage). The Guide button in the header menu reopens it. The spotlight moves with transform only.

const STEPS = [
  { path: "/dashboard", target: '[data-tour="overview"]', title: "Overview", text: "The headline numbers for every fight, today's goals and challenges, and recent activity from all players." },
  { path: "/dashboard", target: 'a[href="/dashboard/live"]', title: "Live Arena", text: "Fights in progress, with both health bars moving as the headset reports hits. Viewers can send cheers." },
  { path: "/dashboard", target: 'a[href="/dashboard/players"]', title: "Player page", text: "Open a player for their journey and stars, their week, an activity calendar, goals and every session." },
  { path: "/dashboard", target: '[data-tour="challenges"]', title: "Challenges", text: "Three small challenges a day and one a week, each worth bonus XP. They reset tomorrow; there is nothing to lose." },
  { path: "/dashboard", target: 'a[href="/dashboard/leaderboard"]', title: "Leaderboard", text: "This week, all time, per level and most improved. Every number comes from recorded fights." },
];
const PAD = 8;

const visible = (el) => el && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";
function findTarget(selector) {
  return [...document.querySelectorAll(selector)].find(visible) || null;
}

export default function Tour({ onClose }) {
  const [i, setI] = useState(0);
  const [rect, setRect] = useState(null);
  const dialog = useRef(null);
  const location = useLocation();
  const navigate = useNavigate();
  const step = STEPS[i];

  const close = useCallback(() => {
    markTourDone();
    onClose();
  }, [onClose]);

  useEffect(() => {
    if (location.pathname !== step.path) navigate(step.path);
  }, [step.path, location.pathname, navigate]);

  // Measure the target (after the page settles), keep it measured on scroll / resize.
  useLayoutEffect(() => {
    let raf = 0;
    let tries = 0;
    const measure = () => {
      const el = findTarget(step.target);
      if (!el) {
        if (tries++ < 30) raf = requestAnimationFrame(measure);
        else setRect(null);
        return;
      }
      const r = el.getBoundingClientRect();
      if (r.top < 64 || r.bottom > window.innerHeight - 40) {
        el.scrollIntoView({ block: "center", behavior: "auto" });
        raf = requestAnimationFrame(measure);
        return;
      }
      setRect({ x: r.left - PAD, y: r.top - PAD, w: r.width + PAD * 2, h: r.height + PAD * 2 });
    };
    measure();
    const again = () => {
      const el = findTarget(step.target);
      if (!el) return;
      const r = el.getBoundingClientRect();
      setRect({ x: r.left - PAD, y: r.top - PAD, w: r.width + PAD * 2, h: r.height + PAD * 2 });
    };
    window.addEventListener("resize", again);
    window.addEventListener("scroll", again, { passive: true });
    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener("resize", again);
      window.removeEventListener("scroll", again);
    };
  }, [step.target, location.pathname]);

  // Focus the dialog on each step; keep Tab inside it.
  useEffect(() => {
    dialog.current?.querySelector("[data-primary]")?.focus();
  }, [i]);
  const onKey = (e) => {
    if (e.key === "Escape") {
      e.preventDefault();
      close();
    } else if (e.key === "ArrowRight") {
      e.preventDefault();
      i < STEPS.length - 1 ? setI(i + 1) : close();
    } else if (e.key === "ArrowLeft" && i > 0) {
      e.preventDefault();
      setI(i - 1);
    } else if (e.key === "Tab") {
      const f = [...dialog.current.querySelectorAll("button")];
      const first = f[0];
      const last = f[f.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  };

  // Popover below the target when it fits, else above; centred when there is no target (e.g. sidebar hidden on a phone).
  const vw = typeof window === "undefined" ? 1024 : window.innerWidth;
  const vh = typeof window === "undefined" ? 768 : window.innerHeight;
  const cardW = Math.min(360, vw - 32);
  let pos;
  if (rect) {
    const below = rect.y + rect.h + 12;
    const top = below + 220 < vh ? below : Math.max(16, rect.y - 12 - 220);
    const left = Math.min(vw - cardW - 16, Math.max(16, rect.x + (rect.w > cardW ? 0 : rect.w / 2 - cardW / 2)));
    pos = { top, left };
  } else pos = { top: Math.max(16, vh / 2 - 120), left: (vw - cardW) / 2 };

  return (
    <div className="fixed inset-0 z-[60]" onKeyDown={onKey}>
      {/* the dim layer with a hole: a box-shadow around the spotlight box, moved with transform only */}
      {rect ? (
        <div
          className="tour-spot pointer-events-none fixed left-0 top-0 rounded-xl"
          style={{ width: rect.w, height: rect.h, transform: `translate3d(${rect.x}px, ${rect.y}px, 0)`, boxShadow: "0 0 0 9999px rgb(17 24 39 / 0.55)", outline: "2px solid var(--accent-ring)" }}
          aria-hidden
        />
      ) : (
        <div className="pointer-events-none fixed inset-0 bg-[rgb(17_24_39/0.55)]" aria-hidden />
      )}
      <div className="fixed inset-0" onClick={close} aria-hidden />
      <div
        ref={dialog}
        role="dialog"
        aria-modal="true"
        aria-labelledby="tour-title"
        aria-describedby="tour-text"
        className="fade-in fixed rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-lift)]"
        style={{ width: cardW, top: pos.top, left: pos.left }}
      >
        <p className="t-kicker">
          Guide · step {i + 1} of {STEPS.length}
        </p>
        <h2 id="tour-title" className="mt-1.5 flex items-center gap-2 text-lg font-extrabold tracking-tight text-[var(--ink)]">
          <Icon name="compass" className="h-5 w-5 text-[var(--accent-strong)]" /> {step.title}
        </h2>
        <p id="tour-text" className="mt-1.5 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">
          {step.text}
          {!rect && <span className="mt-1 block text-xs text-[var(--muted)]">(Open the menu to find it on a small screen.)</span>}
        </p>
        <div className="mt-4 flex items-center justify-between gap-2">
          <Button variant="ghost" onClick={close}>
            Skip tour
          </Button>
          <span className="flex gap-2">
            {i > 0 && (
              <Button icon="chevronLeft" onClick={() => setI(i - 1)}>
                Back
              </Button>
            )}
            <Button variant="primary" data-primary onClick={() => (i < STEPS.length - 1 ? setI(i + 1) : close())}>
              {i < STEPS.length - 1 ? "Next" : "Done"}
            </Button>
          </span>
        </div>
        <p className="mt-3 text-[0.7rem] font-semibold text-[var(--muted)]">Arrow keys move, Esc skips. Reopen it any time from the Guide button.</p>
      </div>
    </div>
  );
}
