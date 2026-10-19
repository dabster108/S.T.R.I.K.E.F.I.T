// The numbers and wording behind the Method page (/about/method). Game numbers come from lib/game.js and the backend's swing
// cap; the calorie constants below mirror backend/fitness.js and lib/method.test.js fails if they drift apart.

export const RESEARCH_QUESTION = "Does scoring a VR fighting game by how fast the player really swings, instead of by whether a gesture registered, give a better workout, and do players enjoy it and want to come back?";

export const SUB_QUESTIONS = [
  "Do players swing faster, and for longer, when speed decides the damage?",
  "Does how hard a fight feels (Borg RPE) follow how fast the player swung?",
  "How usable and enjoyable is the game (SUS, a short enjoyment scale)?",
  "Do players say they would play again, and do they come back (return rate within 1, 3 and 7 days)?",
];

// kcal = MET x body mass (kg) x hours. A session of all-tap swings is priced at MET_MIN and all-full-power swings at MET_MAX.
export const CALORIE = { bodyMassKg: 70, metMin: 3.0, metMax: 6.0, metWalking: 4.3, metJogging: 7.0, tierWeightFull: 4 };

/** Mean tier weight (1 tap .. 4 full power) and duration in seconds -> { met, kcal }. The same formula as backend/fitness.js estimateEffort. */
export function estimateKcal(avgTierWeight, durationSec, c = CALORIE) {
  const met = c.metMin + (c.metMax - c.metMin) * ((avgTierWeight - 1) / (c.tierWeightFull - 1));
  return { met, kcal: met * c.bodyMassKg * (durationSec / 3600) };
}

export const MEASURES = [
  ["Perceived exertion", "Borg RPE, 6 to 20, asked after the fight", "Thesis Lab: RPE against swing speed"],
  ["Enjoyment", "Five statements, 1 to 5; the mean of the five", "Thesis Lab: Enjoyment"],
  ["Effort against a usual workout", "One question, 1 (much easier) to 5 (much harder)", "Thesis Lab: headline figure"],
  ["Usability", "System Usability Scale, ten statements, scored 0 to 100", "Thesis Lab: SUS with bands"],
  ["Intent to return", "“Would you play again this week?” yes / maybe / no", "Thesis Lab: Would play again"],
  ["Return rate", "Share of players who played another fight within 1, 3 and 7 days of their first", "Thesis Lab: Return rate"],
  ["Effort fade", "Change in average swing speed from the first to the last third of a fight", "Thesis Lab: Each participant"],
];

export const PROCEDURE = [
  "The participant reads the consent text on /study and ticks the box; the site issues a code (P01 to P20).",
  "They answer three short questions (age band, VR experience, weekly exercise).",
  "They put the headset on, type the code as the player name, and play one full fight on a level the researcher chooses.",
  "Back on /study they rate the fight (Borg RPE), the five enjoyment statements, effort against a usual workout, whether they would play again, and the ten SUS statements.",
  "The Thesis Lab joins the fight telemetry and the answers by participant code; the study CSV carries the same join.",
];

export const LIMITATIONS = [
  ["Small sample", "The pilot is at most 20 adults, recruited by the researcher. Every Lab chart is flagged as a small sample; none of it is a finding about people in general."],
  ["No heart-rate data", "The headset reports no heart rate or oxygen use. Calories are an estimate from the MET method, and “effort” is swing speed and the player's own rating, not physiology."],
  ["Estimated, assumed numbers", "Body mass (70 kg) and the MET range (3 to 6) are assumptions, not measurements, and the range has not yet been replaced by a cited value for active VR sword games."],
  ["Correlation is not causation", "A link between swing speed and RPE does not show that one causes the other, and with this few fights a correlation can come and go."],
  ["Simulated and real data are different", "Development and the viva demo use simulated fights (names beginning ZZTEST, or the scripted demo data). Only fights recorded by the headset count as results."],
  ["Self-report and novelty", "Enjoyment, RPE and intent to play again are what people say straight after a first, short session. They do not show habit, and a new headset game is novel."],
  ["One tracker, one session", "Swing speed is the controller speed the headset reports; tracking can spike (hence the 8 m/s clamp) and fatigue over several sessions is not measured here."],
];

export const ETHICS = [
  "Taking part is voluntary and for adults only. The consent text says what is recorded, what is not, the exercise and dizziness risks, and how to stop or have data deleted.",
  "The consent wording version and the time it was given are stored with the participant code; the code is the only identifier.",
  "No name, email, phone number, IP address or device detail is stored. Player names in the headset are the participant codes.",
  "Participants can stop at any point. On request their code, answers and fights are deleted.",
  "Exports (CSV) carry the code and the answers, never an IP address or device information.",
];

export const REFERENCES = [
  "Borg GA. Psychophysical bases of perceived exertion. Medicine & Science in Sports & Exercise. 1982;14(5):377-381.",
  "Brooke J. SUS: a “quick and dirty” usability scale. In: Usability Evaluation in Industry. 1996.",
  "Bangor A, Kortum P, Miller J. Determining what individual SUS scores mean: adding an adjective rating scale. Journal of Usability Studies. 2009;4(3):114-123.",
  "Ainsworth BE, et al. 2011 Compendium of Physical Activities. Medicine & Science in Sports & Exercise. 2011;43(8):1575-1581.",
];
