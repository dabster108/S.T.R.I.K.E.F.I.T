// Rank data access: loads a player's finished sessions (the same facts the challenges and mastery use) and hands them to ranks.js.
// Read-only; parameterised queries only (engageStore.loadFacts).

const { query } = require("./db");
const engageStore = require("./engageStore");
const ranks = require("./ranks");
const cfg = require("./config/ranks");

const MAX_NAMES = 50;

/** GET /api/players/:name/rank -> { rank, effortPoints, nextRank, pointsToNext, progress, ladder, rule }; null for an unknown player. */
async function playerRank(name) {
  const facts = await engageStore.loadFacts({ player: name });
  if (!facts.length) {
    // a player whose only fight is still live is a Rookie with nothing yet; an unknown name is a 404
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  return { ok: true, playerName: name, ...ranks.rankInfo(ranks.totalPoints(facts)), fights: facts.length, ladder: ranks.ladder(), rule: ranks.ruleText() };
}

/** GET /api/ranks?players=a,b -> { ranks: { name: { rank, effortPoints, progress } } } for the names that have played (one query). */
async function ranksFor(names) {
  const list = [...new Set(names.map(String))].slice(0, MAX_NAMES);
  const out = {};
  if (!list.length) return out;
  const facts = await engageStore.loadFacts({ players: list });
  const byPlayer = new Map();
  for (const f of facts) byPlayer.set(f.playerName, [...(byPlayer.get(f.playerName) || []), f]);
  for (const [name, fs] of byPlayer) {
    const info = ranks.rankInfo(ranks.totalPoints(fs));
    out[name] = { rank: info.rank, effortPoints: info.effortPoints, progress: info.progress };
  }
  return out;
}

/** Did this finished session move its player up? -> { from, to, effortPoints } | null (the same answer however often it is asked). */
async function rankUpAfter(session) {
  const facts = await engageStore.loadFacts({ player: session.playerName });
  return ranks.rankUpFor(facts, session.id);
}

/** Activity-feed rows for the latest rank-ups of recently active players (derived from stored sessions, nothing is written). */
async function recentRankUps() {
  const { activePlayers, activeDays, recentDays } = cfg.FEED;
  const { rows } = await query(
    `SELECT player_name FROM sessions WHERE status = 'finished' AND ended_at >= $1 GROUP BY player_name ORDER BY max(ended_at) DESC LIMIT $2`,
    [new Date(Date.now() - activeDays * 864e5).toISOString(), activePlayers]
  );
  if (!rows.length) return [];
  const facts = await engageStore.loadFacts({ players: rows.map((r) => r.player_name) });
  const byPlayer = new Map();
  for (const f of facts) byPlayer.set(f.playerName, [...(byPlayer.get(f.playerName) || []), f]);
  return ranks.rankUpEntries(byPlayer, new Date(Date.now() - recentDays * 864e5).toISOString());
}

module.exports = { playerRank, ranksFor, rankUpAfter, recentRankUps, MAX_NAMES };
