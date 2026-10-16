import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { calendarWeeks, level4 } from "../../lib/engage.js";
import { formatNumber } from "../../lib/format.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import { SegmentedControl } from "../ui/Controls.jsx";
import Icon from "../ui/Icon.jsx";

// GitHub-style activity calendar: one cell per local day, Monday at the top, the newest week on the right. Colour is
// relative to the busiest day in view, in the existing blue scale. Cells are one tab stop (roving focus, arrow keys move
// a day / a week), each with a tooltip; the same data is available as a plain list for screen readers and for anyone
// who prefers text. kcal is the project's estimate and says so.

const SHADES = ["var(--grid)", "var(--blue-100)", "var(--blue-300)", "var(--blue-500)", "var(--blue-800)"];
const MODES = [
  { id: "sessions", label: "Sessions" },
  { id: "kcal", label: "kcal (est.)" },
];
const DOW = ["Mon", "", "Wed", "", "Fri", "", ""];
const dayText = (day, opts = { weekday: "short", month: "short", day: "numeric" }) => new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { ...opts, timeZone: "UTC" });
const monthOf = (day) => new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", timeZone: "UTC" });
const describe = (d, site) =>
  `${dayText(d.date)}: ${d.sessions ? `${formatNumber(d.sessions)} session${d.sessions === 1 ? "" : "s"}` : "no sessions"}${d.kcal != null ? `, ${d.kcal} kcal (estimated)` : ""}${site && d.players ? `, ${d.players} player${d.players === 1 ? "" : "s"}` : ""}`;

export function ActivityCalendar({ data, site = false }) {
  const [mode, setMode] = useState("sessions");
  const [focus, setFocus] = useState(null); // index into data.days
  const [tip, setTip] = useState(null); // { i, x, y }
  const scroller = useRef(null);
  const grid = useRef(null);
  const days = data.days;
  const weeks = useMemo(() => calendarWeeks(days), [days]);
  const max = mode === "kcal" ? data.max.kcal : data.max.sessions;
  const active = days.filter((d) => d.sessions > 0);
  const current = focus ?? days.length - 1;

  // Start scrolled to the newest week on narrow screens.
  useEffect(() => {
    if (scroller.current) scroller.current.scrollLeft = scroller.current.scrollWidth;
  }, [days.length]);

  const move = (i) => {
    const next = Math.max(0, Math.min(days.length - 1, i));
    setFocus(next);
    const el = grid.current?.querySelector(`[data-i="${next}"]`);
    el?.focus();
    showTip(next, el);
  };
  const showTip = (i, el) => {
    if (!el || !scroller.current) return;
    const r = el.getBoundingClientRect();
    const box = scroller.current.getBoundingClientRect();
    setTip({ i, x: r.left - box.left + scroller.current.scrollLeft + r.width / 2, y: r.top - box.top });
  };
  const onKey = (e) => {
    const step = { ArrowUp: -1, ArrowDown: 1, ArrowLeft: -7, ArrowRight: 7, Home: -1e6, End: 1e6 }[e.key];
    if (step === undefined) return;
    e.preventDefault();
    move(current + step);
  };

  let lastMonth = null;
  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs font-semibold text-[var(--muted)]">
          {formatNumber(data.totals.sessions)} session{data.totals.sessions === 1 ? "" : "s"} on {formatNumber(data.totals.activeDays)} day{data.totals.activeDays === 1 ? "" : "s"}
          {data.totals.estimatedCalories != null ? ` · ${data.totals.estimatedCalories} kcal (estimated)` : ""} · last {data.weeks} weeks
        </p>
        <SegmentedControl label="Calendar shows" options={MODES} value={mode} onChange={setMode} />
      </div>

      <div ref={scroller} className="scrollbar-thin relative mt-3 overflow-x-auto pb-2" onPointerLeave={() => setTip(null)}>
        <div className="inline-flex gap-1.5">
          <div className="grid grid-rows-[1rem_repeat(7,0.75rem)] gap-[3px] pr-1 text-[0.6rem] font-semibold leading-3 text-[var(--muted)]" aria-hidden>
            <span />
            {DOW.map((d, i) => (
              <span key={i}>{d}</span>
            ))}
          </div>
          <div
            ref={grid}
            role="grid"
            aria-label={`Activity calendar, ${mode === "kcal" ? "estimated kcal" : "sessions"} per day. Arrow keys move between days.`}
            className="flex gap-[3px]"
            onKeyDown={onKey}
          >
            {weeks.map((week, w) => {
              const label = monthOf(week[0].date);
              const showMonth = label !== lastMonth;
              lastMonth = label;
              return (
                <div key={week[0].date} role="row" className="grid grid-cols-[0.75rem] grid-rows-[1rem_repeat(7,0.75rem)] gap-[3px]">
                  <span className="w-3 overflow-visible whitespace-nowrap text-[0.6rem] font-semibold leading-3 text-[var(--muted)]" aria-hidden>
                    {showMonth ? label : ""}
                  </span>
                  {week.map((d, k) => {
                    const i = w * 7 + k;
                    const v = mode === "kcal" ? d.kcal || 0 : d.sessions;
                    return (
                      <button
                        key={d.date}
                        type="button"
                        role="gridcell"
                        data-i={i}
                        tabIndex={i === current ? 0 : -1}
                        aria-label={describe(d, site)}
                        onFocus={(e) => {
                          setFocus(i);
                          showTip(i, e.currentTarget);
                        }}
                        onBlur={() => setTip(null)}
                        onPointerEnter={(e) => showTip(i, e.currentTarget)}
                        className="h-3 w-3 rounded-[3px] outline-none ring-[var(--accent)] ring-offset-1 focus-visible:ring-2"
                        style={{ background: SHADES[level4(v, max)] }}
                      />
                    );
                  })}
                </div>
              );
            })}
          </div>
        </div>
        {tip && days[tip.i] && (
          <span
            className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full whitespace-nowrap rounded-lg bg-[var(--ink)] px-2 py-1 text-xs font-semibold text-white shadow-lg"
            style={{ left: tip.x, top: tip.y - 4 }}
            aria-hidden
          >
            {describe(days[tip.i], site)}
          </span>
        )}
      </div>

      <div className="mt-2 flex flex-wrap items-center justify-between gap-3 text-[0.7rem] font-semibold text-[var(--muted)]">
        <span className="inline-flex items-center gap-1" aria-hidden>
          Less {SHADES.map((c) => <span key={c} className="h-2.5 w-2.5 rounded-[3px]" style={{ background: c }} />)} More
        </span>
        <span>Shade is relative to the busiest day shown. Weeks start on Monday ({data.timeZone}).</span>
      </div>

      <details className="mt-3 rounded-xl border border-[var(--border)] bg-[var(--surface-muted)] px-4 py-2 text-sm">
        <summary className="cursor-pointer text-xs font-bold text-[var(--ink-secondary)]">Show as a list ({active.length} active day{active.length === 1 ? "" : "s"})</summary>
        {active.length ? (
          <ul className="mt-2 max-h-56 space-y-0.5 overflow-y-auto text-xs font-medium text-[var(--ink-secondary)]">
            {[...active].reverse().map((d) => (
              <li key={d.date}>{describe(d, site)}</li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 text-xs font-medium text-[var(--muted)]">No sessions in this period.</p>
        )}
      </details>
    </div>
  );
}

function CalendarCard({ title, subtitle, state, site }) {
  const { data, error } = state;
  return (
    <Card className="overflow-hidden">
      <CardHeader title={title} subtitle={subtitle} />
      <div className="px-5 py-4">
        {data ? (
          data.totals.sessions ? (
            <ActivityCalendar data={data} site={site} />
          ) : (
            <EmptyState icon="calendar" title="No sessions in the last 26 weeks" description="Each day with a finished fight gets a square here." />
          )
        ) : error ? (
          <EmptyState icon="calendar" title="The calendar will appear here" description="It loads as soon as the server answers." />
        ) : (
          <Skeleton className="h-36 w-full" />
        )}
      </div>
      <p className="flex items-start gap-2 px-5 pb-4 text-xs font-medium text-[var(--muted)]">
        <Icon name="info" className="mt-px h-3.5 w-3.5 shrink-0" />
        Finished and abandoned fights count, by the local day they started. kcal is an estimate, not a measurement; days whose fights recorded no swings show no kcal.
      </p>
    </Card>
  );
}

export function PlayerCalendar({ name }) {
  const state = useApi(() => api.playerCalendar(name, 26), [name]);
  const refresh = useThrottledCallback(state.reload, 5000);
  useLiveMessages((type, payload) => type === "session_finished" && payload?.session?.playerName === name && refresh());
  const data = state.data && state.data.playerName === name ? state.data : null;
  return <CalendarCard title="Activity calendar" subtitle="Last 26 weeks, one square per day" state={{ ...state, data }} />;
}

export function SiteCalendar() {
  const state = useApi(() => api.siteCalendar(26), []);
  const refresh = useThrottledCallback(state.reload, 8000);
  useLiveMessages((type) => type === "session_finished" && refresh());
  return <CalendarCard title="Activity calendar, all players" subtitle="Last 26 weeks, one square per day" state={state} site />;
}
