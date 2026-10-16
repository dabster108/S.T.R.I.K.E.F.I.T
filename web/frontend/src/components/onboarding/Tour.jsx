import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import Icon from "../ui/Icon.jsx";
import Button from "../ui/Button.jsx";
import { placePopover, scrollDelta } from "../../lib/tourLayout.js";

// Guided tour: five steps, a spotlight on each target, skippable at any point, fully keyboard driven
// (Right = next, Left = back, Esc = close; Tab stays inside the dialog; focus returns to where it was). Closing it any way
// (Done, Skip, X, Esc, a click outside) calls onClose; the "seen" flag is saved by the app shell, and only because
// onOpened has fired (see DashboardApp). The Guide button in the header menu reopens it. The spotlight moves with transform only.

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

const CARD_H_GUESS = 236; // until the popover has been measured

export default function Tour({ onClose, onOpened }) {
  const [i, setI] = useState(0);
  const [rect, setRect] = useState(null);
  const [cardH, setCardH] = useState(CARD_H_GUESS);
  const [size, setSize] = useState(() => ({ w: window.innerWidth, h: window.innerHeight }));
  const dialog = useRef(null);
  const location = useLocation();
  const navigate = useNavigate();
  const step = STEPS[i];

  const close = useCallback(() => onClose(), [onClose]);

  // The tour is really on screen from here on (the shell saves the flag only after this has fired). Remember what had
  // focus (the Guide button, or the page) and give it back when the tour goes away.
  useEffect(() => {
    const opener = document.activeElement;
    onOpened?.();
    return () => {
      if (opener && opener !== document.body && document.contains(opener) && typeof opener.focus === "function") opener.focus({ preventScroll: true });
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (location.pathname !== step.path) navigate(step.path);
  }, [step.path, location.pathname, navigate]);

  // The popover's real height drives where it can go (the text length changes per step and per width).
  useLayoutEffect(() => {
    if (dialog.current) setCardH(Math.round(dialog.current.getBoundingClientRect().height) || CARD_H_GUESS);
  }, [i, rect, size.w]);

  useEffect(() => {
    const on = () => setSize({ w: window.innerWidth, h: window.innerHeight });
    window.addEventListener("resize", on);
    return () => window.removeEventListener("resize", on);
  }, []);

  // Follow the target until the page has settled. The target can sit deep in a page that is still growing (charts and lists
  // loading), so one scroll is not enough: scroll it into a readable place (instant, never the page's smooth scrolling),
  // keep measuring every frame, correct again if the layout moved it, and stop once it has been still for ~20 frames.
  // A ResizeObserver restarts that when the page height changes later. Corrections are bounded.
  useLayoutEffect(() => {
    let raf = 0;
    let tries = 0;
    let corrections = 0;
    let quiet = 0;
    let lastY = -1;
    const read = (el) => {
      const r = el.getBoundingClientRect();
      return { x: r.left - PAD, y: r.top - PAD, w: r.width + PAD * 2, h: r.height + PAD * 2 };
    };
    const put = (next) => setRect((cur) => (cur && cur.x === next.x && cur.y === next.y && cur.w === next.w && cur.h === next.h ? cur : next));
    const tick = () => {
      const el = findTarget(step.target);
      if (!el) {
        if (tries++ < 30) raf = requestAnimationFrame(tick);
        else setRect(null);
        return;
      }
      const r = read(el);
      const scrolling = window.scrollY !== lastY;
      lastY = window.scrollY;
      const dy = scrollDelta({ y: r.y, h: r.h, vh: window.innerHeight, cardH });
      if (dy && corrections < 10 && !scrolling) {
        corrections++;
        quiet = 0;
        window.scrollBy({ top: dy, behavior: "instant" });
      } else if (!dy && !scrolling) quiet++;
      else quiet = 0;
      put(r);
      if (quiet < 20 && tries++ < 300) raf = requestAnimationFrame(tick);
    };
    tick();
    const restart = () => {
      cancelAnimationFrame(raf);
      quiet = 0;
      tries = 0;
      raf = requestAnimationFrame(tick);
    };
    const again = () => {
      const el = findTarget(step.target);
      if (el) put(read(el));
    };
    const ro = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(restart);
    ro?.observe(document.documentElement);
    window.addEventListener("resize", again);
    window.addEventListener("scroll", again, { passive: true });
    return () => {
      cancelAnimationFrame(raf);
      ro?.disconnect();
      window.removeEventListener("resize", again);
      window.removeEventListener("scroll", again);
    };
    // cardH is read once per step; re-running on every height change would scroll again
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  // Below the target when it fits, else above, else docked at the bottom with the spotlight clipped to what is visible
  // above the sheet (tall targets on phones); centred when there is no target (e.g. the sidebar is hidden on a phone).
  const cardW = Math.min(360, size.w - 32);
  // A target that is not on screen (still being scrolled to, or hidden) gets no spotlight: the popover is centred instead.
  const onScreen = rect && rect.y < size.h && rect.y + rect.h > 0 && rect.x < size.w && rect.x + rect.w > 0;
  const pos = placePopover({ rect: onScreen ? rect : null, vw: size.w, vh: size.h, cardW, cardH });
  const spot = pos.spot;

  return (
    <div className="fixed inset-0 z-[60]" onKeyDown={onKey}>
      {/* the dim layer with a hole: a box-shadow around the spotlight box, moved with transform only */}
      {spot ? (
        <div
          className="tour-spot pointer-events-none fixed left-0 top-0 rounded-xl"
          data-mode={pos.mode}
          style={{ width: spot.w, height: spot.h, transform: `translate3d(${spot.x}px, ${spot.y}px, 0)`, boxShadow: "0 0 0 9999px rgb(17 24 39 / 0.55)", outline: "2px solid var(--accent-ring)" }}
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
        data-tour-dialog
        className="fade-in fixed rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 shadow-[var(--shadow-lift)]"
        data-mode={pos.mode}
        style={{ width: cardW, top: pos.top, left: pos.left }}
      >
        <button
          type="button"
          onClick={close}
          aria-label="Close tour"
          className="press absolute right-3 top-3 flex h-8 w-8 items-center justify-center rounded-lg text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--ink)]"
        >
          <Icon name="x" className="h-4 w-4" />
        </button>
        <p className="t-kicker pr-8">
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
