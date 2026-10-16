// Pure helpers for the engagement features (no React, no fetch), tested by engage.test.js with `npm test`.
// Nothing here invents a number: every function takes API data and returns null / an empty state when it is missing.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
export const LEVEL_KEYS = ["Easy", "Medium", "Hard"];
const LEVEL_NUMBER = { Easy: 1, Medium: 2, Hard: 3 };
const levelLabel = (l) => (LEVEL_NUMBER[l] ? `Level ${LEVEL_NUMBER[l]}` : String(l || ""));

// ---------------------------------------------------------------------------
// Names are typed by people: strip control characters, collapse spaces, cap the length. React escapes the rest.
// ---------------------------------------------------------------------------
export const NAME_MAX = 40; // the API's limit
export function cleanName(raw, max = NAME_MAX) {
  const s = String(raw ?? "")
    .replace(/[\u0000-\u001f\u007f​-‏‪-‮⁦-⁩]/g, "")
    .replace(/\s+/g, " ")
    .trim();
  return s.slice(0, max);
}
// For display in tight places: cleaned, then cut with an ellipsis.
export function shortName(raw, max = 24) {
  const s = cleanName(raw);
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
}

// ---------------------------------------------------------------------------
// "How to beat this score": design facts from Assets/Documentation/project_progress.md and context.md
// (2026-10-02 to 2026-10-09 entries), not invented. Tuning is in progress, so they describe the game as built.
// ---------------------------------------------------------------------------
export const BEAT_TIPS = {
  common: [
    "Damage grows with real swing speed, from 1.2 m/s up to the game's 8 m/s cap, so a fast, full swing does far more than a tap.",
    "Keep hits under 2 seconds apart to build a combo.",
    "When the monster lowers its guard, the next hit is an opening worth 10% extra damage.",
  ],
  Easy: ["Full power starts at 5.0 m/s on Level 1. The best result panels go to wins under 35 s that cost at most 15% of your health."],
  Medium: [
    "Full power starts at 4.6 m/s on Level 2.",
    "Its guard learns a repeated swing angle, so vary the direction of your swings.",
  ],
  Hard: [
    "Aim for the glowing green right forearm: it is the boss's only weak point.",
    "The Earthsplitter Slam can't be blocked: step about two metres sideways during its wind-up and it misses.",
    "Thrown axes can be dodged, or batted away with a fast swing into them.",
  ],
};
export const beatTips = (level) => [...(BEAT_TIPS[level] || []), ...BEAT_TIPS.common];

// ---------------------------------------------------------------------------
// Next step: simple, ordered rules (labelled as such in the UI). Never guilt, never "hurry".
// ---------------------------------------------------------------------------
// mastery: achievements.mastery.levels; rest: challenges.rest; challenges: { daily, weekly } cards.
export function nextStep({ mastery, rest, challenges } = {}) {
  if (rest?.show) {
    return { kind: "rest", title: "Take a rest day", text: rest.text || "A rest day is part of training too." };
  }
  const levels = mastery || [];
  if (!levels.length) return null;
  const firstWin = levels.find((l) => l.wins === 0 && l.played === 0);
  const anyCleared = levels.some((l) => l.wins > 0);
  // An unplayed level, once the one before it has been cleared (or Level 1 for a newcomer).
  if (firstWin) {
    const i = LEVEL_KEYS.indexOf(firstWin.level);
    const prev = i > 0 ? levels.find((l) => l.level === LEVEL_KEYS[i - 1]) : null;
    if (i === 0 || (prev && prev.wins > 0)) {
      return { kind: "unplayed", level: firstWin.level, title: `Try ${levelLabel(firstWin.level)}`, text: i === 0 ? "Start with Level 1: it is the gentlest fight." : `You have cleared ${levelLabel(LEVEL_KEYS[i - 1])}. ${levelLabel(firstWin.level)} is next.` };
    }
  }
  // The nearest missing star on a level that is already cleared.
  const candidates = [];
  for (const l of levels) {
    if (!l.wins) continue;
    const t = l.thresholds || {};
    const [, low, fast] = l.stars || [];
    if (low && !low.earned && isNum(l.best?.lowestDamagePercent) && isNum(t.maxDamagePercent)) {
      candidates.push({ level: l.level, gap: (l.best.lowestDamagePercent - t.maxDamagePercent) / t.maxDamagePercent, text: `Your best ${levelLabel(l.level)} win cost ${Math.round(l.best.lowestDamagePercent)}% of your health. A win losing at most ${t.maxDamagePercent}% earns the second star.` });
    }
    if (fast && !fast.earned && isNum(l.best?.fastestClearSec) && isNum(t.clearWithinSec)) {
      candidates.push({ level: l.level, gap: (l.best.fastestClearSec - t.clearWithinSec) / t.clearWithinSec, text: `Your fastest ${levelLabel(l.level)} win took ${Math.round(l.best.fastestClearSec)} s. Under ${t.clearWithinSec} s (or ${Math.round((t.fullPowerRatio || 0) * 100)}% full-power swings) earns the third star.` });
    }
  }
  if (candidates.length) {
    const c = candidates.sort((a, b) => a.gap - b.gap)[0];
    return { kind: "star", level: c.level, title: `Nearest star: ${levelLabel(c.level)}`, text: c.text };
  }
  // A level played but not yet won.
  const unwon = levels.find((l) => l.played > 0 && l.wins === 0);
  if (unwon) return { kind: "clear", level: unwon.level, title: `Clear ${levelLabel(unwon.level)}`, text: `You have fought it ${unwon.played} time${unwon.played === 1 ? "" : "s"}; a first win earns its first star.` };
  // An open challenge, if any.
  const open = [...(challenges?.daily || []), ...(challenges?.weekly || [])].find((c) => !c.completed && !c.met);
  if (open) return { kind: "challenge", title: open.title, text: `${open.description}. ${open.resets}.` };
  // Otherwise a score to beat on the best level.
  const best = [...levels].filter((l) => isNum(l.best?.score) && l.best.score > 0).sort((a, b) => LEVEL_KEYS.indexOf(b.level) - LEVEL_KEYS.indexOf(a.level))[0];
  if (best) return { kind: "score", level: best.level, title: `Beat ${Math.round(best.best.score).toLocaleString()} on ${levelLabel(best.level)}`, text: "Every star on this level is earned. Your best score is the one to beat." };
  return anyCleared ? null : { kind: "unplayed", level: "Easy", title: "Try Level 1", text: "Start with Level 1: it is the gentlest fight." };
}

// ---------------------------------------------------------------------------
// Rivalry (Compare page): wins per level by best score, and the best-score gap
// ---------------------------------------------------------------------------
// a, b: player profiles (api.player) -> { levels: [{ level, a, b, winner, gap }], winsA, winsB, ties, bestGap } | null
export function rivalry(a, b) {
  if (!a || !b) return null;
  const best = (p, level) => {
    const l = (p.byLevel || []).find((x) => x.level === level);
    return l && l.sessions ? Number(l.bestScore) || 0 : null;
  };
  const levels = LEVEL_KEYS.map((level) => {
    const sa = best(a, level);
    const sb = best(b, level);
    if (sa === null && sb === null) return null;
    const winner = sa === null ? "b" : sb === null ? "a" : sa > sb ? "a" : sb > sa ? "b" : "tie";
    return { level, a: sa, b: sb, winner, gap: sa !== null && sb !== null ? Math.abs(sa - sb) : null };
  }).filter(Boolean);
  const ta = Number(a.totals?.bestScore) || 0;
  const tb = Number(b.totals?.bestScore) || 0;
  return {
    levels,
    winsA: levels.filter((l) => l.winner === "a").length,
    winsB: levels.filter((l) => l.winner === "b").length,
    ties: levels.filter((l) => l.winner === "tie").length,
    bestGap: { a: ta, b: tb, gap: Math.abs(ta - tb), leader: ta > tb ? "a" : tb > ta ? "b" : "tie" },
  };
}

// ---------------------------------------------------------------------------
// Replay ghost: "vs your best run on this level"
// ---------------------------------------------------------------------------
// Median, not mean: a single recorded outlier (some stored speeds exceed the game's 8 m/s cap) would swing a mean.
const median = (xs) => {
  const v = [...xs].sort((a, b) => a - b);
  if (!v.length) return null;
  const m = v.length >> 1;
  return v.length % 2 ? v[m] : (v[m - 1] + v[m]) / 2;
};
// current: { durationSec, outcome, hits: [{ speed }] }; best: api.playerBest() -> one line, or null when nothing to compare.
export function ghostSummary(current, best) {
  if (!best?.best || !current) return null;
  const b = best.best;
  const parts = [];
  if (current.outcome === "victory" && b.outcome === "victory" && current.durationSec > 0 && b.durationSec > 0) {
    const d = Math.round((current.durationSec - b.durationSec) * 10) / 10;
    parts.push(d === 0 ? "Same clear time" : `${Math.abs(d)} s ${d < 0 ? "faster" : "slower"} to clear`);
  }
  const ca = median((current.hits || []).map((h) => h.speed).filter(isNum));
  const ba = median((best.samples?.hits || []).map((h) => h.speed).filter(isNum));
  if (isNum(ca) && isNum(ba) && ba > 0) {
    const pct = Math.round(((ca - ba) / ba) * 100);
    parts.push(pct === 0 ? "same median swing speed" : `median swing speed ${pct > 0 ? "up" : "down"} ${Math.abs(pct)}%`);
  }
  return parts.length ? parts.join(", ") : null;
}

// Value of a step series at time t (last sample at or before t). samples sorted by t.
export function valueAt(samples, t, key) {
  let v = null;
  for (const s of samples || []) {
    if (s.t > t) break;
    v = s[key];
  }
  return v;
}

// ---------------------------------------------------------------------------
// Calendar: cell intensity 0-4 (relative to the busiest day in view), and a text description for screen readers
// ---------------------------------------------------------------------------
export function level4(value, max) {
  if (!isNum(value) || value <= 0 || !isNum(max) || max <= 0) return 0;
  return Math.max(1, Math.min(4, Math.ceil((value / max) * 4)));
}
export function calendarWeeks(days) {
  // Monday-first columns. days[0] is always a Monday (the API aligns the grid).
  const weeks = [];
  for (let i = 0; i < days.length; i += 7) weeks.push(days.slice(i, i + 7));
  return weeks;
}
