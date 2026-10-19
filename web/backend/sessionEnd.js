// What happens when a fight ends, besides saving it: the answers that depend on the finished fight are worked out once, for the
// stream message that announces it (session_finished gets extra fields; the existing ones are untouched), and anything cached about
// the player is dropped. Never throws: a problem here must not stop a session from being saved.

const ranksStore = require("./ranksStore");
const featureRoutes = require("./featureRoutes");
const activityFeed = require("./activityFeed");

/** -> { rankUp: { from, to, effortPoints } | null, newRecords: [{ metric, value, previous }] } for a finished session. */
async function extras(session) {
  featureRoutes.invalidatePlayer(session.playerName);
  activityFeed.invalidate(); // before the stream message goes out, so a page that refetches the feed at once gets the new rows
  const out = { rankUp: null, newRecords: [] };
  try {
    out.rankUp = await ranksStore.rankUpAfter(session);
  } catch (err) {
    console.error("[rank] rank-up check failed:", err.message);
  }
  out.newRecords = await featureRoutes.newRecordsFor(session); // never throws
  return out;
}

module.exports = { extras };
