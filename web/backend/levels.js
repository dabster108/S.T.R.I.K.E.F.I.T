// Which levels exist and whether each can be played. Mirrors LEVELS in frontend/src/lib/game.js (status per level);
// frontend/src/lib/game.test.js loads this file and fails if the two disagree, so the landing copy cannot go stale again.
// To ship or retire a level, change `status` here and in game.js: every count below is computed from this list.

const LEVELS = [
  { key: "Easy", number: 1, status: "playable" },
  { key: "Medium", number: 2, status: "playable" },
  { key: "Hard", number: 3, status: "playable" },
];

// The game's swing speed cap, in m/s (HitDetection / PlayerCombat: both Level 1 and Level 2 use 8; game.js LEVELS.*.swing.cap).
// The headset reports the RAW controller speed at impact in `payload.swingSpeed`; the cap only limits the damage
// multiplier, so stored speeds can exceed it (a fast flick, a tracking spike). Every swing-speed aggregate clamps to
// this value, so an average or a peak can never read higher than the game can actually use.
const SWING_SPEED_CAP = 8;

const countByStatus = (status) => LEVELS.filter((l) => l.status === status).length;

module.exports = {
  LEVELS,
  SWING_SPEED_CAP,
  levelsPlayable: () => countByStatus("playable"),
  levelsPlanned: () => countByStatus("planned"),
};
