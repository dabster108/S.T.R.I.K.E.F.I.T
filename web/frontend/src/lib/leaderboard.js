// Leaderboard boards and comparisons, computed from the sessions list (pure functions; tested by leaderboard.test.js).
//   weekly / all-time / per level: best finished run per player in the pool.
//   most improved: per player with at least MIN_IMPROVED_SESSIONS decided fights (won or lost; abandoned fights carry no
//     result and mostly a 0 score, so they are left out), the average of their latest third of fights against the average
//     of all their earlier ones. Two measures:
//       score  - each session's score divided by the median score of its level (all players), so levels compare;
//                the change is in points of the level median (+25 = a quarter of a typical run better).
//       effort - full-power hits / hits per session (sessions with at least MIN_HITS_FOR_EFFORT hits); the change is in
//                percentage points.
//   you vs last week: one player's fights, best and average score this week against last week.

export const MIN_IMPROVED_SESSIONS = 3;
export const MIN_HITS_FOR_EFFORT = 5;

const num = (v) => (Number.isFinite(Number(v)) ? Number(v) : null);
const time = (s) => new Date(s.startedAt || s.endedAt || 0).getTime();
const finished = (s) => s.status !== "live";
const decided = (s) => finished(s) && (s.outcome === "victory" || s.outcome === "defeat");
const mean = (xs) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : null);

/** Monday 00:00 (local time) of the week containing `date`. */
export function weekStart(date) {
  const d = new Date(date);
  d.setHours(0, 0, 0, 0);
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7));
  return d;
}

/** Best finished run per player, highest score first. */
export function bestPerPlayer(sessions) {
  const best = new Map();
  for (const s of sessions) {
    if (!finished(s)) continue;
    const cur = best.get(s.playerName);
    if (!cur || (num(s.score) || 0) > (num(cur.score) || 0)) best.set(s.playerName, s);
  }
  return [...best.values()].sort((a, b) => (num(b.score) || 0) - (num(a.score) || 0));
}

/** Sessions of the board's pool. board: "weekly" | "alltime" | "level" (with level). */
export function boardPool(sessions, board, { now = new Date(), level = null } = {}) {
  if (board === "weekly") {
    const from = weekStart(now).getTime();
    return sessions.filter((s) => time(s) >= from && time(s) <= now.getTime());
  }
  if (board === "level") return level && level !== "all" ? sessions.filter((s) => s.level === level) : sessions;
  return sessions;
}

function median(xs) {
  if (!xs.length) return null;
  const a = [...xs].sort((p, q) => p - q);
  const m = Math.floor(a.length / 2);
  return a.length % 2 ? a[m] : (a[m - 1] + a[m]) / 2;
}

/** Median score of decided fights per level (all players), zero scores left out. */
export function levelMedians(sessions) {
  const by = new Map();
  for (const s of sessions) {
    const v = num(s.score);
    if (!decided(s) || !v) continue;
    if (!by.has(s.level)) by.set(s.level, []);
    by.get(s.level).push(v);
  }
  return new Map([...by].map(([l, xs]) => [l, median(xs)]));
}

/**
 * Most improved players. measure: "score" | "effort".
 * -> [{ playerName, sessions, earlier, recent, change, measure }] sorted by change (largest first); players without
 *    enough sessions are left out.
 */
export function mostImproved(sessions, measure = "score", { min = MIN_IMPROVED_SESSIONS } = {}) {
  const medians = levelMedians(sessions);
  const byPlayer = new Map();
  for (const s of sessions) {
    if (!decided(s)) continue;
    let v = null;
    if (measure === "effort") {
      const hits = num(s.hitCount), full = num(s.fullSwings);
      if (hits !== null && hits >= MIN_HITS_FOR_EFFORT && full !== null) v = (full / hits) * 100;
    } else {
      const m = medians.get(s.level), sc = num(s.score);
      if (m && sc !== null) v = (sc / m) * 100;
    }
    if (v === null) continue;
    if (!byPlayer.has(s.playerName)) byPlayer.set(s.playerName, []);
    byPlayer.get(s.playerName).push({ t: time(s), v });
  }
  const out = [];
  for (const [playerName, list] of byPlayer) {
    if (list.length < min) continue;
    list.sort((a, b) => a.t - b.t);
    const k = Math.ceil(list.length / 3);
    const earlier = mean(list.slice(0, list.length - k).map((x) => x.v));
    const recent = mean(list.slice(list.length - k).map((x) => x.v));
    out.push({ playerName, sessions: list.length, earlier, recent, change: recent - earlier, measure });
  }
  return out.sort((a, b) => b.change - a.change);
}

/** One player's week against the week before. -> { thisWeek, lastWeek } each { fights, best, avg } */
export function youVsLastWeek(sessions, playerName, now = new Date()) {
  const start = weekStart(now).getTime();
  const prev = start - 7 * 864e5;
  const pick = (from, to) => {
    const list = sessions.filter((s) => s.playerName === playerName && finished(s) && time(s) >= from && time(s) < to);
    const scores = list.map((s) => num(s.score) || 0);
    return { fights: list.length, best: scores.length ? Math.max(...scores) : null, avg: mean(scores) };
  };
  return { thisWeek: pick(start, now.getTime() + 1), lastWeek: pick(prev, start) };
}
