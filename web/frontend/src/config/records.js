// Personal-best alerts: how a record is worded, and the confetti's limits. Which values count as records, and the rounding, is decided
// by the API (backend/config/records.js); this file only says what each metric is called and in what unit.

export const RECORD_METRICS = Object.freeze({
  peakSwingSpeed: { label: "Fastest swing", unit: "m/s" },
  damageDealt: { label: "Damage dealt", unit: "" },
  score: { label: "Score", unit: "pts" },
  fade: { label: "Effort fade", unit: "%", signed: true }, // higher = the swings held up better than before
});
export const RECORD_ORDER = ["peakSwingSpeed", "damageDealt", "score", "fade"];

// The confetti: at most this many small elements, for this long. Calm mode and reduced motion show the static badge only.
export const PB_CONFETTI = Object.freeze({ pieces: 24, ms: 1500 });
