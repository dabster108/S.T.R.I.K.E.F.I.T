// Display helpers for the fitness-outcome features. The numbers come from the backend
// (web/backend/fitness.js); this file only formats them and fills gaps for charts.

export const TIER_NAMES = { 1: "Tap", 2: "Light", 3: "Medium", 4: "Full power" };
export const isNum = (v) => typeof v === "number" && Number.isFinite(v);

export const formatKcal = (v) => (isNum(v) ? `${v < 10 ? v.toFixed(1) : Math.round(v)} kcal` : "—");
export const formatMinutes = (v) => (isNum(v) ? (v < 10 ? `${v.toFixed(1)} min` : `${Math.round(v)} min`) : "—");

export const DIRECTION_TEXT = {
  rose: "Effort rose as the fight went on.",
  level: "Effort stayed about level through the fight.",
  dropped: "Effort dropped off as the fight went on.",
};

// "today", "yesterday", "3 days ago".
export function daysAgo(n) {
  if (!isNum(n)) return "—";
  if (n === 0) return "today";
  if (n === 1) return "yesterday";
  return `${n} days ago`;
}

export const formatLongDate = (iso) => {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : d.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
};

const dayNum = (day) => Date.parse(`${day}T00:00:00Z`) / 864e5;
const dayFromNum = (n) => new Date(n * 864e5).toISOString().slice(0, 10);

// Sparse [{ day, sessions }] -> every day from the later of (first day, `span` days back) to today.
export function fillDays(byDay, today, span = 30) {
  if (!byDay.length) return [];
  const map = new Map(byDay.map((d) => [d.day, d.sessions]));
  const end = dayNum(today);
  const start = Math.max(dayNum(byDay[0].day), end - span + 1);
  return Array.from({ length: end - start + 1 }, (_, i) => {
    const day = dayFromNum(start + i);
    return { day, sessions: map.get(day) || 0 };
  });
}

// Sparse [{ weekStart, sessions }] -> every Monday-start week up to the current one.
export function fillWeeks(byWeek, today, span = 12) {
  if (!byWeek.length) return [];
  const map = new Map(byWeek.map((w) => [w.weekStart, w.sessions]));
  const todayN = dayNum(today);
  const thisWeek = todayN - ((new Date(`${today}T00:00:00Z`).getUTCDay() + 6) % 7);
  const start = Math.max(dayNum(byWeek[0].weekStart), thisWeek - 7 * (span - 1));
  const out = [];
  for (let n = start; n <= thisWeek; n += 7) out.push({ weekStart: dayFromNum(n), sessions: map.get(dayFromNum(n)) || 0 });
  return out;
}

export const shortDay = (day) => new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });
