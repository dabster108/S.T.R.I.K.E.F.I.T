// The rank ladder: Rookie, Brawler, Warrior, Guardian, earned with effort points. Pure functions (no database, no clock), so
// test/ranks.test.js can pin every boundary. ranksStore.js loads the sessions; config/ranks.js holds the numbers.
//
// A "fact" is one finished session as engage.sessionFacts builds it: { id, playerName, level, outcome, startedAt, endedAt,
// swings, fullSwings }. Everything here is a function of those facts in start order, so asking again later gives the same answer.

const { RANKS, POINTS, HARD_LEVEL } = require("./config/ranks");

const num = (v) => (Number.isFinite(Number(v)) ? Number(v) : 0);

/** Effort points one session is worth. */
function sessionPoints(fact) {
  const swings = Math.max(0, num(fact.swings));
  const full = Math.max(0, num(fact.fullSwings));
  const victory = fact.outcome === "victory";
  return swings * POINTS.perCountedSwing + full * POINTS.perFullPowerSwing + (victory ? POINTS.perVictory + (fact.level === HARD_LEVEL ? POINTS.perHardVictoryExtra : 0) : 0);
}

/** Facts in the order the player played them (start time, then id), the order every cumulative figure below depends on. */
const chronological = (facts) => [...facts].sort((a, b) => new Date(a.startedAt) - new Date(b.startedAt) || String(a.id).localeCompare(String(b.id)));

const totalPoints = (facts) => facts.reduce((a, f) => a + sessionPoints(f), 0);

/** The highest rung whose threshold the points reach. */
function rankFor(points) {
  const p = Math.max(0, num(points));
  let found = RANKS[0];
  for (const r of RANKS) if (p >= r.from) found = r;
  return found;
}

/**
 * Where a player stands: { rank, effortPoints, nextRank, pointsToNext, progress }.
 * progress is 0 to 1 within the current rung (exactly on a threshold = 0 on the new rung); 1 at the top rank, which has no next.
 */
function rankInfo(points) {
  const effortPoints = Math.max(0, Math.round(num(points)));
  const rank = rankFor(effortPoints);
  const next = RANKS[RANKS.indexOf(rank) + 1] || null;
  return {
    rank: { key: rank.key, name: rank.name, from: rank.from },
    effortPoints,
    nextRank: next ? { key: next.key, name: next.name, from: next.from } : null,
    pointsToNext: next ? next.from - effortPoints : null,
    progress: next ? (effortPoints - rank.from) / (next.from - rank.from) : 1,
  };
}

/** The ladder as the pages draw it. */
const ladder = () => RANKS.map((r) => ({ key: r.key, name: r.name, from: r.from }));

/**
 * Every time the player moved up, in order: [{ rank, points, sessionId, at }]. `at` is when that session ended. Rookie is where
 * everyone starts, so it is never "reached". Points are cumulative over the fights played so far.
 */
function rankTimeline(facts) {
  const out = [];
  let points = 0;
  let current = RANKS[0];
  for (const f of chronological(facts)) {
    points += sessionPoints(f);
    const now = rankFor(points);
    if (RANKS.indexOf(now) > RANKS.indexOf(current)) {
      // a big session can jump over a rung: every rung it passed counts as reached in that session
      for (const r of RANKS.slice(RANKS.indexOf(current) + 1, RANKS.indexOf(now) + 1)) out.push({ rank: { key: r.key, name: r.name }, points, sessionId: f.id, at: f.endedAt || f.startedAt });
      current = now;
    }
  }
  return out;
}

/**
 * Did this session move the player up a rank? `facts` are all the player's finished sessions (this one included).
 * "Before" is what they had when this session began: the sessions that started earlier. -> { from, to, effortPoints } | null
 */
function rankUpFor(facts, sessionId) {
  const ordered = chronological(facts);
  const i = ordered.findIndex((f) => f.id === sessionId);
  if (i < 0) return null;
  const before = rankFor(totalPoints(ordered.slice(0, i)));
  const afterPoints = totalPoints(ordered.slice(0, i + 1));
  const after = rankFor(afterPoints);
  if (RANKS.indexOf(after) <= RANKS.indexOf(before)) return null;
  return { from: { key: before.key, name: before.name }, to: { key: after.key, name: after.name }, effortPoints: Math.round(afterPoints) };
}

/**
 * Activity-feed rows for the rank-ups of the given players: [{ id, type: "rank_up", playerName, label, at, sessionId }].
 * The id is player + rank, so a session that is finished or re-reported twice, or a feed that is rebuilt, never gives a rank-up twice.
 * factsByPlayer: Map name -> facts. since: ISO time, older rank-ups are left out.
 */
function rankUpEntries(factsByPlayer, since = null) {
  const seen = new Set();
  const rows = [];
  for (const [name, facts] of factsByPlayer) {
    for (const step of rankTimeline(facts)) {
      const id = `rank:${name}:${step.rank.key}`;
      if (seen.has(id) || (since && new Date(step.at) < new Date(since))) continue;
      seen.add(id);
      rows.push({ id, type: "rank_up", playerName: name, label: step.rank.name, at: new Date(step.at).toISOString(), sessionId: step.sessionId });
    }
  }
  return rows;
}

/** The rule in words, built from the config so the text can never disagree with the numbers. */
function ruleText() {
  const p = POINTS;
  return `Effort points: ${p.perCountedSwing} per counted swing, ${p.perFullPowerSwing} more per full-power swing, ${p.perVictory} per victory and ${p.perHardVictoryExtra} more for a Level 3 victory.`;
}

module.exports = { RANKS, POINTS, ruleText, sessionPoints, totalPoints, rankFor, rankInfo, ladder, rankTimeline, rankUpFor, rankUpEntries, chronological };
