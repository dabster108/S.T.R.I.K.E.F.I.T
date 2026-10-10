import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../../lib/api.js";
import { formatLongDate } from "../../lib/fitness.js";
import { formatNumber } from "../../lib/format.js";
import { useApi, useThrottledCallback } from "../../hooks/useApi.js";
import { useLiveMessages } from "../../live/LiveProvider.jsx";
import { Card, CardHeader } from "../ui/Card.jsx";
import { EmptyState, ProgressBar, Skeleton } from "../ui/Feedback.jsx";
import Icon from "../ui/Icon.jsx";
import Confetti from "../ui/Confetti.jsx";

const BADGE_ICON = {
  first_blood: "crosshair",
  full_power_50: "bolt",
  combo_10: "flame",
  flawless_medium: "shield",
  burn_100: "activity",
  streak_3: "calendar",
  weekly_goal: "target",
  goal_getter: "target",
  goal_week: "calendar",
};

// "YYYY-MM-DD" day strings from the API are calendar days, not instants: format them in UTC so no viewer time zone shifts them.
const dayLabel = (day) => new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });
const plural = (n, word) => `${formatNumber(n)} ${word}${n === 1 ? "" : "s"}`;

function LevelCard({ level, goalBonusXp = 0 }) {
  return (
    <Card className="flex flex-col p-5">
      <p className="t-kicker">Level and title</p>
      <p className="mt-2 flex flex-wrap items-baseline gap-x-2">
        <span className="t-figure-md text-[var(--ink)]">Level {level.level}</span>
        <span className="text-base font-extrabold text-[var(--accent-strong)]">{level.title}</span>
      </p>
      <div className="mt-4">
        <ProgressBar value={level.xpIntoLevel} max={level.xpForLevel} label={`Progress to level ${level.nextLevel}`} />
        <p className="mt-2 flex flex-wrap justify-between gap-x-3 text-xs font-semibold text-[var(--muted)]">
          <span className="tabular-nums">
            {formatNumber(level.xpIntoLevel)} / {formatNumber(level.xpForLevel)} XP
          </span>
          <span>
            {formatNumber(level.xpToNext)} XP to Level {level.nextLevel}
            {level.nextTitle !== level.title ? ` (${level.nextTitle})` : ""}
          </span>
        </p>
      </div>
      <p className="mt-auto pt-4 text-xs font-medium text-[var(--muted)]">{formatNumber(level.totalXp)} XP earned in total (incl. goal bonuses{goalBonusXp > 0 ? `: ${formatNumber(goalBonusXp)} XP` : ""}). {level.curve}.</p>
    </Card>
  );
}

function StreakCard({ streak }) {
  return (
    <Card className="flex flex-col p-5">
      <p className="t-kicker">Daily streak</p>
      <p className="mt-2 flex items-baseline gap-2">
        <span className="t-figure-md text-[var(--ink)]">{streak.current}</span>
        <span className="text-base font-extrabold text-[var(--ink-secondary)]">day{streak.current === 1 ? "" : "s"}</span>
        {streak.current > 0 && <Icon name="flame" className="h-5 w-5 self-center text-[var(--warn)]" />}
      </p>
      <div className="mt-3 flex flex-wrap gap-2">
        {streak.freezeUsed && (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--accent-soft)] px-2.5 py-1 text-xs font-bold text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">
            <Icon name="shield" className="h-3.5 w-3.5" /> Freeze used
          </span>
        )}
        {streak.freezePending && (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--warn-soft)] px-2.5 py-1 text-xs font-bold text-[var(--warn-ink)] ring-1 ring-inset ring-amber-200">
            <Icon name="clock" className="h-3.5 w-3.5" /> Play today to keep it
          </span>
        )}
      </div>
      <p className="mt-3 text-xs font-semibold text-[var(--ink-secondary)]">
        {streak.current > 0
          ? streak.freezeUsed
            ? `A missed day (${streak.freezeDays.map(dayLabel).join(", ")}) was forgiven.`
            : streak.freezePending
              ? "Yesterday was missed. A freeze covers it if you play today."
              : "Keep it going: play today or tomorrow."
          : streak.longest > 0
            ? "The streak is broken. Play a session to start a new one."
            : "Play on consecutive days to build a streak."}
      </p>
      <p className="mt-auto pt-4 text-xs font-medium text-[var(--muted)]">
        Longest: {plural(streak.longest, "day")}. {streak.rule}
      </p>
    </Card>
  );
}

function GoalCard({ goal }) {
  const r = 38;
  const circ = 2 * Math.PI * r;
  const pct = Math.min(1, goal.target ? goal.sessionsThisWeek / goal.target : 0);
  return (
    <Card className="flex flex-col p-5">
      <p className="t-kicker">Weekly goal</p>
      <div className="mt-3 flex items-center gap-4">
        <div className="relative h-24 w-24 shrink-0" role="img" aria-label={`${goal.sessionsThisWeek} of ${goal.target} sessions this week`}>
          <svg viewBox="0 0 96 96" className="h-full w-full -rotate-90">
            <circle cx="48" cy="48" r={r} fill="none" stroke="var(--grid)" strokeWidth="9" />
            <circle
              cx="48"
              cy="48"
              r={r}
              fill="none"
              stroke={goal.met ? "var(--good)" : "var(--accent)"}
              strokeWidth="9"
              strokeLinecap="round"
              strokeDasharray={circ}
              strokeDashoffset={circ * (1 - pct)}
              style={{ transition: "stroke-dashoffset 600ms var(--ease-out)" }}
            />
          </svg>
          <span className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="text-xl font-extrabold tabular-nums leading-none text-[var(--ink)]">
              {goal.sessionsThisWeek}
              <span className="text-sm font-bold text-[var(--muted)]">/{goal.target}</span>
            </span>
            <span className="mt-0.5 text-[0.65rem] font-semibold text-[var(--muted)]">sessions</span>
          </span>
        </div>
        <p className="min-w-0 text-sm font-bold text-[var(--ink)]">
          {goal.met ? (
            <span className="inline-flex items-center gap-1.5 text-[var(--good-ink)]">
              <Icon name="check" className="h-4 w-4" strokeWidth={2.6} /> Goal met this week
            </span>
          ) : (
            `${plural(goal.target - goal.sessionsThisWeek, "session")} to go this week`
          )}
        </p>
      </div>
      <p className="mt-auto pt-4 text-xs font-medium text-[var(--muted)]">
        Week of {dayLabel(goal.weekStart)} to {dayLabel(goal.weekEnd)}. {goal.definition}
      </p>
    </Card>
  );
}

function Trophy({ badge }) {
  const icon = BADGE_ICON[badge.id] || "trophy";
  const pct = badge.progress ? Math.min(100, (badge.progress.current / badge.progress.target) * 100) : 0;
  if (badge.unlocked) {
    return (
      <li className="rounded-2xl border border-[var(--accent-ring)] bg-[var(--accent-soft)] p-4">
        <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--accent)] text-white shadow-[var(--shadow-glow)]">
          <Icon name={icon} className="h-5 w-5" />
        </span>
        <p className="mt-3 text-sm font-extrabold text-[var(--ink)]">{badge.name}</p>
        <p className="mt-0.5 text-xs font-medium text-[var(--ink-secondary)]">{badge.description}</p>
        <p className="mt-2 text-xs font-semibold text-[var(--accent-strong)]">
          <span className="sr-only">Unlocked </span>
          {badge.sessionId ? (
            <Link to={`/dashboard/session/${encodeURIComponent(badge.sessionId)}`} className="press inline-flex items-center gap-1 rounded hover:underline">
              {formatLongDate(badge.unlockedAt)}
              <Icon name="arrowRight" className="h-3 w-3" />
            </Link>
          ) : (
            formatLongDate(badge.unlockedAt)
          )}
        </p>
      </li>
    );
  }
  return (
    <li className={`rounded-2xl border p-4 ${badge.tracking ? "border-dashed border-[var(--border-strong)] bg-[var(--surface-muted)]" : "border-[var(--border)] bg-[var(--surface)]"}`}>
      <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-gray-100 text-[var(--muted)]">
        <Icon name={icon} className="h-5 w-5" />
      </span>
      <p className="mt-3 text-sm font-extrabold text-[var(--ink-secondary)]">{badge.name}</p>
      <p className="mt-0.5 text-xs font-medium text-[var(--muted)]">{badge.description}</p>
      <p className="mt-2 text-xs font-bold text-[var(--muted)]">
        <Icon name="x" className="mr-1 inline h-3 w-3 align-[-1px]" strokeWidth={2.6} />
        Locked
      </p>
      {badge.tracking ? (
        <p className="mt-1 text-xs font-medium text-[var(--muted)]">{badge.trackingNote}</p>
      ) : (
        badge.progress && (
          <div className="mt-2">
            <ProgressBar value={pct} max={100} label={`${badge.name} progress`} />
            <p className="mt-1 text-xs font-semibold tabular-nums text-[var(--muted)]">
              {formatNumber(badge.progress.current)} / {formatNumber(badge.progress.target)}
            </p>
          </div>
        )
      )}
    </li>
  );
}

// Confetti when a badge unlocks: on a live reload that unlocks more than before, or on opening the page when a badge was
// earned in this player's latest finished fight (latestSessionId). Nothing under reduced motion (Confetti handles it).
export function AchievementsSection({ name, latestSessionId }) {
  const { data, error, reload } = useApi(() => api.playerAchievements(name), [name]);
  const [burst, setBurst] = useState(0);
  const seen = useRef({ name: null, unlocked: null });
  useEffect(() => {
    if (!data || data.playerName !== name) return;
    const prev = seen.current;
    const fresh = prev.name === name ? data.badgesUnlocked > prev.unlocked : Boolean(latestSessionId) && data.badges.some((b) => b.unlocked && b.sessionId === latestSessionId);
    seen.current = { name, unlocked: data.badgesUnlocked };
    if (fresh) setBurst((n) => n + 1);
  }, [data, name, latestSessionId]);
  const refresh = useThrottledCallback(reload, 4000);
  useLiveMessages((type, payload) => {
    if (type === "session_finished" && payload?.session?.playerName === name) refresh();
    if (type === "player_renamed" && payload.newName === name) reload();
  });

  if (!data) {
    return error ? (
      <Card>
        <EmptyState icon="trophy" title="Progress will appear here" description="It loads as soon as the server answers." />
      </Card>
    ) : (
      <div className="grid gap-4 md:grid-cols-3" aria-busy>
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-44 w-full" />
        ))}
      </div>
    );
  }
  return (
    <div className="space-y-6">
      <div className="stagger grid gap-4 lg:grid-cols-3">
        <LevelCard level={data.levelWithGoals || data.level} goalBonusXp={data.goalBonusXp || 0} />
        <StreakCard streak={data.streak} />
        <GoalCard goal={data.weeklyGoal} />
      </div>
      <Card className="relative overflow-hidden">
        <Confetti fire={burst} />
        <CardHeader title="Trophies" subtitle={`${data.badgesUnlocked} of ${data.badgesTotal} unlocked. Each one links to the session that earned it.`} />
        <ul className="stagger grid gap-3 p-4 sm:grid-cols-2 sm:p-5 xl:grid-cols-4">
          {data.badges.map((b) => (
            <Trophy key={b.id} badge={b} />
          ))}
        </ul>
        {data.badgesUnlocked === 0 && <p className="border-t border-[var(--border)] px-5 py-2.5 text-xs font-medium text-[var(--muted)]">Nothing unlocked yet. Finish a session and the first trophies appear here.</p>}
      </Card>
    </div>
  );
}

// Records a single session holds (GET /api/sessions/:id -> personalBestsSet).
export function NewPbChip({ records }) {
  if (!records?.length) return null;
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-50 px-2.5 py-1 text-xs font-extrabold text-amber-800 ring-1 ring-inset ring-amber-200">
      <Icon name="sparkles" className="h-3.5 w-3.5" /> New PB
    </span>
  );
}

export function PersonalBestsHeld({ records }) {
  if (!records?.length) return null;
  return (
    <Card className="flash-in border-amber-200 bg-amber-50/60">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-3 px-5 py-4">
        <p className="flex items-center gap-2 text-sm font-extrabold text-amber-900">
          <Icon name="sparkles" className="h-4 w-4" /> New personal best{records.length === 1 ? "" : "s"}
        </p>
        <ul className="flex flex-wrap gap-x-6 gap-y-2">
          {records.map((r) => (
            <li key={r.key} className="text-sm">
              <span className="font-semibold text-[var(--ink-secondary)]">{r.label}: </span>
              <span className="font-extrabold tabular-nums text-[var(--ink)]">
                {formatNumber(r.value)} <span className="font-semibold text-[var(--muted)]">{r.unit}</span>
              </span>
            </li>
          ))}
        </ul>
      </div>
    </Card>
  );
}
