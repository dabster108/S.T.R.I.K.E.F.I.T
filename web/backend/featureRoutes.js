// The read-only GET endpoints added with the engagement features (rank, ...): one place that validates the name, caches the answer
// for 30 to 60 seconds in memory (ttlCache.js) and drops it when something changes for that player (sessionEnd.js, delete, rename).
// Additions only; mounted from server.js. Every answer carries an ETag, and Cache-Control: no-cache makes the browser ask again
// (a cheap 304 when nothing changed), so a page that refetches right after a fight never shows the old rank.

const goals = require("./goals");
const pace = require("./pace");
const ranksStore = require("./ranksStore");
const recordsStore = require("./recordsStore");
const wrappedStore = require("./wrappedStore");
const wrapped = require("./wrapped");
const { createTtlCache } = require("./ttlCache");

const TTL = { rank: 30_000, records: 30_000, wrapped: 60_000 };
const caches = {
  rank: createTtlCache({ ttlMs: TTL.rank }),
  ranks: createTtlCache({ ttlMs: TTL.rank, max: 100 }),
  records: createTtlCache({ ttlMs: TTL.records }),
  wrapped: createTtlCache({ ttlMs: TTL.wrapped }),
};
const keyOf = (name, what) => `${name}\u0000${what}`;

/** Forget everything cached about one player (and the many-players answers, which may include them). */
function invalidatePlayer(name) {
  for (const c of Object.values(caches)) c.invalidate(c === caches.ranks ? "" : `${name}\u0000`);
  pace.invalidatePlayer(name); // the Ghost Pace best fight can change when a fight ends
}
/** Forget everything (a clear-all, a rename of unknown scope). */
function invalidateAll() {
  for (const c of Object.values(caches)) c.invalidate();
}

/** The records a finished session set (cached 30 s; dropped when that player's fights change). Never throws: [] on a failure. */
async function newRecordsFor(session) {
  if (!session || session.status === "live") return [];
  try {
    return await caches.records.get(keyOf(session.playerName, `session:${session.id}`), () => recordsStore.forSession(session));
  } catch (err) {
    console.error("[records] could not work out the records:", err.message);
    return [];
  }
}

function validName(req, res) {
  const n = goals.validateName(req.params.name);
  if (!n.ok) {
    res.status(400).json({ ok: false, error: n.error });
    return null;
  }
  return n.value;
}

function mount(app, { route }) {
  // Rank, effort points, next rank and progress (0 to 1) from the player's finished fights (ranks.js).
  app.get("/api/players/:name/rank", route(async (req, res) => {
    const name = validName(req, res);
    if (!name) return;
    const out = await caches.rank.get(keyOf(name, "rank"), () => ranksStore.playerRank(name));
    if (!out) return res.status(404).json({ ok: false, error: "Player not found" });
    res.set("Cache-Control", "no-cache");
    res.json(out);
  }));

  // STRIKEFIT Wrapped: one week (Monday to Sunday, Asia/Kathmandu) of one player; ?weekStart=YYYY-MM-DD, default the current week.
  app.get("/api/players/:name/wrapped", route(async (req, res) => {
    const name = validName(req, res);
    if (!name) return;
    const week = wrapped.parseWeekStart(req.query.weekStart);
    if (!week.ok) return res.status(400).json({ ok: false, error: week.error });
    // the key holds the week the answer is for (the current week when none was given), so a new week never serves the old one
    const key = keyOf(name, `wrapped:${week.value || "current"}`);
    const out = await caches.wrapped.get(key, () => wrappedStore.playerWrapped(name, week.value));
    if (!out) return res.status(404).json({ ok: false, error: "Player not found" });
    res.set("Cache-Control", "no-cache");
    res.json(out);
  }));

  // Which of a player's fights set a record (the PB chip in the lists): { bySession: { id: [metric, ...] } }.
  app.get("/api/players/:name/records", route(async (req, res) => {
    const name = validName(req, res);
    if (!name) return;
    const out = await caches.records.get(keyOf(name, "records"), () => recordsStore.playerRecords(name));
    if (!out) return res.status(404).json({ ok: false, error: "Player not found" });
    res.set("Cache-Control", "no-cache");
    res.json(out);
  }));

  // Ranks for several players at once (the leaderboard rows): /api/ranks?players=Ava,Ben (at most 50 names).
  app.get("/api/ranks", route(async (req, res) => {
    const raw = typeof req.query.players === "string" ? req.query.players : "";
    const names = raw.split(",").map((s) => s.trim()).filter((s) => goals.validateName(s).ok).slice(0, ranksStore.MAX_NAMES);
    const key = [...new Set(names)].sort().join("\u0001");
    const out = await caches.ranks.get(key, () => ranksStore.ranksFor(names));
    res.set("Cache-Control", "no-cache");
    res.json({ ok: true, ranks: out });
  }));
}

module.exports = { mount, invalidatePlayer, invalidateAll, newRecordsFor, TTL };
