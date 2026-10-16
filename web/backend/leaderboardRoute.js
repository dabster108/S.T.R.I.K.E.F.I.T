// GET /api/leaderboard?level=all|Easy|Medium|Hard (or 1/2/3)&limit=10&player=NAME[&score=N&durationSec=S]
// (score / durationSec: a fight the headset just finished, so its rank line is right while this board is still cached)
// Public scoreboard for the headset menu and the web Leaderboard page; the rule lives in leaderboard.js.
//   -> { level, rule, updatedAt, cached, total, rows: [{ rank, name, score, level, outcome, date, durationSec, sessionId, you? }],
//        you?: { rank, score, level, date, inTop } | null }   (you only when ?player= is given; null = no scored run)
// One parameterised query per level and cache window (CACHE_MS, in memory per instance): DISTINCT ON keeps the best run
// per player in SQL, on the existing sessions_status_idx / sessions_level_idx. Additions only; mounted from server.js.

const leaderboard = require("./leaderboard");

const CACHE_MS = 45_000;
const cache = new Map(); // level -> { at, ranked }

// The same ordering as leaderboard.compareRuns, so DISTINCT ON keeps exactly the run the rule would keep.
const BEST_SQL = (levelFilter) => `
  SELECT DISTINCT ON (player_name)
         id, player_name AS "playerName", level, outcome, score, duration_sec AS "durationSec",
         ended_at AS "endedAt", started_at AS "startedAt", status
  FROM sessions
  WHERE status <> 'live' AND score > 0 ${levelFilter ? "AND level = $1" : ""}
  ORDER BY player_name, score DESC,
           CASE WHEN duration_sec > 0 THEN duration_sec ELSE 'Infinity'::float8 END ASC,
           ended_at ASC, seq ASC`;

async function ranked(query, level) {
  const hit = cache.get(level);
  if (hit && Date.now() - hit.at < CACHE_MS) return { ...hit, cached: true };
  const { rows } = level === "all" ? await query(BEST_SQL(false)) : await query(BEST_SQL(true), [level]);
  const sessions = rows.map((r) => ({ ...r, endedAt: r.endedAt instanceof Date ? r.endedAt.toISOString() : r.endedAt }));
  const entry = { at: Date.now(), ranked: leaderboard.rankBest(sessions, { level }) };
  cache.set(level, entry);
  return { ...entry, cached: false };
}

function mount(app, { query, route }) {
  app.get("/api/leaderboard", route(async (req, res) => {
    const level = leaderboard.parseLevel(req.query.level);
    if (!level) return res.status(400).json({ ok: false, error: "level must be all, Easy, Medium or Hard (or 1, 2, 3)" });
    const limit = leaderboard.parseLimit(req.query.limit);
    const player = typeof req.query.player === "string" && req.query.player.trim() ? req.query.player.trim().slice(0, 40) : null;
    const { at, ranked: list, cached } = await ranked(query, level);
    const score = Number(req.query.score);
    const run = player && Number.isFinite(score) && score > 0 ? { score, durationSec: Number(req.query.durationSec) || 0, level: level === "all" ? null : level } : null;
    res.set("Cache-Control", "public, max-age=30");
    res.json({ level, rule: leaderboard.RULE, updatedAt: new Date(at).toISOString(), cached, ...leaderboard.board(list, { limit, player, run }) });
  }));
}

/** Drops the cached boards (tests). */
function clearCache() {
  cache.clear();
}

module.exports = { mount, clearCache, CACHE_MS };
