import { useMemo, useState } from "react";
import { api } from "../../lib/api.js";
import { formatNumber } from "../../lib/format.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

// Daily goal: one metric and one target for the player's local day (the same day the headset uses: the API's default
// Asia/Kathmandu day, so no ?tz= is sent). kcal is always labelled "(estimated)". There is no login: anyone who knows a
// name can set that player's goal, and the card says so.

export const METRICS = [
  { id: "kcal", label: "Calories", unit: "kcal (estimated)", short: "kcal (est.)", icon: "flame" },
  { id: "fullSwings", label: "Full-power swings", unit: "full-power swings", short: "full swings", icon: "bolt" },
  { id: "activeMinutes", label: "Active minutes", unit: "active minutes", short: "active min", icon: "timer" },
  { id: "xp", label: "XP", unit: "XP", short: "XP", icon: "trophy" },
];
const METRIC = Object.fromEntries(METRICS.map((m) => [m.id, m]));
export const metricLabel = (id) => METRIC[id]?.unit || id;
const fmt = (metric, v) => (metric === "kcal" ? (Math.round(v * 10) / 10).toString() : formatNumber(Math.round(v)));
const dayLabel = (day) => new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });

// Live updates for one player: goal_set / goal_met, a finished session (progress changed), a rename.
export function useDailyGoal(name) {
  const today = useApi(() => api.playerGoalToday(name), [name]);
  const history = useApi(() => api.playerGoals(name, 14), [name]);
  const refresh = useThrottledCallback(() => { today.reload(); history.reload(); }, 3000);
  useLiveMessages((type, payload) => {
    const mine = payload?.playerName === name || payload?.session?.playerName === name;
    if ((type === "goal_set" || type === "goal_met") && mine) refresh();
    if (type === "session_finished" && mine) refresh();
    if (type === "player_renamed" && payload.newName === name) refresh();
  });
  return { today, history, refresh };
}

function Ring({ percent, met, children }) {
  const r = 38;
  const circ = 2 * Math.PI * r;
  return (
    <div className="relative h-28 w-28 shrink-0" role="img" aria-label={`${percent}% of today's goal`}>
      <svg viewBox="0 0 96 96" className="h-full w-full -rotate-90">
        <circle cx="48" cy="48" r={r} fill="none" stroke="var(--grid)" strokeWidth="9" />
        <circle
          cx="48" cy="48" r={r} fill="none"
          stroke={met ? "var(--good)" : "var(--accent)"}
          strokeWidth="9" strokeLinecap="round"
          strokeDasharray={circ} strokeDashoffset={circ * (1 - Math.min(1, percent / 100))}
          style={{ transition: "stroke-dashoffset 600ms var(--ease-out)" }}
        />
      </svg>
      <span className="absolute inset-0 flex flex-col items-center justify-center text-center">{children}</span>
    </div>
  );
}

function Strip({ days }) {
  return (
    <div>
      <ul className="flex flex-wrap gap-1.5" aria-label="Last 14 days: goal met or missed">
        {days.map((d) => {
          // Today is still being played: an unmet goal today is "in progress", never "missed".
          const isToday = d.date === days[days.length - 1].date;
          const state = d.met ? "met" : d.goal ? (isToday ? "open" : "missed") : "none";
          const cls = state === "met" ? "bg-[var(--good)] text-white" : state === "missed" ? "bg-[var(--bad-soft)] text-[var(--bad)] ring-1 ring-inset ring-red-200" : state === "open" ? "bg-gray-100 text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-strong)]" : "bg-gray-100 text-[var(--muted)]";
          const tip = `${dayLabel(d.date)}: ${state === "met" ? "goal met" : state === "missed" ? `goal missed (${d.progress?.percent ?? 0}%)` : state === "open" ? `goal in progress (${d.progress?.percent ?? 0}%)` : "no goal set"}`;
          return (
            <li key={d.date} title={tip} className={`flex h-8 w-8 items-center justify-center rounded-lg text-[0.7rem] font-extrabold ${cls}`}>
              <span className="sr-only">{tip}</span>
              <span aria-hidden>{state === "met" ? <Icon name="check" className="h-3.5 w-3.5" strokeWidth={3} /> : state === "missed" ? <Icon name="x" className="h-3.5 w-3.5" strokeWidth={3} /> : state === "open" ? "…" : "·"}</span>
            </li>
          );
        })}
      </ul>
      <p className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-[0.7rem] font-semibold text-[var(--muted)]">
        <span>{dayLabel(days[0].date)}</span>
        <span className="ml-auto">{dayLabel(days[days.length - 1].date)} (today)</span>
      </p>
    </div>
  );
}

function Editor({ name, data, onSaved, onCancel }) {
  const [metric, setMetric] = useState(data.goal?.metric || "kcal");
  const range = data.ranges[metric];
  const preset = data.suggestions.presets[metric];
  const [target, setTarget] = useState(data.goal?.metric === metric ? data.goal.target : preset.standard);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const clamp = (v) => Math.min(range.max, Math.max(range.min, v));
  const pickMetric = (m) => { setMetric(m); setTarget(data.suggestions.presets[m].standard); setError(null); };
  const save = async () => {
    setSaving(true); setError(null);
    try { await api.setPlayerGoal(name, { metric, target }); onSaved(); }
    catch (err) { setError(err.message || "Couldn't save the goal."); }
    finally { setSaving(false); }
  };
  const step = range.step;
  return (
    <div className="mt-5 space-y-4 rounded-2xl border border-[var(--border)] bg-[var(--surface-muted)] p-4">
      <div>
        <p className="t-kicker">Measure</p>
        <div className="mt-2 flex flex-wrap gap-2" role="radiogroup" aria-label="Goal metric">
          {METRICS.map((m) => (
            <button key={m.id} type="button" role="radio" aria-checked={metric === m.id} onClick={() => pickMetric(m.id)}
              className={`press inline-flex min-h-10 items-center gap-1.5 rounded-xl border px-3 text-sm font-bold ${metric === m.id ? "border-[var(--accent-ring)] bg-[var(--accent-soft)] text-[var(--accent-strong)]" : "border-[var(--border)] bg-[var(--surface)] text-[var(--ink-secondary)]"}`}>
              <Icon name={m.icon} className="h-4 w-4" /> {m.label}
            </button>
          ))}
        </div>
      </div>
      <div>
        <p className="t-kicker">Target ({metricLabel(metric)})</p>
        <div className="mt-2 flex items-center gap-3">
          <button type="button" aria-label="Lower the target" className="press flex h-11 w-11 items-center justify-center rounded-xl border border-[var(--border)] bg-[var(--surface)] text-lg font-extrabold text-[var(--ink)]" onClick={() => setTarget((t) => clamp(Math.round((t - step) * 10) / 10))} disabled={target <= range.min}>−</button>
          <span className="min-w-[4.5rem] text-center text-2xl font-extrabold tabular-nums text-[var(--ink)]">{fmt(metric, target)}</span>
          <button type="button" aria-label="Raise the target" className="press flex h-11 w-11 items-center justify-center rounded-xl border border-[var(--border)] bg-[var(--surface)] text-lg font-extrabold text-[var(--ink)]" onClick={() => setTarget((t) => clamp(Math.round((t + step) * 10) / 10))} disabled={target >= range.max}>+</button>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          {[["easy", "Easy"], ["standard", "Standard"], ["hard", "Hard"]].map(([k, label]) => (
            <button key={k} type="button" onClick={() => setTarget(preset[k])}
              className={`press min-h-9 rounded-full border px-3 text-xs font-bold ${target === preset[k] ? "border-[var(--accent-ring)] bg-[var(--accent-soft)] text-[var(--accent-strong)]" : "border-[var(--border)] bg-[var(--surface)] text-[var(--ink-secondary)]"}`}>
              {label} {fmt(metric, preset[k])}
            </button>
          ))}
        </div>
        <p className="mt-2 text-xs font-medium text-[var(--muted)]">
          {data.suggestions.source === "data" ? `Suggestions come from the daily totals of ${data.suggestions.playersUsed} players with 3+ active days.` : "Starting suggestions (not enough player data yet)."}
          {" "}Allowed: {fmt(metric, range.min)} to {fmt(metric, range.max)}.
        </p>
      </div>
      {error && <p role="alert" className="text-sm font-semibold text-[var(--bad)]">{error}</p>}
      <div className="flex flex-wrap gap-2">
        <Button variant="primary" size="md" onClick={save} disabled={saving}>{saving ? "Saving…" : "Save today's goal"}</Button>
        <Button size="md" onClick={onCancel} disabled={saving}>Cancel</Button>
      </div>
    </div>
  );
}

export function DailyGoalCard({ name }) {
  const { today, history, refresh } = useDailyGoal(name);
  const [editing, setEditing] = useState(false);
  const data = today.data && today.data.playerName === name ? today.data : null;
  const days = history.data?.days;
  const goalStreak = history.data?.goalStreak;

  const body = useMemo(() => {
    if (!data) return null;
    const g = data.goal;
    const m = g ? g.metric : null;
    const p = data.progress;
    return { g, m, p };
  }, [data]);

  if (!data) {
    return today.error ? (
      <Card><EmptyState icon="target" title="Today's goal will appear here" description="It loads as soon as the server answers." /></Card>
    ) : (
      <Skeleton className="h-56 w-full" />
    );
  }
  const { g, m, p } = body;
  const mt = m ? METRIC[m] : null;
  return (
    <Card>
      <CardHeader
        title="Today's goal"
        subtitle={`${dayLabel(data.date)} · set from the headset or here · ${data.bonusXpOnCompletion} XP when it is met`}
        actions={g && !g.completedAt && !editing ? <Button icon="settings" onClick={() => setEditing(true)}>Change</Button> : null}
      />
      <div className="p-5">
        {g ? (
          <div className="flex flex-wrap items-center gap-5">
            <Ring percent={p.percent} met={data.completed}>
              <span className="text-xl font-extrabold tabular-nums leading-none text-[var(--ink)]">{p.percent}%</span>
              <span className="mt-0.5 text-[0.65rem] font-semibold text-[var(--muted)]">of today</span>
            </Ring>
            <div className="min-w-0 flex-1">
              <p className="t-kicker">{mt.label}</p>
              <p className="mt-1 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">
                {fmt(m, p.value)} <span className="text-base font-bold text-[var(--muted)]">/ {fmt(m, g.target)} {mt.short}</span>
              </p>
              <p className="mt-2 text-sm font-bold">
                {data.completed ? (
                  <span className="inline-flex items-center gap-1.5 text-[var(--good-ink)]"><Icon name="check" className="h-4 w-4" strokeWidth={2.6} /> Goal reached! +{formatNumber(data.bonusXp)} XP</span>
                ) : (
                  <span className="text-[var(--ink-secondary)]">{fmt(m, Math.max(0, g.target - p.value))} {mt.short} to go · +{data.bonusXpOnCompletion} XP when met</span>
                )}
              </p>
              {goalStreak && goalStreak.current > 0 && (
                <p className="mt-1 inline-flex items-center gap-1.5 text-xs font-bold text-[var(--accent-strong)]"><Icon name="flame" className="h-3.5 w-3.5" /> Goal streak: {goalStreak.current} day{goalStreak.current === 1 ? "" : "s"}</p>
              )}
            </div>
          </div>
        ) : (
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="min-w-0">
              <p className="text-sm font-bold text-[var(--ink)]">No goal set for today.</p>
              <p className="mt-1 text-xs font-medium text-[var(--muted)]">
                {data.totals.sessions ? `${formatNumber(data.totals.sessions)} session${data.totals.sessions === 1 ? "" : "s"} played today: ${fmt("kcal", data.totals.kcal)} kcal (estimated), ${formatNumber(data.totals.fullSwings)} full-power swings, ${formatNumber(data.totals.activeMinutes)} active minutes, ${formatNumber(data.totals.xp)} XP.` : "No sessions played yet today."}
              </p>
            </div>
            {!editing && <Button variant="primary" size="md" icon="target" onClick={() => setEditing(true)}>Set today's goal</Button>}
          </div>
        )}
        {editing && <Editor name={name} data={data} onCancel={() => setEditing(false)} onSaved={() => { setEditing(false); today.reload(); history.reload(); }} />}
        {days && days.length > 0 && (
          <div className="mt-5 border-t border-[var(--border)] pt-4">
            <p className="t-kicker mb-2">Last 14 days</p>
            <Strip days={days} />
          </div>
        )}
        <p className="mt-4 text-xs font-medium text-[var(--muted)]">
          {data.estimateNote} The day follows {data.timeZone}. Progress counts finished sessions that started today.
        </p>
      </div>
    </Card>
  );
}

// Overview page: a card with how many players met today's goal (the site-wide number from /api/overview, no names).
export function GoalsMetToday() {
  const { data, error, reload } = useApi(() => api.overview(), []);
  const refresh = useThrottledCallback(reload, 4000);
  useLiveMessages((type) => (type === "goal_set" || type === "goal_met" || type === "session_finished") && refresh());
  const g = data?.goalsToday;
  return (
    <Card className="flex flex-wrap items-center justify-between gap-x-6 gap-y-2 px-5 py-4">
      <div className="flex min-w-0 items-center gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[var(--accent-soft)] text-[var(--accent-strong)]">
          <Icon name="target" className="h-5 w-5" />
        </span>
        <div className="min-w-0">
          <p className="t-kicker">Goals met today</p>
          <p className="mt-0.5 text-sm font-bold text-[var(--ink)]">
            {!data && !error ? "Loading…" : !g ? "Not available yet" : g.goalsSet ? `${formatNumber(g.goalsMet)} of ${formatNumber(g.goalsSet)} player${g.goalsSet === 1 ? "" : "s"} who set a goal` : "No goals set today"}
          </p>
        </div>
      </div>
      <p className="text-xs font-medium text-[var(--muted)]">Goals have no login: they are set by name. Calorie goals use estimated kcal.</p>
    </Card>
  );
}
