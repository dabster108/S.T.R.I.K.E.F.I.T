// STRIKEFIT Wrapped tunables. The week runs Monday to Sunday in the project's day-boundary zone (progression.DEFAULT_TZ, Asia/Kathmandu).

module.exports = {
  // The effort personality: the highest Fingerprint axis (effort.js) names it.
  PERSONALITY_BY_AXIS: {
    power: "The Hammer",
    consistency: "The Metronome",
    stamina: "The Marathoner",
    dodge: "The Shadow",
    guard: "The Tactician",
  },
  ALL_ROUNDER: "The All-Rounder", // the top two axes are this close (or closer), in Fingerprint points
  ALL_ROUNDER_WITHIN: 5,
  WARMING_UP: "Warming Up", // not enough data for a fingerprint yet
  WEEKS_LISTED: 26, // the week picker offers this many weeks that have fights
  EARLIEST_WEEK: "2020-01-06", // anything earlier is not a date this site has data for
};
