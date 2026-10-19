// Rank ladder tunables: the rungs and what earns effort points. Nothing else in the code holds these numbers (ranks.js reads them,
// the API sends the ladder to the pages, the pages never repeat it).
//
//   effortPoints = counted swings x perCountedSwing + full-power swings x perFullPowerSwing
//                + victories x perVictory + Hard (Level 3) victories x perHardVictoryExtra
// "Counted swings" is the number the player page shows as Counted (a session's hit count); a full-power swing is also a counted swing,
// so it is worth perCountedSwing + perFullPowerSwing. Abandoned and lost fights earn their swings, not the victory bonus.

module.exports = {
  RANKS: [
    { key: "rookie", name: "Rookie", from: 0 },
    { key: "brawler", name: "Brawler", from: 300 },
    { key: "warrior", name: "Warrior", from: 1200 },
    { key: "guardian", name: "Guardian", from: 3500 },
  ],
  POINTS: { perCountedSwing: 1, perFullPowerSwing: 2, perVictory: 40, perHardVictoryExtra: 60 },
  HARD_LEVEL: "Hard",
  FEED: { activePlayers: 8, activeDays: 14, recentDays: 30 }, // the activity feed looks at this many recently active players
};
