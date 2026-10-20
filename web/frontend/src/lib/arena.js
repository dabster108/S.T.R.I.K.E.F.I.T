// Live Arena logic with no React in it (tested in arena.test.js): who the monster is, which phase a boss is in, how long an attack
// telegraphs, and how raw events become the arena's transient state and its kill-feed lines.
//
// Names, weapons and lore come from the shared story content (content/story.json); attack names, categories and wind-up times from
// content/monsterAttacks.json (read off the monster prefabs by web/tools/extract-monster-attacks.py). Nothing is typed in a second
// time here. No JSON is imported in this file (so node --test can run it): makeArena() takes both as arguments, and arenaApp.js binds
// it to the real files for the app, the same split as storyData.js / story.js.

import { LEVELS } from "./game.js";

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
export const LEVEL_KEYS = ["Easy", "Medium", "Hard"];

// --- Level 3 boss phases (BossPhaseController: breaks at 66 % and 33 % HP, the 33 % break starts Enrage) ---------------------
export const PHASE_BREAKS = [0.66, 0.33];

/** 1, 2 or 3 (enraged) from the monster's HP fraction; null for levels without phases or without HP data. */
export function phaseOf(levelKey, hpFraction) {
  if (levelKey !== "Hard" || !isNum(hpFraction)) return null;
  if (hpFraction > PHASE_BREAKS[0]) return 1;
  if (hpFraction > PHASE_BREAKS[1]) return 2;
  return 3;
}
export const phaseLabel = (phase) => (phase === 3 ? "Enraged" : phase ? `Phase ${phase}` : null);

// How long a resolved attack stays on the strip before it fades, and how long an unresolved one waits for its outcome.
export const ATTACK_RESULT_HOLD_MS = 1400;
export const ATTACK_GRACE_MS = 900;
export const NAME_ONLY_HOLD_MS = 2200;
export const GUARD_FALLBACK_MS = 4500;
export const CLASH_FLASH_MS = 700;

/** How long (ms) an attack sits on the strip if no outcome event arrives. */
export const attackLifetimeMs = (info) => (info.windUp === null ? NAME_ONLY_HOLD_MS : info.windUp * 1000 + ATTACK_GRACE_MS);

export function initialArena() {
  return { attack: null, guardUntil: 0, clashAt: 0, seq: 0 };
}

/** Is the guard up at time `now`? */
export const isGuarding = (state, now) => state.guardUntil > now;
/** Is the clash flash showing? */
export const isClashing = (state, now) => state.clashAt > 0 && now - state.clashAt < CLASH_FLASH_MS;
/** The attack still worth showing at `now`, or null once it has faded. */
export function visibleAttack(state, now) {
  const a = state.attack;
  if (!a) return null;
  const end = a.result ? a.resultAt + ATTACK_RESULT_HOLD_MS : a.startedAt + attackLifetimeMs(a.info);
  return now < end ? a : null;
}

/** When the soonest thing on screen (guard, clash flash, attack strip) stops being shown, in the same ms clock; null when nothing is. */
export function nextExpiry(state, now) {
  const ends = [];
  if (state.guardUntil > now) ends.push(state.guardUntil);
  if (isClashing(state, now)) ends.push(state.clashAt + CLASH_FLASH_MS);
  const a = visibleAttack(state, now);
  if (a) ends.push(a.result ? a.resultAt + ATTACK_RESULT_HOLD_MS : a.startedAt + attackLifetimeMs(a.info));
  return ends.length ? Math.min(...ends) : null;
}

// --- Kill-feed ------------------------------------------------------------------------------------------------------------------

/** The result moment's wording. */
export function resultLabel(outcome) {
  if (outcome === "victory") return { title: "Victory", tone: "good", icon: "crown", line: "The monster is down." };
  if (outcome === "defeat") return { title: "Defeat", tone: "bad", icon: "xCircle", line: "The player fell." };
  if (outcome === "abandoned") return { title: "Abandoned", tone: "muted", icon: "headset", line: "The headset went quiet before the fight ended." };
  return { title: "Fight finished", tone: "muted", icon: "check", line: "" };
}
// How long a finished fight stays on the stage: the result moment, then the full-time report (it appears after 1.5 s and types in over about
// 5 s, so it needs time to be read). It was 8 s before the report existed. liveStore.js keeps the fight a little longer than this.
export const RESULT_HOLD_MS = 28000;

// --- Impact moments -------------------------------------------------------------------------------------------------------------

/**
 * What a stream event means for the arena's impact moments, or null: a full-power hit that landed (not absorbed by the guard) gets
 * the gauge punch and a floating "+damage"; a weapon clash gets a ring pulse. The tier is the one the game reported.
 * -> { kind: "full", damage } | { kind: "clash" } | null
 */
export function impactOf(event) {
  const p = event?.payload && typeof event.payload === "object" ? event.payload : {};
  if (event?.type === "clash") return { kind: "clash" };
  if (event?.type !== "hit" || p.defended === true) return null;
  const tier = typeof p.tier === "string" ? p.tier.toLowerCase() : "";
  if (tier !== "full" || !isNum(p.damage) || p.damage <= 0) return null;
  return { kind: "full", damage: Math.round(p.damage) };
}

const num = (v, d = 0) => (isNum(v) ? Math.round(v * 10 ** d) / 10 ** d : null);

/** The data-dependent half: monsters (story content) and attacks (prefab table). */
export function makeArena({ attackTable, guardianForKey, levels = LEVELS }) {
  const CATEGORY_LABEL = attackTable.categoryLabels;
  const categoryLabel = (category) => CATEGORY_LABEL[category] || category || "Attack";

  const monsterFor = (levelKey) => {
    const lv = levels[levelKey];
    if (!lv) return null;
    const g = guardianForKey(levelKey);
    return {
      levelKey,
      levelNumber: lv.number,
      levelName: lv.name,
      difficulty: lv.difficulty,
      name: g?.name || lv.monster || "The monster",
      weapon: g?.weapon || lv.weapons || null,
      lore: g?.lore || null,
      tips: g?.tips || [],
      maxHp: lv.hp || null,
      hasPhases: levelKey === "Hard",
    };
  };

  /**
   * What is known about an attack the monster just started. windUp (seconds before the first hit lands = the player's telegraph) is the
   * authored prefab value, shortened by the enrage tempo for the Level 3 boss in phase 3; null when the attack isn't in the table (the
   * strip then shows the name only). The category the event reported wins over the table's.
   */
  const attackInfo = (levelKey, name, category, { enraged = false } = {}) => {
    const row = attackTable.levels[levelKey]?.[name] || null;
    const tempo = enraged && levelKey === "Hard" ? attackTable.enragedTempo : 1;
    return {
      name: name || "Attack",
      category: category || row?.category || null,
      categoryLabel: categoryLabel(category || row?.category),
      windUp: row ? Math.round((row.windUp / tempo) * 1000) / 1000 : null,
    };
  };

  /**
   * Folds one stream event into the arena's transient state for one fight. `at` is the local arrival time in ms. Pure: returns a new
   * state (or the same object when the event changes nothing).
   *   attack   the attack in progress { key, info, startedAt, result: null | "hit" | "dodged", resultAt }
   *   guarding true from monster_defend until monster_guard (or GUARD_FALLBACK_MS)
   *   clashAt  when the last weapon clash arrived
   */
  const reduceArena = (state, event, ctx) => {
    const { levelKey, enraged = false, at } = ctx;
    const p = event.payload && typeof event.payload === "object" ? event.payload : {};
    switch (event.type) {
      case "monster_attack":
        return { ...state, seq: state.seq + 1, attack: { key: state.seq + 1, info: attackInfo(levelKey, p.attack, p.category, { enraged }), startedAt: at, result: null, resultAt: 0 } };
      case "player_hurt":
      case "dodge": {
        const a = state.attack;
        if (!a || a.result) return state;
        if (p.attack && a.info.name !== p.attack) return state;
        return { ...state, attack: { ...a, result: event.type === "dodge" ? "dodged" : "hit", resultAt: at } };
      }
      case "monster_defend":
        return { ...state, guardUntil: at + GUARD_FALLBACK_MS };
      case "monster_guard":
        return state.guardUntil ? { ...state, guardUntil: 0 } : state;
      case "clash":
        return { ...state, clashAt: at };
      case "hit":
        // a hit that the guard absorbed also proves the guard is up
        return p.defended === true && state.guardUntil <= at ? { ...state, guardUntil: at + 1200 } : state;
      default:
        return state;
    }
  };

  /**
   * One kill-feed line for a stored/streamed event, or null for events that are noise there (HP snapshots, too-slow swings, goal
   * bookkeeping). monster: the monster's short name for the fight's level.
   * -> { icon, tone: "accent" | "good" | "bad" | "warn" | "muted", text }
   */
  const killFeedItem = (e, { monster = "The monster" } = {}) => {
    const p = e.payload && typeof e.payload === "object" ? e.payload : {};
    const who = e.playerName || "Player";
    switch (e.type) {
      case "session_start":
        return { icon: "headset", tone: "accent", text: `${who} entered the arena against ${monster}` };
      case "hit": {
        const bits = [isNum(p.swingSpeed) ? `${num(Math.min(p.swingSpeed, 8), 1)} m/s` : null, p.tier || null].filter(Boolean);
        const dmg = isNum(p.damage) ? `${num(p.damage)} dmg` : "a hit";
        return { icon: "bolt", tone: p.defended ? "muted" : "accent", text: `${who} hit ${p.defended ? "the guard" : monster} for ${dmg}${bits.length ? ` (${bits.join(", ")})` : ""}` };
      }
      case "monster_attack":
        return { icon: "flame", tone: "bad", text: `${monster} winds up ${p.attack || "an attack"}${p.category ? ` (${categoryLabel(p.category)})` : ""}` };
      case "player_hurt":
        return { icon: "heart", tone: "bad", text: `${who} took ${num(p.amount, 1)} damage${p.attack ? ` from ${p.attack}` : ""}` };
      case "dodge":
        return { icon: "shieldCheck", tone: "good", text: `${who} avoided ${p.attack || "the attack"}` };
      case "block":
        return { icon: "shield", tone: "good", text: `${who} blocked the attack` };
      case "clash":
        return { icon: "swords", tone: "warn", text: "Weapons clash" };
      case "monster_defend":
        return { icon: "shield", tone: "warn", text: `${monster} raised its guard` };
      case "monster_guard":
        return p.blockedHits > 0 ? { icon: "shield", tone: "warn", text: `${monster}'s guard blocked ${p.blockedHits} hit${p.blockedHits === 1 ? "" : "s"}` } : null;
      case "opening_hit":
        return { icon: "sparkles", tone: "good", text: `${who} found an opening` };
      case "session_complete": {
        const o = p.outcome || "finished";
        return { icon: o === "victory" ? "crown" : o === "defeat" ? "xCircle" : "check", tone: o === "victory" ? "good" : o === "defeat" ? "bad" : "muted", text: `${who}: ${o === "victory" ? "Victory" : o === "defeat" ? "Defeat" : o}, score ${num(p.score)}` };
      }
      default:
        return null;
    }
  };

  return { categoryLabel, monsterFor, attackInfo, reduceArena, killFeedItem };
}
