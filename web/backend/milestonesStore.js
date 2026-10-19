// Milestone data access: one player's finished sessions (the facts the challenges and ranks use), handed to milestones.js. Read-only.

const { query } = require("./db");
const engageStore = require("./engageStore");
const milestones = require("./milestones");

/** GET /api/players/:name/milestones -> milestones.buildMilestones; null for an unknown player. */
async function playerMilestones(name, now = new Date()) {
  const facts = await engageStore.loadFacts({ player: name });
  if (!facts.length) {
    const any = await query("SELECT 1 FROM sessions WHERE player_name = $1 LIMIT 1", [name]);
    if (!any.rowCount) return null;
  }
  return milestones.buildMilestones({ playerName: name, facts, zone: engageStore.ZONE, now });
}

module.exports = { playerMilestones };
