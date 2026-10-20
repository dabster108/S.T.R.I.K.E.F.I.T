// THE win-rate rule on the web side. Mirror of backend/outcomes.js (the packages deploy separately, so a file can't be
// shared); lib/outcomes.test.js loads the backend module and fails if the two ever disagree.
//
//   win rate = victories / (victories + defeats), as a percentage
//
// Abandoned fights are not decided fights: they stay out of the rate and are shown separately. Anything else that is
// neither a win nor a loss (timeout, in progress, unknown) is `other`. No decided fights means no rate: null, never 0%.

const round1 = (n) => Math.round(n * 10) / 10;

/** Win rate in percent (one decimal) from counts; null when nothing was decided. */
export function winRate(victories, defeats) {
  const v = Number(victories) || 0;
  const d = Number(defeats) || 0;
  return v + d > 0 ? round1((v / (v + d)) * 100) : null;
}

/** { victories, defeats, abandoned, other, total, decided, winRate } from raw counts. `other` defaults to the rest of `total`. */
export function outcomeSummary({ victories = 0, defeats = 0, abandoned = 0, other, total }) {
  const v = Number(victories) || 0, d = Number(defeats) || 0, a = Number(abandoned) || 0;
  const t = total === undefined ? undefined : Number(total) || 0;
  const o = other === undefined ? Math.max(0, (t ?? v + d + a) - v - d - a) : Number(other) || 0;
  return { victories: v, defeats: d, abandoned: a, other: o, total: t === undefined ? v + d + a + o : t, decided: v + d, winRate: winRate(v, d) };
}

/** Counts from a list of outcome strings. */
export function countOutcomes(outcomes) {
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

export const isDecided = (outcome) => outcome === "victory" || outcome === "defeat";

export const WIN_RATE_RULE = "Win rate = victories / (victories + defeats). Abandoned fights are excluded and shown separately.";

/** "66.7%" or "—" when nothing was decided. */
export const formatWinRate = (rate) => (rate === null || rate === undefined || !Number.isFinite(rate) ? "—" : `${Math.round(rate * 10) / 10}%`);
