// Personal-best data access: the rows records.js compares (one per finished session: score, damage, peak swing speed, effort fade).
// Read-only. Nothing is stored: a record is worked out from the sessions every time, so it is the same answer whenever it is asked.

const engageStore = require("./engageStore");
const analytics = require("./analytics");
const effort = require("./effort");
const records = require("./records");

async function loadRows(player, { level } = {}) {
  const facts = await engageStore.loadFacts({ player, level });
  const hits = await analytics.hitsForSessions(facts.map((f) => f.id));
  return facts.map((f) => ({
    id: f.id,
    level: f.level,
    outcome: f.outcome,
    startedAt: f.startedAt,
    values: records.recordValues(f, effort.fade(hits.get(f.id) || [], f.durationSec)),
  }));
}

/** The records a finished session set: [{ metric, value, previous }]. A live session has none yet. */
async function forSession(session) {
  if (!session || session.status === "live") return [];
  return records.newRecordsFor(session.id, await loadRows(session.playerName, { level: session.level }));
}

/** GET /api/players/:name/records -> { bySession: { [id]: ["score", ...] } } for the PB chips. null for an unknown player. */
async function playerRecords(name) {
  const rows = await loadRows(name);
  if (!rows.length) return null;
  return { ok: true, playerName: name, bySession: records.recordFlags(rows), rule: "A record beats everything this player did earlier on the same level (finished fights only; the first fight on a level has nothing to beat)." };
}

module.exports = { forSession, playerRecords, loadRows };
