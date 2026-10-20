// STRIKEFIT Wrapped: the five slides, the card size and the wording of its empty and not-enough-data states. The numbers come from the API
// (GET /api/players/:name/wrapped); nothing about a week is typed here, only how it is presented.

export const CARD = Object.freeze({ w: 1080, h: 1350 }); // the exported PNG, in pixels (4:5)
export const AUTO_ADVANCE_MS = 6000; // only when the viewer turns auto-play on (it is off by default)
export const SLIDE_IDS = Object.freeze(["swings", "fastest", "monster", "bestDay", "personality"]);

export const COPY = Object.freeze({
  emptyTitle: "No fights this week yet",
  emptyText: "Wrapped is built from the fights you finish in a week. Play one, or look at a week you did play.",
  personalityWaiting: "Warming Up",
  personalityWaitingText: "There is not enough swing data this week to read an effort personality yet. A few more fights will do it.",
  noSwingSpeed: "No swing speed was recorded this week.",
  noFade: "Not enough swings this week to measure effort fade.",
  noMonster: "No monster fought this week.",
  noBestDay: "No counted swings this week.",
});
