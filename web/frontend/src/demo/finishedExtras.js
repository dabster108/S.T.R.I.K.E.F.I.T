// What a scripted demo fight adds to its session_finished message, so the Live Arena's result moment shows the same "New PB" and "Rank up"
// banners the real stream carries (newRecords and rankUp, see backend/sessionEnd.js). These are SCRIPTED sample values, like the whole demo:
// a victory after the first fight of a player's loop beats a made-up earlier score and damage, and Ava's second fight is a rank-up.

const round1 = (v) => Math.round(v * 10) / 10;
const RANKS = { brawler: { key: "brawler", name: "Brawler" }, warrior: { key: "warrior", name: "Warrior" } };

/** { lane, run, step } -> { newRecords, rankUp } (the demo engine merges this into the finished message). */
export function demoFinishedExtras({ lane, step }) {
  const fight = lane.cursor; // 1 = the first fight of this player's loop
  const s = step.session || {};
  const won = step.outcome === "victory";
  const newRecords =
    won && fight > 1 && Number.isFinite(s.score) && Number.isFinite(s.damageDealt)
      ? [
          { metric: "damageDealt", value: round1(s.damageDealt), previous: round1(s.damageDealt * 0.85) },
          { metric: "score", value: Math.round(s.score), previous: Math.round(s.score * 0.82) },
        ]
      : [];
  const rankUp = lane.player === "Ava" && fight === 2 ? { from: RANKS.brawler, to: RANKS.warrior, effortPoints: 1240 } : null;
  return { newRecords, rankUp };
}
