import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import {
  ATTACK_GRACE_MS, ATTACK_RESULT_HOLD_MS, attackLifetimeMs, GUARD_FALLBACK_MS, initialArena, isClashing, isGuarding, makeArena, phaseLabel, phaseOf,
  impactOf, nextExpiry, resultLabel, visibleAttack,
} from "./arena.js";
import { swingBands } from "./game.js";
import { makeStory } from "./storyData.js";

const require = createRequire(import.meta.url);
const story = require("../content/story.json");
const { monsterFor, attackInfo, categoryLabel, reduceArena, killFeedItem } = makeArena({ attackTable: require("../content/monsterAttacks.json"), guardianForKey: makeStory(story).guardianForKey });

const ev = (type, payload = {}, extra = {}) => ({ type, payload, playerName: "ann", ...extra });
const ctx = (at, over = {}) => ({ levelKey: "Easy", at, ...over });

test("each level's monster comes from the shared story content, not a second copy", () => {
  assert.deepEqual([1, 2, 3].map((n) => monsterFor(["Easy", "Medium", "Hard"][n - 1]).name), story.guardians.map((g) => g.name));
  const m = monsterFor("Hard");
  assert.equal(m.name, "The Harbor Tyrant");
  assert.equal(m.weapon, "Twin axes");
  assert.equal(m.levelNumber, 3);
  assert.equal(m.difficulty, "Hard");
  assert.equal(m.hasPhases, true);
  assert.equal(monsterFor("Easy").hasPhases, false);
  assert.equal(monsterFor("Nope"), null);
});

test("boss phases follow the 66 % and 33 % HP breaks; only Level 3 has them", () => {
  assert.equal(phaseOf("Hard", 1), 1);
  assert.equal(phaseOf("Hard", 0.67), 1);
  assert.equal(phaseOf("Hard", 0.66), 2);
  assert.equal(phaseOf("Hard", 0.34), 2);
  assert.equal(phaseOf("Hard", 0.33), 3);
  assert.equal(phaseOf("Hard", 0), 3);
  assert.equal(phaseOf("Easy", 0.2), null);
  assert.equal(phaseOf("Hard", null), null);
  assert.deepEqual([1, 2, 3, null].map(phaseLabel), ["Phase 1", "Phase 2", "Enraged", null]);
});

test("attack wind-ups come from the prefab table and are shorter while the boss is enraged", () => {
  assert.equal(attackInfo("Easy", "Slash", "Standard").windUp, 0.9);
  assert.equal(attackInfo("Easy", "Spin Slash", "Heavy").windUp, 1.28);
  assert.equal(attackInfo("Easy", "Leaping Slam", "Special").windUp, 1.6);
  assert.equal(attackInfo("Hard", "Earthsplitter Slam", "Special").windUp, 1.3);
  assert.equal(attackInfo("Hard", "Earthsplitter Slam", "Special", { enraged: true }).windUp, 1.13); // 1.3 / 1.15
  assert.equal(attackInfo("Easy", "Slash", "Standard", { enraged: true }).windUp, 0.9); // enrage tempo is a Level 3 thing
});

test("an attack the table doesn't know shows its name only (no wind-up)", () => {
  const a = attackInfo("Easy", "Mystery Move", "Heavy");
  assert.equal(a.windUp, null);
  assert.equal(a.name, "Mystery Move");
  assert.equal(a.categoryLabel, "Heavy");
  assert.equal(attackInfo("Easy", "Slash", null).category, "Standard"); // the table fills in a missing category
  assert.equal(categoryLabel("IdleSlam"), "Slam");
  assert.equal(categoryLabel("Combo"), "Combo");
});

test("a monster_attack starts the strip; the player being hurt or dodging resolves it", () => {
  let s = initialArena();
  s = reduceArena(s, ev("monster_attack", { attack: "Slash", category: "Standard" }), ctx(1000));
  assert.equal(s.attack.info.windUp, 0.9);
  assert.equal(s.attack.result, null);
  assert.equal(visibleAttack(s, 1500).info.name, "Slash");
  const hurt = reduceArena(s, ev("player_hurt", { attack: "Slash", amount: 9 }), ctx(1900));
  assert.equal(hurt.attack.result, "hit");
  const dodge = reduceArena(s, ev("dodge", { attack: "Slash" }), ctx(1900));
  assert.equal(dodge.attack.result, "dodged");
  // an outcome for a DIFFERENT attack is ignored, and so is a second outcome
  assert.equal(reduceArena(s, ev("dodge", { attack: "Overhead Strike" }), ctx(1900)), s);
  assert.equal(reduceArena(hurt, ev("dodge", { attack: "Slash" }), ctx(1950)), hurt);
});

test("the strip fades: after the hold once resolved, after wind-up plus grace if nothing resolves it, after 2.2 s for a name-only attack", () => {
  let s = reduceArena(initialArena(), ev("monster_attack", { attack: "Slash" }), ctx(0));
  assert.equal(attackLifetimeMs(s.attack.info), 900 + ATTACK_GRACE_MS);
  assert.ok(visibleAttack(s, 1799));
  assert.equal(visibleAttack(s, 1801), null);
  const done = reduceArena(s, ev("dodge", { attack: "Slash" }), ctx(1000));
  assert.ok(visibleAttack(done, 1000 + ATTACK_RESULT_HOLD_MS - 1));
  assert.equal(visibleAttack(done, 1000 + ATTACK_RESULT_HOLD_MS + 1), null);
  const unknown = reduceArena(initialArena(), ev("monster_attack", { attack: "Mystery" }), ctx(0));
  assert.ok(visibleAttack(unknown, 2100));
  assert.equal(visibleAttack(unknown, 2300), null);
  assert.equal(visibleAttack(initialArena(), 0), null);
});

test("enraged ctx shortens the telegraph of a Level 3 attack", () => {
  const s = reduceArena(initialArena(), ev("monster_attack", { attack: "Twin Whirlwind", category: "Heavy" }), { levelKey: "Hard", enraged: true, at: 0 });
  assert.equal(s.attack.info.windUp, 0.768); // 0.883 / 1.15
});

test("guard: up from monster_defend until monster_guard, or after the fallback; a defended hit also shows it", () => {
  let s = reduceArena(initialArena(), ev("monster_defend"), ctx(100));
  assert.equal(isGuarding(s, 200), true);
  assert.equal(isGuarding(s, 100 + GUARD_FALLBACK_MS + 1), false);
  assert.equal(isGuarding(reduceArena(s, ev("monster_guard", { blockedHits: 0 }), ctx(900)), 1000), false);
  const hit = reduceArena(initialArena(), ev("hit", { defended: true }), ctx(500));
  assert.equal(isGuarding(hit, 600), true);
  assert.equal(isGuarding(reduceArena(initialArena(), ev("hit", { defended: false }), ctx(500)), 600), false);
});

test("clash flash shows for 700 ms", () => {
  const s = reduceArena(initialArena(), ev("clash", { swingSpeed: 6 }), ctx(1000));
  assert.equal(isClashing(s, 1500), true);
  assert.equal(isClashing(s, 1800), false);
  assert.equal(isClashing(initialArena(), 5), false);
});

test("kill-feed lines: icon, tone and plain text; noise returns null", () => {
  const k = (e, o) => killFeedItem(e, { monster: "The Gatekeeper", ...o });
  assert.deepEqual(k(ev("hit", { damage: 31.4, swingSpeed: 11.6, tier: "full" })), { icon: "bolt", tone: "accent", text: "ann hit The Gatekeeper for 31 dmg (8 m/s, full)" }); // 11.6 is clamped to the cap
  assert.match(k(ev("hit", { damage: 9, swingSpeed: 3, tier: "light", defended: true })).text, /hit the guard/);
  assert.equal(k(ev("monster_attack", { attack: "Slash", category: "Standard" })).text, "The Gatekeeper winds up Slash (Standard)");
  assert.equal(k(ev("monster_attack", { attack: "Earthsplitter Slam", category: "IdleSlam" })).text, "The Gatekeeper winds up Earthsplitter Slam (Slam)");
  assert.equal(k(ev("player_hurt", { amount: 12.34, attack: "Slash" })).text, "ann took 12.3 damage from Slash");
  assert.equal(k(ev("dodge", { attack: "Slash" })).tone, "good");
  assert.equal(k(ev("clash")).text, "Weapons clash");
  assert.equal(k(ev("monster_defend")).text, "The Gatekeeper raised its guard");
  assert.equal(k(ev("monster_guard", { blockedHits: 2 })).text, "The Gatekeeper's guard blocked 2 hits");
  assert.equal(k(ev("monster_guard", { blockedHits: 0 })), null);
  assert.equal(k(ev("session_complete", { outcome: "victory", score: 1012 })).text, "ann: Victory, score 1012");
  for (const type of ["hp", "ping", "weak_swing", "goal_progress", "pause"]) assert.equal(k(ev(type)), null, type);
});

test("result wording for the 8 s result moment", () => {
  assert.equal(resultLabel("victory").title, "Victory");
  assert.equal(resultLabel("defeat").tone, "bad");
  assert.equal(resultLabel("abandoned").title, "Abandoned");
  assert.equal(resultLabel("timeout").title, "Fight finished");
});

test("swing gauge bands follow each level's thresholds and end at the 8 m/s cap", () => {
  const easy = swingBands("Easy");
  assert.deepEqual(easy.map((b) => b.id), ["none", "tap", "light", "medium", "full"]);
  assert.deepEqual([easy[1].from, easy[2].from, easy[3].from, easy[4].from, easy[4].to], [1.2, 1.5, 3, 5, 8]);
  const hard = swingBands("Hard");
  const by = (bands, id) => bands.find((b) => b.id === id);
  assert.deepEqual(hard.map((b) => b.id), ["none", "light", "medium", "full"]); // Level 3 counts nothing under 1.6 m/s, above its 1.4 light mark: no tap band
  assert.deepEqual([by(hard, "none").to, by(hard, "light").from, by(hard, "medium").from, by(hard, "full").from], [1.6, 1.6, 2.8, 4.6]);
  for (const b of [easy, hard]) for (let i = 1; i < b.length; i++) assert.equal(b[i].from, b[i - 1].to, "bands are contiguous");
});

test("nextExpiry says when the arena next needs to redraw, and null when it never does", () => {
  assert.equal(nextExpiry(initialArena(), 0), null);
  let s = reduceArena(initialArena(), ev("monster_defend"), ctx(1000));
  assert.equal(nextExpiry(s, 1100), 1000 + GUARD_FALLBACK_MS);
  s = reduceArena(s, ev("clash"), ctx(1200));
  assert.equal(nextExpiry(s, 1300), 1900); // the clash flash ends first
  s = reduceArena(s, ev("monster_attack", { attack: "Slash" }), ctx(1400));
  assert.equal(nextExpiry(s, 1500), 1900);
  assert.equal(nextExpiry(s, 2000), 1400 + 900 + ATTACK_GRACE_MS); // then the attack strip
  assert.equal(nextExpiry(s, 1000 + GUARD_FALLBACK_MS + 5000), null); // everything has faded
});

test("impactOf: a landed full-power hit and a clash are impact moments; everything else is not", () => {
  assert.deepEqual(impactOf({ type: "hit", payload: { tier: "Full", damage: 41.6, swingSpeed: 6.1 } }), { kind: "full", damage: 42 });
  assert.deepEqual(impactOf({ type: "hit", payload: { tier: "full", damage: 30 } }), { kind: "full", damage: 30 });
  assert.deepEqual(impactOf({ type: "clash", payload: {} }), { kind: "clash" });
  assert.equal(impactOf({ type: "hit", payload: { tier: "medium", damage: 20 } }), null);
  assert.equal(impactOf({ type: "hit", payload: { tier: "full", damage: 30, defended: true } }), null, "the guard absorbed it");
  assert.equal(impactOf({ type: "hit", payload: { tier: "full" } }), null, "no damage reported: nothing to show");
  assert.equal(impactOf({ type: "hit", payload: { tier: "full", damage: 0 } }), null);
  assert.equal(impactOf({ type: "monster_attack", payload: {} }), null);
  assert.equal(impactOf(null), null);
});
