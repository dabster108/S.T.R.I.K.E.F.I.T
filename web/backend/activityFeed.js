// Activity feed: level cleared, new personal record, daily goal met, challenge met, badge unlocked. Derived from stored
// rows only (sessions, daily_goals, challenge_completions, and the badge rules in progression.js); nothing is written.
// Rows carry a player name, an event type and a short label, never scores or health.
//
// Badges are not stored anywhere, so they are re-derived with the achievements rules for recently active players only
// (bounded below). A badge that the rules cannot date, or a player outside that window, is simply not shown.

const { query } = require("./db");
const analytics = require("./analytics");
const ranksStore = require("./ranksStore");

const FEED_MAX = 20;
const CACHE_MS = 30_000;
const BADGE_PLAYERS_MAX = 8;    // at most this many players are re-evaluated for badges
const BADGE_ACTIVE_DAYS = 14;   // ... those with a finished session in the last 14 days
const BADGE_RECENT_DAYS = 30;   // ... and only badges unlocked in the last 30 days are listed
const LEVEL_NUMBER = { Easy: 1, Medium: 2, Hard: 3 };
const levelLabel = (level) => (LEVEL_NUMBER[level] ? `Level ${LEVEL_NUMBER[level]}` : String(level || ""));
const iso = (v) => new Date(v).toISOString();

async function build() {
  const since = new Date(Date.now() - BADGE_RECENT_DAYS * 864e5).toISOString();
  const [cleared, records, goalsMet, challengesDone, active, rankUps] = await Promise.all([
    query(`SELECT id, player_name, level, ended_at FROM sessions WHERE status = 'finished' AND outcome = 'victory' ORDER BY ended_at DESC LIMIT $1`, [FEED_MAX]),
    // A new personal record: a higher score than every earlier finished fight of the same player on the same level
    // (a first fight on a level has nothing to beat, so it is not a record).
    query(
      `SELECT id, player_name, level, ended_at FROM (
         SELECT id, player_name, level, ended_at, score,
                max(score) OVER (PARTITION BY player_name, level ORDER BY started_at, seq ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS prev_best
         FROM sessions WHERE status = 'finished'
       ) x WHERE prev_best IS NOT NULL AND score > prev_best ORDER BY ended_at DESC LIMIT $1`,
      [FEED_MAX]
    ),
    query(`SELECT player_name, goal_date::text AS day, completed_at FROM daily_goals WHERE completed_at IS NOT NULL ORDER BY completed_at DESC LIMIT $1`, [FEED_MAX]),
    query(`SELECT player_name, challenge_key, title, completed_at FROM challenge_completions ORDER BY completed_at DESC LIMIT $1`, [FEED_MAX]),
    query(
      `SELECT player_name, max(ended_at) AS last FROM sessions WHERE status = 'finished' AND ended_at >= $1 GROUP BY player_name ORDER BY last DESC LIMIT $2`,
      [new Date(Date.now() - BADGE_ACTIVE_DAYS * 864e5).toISOString(), BADGE_PLAYERS_MAX]
    ),
    ranksStore.recentRankUps().catch(() => []), // "<Name> reached <Rank>": derived from the sessions, so it can never appear twice
  ]);
  const rows = [
    ...cleared.rows.map((r) => ({ id: `clear:${r.id}`, type: "level_cleared", playerName: r.player_name, label: levelLabel(r.level), at: iso(r.ended_at), sessionId: r.id })),
    ...records.rows.map((r) => ({ id: `pb:${r.id}`, type: "personal_record", playerName: r.player_name, label: levelLabel(r.level), at: iso(r.ended_at), sessionId: r.id })),
    ...goalsMet.rows.map((r) => ({ id: `goal:${r.player_name}:${r.day}`, type: "goal_met", playerName: r.player_name, label: "Daily goal", at: iso(r.completed_at) })),
    ...challengesDone.rows.map((r) => ({ id: `ch:${r.player_name}:${r.challenge_key}`, type: "challenge_done", playerName: r.player_name, label: r.title, at: iso(r.completed_at) })),
    ...rankUps,
  ];
  const badgeLists = await Promise.all(active.rows.map((p) => analytics.playerAchievements(p.player_name, {}).catch(() => null)));
  for (const a of badgeLists) {
    if (!a) continue;
    for (const b of a.badges) {
      if (!b.unlocked || !b.unlockedAt || b.unlockedAt < since) continue;
      rows.push({ id: `badge:${a.playerName}:${b.id}`, type: "badge_unlocked", playerName: a.playerName, label: b.name, at: iso(b.unlockedAt), sessionId: b.sessionId || null });
    }
  }
  rows.sort((a, b) => (a.at < b.at ? 1 : a.at > b.at ? -1 : 0));
  return rows.slice(0, FEED_MAX);
}

let cache = null;
let building = null;
let dirty = false;
function rebuild() {
  if (!building) {
    dirty = false;
    building = build()
      .then((items) => { cache = { at: Date.now(), items }; })
      .finally(() => { building = null; });
  }
  return building;
}
async function feed(limit) {
  const n = Math.min(FEED_MAX, Math.max(1, Math.trunc(Number(limit)) || FEED_MAX));
  if (!cache || dirty || building) await rebuild();        // first read, or something new happened: wait for the fresh list
  else if (Date.now() - cache.at > CACHE_MS) rebuild().catch(() => {}); // merely old: answer now, refresh behind
  return { ok: true, items: cache.items.slice(0, n), max: FEED_MAX, note: "Derived from stored sessions, goals and challenges; badges are re-derived for recently active players only." };
}
// Something feed-worthy just happened (a fight finished, a goal or challenge was met): rebuild now, so the clients
// that refetch on the matching stream message get the new row.
function invalidate() { dirty = true; rebuild().catch((err) => console.error("[feed] rebuild failed:", err.message)); }
function warm() { return rebuild().catch(() => {}); }

module.exports = { feed, invalidate, warm, FEED_MAX };
