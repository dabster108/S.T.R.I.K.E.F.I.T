// STRIKEFIT Wrapped data access: loads a player's finished sessions once, then the swings of the chosen week for the fade and the
// fingerprint. Read-only; the rules are in wrapped.js.

const { query } = require("./db");
const engageStore = require("./engageStore");
const analytics = require("./analytics");
const effort = require("./effort");
const wrapped = require("./wrapped");

/** GET /api/players/:name/wrapped?weekStart=YYYY-MM-DD -> wrapped.buildWrapped; null for an unknown player. */
async function playerWrapped(name, requestedWeek, now = new Date()) {
  const zone = engageStore.ZONE;
  const facts = await engageStore.loadFacts({ player: name }, zone);
  if (!facts.length) {
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  const weekStart = requestedWeek || wrapped.currentWeekStart(now, zone);
  const weekRows = facts.filter((f) => f.day >= weekStart && f.day <= weekEndOf(weekStart));

  // the effort fade and the fingerprint need the swings of the week's fights (only fetched when the week has any)
  let fades = [];
  let fingerprint = null;
  if (weekRows.length) {
    const hits = await analytics.hitsForSessions(weekRows.map((f) => f.id));
    fades = weekRows.map((f) => effort.fade(hits.get(f.id) || [], f.durationSec));
    const withSwings = weekRows.filter((f) => (hits.get(f.id) || []).length);
    fingerprint = await analytics.fingerprintOfRows(withSwings, hits);
  }
  return wrapped.buildWrapped({ playerName: name, facts, weekStart, fades, fingerprint, now, zone });
}

const weekEndOf = (weekStart) => new Date(Date.parse(`${weekStart}T00:00:00Z`) + 6 * 864e5).toISOString().slice(0, 10);

module.exports = { playerWrapped };
