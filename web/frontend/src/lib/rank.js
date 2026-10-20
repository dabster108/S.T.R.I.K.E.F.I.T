// Words for a rank answer (GET /api/players/:name/rank), used for the ring's text alternative. Pure, so it is tested.
import { formatNumber } from "./format.js";

export function rankSentence(info) {
  if (!info) return "";
  const base = `${info.rank.name}, ${formatNumber(info.effortPoints)} effort points`;
  return info.nextRank ? `${base}, ${Math.round(info.progress * 100)}% of the way to ${info.nextRank.name} (${formatNumber(info.pointsToNext)} points to go)` : `${base}, the top rank`;
}
