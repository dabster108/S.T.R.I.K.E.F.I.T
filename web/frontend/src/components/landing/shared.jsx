import { useEffect, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { useReveal } from "../../hooks/motion.js";

export const GITHUB = "https://github.com/dabster108/S.T.R.I.K.E.F.I.T";
export const THESIS = "Design, Development, and Evaluation of a Virtual Reality-Based Gamified Fitness System";

export const card = "rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-[var(--shadow-card)]";

export function Reveal({ as: Tag = "div", className = "", delay = 0, from, children, ...rest }) {
  const ref = useReveal();
  return (
    <Tag ref={ref} data-reveal={from} className={`reveal ${className}`} style={delay ? { "--reveal-delay": `${delay}ms` } : undefined} {...rest}>
      {children}
    </Tag>
  );
}

export function SectionHead({ id, eyebrow, title, children, center = false }) {
  return (
    <Reveal className={`max-w-2xl ${center ? "mx-auto text-center" : ""}`}>
      <p className="t-eyebrow">{eyebrow}</p>
      <h2 id={id} className="t-h2 mt-3 text-[var(--ink)]">
        {title}
      </h2>
      {children && <p className="mt-4 text-lg font-medium leading-relaxed text-[var(--ink-secondary)]">{children}</p>}
    </Reveal>
  );
}

// Retries a failing request quietly: 5 s, 10 s, 20 s… up to a minute. Nothing is ever shown for a failure.
function useQuietPoll(fetcher, { every = 20000 } = {}) {
  const [data, setData] = useState(null);
  const failures = useRef(0);
  useEffect(() => {
    let alive = true;
    let timer;
    const run = () =>
      fetcher()
        .then((d) => {
          if (!alive) return;
          failures.current = 0;
          setData(d);
          if (every) timer = setTimeout(run, every);
        })
        .catch(() => {
          if (!alive) return;
          failures.current += 1;
          timer = setTimeout(run, Math.min(60000, 5000 * 2 ** (failures.current - 1)));
        });
    run();
    return () => {
      alive = false;
      clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return data;
}

// null until /api/live answers; the pill stays hidden until then (and if it never does).
export function useLiveStatus() {
  const d = useQuietPoll(api.liveQuiet, { every: 20000 });
  return d ? { state: d.count ? "live" : "idle", count: d.count || 0 } : null;
}

// { state: "loading" | "ok" | "empty", stats }. Slow or failing → "empty" after 4 s, then
// keeps retrying and swaps in the real numbers the moment they arrive.
export function usePublicStats() {
  const stats = useQuietPoll(api.publicStatsQuiet, { every: 60000 });
  const [waited, setWaited] = useState(false);
  useEffect(() => {
    const t = setTimeout(() => setWaited(true), 4000);
    return () => clearTimeout(t);
  }, []);
  if (stats && stats.totalSessions > 0) return { state: "ok", stats };
  if (stats || waited) return { state: "empty", stats };
  return { state: "loading", stats: null };
}
