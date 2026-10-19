// Everything the /study page says and asks, in one place. The option values are what the backend accepts (backend/studyCore.js;
// lib/study.test.js checks they agree), so editing wording here is safe and editing a value is not.

// Must equal CONSENT_VERSION in backend/studyCore.js. Bump both when the wording of the consent text changes.
export const CONSENT_VERSION = "2026-10-v1";

// Fill this in with the approval or reference your institution gave the study (it is shown on the consent screen when set).
export const ETHICS_REFERENCE = "";

export const CONSENT = {
  title: "Before you start",
  lead: "This is a small research study about a VR fitness game. Please read this, it takes a minute. Taking part is your choice.",
  points: [
    ["What you will do", "Play STRIKEFIT in a Meta Quest 2 headset for a few minutes, and answer two short forms, one before and one after you play."],
    ["What we record", "Your answers, and what the game measures while you play: how fast you swing, how many hits you land, how the fight ends. We do not record video, sound or your face."],
    ["Who you are", "You get a code such as P07. That code is your player name in the game. We do not ask for your name, email or phone number, and this website does not store your IP address or any device details."],
    ["How we use it", "The results go into a university project about whether swing-speed scoring makes VR games a better workout. Only totals and anonymous rows are shared, never your code with anything that could identify you."],
    ["Your health", "Swinging hard is exercise and VR can make some people feel dizzy or sick. Stop at any time. Please do not take part if exercise is unsafe for you, if you are under 18, or if you feel unwell."],
    ["Your choice", "You can stop at any point, or leave a question blank only by closing the page. To have your data deleted afterwards, tell the researcher your code and it will be removed."],
  ],
  agree: "I have read this, I am 18 or older, and I agree to take part.",
};

export const STEPS = [
  { id: "consent", label: "Consent" },
  { id: "pre", label: "About you" },
  { id: "play", label: "Play" },
  { id: "post", label: "After" },
];

export const AGE_BANDS = [
  ["18-24", "18 to 24"], ["25-34", "25 to 34"], ["35-44", "35 to 44"], ["45-54", "45 to 54"], ["55-64", "55 to 64"], ["65+", "65 or older"], ["prefer-not", "Prefer not to say"],
];
export const VR_EXPERIENCE = [
  ["never", "Never used VR"], ["once-or-twice", "Once or twice"], ["occasionally", "Now and then"], ["regularly", "Regularly (most weeks)"],
];
export const WEEKLY_EXERCISE = [
  ["none", "None"], ["under-1h", "Under 1 hour"], ["1-3h", "1 to 3 hours"], ["3-5h", "3 to 5 hours"], ["over-5h", "More than 5 hours"],
];
export const PLAY_AGAIN = [["yes", "Yes"], ["maybe", "Maybe"], ["no", "No"]];

export const LIKERT = ["Strongly disagree", "Disagree", "Neutral", "Agree", "Strongly agree"];

export const ENJOYMENT_ITEMS = [
  "I enjoyed playing this game.",
  "Swinging at the monster felt satisfying.",
  "The time passed quickly while I played.",
  "I felt like trying hard, not just going through the motions.",
  "I would recommend this game to a friend.",
];

export const EFFORT_VS_USUAL = [
  [1, "Much easier than my usual workout"], [2, "A bit easier"], [3, "About the same"], [4, "A bit harder"], [5, "Much harder than my usual workout"],
];

// Borg Rating of Perceived Exertion, 6 to 20. Anchors are the scale's standard verbal labels.
export const BORG = [
  [6, "6 · No exertion at all"], [7, "7 · Extremely light"], [8, "8"], [9, "9 · Very light"], [10, "10"], [11, "11 · Light"], [12, "12"],
  [13, "13 · Somewhat hard"], [14, "14"], [15, "15 · Hard"], [16, "16"], [17, "17 · Very hard"], [18, "18"], [19, "19 · Extremely hard"], [20, "20 · Maximal exertion"],
];

// System Usability Scale (Brooke, 1996), "system" reworded to "game". Odd items are positive, even items negative; the page shows
// them in this order and the server does the scoring.
export const SUS_ITEMS = [
  "I think that I would like to play this game frequently.",
  "I found the game unnecessarily complex.",
  "I thought the game was easy to use.",
  "I think that I would need the support of a technical person to be able to play this game.",
  "I found the various functions in this game were well integrated.",
  "I thought there was too much inconsistency in this game.",
  "I would imagine that most people would learn to play this game very quickly.",
  "I found the game very cumbersome to use.",
  "I felt very confident playing the game.",
  "I needed to learn a lot of things before I could get going with this game.",
];
