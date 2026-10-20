// GET /api/players/:name/best-pace?level=N  (N = 1, 2, 3 or Easy, Medium, Hard)
// Ghost pace for the Live Arena: the player's personal-best finished fight on that level as a cumulative-damage-over-time
// curve, so a live fight can be drawn against it. "Best" is the same rule as the replay ghost (engage.pickBest): the highest
// score among that player's finished fights on the level that ended in victory, defeat or timeout (an abandoned fight is
// never a personal best); ties go to the shorter, then the earlier fight. A fight that is still live is never the ghost.
//   -> { ok, playerName, level, best: null | { id, score, outcome, durationSec, startedAt, totalDamage }, points: [{ t, damage }],
//        reason, cached, cachedAt, ttlSec }
// best is null when the player has no finished fight on that level (the page then hides the ghost). Cached per player + level
// for 60 s in memory, so a live view that opens many times does not query Postgres each time. Read-only.

const goals = require("./goals");
const leaderboard = require("./leaderboard");
const effort = require("./effort");

const CACHE_MS = 60_000;
const MAX_ENTRIES = 500; // a safety bound; the cache is a convenience, not storage

/** A tiny TTL cache. now() is injectable so tests don't wait a minute. */
function createCache({ ttlMs = CACHE_MS, now = Date.now } = {}) {
  const map = new Map();
  return {
    get(key) {
      const hit = map.get(key);
      if (!hit) return null;
      if (now() - hit.at >= ttlMs) {
        map.delete(key);
        return null;
      }
      return hit;
    },
    set(key, value) {
      if (map.size >= MAX_ENTRIES) map.delete(map.keys().next().value);
      const entry = { at: now(), value };
      map.set(key, entry);
      return entry;
    },
    clear: () => map.clear(),
    /** Drops every entry whose key starts with `prefix` (a player's keys are "name\u0000level"). */
    deleteWhere(prefix) {
      for (const k of [...map.keys()]) if (k.startsWith(prefix)) map.delete(k);
    },
    size: () => map.size,
  };
}

/** "1" / "2" / "3" / "easy" ... -> "Easy" | "Medium" | "Hard"; null for anything else (including "all"). */
function parseLevelParam(v) {
  const level = leaderboard.parseLevel(v === undefined || v === "" ? "x" : v);
  return level && level !== "all" ? level : null;
}

const BEST_SQL = `
  SELECT id, outcome, score, duration_sec AS "durationSec", started_at AS "startedAt", damage_dealt AS "damageDealt"
  FROM sessions
  WHERE player_name = $1 AND level = $2 AND status = 'finished' AND outcome IN ('victory', 'defeat', 'timeout') AND score > 0
  ORDER BY score DESC, CASE WHEN duration_sec > 0 THEN duration_sec ELSE 1e9 END ASC, started_at ASC, seq ASC
  LIMIT 1`;
const HITS_SQL = `
  SELECT CASE WHEN json_typeof(payload->'t') = 'number' THEN (payload->>'t')::float8 END AS t,
         CASE WHEN json_typeof(payload->'damage') = 'number' THEN (payload->>'damage')::float8 END AS damage
  FROM events WHERE session_ref = $1 AND type = 'hit' ORDER BY occurred_at, seq`;

/** The real loader: two parameterised queries. -> { best, points } */
async function loadBestPace(query, name, level) {
  const found = await query(BEST_SQL, [name, level]);
  if (!found.rows.length) return { best: null, points: [] };
  const b = found.rows[0];
  const hits = (await query(HITS_SQL, [b.id])).rows;
  const points = effort.cumulativeDamage(hits.map((h) => ({ t: h.t, damage: h.damage })));
  return {
    best: {
      id: b.id,
      score: Number(b.score) || 0,
      outcome: b.outcome,
      durationSec: Number(b.durationSec) || 0,
      startedAt: new Date(b.startedAt).toISOString(),
      totalDamage: points.length ? points[points.length - 1].damage : 0,
    },
    points,
  };
}

/** The Express handler; loadBest(name, level) and the cache are injected so the route can be tested without a database. */
function createHandler({ loadBest, cache = createCache() }) {
  return async function bestPace(req, res, next) {
    try {
      const n = goals.validateName(req.params.name);
      if (!n.ok) return res.status(400).json({ ok: false, error: n.error });
      const level = parseLevelParam(req.query.level);
      if (!level) return res.status(400).json({ ok: false, error: "level is required: 1, 2, 3 or Easy, Medium, Hard" });
      const key = `${n.value}\u0000${level}`;
      let entry = cache.get(key);
      const cached = Boolean(entry);
      if (!entry) entry = cache.set(key, await loadBest(n.value, level));
      const { best, points } = entry.value;
      res.set("Cache-Control", "private, no-cache"); // revalidate (a cheap 304): the best fight changes the moment a fight ends
      res.json({
        ok: true,
        playerName: n.value,
        level,
        best,
        points,
        // A best fight from before the live-telemetry build has no hit events, so there is nothing to draw.
        reason: !best ? "no_finished_fight" : points.length < 2 ? "no_events" : null,
        rule: "Highest score among this player's finished fights on this level (victory, defeat or timeout), as cumulative damage over fight time.",
        cached,
        cachedAt: new Date(entry.at).toISOString(),
        ttlSec: CACHE_MS / 1000,
      });
    } catch (err) {
      next(err);
    }
  };
}

let shared = null; // the cache of the mounted route, so a finished fight can drop its player's answers (invalidatePlayer)
function mount(app, { query }) {
  shared = createCache();
  app.get("/api/players/:name/best-pace", createHandler({ loadBest: (name, level) => loadBestPace(query, name, level), cache: shared }));
}

/** A fight just ended (or was deleted): this player's best fight may have changed, so their cached answers are dropped. */
function invalidatePlayer(name) {
  if (shared) shared.deleteWhere(`${name}\u0000`);
}

module.exports = { CACHE_MS, createCache, createHandler, parseLevelParam, loadBestPace, mount, invalidatePlayer };
