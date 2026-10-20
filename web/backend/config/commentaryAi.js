// Every number and every sentence of instruction for the AI commentator and its voice. Nothing here is a secret: the keys
// come from the environment (GROQ_API_KEY, or GORQ_API_KEY as it is spelled in web/backend/.env, and GOOGLE_TTS_API_KEY).

module.exports = Object.freeze({
  // The words. Groq serves the model; GROQ_COMMENTARY_MODEL changes it without a code change (Groq no longer lists a Mistral model).
  groq: {
    url: "https://api.groq.com/openai/v1/chat/completions",
    model: "qwen/qwen3.8-27b",
    temperature: 0.9,
    maxTokens: 60,
    timeoutMs: 2500,
  },
  // The voice. Google Cloud Text-to-Speech, REST. A British Neural2 male voice is the closest thing to a football commentator.
  google: {
    url: "https://texttospeech.googleapis.com/v1/text:synthesize",
    languageCode: "en-GB",
    voice: "en-GB-Neural2-B",
    speakingRate: 1.08,
    pitch: 0,
    timeoutMs: 4000,
  },
  // What a request may contain (everything is validated; nothing else reaches the model).
  input: { baseMax: 160, nameMax: 40, recentMax: 3, comboMax: 999 },
  // What an answer must look like, or the plain template line is used instead.
  output: { maxChars: 140, maxWords: 24 },
  speechMaxChars: 400, // a full-time report is read in one go
  // The full-time report: a short match summary after the fight, from the fight's own totals.
  summary: {
    maxTokens: 160,
    baseMax: 400,
    output: { maxChars: 360, maxWords: 60 },
    system:
      "You are a football TV commentator giving the full-time match report on a VR fitness fight (a player against a monster) as if it were a football match. " +
      "Write 2 or 3 short sentences, at most 50 words in total, with football words (full time, the final whistle, the performance, the margin). " +
      "Use only the given facts; keep names exactly as written; write numbers as digits and add none; never use he, she, his or her: say the name or they. " +
      "Call the score a score or points, never a margin or a lead (the opponent has no score). The player is the side we follow. A victory is a win for the player, a defeat a loss. An abandoned fight is a match called off. No emojis, quotation marks or hashtags.",
  },
  // Cost guards: a token bucket per IP and one for the whole server. Groq's free tier allows 8,000 tokens a minute and 1,000 requests a day
  // per model; a request is about 250 tokens, so about 20 a minute is the most the server may send (global line limit: 0.35/s, 21 a minute).
  limits: {
    line: { perIp: { capacity: 12, refillPerSec: 0.25 }, global: { capacity: 20, refillPerSec: 0.35 } },
    speech: { perIp: { capacity: 30, refillPerSec: 0.7 }, global: { capacity: 120, refillPerSec: 3 } },
  },
  // After this many failures in a row the service is treated as down for a while (the browser then uses the template lines at once).
  breaker: { failures: 3, coolOffMs: 30_000, rateLimitedMaxMs: 120_000 }, // a 429 from Groq rests for its own retry-after (at most rateLimitedMaxMs)
  lineCacheMs: 8000, // the same moment asked again (another viewer of the same fight sees it within a second or two) gets the same sentence for free
  lineCacheMax: 200,
  speechCacheMax: 120,
  speechCacheSeconds: 600,
  situations: [
    "enter", "fullPower", "blocked", "combo", "clash", "windUp", "guardUp", "guardDown", "hurt", "dodge", "playerLow", "monsterLow",
    "phase2", "phase3", "paceAhead", "paceBehind", "idle", "victory", "defeat", "abandoned",
  ],
  // How each situation should sound. The facts always come from the game; this only sets the mood.
  moods: {
    enter: "kick-off, the players walk out",
    fullPower: "a thunderous strike, like a goal from distance",
    blocked: "a good save by the keeper, the effort is soaked up",
    combo: "a flowing move of one-touch passes, the crowd rising",
    clash: "a fifty-fifty tackle in midfield",
    windUp: "the other side are building an attack, danger",
    guardUp: "the opponents have parked the bus and defend deep",
    guardDown: "the defence has been opened up, space to attack",
    hurt: "a heavy foul, the player is hurt",
    dodge: "a brilliant dodge, a near miss",
    playerLow: "the player is running on empty, final minutes",
    monsterLow: "the opponent is on the ropes, the crowd smells blood",
    phase2: "the opponent changes formation and presses harder",
    phase3: "all-out attack in the last minutes, it could go either way",
    paceAhead: "ahead of their own best time, a record-breaking pace",
    paceBehind: "behind their own best pace, needs to lift the tempo",
    idle: "play has stalled, the crowd is restless",
    victory: "the final whistle, a famous win",
    defeat: "the final whistle, a heartbreaking loss",
    abandoned: "the match is called off",
  },
  system:
    "You are a football TV commentator calling a VR fitness fight (a player against a monster) as if it were a live match. " +
    "Say the given event in ONE punchy sentence of at most 16 words, in the style of Martin Tyler or Peter Drury, with football words, and vary them from line to line (strike, finish, pressing, keeper, counter-attack, midfield, set piece, final whistle). " +
    "Use only the given facts; keep names exactly as written; write numbers as digits and add none; never use he, she, his or her: say the name or they. " +
    "The player is the side we follow: low opponent health means the opponent is losing, low player health means the player is struggling. " +
    "Do not repeat the wording of the recent lines. No emojis, quotation marks or hashtags.",
});
