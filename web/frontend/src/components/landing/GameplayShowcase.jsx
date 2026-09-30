import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { LEVELS } from "../../lib/game.js";
import { useOnScreen, useReducedMotion } from "../../hooks/motion.js";
import Icon from "../ui/Icon.jsx";
import { Reveal, SectionHead } from "./shared.jsx";

// ─── Screenshot walkthrough: edit shots and captions here ────────────────────
// Put the real captures in web/frontend/public/showcase/ under these exact file
// names (Vite serves /public from the site root: /showcase/<file>). Any file that
// isn't there yet shows the "Screenshot coming soon" placeholder instead.
// Captions only state what the game does; keep them that way.
export const SHOWCASE_SHOTS = [
  {
    file: "showcase-1-start.jpg",
    tab: "Start",
    title: "Pick a name, press Start",
    caption: "Point at your name and pull the trigger, then press Start. The fight appears on the dashboard as it begins.",
    alt: "The name picker and Start button in front of the arena",
  },
  {
    file: "showcase-2-combat.jpg",
    tab: "Swing",
    title: "Swing to hit",
    caption: `Dual hammers on Level 1, dual swords on Level 2. Swings under ${LEVELS.Easy.swing.min} m/s don't count; faster ones hit harder.`,
    alt: "Swinging at the monster in the arena",
  },
  {
    file: "showcase-3-defend.jpg",
    tab: "Read",
    title: "Read the monster",
    caption: "Every attack winds up before it lands, and while it raises its guard your hits deal 70% less damage.",
    alt: "The monster winding up an attack",
  },
  {
    file: "showcase-4-impact.jpg",
    tab: "Impact",
    title: "Feel the impact",
    caption: "Damage numbers appear where the hit lands, with a heavier impact sound for bigger swings.",
    alt: "Damage numbers popping up where a hit landed",
  },
  {
    file: "showcase-5-result.jpg",
    tab: "Result",
    title: "Win or lose",
    caption: "The fight ends in victory or defeat, and the result lands on the dashboard within a second.",
    alt: "The end-of-fight result screen",
  },
];

const SHOWCASE_DIR = `${import.meta.env.BASE_URL}showcase/`;
const CYCLE_MS = 6000; // time on each shot while auto-cycling
// ─────────────────────────────────────────────────────────────────────────────

// file -> "ok" | "missing". Preloads off-DOM so a missing file never shows a broken image.
function useShotStatus(files) {
  const [status, setStatus] = useState({});
  const key = files.join("|");
  useEffect(() => {
    let alive = true;
    for (const file of files) {
      const img = new Image();
      img.onload = () => alive && setStatus((s) => ({ ...s, [file]: img.naturalWidth > 0 ? "ok" : "missing" }));
      img.onerror = () => alive && setStatus((s) => ({ ...s, [file]: "missing" }));
      img.src = SHOWCASE_DIR + file;
    }
    return () => {
      alive = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return status;
}

function Placeholder({ shot, index }) {
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-gradient-to-br from-[var(--accent-soft)] via-[var(--surface)] to-[var(--surface-muted)] px-6 text-center">
      {/* faint grid so the empty frame still reads as a screen */}
      <div
        className="absolute inset-0 opacity-70"
        style={{
          backgroundImage: "linear-gradient(var(--grid) 1px, transparent 1px), linear-gradient(90deg, var(--grid) 1px, transparent 1px)",
          backgroundSize: "2.5rem 2.5rem",
        }}
        aria-hidden
      />
      <span className="relative flex h-11 w-11 items-center justify-center rounded-xl sm:h-14 sm:w-14 sm:rounded-2xl bg-[var(--surface)] text-[var(--accent)] shadow-[var(--shadow-lift)] ring-1 ring-[var(--border)]" aria-hidden>
        <Icon name="headset" className="h-5 w-5 sm:h-7 sm:w-7" />
      </span>
      <div className="relative">
        <p className="t-kicker">Screenshot coming soon</p>
        <p className="mt-2 text-base font-extrabold tracking-tight text-[var(--ink)] sm:text-lg">
          <span className="t-index mr-2 align-middle">{String(index + 1).padStart(2, "0")}</span>
          {shot.title}
        </p>
        {import.meta.env.DEV && <p className="readout mt-2 text-xs font-semibold text-[var(--muted)]">public/showcase/{shot.file}</p>}
      </div>
    </div>
  );
}

export default function GameplayShowcase() {
  const reduced = useReducedMotion();
  const [active, setActive] = useState(0);
  const [hovered, setHovered] = useState(false);
  const [focused, setFocused] = useState(false);
  const [stopped, setStopped] = useState(false);
  const [viewRef, inView] = useOnScreen();
  const status = useShotStatus(SHOWCASE_SHOTS.map((s) => s.file));
  const list = useRef(null);
  const tabs = useRef([]);
  const [dot, setDot] = useState(null);

  const autoplay = !reduced && !stopped;
  const running = autoplay && inView && !hovered && !focused;
  const count = SHOWCASE_SHOTS.length;
  const shot = SHOWCASE_SHOTS[active];

  // The dot rides a rail: along the bottom of the row on phones, down the left of the list on desktop.
  useLayoutEffect(() => {
    const measure = () => {
      const el = tabs.current[active];
      if (el) setDot({ x: el.offsetLeft + el.offsetWidth / 2, y: el.offsetTop + el.offsetHeight / 2 });
    };
    measure();
    const ro = new ResizeObserver(measure);
    if (list.current) ro.observe(list.current);
    return () => ro.disconnect();
  }, [active]);

  // Keep the active tab visible in the horizontal row without scrolling the page.
  useEffect(() => {
    const row = list.current;
    const el = tabs.current[active];
    if (!row || !el || row.scrollWidth <= row.clientWidth) return;
    // on phones the tab's offsetParent is the track inside the row, so add the track's own offset
    const left = el.offsetLeft + (el.offsetParent === row ? 0 : el.offsetParent?.offsetLeft ?? 0);
    row.scrollTo({ left: left - (row.clientWidth - el.offsetWidth) / 2, behavior: reduced ? "auto" : "smooth" });
  }, [active, reduced]);

  const select = (i) => setActive((i + count) % count);

  const onKey = (e) => {
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
    if (e.key === "Home" || e.key === "End" || step) {
      e.preventDefault();
      const next = e.key === "Home" ? 0 : e.key === "End" ? count - 1 : (active + step + count) % count;
      select(next);
      tabs.current[next]?.focus();
    }
  };

  return (
    <section id="gameplay" aria-labelledby="gameplay-title" className="mx-auto max-w-6xl px-4 pb-24 pt-24 sm:px-6 lg:pb-32 lg:pt-32">
      <SectionHead id="gameplay-title" index="03" eyebrow="Gameplay" title="Inside the headset" layout="split">
        One fight, start to finish. Pick a moment to see it from the player&apos;s side.
      </SectionHead>

      <Reveal className="mt-12 lg:mt-16">
        <div
          ref={viewRef}
          className="grid gap-5 lg:grid-cols-12 lg:gap-10"
          onPointerEnter={() => setHovered(true)}
          onPointerLeave={() => setHovered(false)}
          onFocus={() => setFocused(true)}
          onBlur={(e) => !e.currentTarget.contains(e.relatedTarget) && setFocused(false)}
        >
          {/* screenshot frame */}
          <div className="lg:col-span-8">
            <div
              id="showcase-panel"
              role="tabpanel"
              aria-labelledby={`showcase-tab-${active}`}
              className="relative aspect-video overflow-hidden rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-lift)]"
            >
              {SHOWCASE_SHOTS.map((s, i) => {
                const on = i === active;
                return (
                  <div key={s.file} className={`showcase-shot absolute inset-0 ${on ? "is-active" : ""}`} aria-hidden={!on}>
                    {status[s.file] === "ok" ? (
                      <img src={SHOWCASE_DIR + s.file} alt={on ? s.alt : ""} className="h-full w-full object-cover" loading={i ? "lazy" : "eager"} decoding="async" />
                    ) : (
                      <Placeholder shot={s} index={i} />
                    )}
                  </div>
                );
              })}

              <div className="pointer-events-none absolute left-3 top-3 flex items-center gap-2 sm:left-4 sm:top-4">
                <span className="readout rounded-full bg-[var(--surface)]/90 px-2.5 py-1 text-xs font-bold text-[var(--ink-secondary)] shadow-sm ring-1 ring-[var(--border)] backdrop-blur">
                  <span className="text-[var(--accent)]">{String(active + 1).padStart(2, "0")}</span> / {String(count).padStart(2, "0")}
                </span>
              </div>

              {!reduced && (
                <button
                  type="button"
                  onClick={() => setStopped((s) => !s)}
                  aria-label={stopped ? "Play the walkthrough" : "Pause the walkthrough"}
                  className="press absolute right-3 top-3 flex h-8 w-8 items-center justify-center rounded-full bg-[var(--surface)]/90 text-[var(--ink-secondary)] shadow-sm ring-1 ring-[var(--border)] backdrop-blur hover:text-[var(--accent)] sm:right-4 sm:top-4"
                >
                  <Icon name={stopped ? "play" : "pause"} className="h-3.5 w-3.5" strokeWidth={2.4} />
                </button>
              )}
            </div>
            {/* caption under the frame on phones; desktop shows it inside the active tab */}
            <p className="measure mt-4 text-[0.95rem] font-medium leading-relaxed text-[var(--ink-secondary)] lg:hidden" aria-live="polite">
              <span className="font-bold text-[var(--ink)]">{shot.title}.</span> {shot.caption}
            </p>
          </div>

          {/* hotspots */}
          <div className="order-first min-w-0 lg:order-none lg:col-span-4">
            <div
              ref={list}
              role="tablist"
              aria-label="Gameplay moments"
              aria-orientation="vertical"
              onKeyDown={onKey}
              className="scrollbar-thin relative -mx-4 overflow-x-auto px-4 pb-4 sm:mx-0 sm:px-0 lg:overflow-visible lg:pb-0 lg:pl-8"
            >
              {/* On phones the track is as wide as all the tabs, so the rail and dot span the whole scrolling row
                  (positioned against the row itself they stopped at the visible edge). Desktop positions against the list. */}
              <div role="presentation" className="relative flex w-max gap-2 lg:static lg:w-auto lg:flex-col lg:gap-0">
                {/* rail + sliding dot */}
                <span className="pointer-events-none absolute -bottom-3 left-0 right-0 h-px bg-[var(--border)] lg:bottom-6 lg:left-[0.6rem] lg:right-auto lg:top-6 lg:h-auto lg:w-px" aria-hidden />
                {dot && (
                  <span className="showcase-dot pointer-events-none absolute" style={{ "--dot-x": `${dot.x}px`, "--dot-y": `${dot.y}px` }} aria-hidden>
                    <span className="block h-2.5 w-2.5 rounded-full bg-[var(--accent)] shadow-[var(--shadow-glow)] ring-4 ring-[var(--accent-soft)]" />
                  </span>
                )}

                {SHOWCASE_SHOTS.map((s, i) => {
                  const on = i === active;
                  return (
                    <button
                      key={s.file}
                      ref={(el) => (tabs.current[i] = el)}
                      id={`showcase-tab-${i}`}
                      type="button"
                      role="tab"
                      aria-selected={on}
                      aria-controls="showcase-panel"
                      tabIndex={on ? 0 : -1}
                      onClick={() => select(i)}
                      className={`press group relative shrink-0 rounded-xl px-3.5 py-2.5 text-left lg:w-full lg:px-4 lg:py-4 ${
                        on ? "bg-[var(--surface)] shadow-[var(--shadow-card)] ring-1 ring-[var(--border)]" : "hover:bg-[var(--surface)]/60"
                      }`}
                    >
                      <span className="flex items-baseline gap-2.5">
                        <span className={`t-index ${on ? "" : "text-[var(--muted)]"}`}>{String(i + 1).padStart(2, "0")}</span>
                        <span className={`whitespace-nowrap text-sm font-bold lg:hidden ${on ? "text-[var(--ink)]" : "text-[var(--ink-secondary)]"}`}>{s.tab}</span>
                        <span className={`hidden text-[0.95rem] font-bold tracking-tight lg:inline ${on ? "text-[var(--ink)]" : "text-[var(--ink-secondary)] group-hover:text-[var(--ink)]"}`}>{s.title}</span>
                      </span>
                      <span className={`showcase-caption hidden lg:grid ${on ? "is-open" : ""}`}>
                        <span className="min-h-0 overflow-hidden">
                          <span className="measure-tight block pt-2 text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{s.caption}</span>
                        </span>
                      </span>
                      {/* time left on this shot; its end advances the walkthrough */}
                      {on && autoplay && (
                        <span className="absolute inset-x-3.5 bottom-1 h-0.5 overflow-hidden rounded-full lg:inset-x-4 lg:bottom-2" aria-hidden>
                          <span
                            key={active}
                            className="showcase-progress block h-full rounded-full bg-[var(--accent)]"
                            style={{ animationDuration: `${CYCLE_MS}ms`, animationPlayState: running ? "running" : "paused" }}
                            onAnimationEnd={() => select(active + 1)}
                          />
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>
        </div>
      </Reveal>
    </section>
  );
}
