// THE win-rate rule, used by every backend aggregate (stats, players, overview, analytics, Lab, CSV).
// frontend/src/lib/outcomes.js is a line-for-line mirror (the two packages deploy separately, so they cannot share a
// file); frontend/src/lib/outcomes.test.js loads this module and fails if the two ever disagree.
//
//   win rate = victories / (victories + defeats), as a percentage
//
// Abandoned fights (the headset was taken off, or the fight was never finished) are NOT decided fights: they are left out
// of the rate and counted on their own. Anything else that is neither a win nor a loss (a timeout, a fight still in
// progress, an unknown outcome) is counted as `other` and also stays out of the rate.
// With no decided fights there is no rate: null, never a made-up 0.

const round1 = (n) => Math.round(n * 10) / 10;

/** Win rate in percent (one decimal) from counts; null when nothing was decided. */
function winRate(victories, defeats) {
  const v = Number(victories) || 0;
  const d = Number(defeats) || 0;
  return v + d > 0 ? round1((v / (v + d)) * 100) : null;
}

/** Counts from a list of outcome strings ("victory", "defeat", "abandoned", ...). */
function countOutcomes(outcomes) {
  let victories = 0, defeats = 0, abandoned = 0, other = 0, total = 0;
  for (const o of outcomes) {
    total++;
    if (o === "victory") victories++;
    else if (o === "defeat") defeats++;
    else if (o === "abandoned") abandoned++;
    else other++;
  }
  return outcomeSummary({ victories, defeats, abandoned, other, total });
}

/** { victories, defeats, abandoned, other, total, decided, winRate } from raw counts (SQL FILTER counts work too).
 *  `other` defaults to whatever is left of `total`. */
function outcomeSummary({ victories = 0, defeats = 0, abandoned = 0, other, total }) {
  const v = Number(victories) || 0, d = Number(defeats) || 0, a = Number(abandoned) || 0;
  const t = total === undefined ? undefined : Number(total) || 0;
  const o = other === undefined ? Math.max(0, (t ?? v + d + a) - v - d - a) : Number(other) || 0;
  return { victories: v, defeats: d, abandoned: a, other: o, total: t === undefined ? v + d + a + o : t, decided: v + d, winRate: winRate(v, d) };
}

/** Sessions the score / swing averages are computed over: decided fights only (see the rule above). */
const isDecided = (outcome) => outcome === "victory" || outcome === "defeat";

// SQL fragments with the same classification, so a GROUP BY query returns what countOutcomes would.
const SQL_COUNTS = `count(*)::int AS total,
  count(*) FILTER (WHERE outcome = 'victory')::int AS victories,
  count(*) FILTER (WHERE outcome = 'defeat')::int AS defeats,
  count(*) FILTER (WHERE outcome = 'abandoned')::int AS abandoned`;
const SQL_DECIDED = "outcome IN ('victory', 'defeat')";

module.exports = { winRate, countOutcomes, outcomeSummary, isDecided, SQL_COUNTS, SQL_DECIDED, RULE: "Win rate = victories / (victories + defeats). Abandoned fights are excluded and shown separately." };
