// Live Commentary, the logic (no React, no clock, no storage; tested in commentary.test.js). The numbers and the wording are in
// config/commentary.js; what the game itself decides (full-power speed, minimum swing speed, boss phase breaks, the 8 m/s cap) is read
// from lib/game.js and lib/arena.js, so nothing is typed in a second time here.
//
// Two small parts:
//   step(event, state)  -> { lines, state }   what a stream event means: which situations it triggers, and the next memory
//   a queue             enqueue / release     the pacing rules: one line per 1.2 s, three waiting at most, low priority dropped first,
//                                             the same situation never twice within 4 s
// nextLine(event, state) is step() reduced to "the one line this event is worth", the shape the feature was specified with.
//
// Events are { type, payload, at } where `at` is a millisecond clock chosen by the caller (performance.now() in the app). Besides the
// game's own event types (hit, monster_attack, monster_defend, monster_guard, clash, player_hurt, dodge, weak_swing, hp,
// session_start) there are four that the caller adds: "state" (a live-state snapshot), "tick" (time passing), "pace" ({ delta }, the
// Ghost Pace gap in damage) and "result" ({ outcome }).

import { COMMENTARY, LINES, SITUATIONS } from "../config/commentary.js";
import { phaseOf } from "./arena.js";
import { clampSpeed, swingBands } from "./game.js";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const frac = (v, max) => (isNum(v) && isNum(max) && max > 0 ? Math.max(0, Math.min(1, v / max)) : null);

/** "The Harbor Tyrant" -> "The Tyrant"; names with fewer words stay as they are. */
export function shortMonsterName(name) {
  const words = String(name || "The monster").trim().split(/\s+/);
  return words.length >= 3 && /^the$/i.test(words[0]) ? `${words[0]} ${words[words.length - 1]}` : words.join(" ");
}

/** { fullPower, minCounted } in m/s for a level, from the levels config. */
export function swingLimits(levelKey) {
  const bands = swingBands(levelKey);
  return { fullPower: bands.find((b) => b.id === "full")?.from ?? null, minCounted: bands[0]?.id === "none" ? bands[0].to : null };
}

// ---------------------------------------------------------------------------------------------------------------- wording

/** Replaces {name} placeholders; a placeholder with no value is left out cleanly. */
export function fill(template, vars) {
  return template.replace(/\{(\w+)\}/g, (_, k) => (vars[k] === undefined || vars[k] === null ? "" : String(vars[k]))).replace(/\s+([.,!?])/g, "$1").replace(/\s{2,}/g, " ").trim();
}

// A tiny seeded generator, so the same events always give the same lines (and tests can pin them).
function nextSeed(seed) {
  let a = (seed + 0x6d2b79f5) >>> 0;
  let t = Math.imul(a ^ (a >>> 15), 1 | a);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return { seed: a, value: ((t ^ (t >>> 14)) >>> 0) / 4294967296 };
}

/** Picks a variant of a pool that is not the one used last for this situation. -> { index, seed } */
export function pickVariant(poolSize, lastIndex, seed) {
  const r = nextSeed(seed);
  if (poolSize <= 1) return { index: 0, seed: r.seed };
  let index = Math.floor(r.value * poolSize);
  if (index === lastIndex) index = (index + 1) % poolSize;
  return { index, seed: r.seed };
}

/** A stable number from a fight id, so two fights do not pick their variants in lockstep. */
export function hashSeed(text) {
  let h = 2166136261;
  for (const c of String(text)) h = Math.imul(h ^ c.charCodeAt(0), 16777619);
  return h >>> 0;
}

// ---------------------------------------------------------------------------------------------------------------- memory

/**
 * The commentator's memory for one fight. `initial` (optional) is the fight as it already is when the page opens, so that opening the
 * Live Arena mid-fight does not announce a phase change or a low-health warning that happened earlier.
 *   initial: { playerHp, playerMaxHp, monsterHp, monsterMaxHp, combo }
 */
export function createState({ levelKey, playerName = "Player", monsterName = "The monster", seed = 1, initial = {} } = {}) {
  const limits = swingLimits(levelKey);
  const monsterFrac = frac(initial.monsterHp, initial.monsterMaxHp);
  const playerFrac = frac(initial.playerHp, initial.playerMaxHp);
  const combo = isNum(initial.combo) ? Math.max(0, Math.round(initial.combo)) : 0;
  return {
    levelKey,
    playerName,
    monsterName,
    shortName: shortMonsterName(monsterName),
    fullPowerSpeed: limits.fullPower,
    minCounted: limits.minCounted,
    hasPhases: levelKey === "Hard",
    seed,
    lastVariant: {}, // situation family -> index used last
    monsterFrac,
    playerFrac,
    phase: phaseOf(levelKey, monsterFrac), // 1, 2, 3 or null
    monsterLowSaid: monsterFrac !== null && monsterFrac < COMMENTARY.monsterHpLowFrac,
    playerLowSaid: playerFrac !== null && playerFrac < COMMENTARY.playerHpLowFrac,
    combo,
    comboSaid: COMMENTARY.comboMilestones.filter((m) => combo >= m), // milestones already announced in this chain
    lastSwingAt: null,
    idleSaid: false,
    lastPaceAt: -Infinity,
    finished: false,
  };
}

// ---------------------------------------------------------------------------------------------------------------- one line

function makeLine(state, situation, vars, at) {
  const meta = SITUATIONS[situation];
  const pool = LINES[situation];
  const picked = pickVariant(pool.length, state.lastVariant[situation], state.seed);
  const text = fill(pool[picked.index], {
    player: state.playerName,
    monster: state.monsterName,
    short: state.shortName,
    ...vars,
  });
  return {
    // each combo milestone is its own situation, so "10-hit combo" is not blocked by a "5-hit combo" a moment ago
    line: { text, situation: situation === "combo" ? `combo${vars.n}` : situation, priority: COMMENTARY.priority[meta.priority], tone: meta.tone, icon: meta.icon, at },
    next: { ...state, seed: picked.seed, lastVariant: { ...state.lastVariant, [situation]: picked.index } },
  };
}

/**
 * What one event means. Returns every line it is worth (highest priority first) and the new memory. Never mutates `state`.
 */
export function step(event, state) {
  if (!event || !event.type || state.finished) return { lines: [], state };
  const at = isNum(event.at) ? event.at : 0;
  const p = event.payload && typeof event.payload === "object" ? event.payload : {};
  let s = { ...state };
  const lines = [];
  const say = (situation, vars = {}) => {
    const made = makeLine(s, situation, vars, at);
    s = made.next;
    lines.push(made.line);
  };

  // 1. keep the picture of the fight up to date from any payload that carries it
  const monsterFrac = frac(p.monsterHp, p.monsterMaxHp);
  const playerFrac = frac(p.playerHp, p.playerMaxHp);
  const hasCombo = isNum(p.combo);

  if (event.type === "result") {
    const outcome = p.outcome === "victory" || p.outcome === "defeat" ? p.outcome : "abandoned";
    say(outcome);
    return { lines: sortLines(lines), state: { ...s, finished: true } };
  }

  // 2. what the game itself reported
  switch (event.type) {
    case "session_start":
      s.lastSwingAt = at;
      say("enter");
      break;
    case "hit": {
      s.lastSwingAt = at;
      s.idleSaid = false;
      const speed = isNum(p.swingSpeed) ? clampSpeed(p.swingSpeed) : null;
      if (p.defended === true) say("blocked", { pct: COMMENTARY.guardAbsorbPct });
      else if (speed !== null && (p.tier === "full" || (isNum(s.fullPowerSpeed) && speed >= s.fullPowerSpeed && p.tier === undefined))) say("fullPower", { speed: speed.toFixed(1) });
      break;
    }
    case "weak_swing":
      s.lastSwingAt = at; // a too-slow swing is still a swing: the player is moving
      s.idleSaid = false;
      break;
    case "monster_attack":
      say("windUp", { attack: p.attack || "an attack" });
      break;
    case "monster_defend":
      say("guardUp");
      break;
    case "monster_guard":
      say("guardDown");
      break;
    case "clash":
      say("clash");
      break;
    case "player_hurt":
      if (isNum(p.amount) && p.amount > 0) say("hurt", { amount: Math.round(p.amount), attack: p.attack || "the attack" });
      break;
    case "dodge":
      say("dodge", { attack: p.attack || "the attack" });
      break;
    case "pace":
      if (isNum(p.delta) && Math.abs(p.delta) >= COMMENTARY.paceMinDamage && at - s.lastPaceAt >= COMMENTARY.paceMinIntervalMs) {
        s.lastPaceAt = at;
        say(p.delta > 0 ? "paceAhead" : "paceBehind", { n: Math.round(Math.abs(p.delta)).toLocaleString("en-US") });
      }
      break;
    case "tick":
      if (s.lastSwingAt === null) s.lastSwingAt = at; // opened mid-fight: the idle clock starts now
      else if (!s.idleSaid && at - s.lastSwingAt >= COMMENTARY.idleMs && isNum(s.minCounted)) {
        s.idleSaid = true;
        say("idle", { min: s.minCounted });
      }
      break;
    default:
      break;
  }

  // 3. thresholds crossed by the new picture of the fight
  if (monsterFrac !== null) {
    const was = s.monsterFrac;
    s.monsterFrac = monsterFrac;
    if (s.hasPhases) {
      const now = phaseOf(s.levelKey, monsterFrac);
      if (now !== null && s.phase !== null && now > s.phase) say(now >= 3 ? "phase3" : "phase2");
      if (now !== null) s.phase = Math.max(s.phase ?? 0, now);
    }
    if (!s.monsterLowSaid && monsterFrac < COMMENTARY.monsterHpLowFrac && monsterFrac > 0 && (was === null || was >= COMMENTARY.monsterHpLowFrac)) {
      s.monsterLowSaid = true;
      say("monsterLow");
    } else if (monsterFrac < COMMENTARY.monsterHpLowFrac) s.monsterLowSaid = true;
  }
  if (playerFrac !== null) {
    const was = s.playerFrac;
    s.playerFrac = playerFrac;
    if (!s.playerLowSaid && playerFrac < COMMENTARY.playerHpLowFrac && playerFrac > 0 && (was === null || was >= COMMENTARY.playerHpLowFrac)) {
      s.playerLowSaid = true;
      say("playerLow", { pct: Math.round(COMMENTARY.playerHpLowFrac * 100) });
    } else if (playerFrac >= COMMENTARY.playerHpRearmFrac) s.playerLowSaid = false;
  }
  if (hasCombo) {
    const combo = Math.max(0, Math.round(p.combo));
    if (combo < s.combo) s.comboSaid = s.comboSaid.filter((m) => m <= combo); // the chain broke: milestones can be earned again
    s.combo = combo;
    // Several milestones crossed by one update (an event was missed): all count as announced, only the highest is said.
    const crossed = COMMENTARY.comboMilestones.filter((m) => combo >= m && !s.comboSaid.includes(m));
    if (crossed.length) {
      s.comboSaid = [...s.comboSaid, ...crossed];
      say("combo", { n: Math.max(...crossed) });
    }
  }
  return { lines: sortLines(lines), state: s };
}

const sortLines = (lines) => [...lines].sort((a, b) => b.priority - a.priority);

/** The one line an event is worth (the most important), or null. A pure function of the event and the memory it is given. */
export function nextLine(event, state) {
  const { lines } = step(event, state);
  return lines.length ? { text: lines[0].text, priority: lines[0].priority, tone: lines[0].tone } : null;
}

// ---------------------------------------------------------------------------------------------------------------- pacing

export const createQueue = () => ({ items: [], lastEmitAt: -Infinity, lastBySituation: {} });

/**
 * Adds a line unless the same situation was shown less than sameSituationMs ago or is already waiting. When the queue is full the
 * lowest priority goes first (the oldest of equals); a new line that is the lowest of all is the one dropped. A result line
 * (victory, defeat, abandoned) replaces everything that was waiting.
 */
export function enqueue(queue, line, now) {
  const lastShown = queue.lastBySituation[line.situation];
  if (lastShown !== undefined && now - lastShown < COMMENTARY.sameSituationMs) return queue;
  if (queue.items.some((l) => l.situation === line.situation)) return queue;
  // The result ends the commentary: whatever was still waiting is out of date the moment the fight is over.
  if (line.priority >= COMMENTARY.priority.result) return { ...queue, items: [{ ...line, queuedAt: now }] };
  let items = [...queue.items, { ...line, queuedAt: now }];
  if (items.length > COMMENTARY.queueMax) {
    let drop = 0;
    for (let i = 1; i < items.length; i++) if (items[i].priority < items[drop].priority) drop = i; // strictly lower: the oldest of equals stays first in line to go
    items = items.filter((_, i) => i !== drop);
  }
  return { ...queue, items };
}

/** Milliseconds until a line may be shown (0 = now); null when nothing is waiting. */
export function nextReleaseDelay(queue, now) {
  if (!queue.items.length) return null;
  return Math.max(0, queue.lastEmitAt + COMMENTARY.minGapMs - now);
}

/** Shows the most important waiting line if at least minGapMs has passed since the last one. Stale lines are dropped first. */
export function release(queue, now) {
  const fresh = queue.items.filter((l) => now - l.queuedAt <= COMMENTARY.staleMs);
  if (!fresh.length) return { queue: { ...queue, items: [] }, line: null };
  if (now - queue.lastEmitAt < COMMENTARY.minGapMs) return { queue: { ...queue, items: fresh }, line: null };
  let best = 0;
  for (let i = 1; i < fresh.length; i++) if (fresh[i].priority > fresh[best].priority) best = i; // equal priority: the earlier one
  const line = fresh[best];
  return {
    queue: { items: fresh.filter((_, i) => i !== best), lastEmitAt: now, lastBySituation: { ...queue.lastBySituation, [line.situation]: now } },
    line,
  };
}
