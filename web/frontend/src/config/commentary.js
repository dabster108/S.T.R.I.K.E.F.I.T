// Live Commentary: every tunable and every line of text, in one place. lib/commentary.js has the logic and no numbers of its own.
//
// Numbers that come from the game are NOT here: the full-power threshold and the minimum counted swing speed are read from the
// levels config (lib/game.js, swingBands), the boss phase breaks from lib/arena.js (PHASE_BREAKS), the swing cap from game.js.

export const COMMENTARY = Object.freeze({
  // --- pacing rules ---------------------------------------------------------------------------------------------------
  minGapMs: 1200, // at most one line on screen per this long
  queueMax: 3, // lines waiting for their turn; when it is full the lowest priority is dropped first
  sameSituationMs: 4000, // the same situation is never announced twice within this window
  staleMs: 6000, // a line that waited longer than this is out of date and is dropped instead of shown
  logMax: 20, // lines kept in the Commentary log
  captionHoldMs: 5000, // a caption stays this long after it is shown, then the bar returns to its resting text

  // --- thresholds -----------------------------------------------------------------------------------------------------
  comboMilestones: [5, 10, 20],
  playerHpLowFrac: 0.25, // "player HP under 25%"
  playerHpRearmFrac: 0.4, // the low-health line can fire again once health has recovered above this
  monsterHpLowFrac: 0.1, // "monster HP under 10%"
  idleMs: 6000, // no counted swing for this long: nudge once
  paceMinIntervalMs: 20000, // Ghost Pace lines: at most one per this long
  paceMinDamage: 25, // ... and only when the gap to the personal best is at least this many damage
  // The guard's damage reduction. MonsterAI.defendDamageMultiplier is 0.3 in code (damage x 0.3 = 70% absorbed); no scene override
  // was found, but that is not verified against a running build.
  guardAbsorbPct: 70,

  // --- type-in effect (opacity only, one CSS animation per character; off under reduced motion and in Calm mode) ----------
  typeStepMs: 16,
  typeCharMs: 110,
  typeMaxChars: 90, // a longer line appears at once

  // --- priority: result > phase change > monster HP low > player HP low > full power > attack wind-up > combo > clash > rest ---
  priority: Object.freeze({ result: 9, phase: 8, monsterLow: 7, playerLow: 6, fullPower: 5, windUp: 4, combo: 3, clash: 2, rest: 1 }),

  // --- local preferences ----------------------------------------------------------------------------------------------
  storageKeys: Object.freeze({ hidden: "strikefit.commentary.hidden", speech: "strikefit.commentary.speech", plain: "strikefit.commentary.plain" }),
  speechRate: 1.05,

  // --- the full-time report: a short summary under the caption when the fight is over, typed in slowly -------------------------
  summary: Object.freeze({
    title: "Full-time report",
    delayMs: 1500, // after the result line, so the victory or defeat line has its moment
    aiWaitMs: 2500, // how long to wait for the AI's version; after that the plain report is shown
    typeStepMs: 28, // one character every 28 ms: a 200-character report takes about six seconds
    typeMaxChars: 400,
  }),

  // --- the AI commentator (server: /api/commentary/*; Groq writes the line, Google reads it aloud) ----------------------------
  // The plain template line is always the fallback, so these only decide how long to wait for the better one.
  ai: Object.freeze({
    requestTimeoutMs: 1800, // give up on a line request after this long (the server has its own, shorter limit)
    showWaitMs: 600, // when a line's turn comes and the AI sentence is not back yet, wait this much longer, then show the plain one
    maxInFlight: 3, // line requests running at once for one fight
    // The free Groq tier allows about 20 requests a minute in total, so the AI is asked about the moments that gain from colour, not
    // about every line. Advice and bookkeeping lines ("swings under 2 m/s do not count", the guard, the Ghost Pace gap) stay as written.
    situations: Object.freeze(["enter", "fullPower", "combo", "clash", "windUp", "hurt", "dodge", "playerLow", "monsterLow", "phase2", "phase3", "victory", "defeat"]),
    maxPerMinute: 12, // line requests one fight may send per minute ...
    alwaysAskPriority: 6, // ... except for lines of this priority or more (low health, boss phase, result), which are the big moments
    recentLines: 3, // the last lines shown, sent so the model does not repeat their wording
    voiceTimeoutMs: 4000, // the audio of one line
    voiceStaleMs: 3500, // a spoken line that had to wait for the one before it is skipped if it waited this long (the caption still showed it)
    statusRetryMs: 60000, // when the status check failed (server asleep), ask again no sooner than this
  }),
});

// Template pools: at least three variants per situation (the logic never uses the same one twice in a row for a situation).
// Placeholders: {player} {monster} {short} {attack} {speed} {n} {pct} {min} {amount}
export const LINES = Object.freeze({
  enter: ["{player} steps into the arena against {monster}.", "{monster} awaits {player}. Fight!", "The fight is on: {player} against {monster}."],
  fullPower: ["Full-power swing, {speed} m/s!", "{speed} m/s! That is a full-power hit.", "Huge swing from {player}, {speed} m/s!", "Full power! {speed} m/s lands clean."],
  blocked: ["Blocked, {pct}% absorbed.", "The guard soaks it up, {pct}% absorbed.", "{pct}% of that hit was absorbed by the guard."],
  combo: ["{n}-hit combo!", "{n} in a row, {player} is on a roll!", "Combo times {n}, keep it going!"],
  clash: ["Weapons clash!", "Steel meets steel!", "A clash! Both weapons ring out."],
  windUp: ["{monster} winds up {attack}!", "Here comes {attack} from {monster}!", "{monster} readies {attack}, watch out!"],
  guardUp: ["{monster} raises its guard.", "Guard up! {monster} is defending.", "{monster} braces behind its guard."],
  guardDown: ["{monster}'s guard drops.", "The guard is down. Strike now!", "{monster} lowers its guard."],
  hurt: ["{player} takes {amount} damage from {attack}.", "Ouch! {attack} connects.", "{player} is hit by {attack}."],
  dodge: ["{player} dodges {attack}!", "Clean dodge, {attack} misses.", "{attack} whistles past {player}."],
  playerLow: ["{player} is under {pct}% health, careful!", "Danger! {player} is running low.", "Health is low. {player} must defend."],
  monsterLow: ["One more hit!", "{monster} is almost down. One more hit!", "Nearly there, finish it!"],
  phase2: ["{short} enters phase 2!", "Phase 2! {short} changes tactics.", "{short} has had enough and moves to phase 2."],
  phase3: ["{short} enters phase 3, enraged!", "Enraged! {short} attacks faster now.", "Phase 3: {short} is enraged!"],
  paceAhead: ["{player} is {n} damage ahead of their best pace.", "Ahead of the personal best by {n} damage!", "Record pace: {n} damage up."],
  paceBehind: ["{n} damage behind the personal best pace.", "Behind best pace by {n}, time to push.", "{player} needs {n} more damage to catch their best."],
  idle: ["Swings under {min} m/s don't count.", "No swing for a while. Anything under {min} m/s does nothing.", "Swing harder: {min} m/s is the minimum that counts."],
  victory: ["Victory! {player} defeats {monster}!", "{monster} falls! Victory for {player}!", "It is over. {player} wins!"],
  defeat: ["Defeat. {monster} takes this round.", "{player} is down, {monster} wins.", "Defeated! Time to go again."],
  abandoned: ["{player}'s headset went quiet: fight abandoned.", "Fight abandoned.", "The fight ended early and was abandoned."],
});

// How each situation looks: its priority class (a key of COMMENTARY.priority), the caption tone and its icon name.
export const SITUATIONS = Object.freeze({
  enter: { priority: "rest", tone: "accent", icon: "headset" },
  fullPower: { priority: "fullPower", tone: "accent", icon: "bolt" },
  blocked: { priority: "rest", tone: "warn", icon: "shield" },
  combo: { priority: "combo", tone: "good", icon: "flame" },
  clash: { priority: "clash", tone: "warn", icon: "swords" },
  windUp: { priority: "windUp", tone: "bad", icon: "flame" },
  guardUp: { priority: "rest", tone: "warn", icon: "shield" },
  guardDown: { priority: "rest", tone: "good", icon: "sparkles" },
  hurt: { priority: "rest", tone: "bad", icon: "heart" },
  dodge: { priority: "rest", tone: "good", icon: "shieldCheck" },
  playerLow: { priority: "playerLow", tone: "bad", icon: "heart" },
  monsterLow: { priority: "monsterLow", tone: "good", icon: "target" },
  phase2: { priority: "phase", tone: "warn", icon: "flame" },
  phase3: { priority: "phase", tone: "bad", icon: "flame" },
  paceAhead: { priority: "rest", tone: "good", icon: "arrowUp" },
  paceBehind: { priority: "rest", tone: "warn", icon: "clock" },
  idle: { priority: "rest", tone: "muted", icon: "info" },
  victory: { priority: "result", tone: "good", icon: "crown" },
  defeat: { priority: "result", tone: "bad", icon: "xCircle" },
  abandoned: { priority: "result", tone: "muted", icon: "headset" },
});

// What the bar says between lines.
export const RESTING_TEXT = "Commentary is on. The next big moment shows here.";
