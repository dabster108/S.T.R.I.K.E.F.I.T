// Leaderboard rules, shared by GET /api/leaderboard (leaderboardRoute.js), the headset's in-game scoreboard and the web
// Leaderboard page. Pure functions, no database, no clock (tested by test/leaderboard.test.js).
//
// Ranking rule ("ranked by best score per level"):
//   pool     finished sessions (status <> 'live') with a score above 0, optionally one level only;
//   one row  per player: that player's best run in the pool (on "all" levels: their best run on any level);
//   order    score high to low; equal scores: the faster run (shorter durationSec; 0 / missing counts as slowest), then the
//            run that was finished first (endedAt), then the name. Ranks are 1..N in that order (no shared ranks).
//
// Public output: rank, display name, score, level, outcome, date (+ duration and session id for the web's click-through).
// Names are typed freely on the headset with no login or moderation, so the display name is trimmed, whitespace-collapsed,
// capped at DISPLAY_NAME_MAX characters, and replaced by HIDDEN_NAME when it contains a blocklisted word.

const LEVELS = ["Easy", "Medium", "Hard"];
const DISPLAY_NAME_MAX = 20;
const HIDDEN_NAME = "Hidden name";
const DEFAULT_LIMIT = 10;
const MAX_LIMIT = 100;

// Small on purpose: strong words only, matched inside the name after lower-casing, dropping everything but letters and
// digits, and undoing common digit / symbol swaps. Extend it per deployment with LEADERBOARD_BLOCKLIST="word1,word2".
const DEFAULT_BLOCKLIST = ["fuck", "shit", "cunt", "bitch", "nigg", "faggot", "whore", "slut", "rapist", "nazi", "retard", "asshole", "pussy", "wanker"];

const SWAPS = { 0: "o", 1: "i", 3: "e", 4: "a", 5: "s", 7: "t", "@": "a", $: "s", "!": "i" };

function blocklist(extra = process.env.LEADERBOARD_BLOCKLIST) {
  const words = new Set(DEFAULT_BLOCKLIST);
  for (const w of String(extra || "").split(",")) {
    const k = squash(w);
    if (k.length >= 3) words.add(k);
  }
  return [...words];
}

function squash(s) {
  return String(s || "")
    .toLowerCase()
    .replace(/[0-9@$!]/g, (c) => SWAPS[c] || c)
    .replace(/[^a-z0-9]/g, "");
}

/** The name as shown on any public board. */
function displayName(raw, words = blocklist()) {
  const clean = String(raw ?? "").replace(/\s+/g, " ").trim();
  if (!clean) return "Player";
  const key = squash(clean);
  if (words.some((w) => key.includes(w))) return HIDDEN_NAME;
  return clean.length > DISPLAY_NAME_MAX ? `${clean.slice(0, DISPLAY_NAME_MAX - 1).trimEnd()}…` : clean;
}

/** "all" | "Easy" | "Medium" | "Hard" from ?level= ("1"/"L1"/"easy" accepted). null = not a level. */
function parseLevel(v) {
  if (v === undefined || v === null || v === "") return "all";
  const s = String(v).trim().toLowerCase();
  if (s === "all" || s === "any") return "all";
  const byNumber = { 1: "Easy", l1: "Easy", 2: "Medium", l2: "Medium", 3: "Hard", l3: "Hard" };
  if (byNumber[s]) return byNumber[s];
  return LEVELS.find((l) => l.toLowerCase() === s) || null;
}

function parseLimit(v) {
  const n = Math.trunc(Number(v));
  return Number.isFinite(n) && n > 0 ? Math.min(n, MAX_LIMIT) : DEFAULT_LIMIT;
}

const num = (v) => (Number.isFinite(Number(v)) ? Number(v) : 0);
const duration = (s) => (num(s.durationSec) > 0 ? num(s.durationSec) : Infinity);
const ended = (s) => {
  const t = new Date(s.endedAt || s.startedAt || 0).getTime();
  return Number.isFinite(t) ? t : 0;
};

/** Negative when a ranks above b. */
function compareRuns(a, b) {
  return (
    num(b.score) - num(a.score) ||
    (duration(a) === duration(b) ? 0 : duration(a) < duration(b) ? -1 : 1) ||
    ended(a) - ended(b) ||
    String(a.playerName).localeCompare(String(b.playerName))
  );
}

const eligible = (s, level) => s && s.status !== "live" && num(s.score) > 0 && (level === "all" || s.level === level);

/**
 * Full ranking for one pool. sessions: API-shaped rows ({ id, playerName, level, outcome, score, durationSec, endedAt,
 * status }). -> [{ rank, playerName, name, score, level, outcome, date, durationSec, sessionId }]
 */
function rankBest(sessions, { level = "all", words = blocklist() } = {}) {
  const best = new Map();
  for (const s of sessions || []) {
    if (!eligible(s, level)) continue;
    const cur = best.get(s.playerName);
    if (!cur || compareRuns(s, cur) < 0) best.set(s.playerName, s);
  }
  return [...best.values()].sort(compareRuns).map((s, i) => ({
    rank: i + 1,
    playerName: s.playerName, // raw, for matching "you" only; stripped from public output by publicRows
    name: displayName(s.playerName, words),
    score: Math.round(num(s.score)),
    level: s.level,
    outcome: s.outcome,
    date: s.endedAt || s.startedAt || null,
    durationSec: num(s.durationSec) > 0 ? Math.round(num(s.durationSec)) : null,
    sessionId: s.id,
  }));
}

const asRun = (r) => ({ playerName: r.playerName, score: r.score, durationSec: r.durationSec, endedAt: r.date });

/**
 * Top `limit` rows for the public, plus the player's own rank when `player` is given. `run` ({ score, durationSec }) is a
 * fight the headset just finished: it may not be in a cached board yet, so `you` is that run's place when it beats the
 * player's stored best (display only; the public rows are never changed by it).
 */
function board(ranked, { limit = DEFAULT_LIMIT, player = null, run = null } = {}) {
  const key = player ? String(player).trim() : "";
  let mine = key ? ranked.find((r) => r.playerName === key) : null;
  if (key && run && num(run.score) > 0) {
    const fresh = { playerName: key, score: Math.round(num(run.score)), durationSec: num(run.durationSec) > 0 ? Math.round(num(run.durationSec)) : null, endedAt: new Date().toISOString() };
    if (!mine || compareRuns(fresh, asRun(mine)) < 0) {
      const ahead = ranked.filter((r) => r.playerName !== key && compareRuns(asRun(r), fresh) < 0).length;
      mine = { rank: ahead + 1, score: fresh.score, level: run.level || (mine && mine.level) || null, date: fresh.endedAt };
    }
  }
  const rows = ranked.slice(0, limit).map((r) => {
    const { playerName, ...pub } = r;
    return key && playerName === key ? { ...pub, you: true } : pub;
  });
  return {
    total: ranked.length,
    rows,
    you: key ? (mine ? { rank: mine.rank, score: mine.score, level: mine.level, date: mine.date, inTop: mine.rank <= limit } : null) : undefined,
  };
}

module.exports = {
  LEVELS,
  DISPLAY_NAME_MAX,
  HIDDEN_NAME,
  DEFAULT_LIMIT,
  MAX_LIMIT,
  DEFAULT_BLOCKLIST,
  RULE: "Best score per player (per level on a level board). Ties: the faster run, then the earlier one.",
  blocklist,
  displayName,
  parseLevel,
  parseLimit,
  compareRuns,
  rankBest,
  board,
};
