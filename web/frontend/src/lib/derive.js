// Client-side aggregates, so every view can respect the active time range.
import { countOutcomes, isDecided } from "./outcomes.js";

const LEVEL_ORDER = ["Easy", "Medium", "Hard"];
const num = (v) => (Number.isFinite(Number(v)) ? Number(v) : 0);
export const sessionTime = (s) => new Date(s.endedAt || s.startedAt || 0).getTime();

export const RANGES = [
  { id: "all", label: "All time", ms: null },
  { id: "7d", label: "7 days", ms: 7 * 864e5 },
  { id: "24h", label: "24 hours", ms: 864e5 },
];

export function filterByRange(sessions, rangeId) {
  const range = RANGES.find((r) => r.id === rangeId);
  if (!range?.ms) return sessions;
  const cutoff = Date.now() - range.ms;
  return sessions.filter((s) => sessionTime(s) >= cutoff);
}

export function levelRank(level) {
  const i = LEVEL_ORDER.indexOf(level);
  return i === -1 ? 99 : i;
}

// winRate follows the one shared rule (lib/outcomes.js): victories / (victories + defeats), abandoned fights left out and
// reported as `abandoned`. It stays a number here (0 when nothing was decided; check `decided`) because the KPI tiles animate
// it. avgScore is the mean over decided fights, so abandoned zero-score fights don't drag it down.
export function summarize(sessions) {
  const n = sessions.length;
  const o = countOutcomes(sessions.map((s) => s.outcome));
  const { victories, defeats } = o;
  const decided = sessions.filter((s) => isDecided(s.outcome));
  const sum = (k, list = sessions) => list.reduce((a, s) => a + num(s[k]), 0);

  const chrono = [...sessions].sort((a, b) => sessionTime(a) - sessionTime(b));
  let bestStreak = 0;
  let run = 0;
  for (const s of chrono) {
    run = s.outcome === "victory" ? run + 1 : 0;
    bestStreak = Math.max(bestStreak, run);
  }
  let currentStreak = 0;
  for (let i = chrono.length - 1; i >= 0 && chrono[i].outcome === "victory"; i--) currentStreak++;

  return {
    sessions: n,
    victories,
    defeats,
    abandoned: o.abandoned,
    other: o.other,
    decided: o.decided,
    winRate: o.winRate ?? 0,
    winRateKnown: o.winRate !== null,
    totalScore: sum("score"),
    bestScore: sessions.reduce((m, s) => Math.max(m, num(s.score)), 0),
    avgScore: decided.length ? sum("score", decided) / decided.length : 0,
    totalHits: sum("hitCount"),
    totalDamage: sum("damageDealt"),
    totalTaps: sum("tapCount"),
    totalDurationSec: sum("durationSec"),
    avgDurationSec: n ? sum("durationSec") / n : 0,
    currentStreak,
    bestStreak,
    players: new Set(sessions.map((s) => s.playerName)).size,
  };
}

export function byLevel(sessions) {
  const levels = [...new Set(sessions.map((s) => s.level || "Unknown"))].sort((a, b) => levelRank(a) - levelRank(b));
  return levels.map((level) => ({ level, ...summarize(sessions.filter((s) => (s.level || "Unknown") === level)) }));
}

export function byPlayer(sessions) {
  const groups = new Map();
  for (const s of sessions) {
    if (!groups.has(s.playerName)) groups.set(s.playerName, []);
    groups.get(s.playerName).push(s);
  }
  return [...groups.entries()]
    .map(([playerName, list]) => {
      const chrono = [...list].sort((a, b) => sessionTime(a) - sessionTime(b));
      return {
        playerName,
        ...summarize(list),
        lastPlayedAt: chrono.length ? chrono[chrono.length - 1].endedAt : null,
        scores: chrono.map((s) => num(s.score)),
        favouriteLevel: byLevel(list).sort((a, b) => b.sessions - a.sessions)[0]?.level ?? "—",
      };
    })
    .sort((a, b) => b.bestScore - a.bestScore);
}

// Personal-best style records. Each is null when no session qualifies.
export function records(sessions) {
  const pick = (list, key, dir = "max") =>
    list.reduce((best, s) => {
      if (!best) return s;
      return dir === "max" ? (num(s[key]) > num(best[key]) ? s : best) : num(s[key]) < num(best[key]) ? s : best;
    }, null);
  const wins = sessions.filter((s) => s.outcome === "victory" && num(s.durationSec) > 0);
  const withHits = sessions.filter((s) => num(s.hitCount) > 0);
  const withDamage = sessions.filter((s) => num(s.damageDealt) > 0);
  return {
    highScore: pick(sessions, "score"),
    fastestWin: pick(wins, "durationSec", "min"),
    mostHits: pick(withHits, "hitCount"),
    mostDamage: pick(withDamage, "damageDealt"),
    longest: pick(sessions.filter((s) => num(s.durationSec) > 0), "durationSec"),
  };
}

// A session is "in progress" when its session_start event has no matching
// session_complete yet. Returns the newest such session with its live hit tally.
export function liveSession(events) {
  const completed = new Set(events.filter((e) => e.type === "session_complete").map((e) => e.sessionId));
  const start = events.find((e) => e.type === "session_start" && e.sessionId && !completed.has(e.sessionId));
  if (!start) return null;
  if (Date.now() - new Date(start.at).getTime() > 30 * 60 * 1000) return null; // abandoned
  const hits = events.filter((e) => e.type === "hit" && e.sessionId === start.sessionId);
  const lastHit = hits[0];
  return {
    sessionId: start.sessionId,
    playerName: start.playerName,
    startedAt: start.at,
    hits: hits.length,
    damage: hits.reduce((a, e) => a + num(e.payload?.damage), 0),
    score: num(lastHit?.payload?.score),
    lastHitAt: lastHit?.at ?? null,
  };
}

export function toCsv(sessions) {
  const cols = [
    "id",
    "playerName",
    "level",
    "outcome",
    "score",
    "hitCount",
    "damageDealt",
    "tapCount",
    "durationSec",
    "playerHpRemaining",
    "monsterHpRemaining",
    "startedAt",
    "endedAt",
    "source",
  ];
  const esc = (v) => {
    const s = String(v ?? "");
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  return [cols.join(","), ...sessions.map((s) => cols.map((c) => esc(s[c])).join(","))].join("\n");
}
