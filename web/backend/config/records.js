// Personal-best tunables. A record is a value that beats everything the player did before on the SAME level.

module.exports = {
  // The four things compared. `decimals` is the precision values are rounded to before comparing, so a difference too small to show
  // is not a record. All four are "higher is better": for the fade, higher means the swings held up better (a smaller slowdown).
  METRICS: [
    { key: "peakSwingSpeed", decimals: 2 }, // the fastest counted swing, clamped to the game's 8 m/s cap (levels.js)
    { key: "damageDealt", decimals: 1 },
    { key: "score", decimals: 0 },
    { key: "fade", decimals: 1 }, // effort fade in percent (effort.js): -12 slowed down, +5 got stronger
  ],
  // Only decided fights are compared. An abandoned fight (the headset came off) is not a fight to beat and never sets a record.
  DECIDED_OUTCOMES: ["victory", "defeat", "timeout"],
  MIN_EARLIER_FIGHTS: 1, // a first fight on a level has nothing to beat, so it holds no record
};
