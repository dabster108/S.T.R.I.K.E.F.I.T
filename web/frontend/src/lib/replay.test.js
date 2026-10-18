import test from "node:test";
import assert from "node:assert/strict";
import { buildReplay, neighbourTime, readoutAt, replaySummary, ATTACK_WINDOW_SEC } from "./replay.js";

const ev = (type, t, payload = {}) => ({ type, t, payload: { t, ...payload } });
const events = [
  ev("session_start", 0),
  ev("monster_attack", 2, { attack: "Overhead", category: "Standard" }),
  ev("hit", 3, { swingSpeed: 4.2, tier: "medium", damage: 20 }),
  ev("player_hurt", 4, { amount: 12.5, attack: "Overhead" }),
  ev("hit", 6, { swingSpeed: 30, tier: "FULL", damage: 40 }),
  ev("clash", 7, { swingSpeed: 5 }),
  ev("player_hurt", 9, { amount: 0, blocked: true }),
  ev("player_hurt", 10, { amount: 7.5, attack: "Sweep" }),
  ev("hp", 10, { playerHp: 5 }),
];

test("events are split into the four lanes; zero-damage hurts and other types are skipped", () => {
  const m = buildReplay(events, 12);
  assert.equal(m.swings.length, 2);
  assert.equal(m.attacks.length, 1);
  assert.equal(m.clashes.length, 1);
  assert.equal(m.hurts.length, 2);
  assert.equal(m.duration, 12);
  assert.equal(m.maxHurt, 12.5);
  assert.equal(m.isEmpty, false);
});

test("swing speed is clamped to the 8 m/s cap and tiers are lower-cased", () => {
  const m = buildReplay(events, 12);
  assert.equal(m.swings[1].speed, 8);
  assert.equal(m.swings[1].rawSpeed, 30);
  assert.equal(m.swings[1].tier, "full");
});

test("duration is never shorter than the last event", () => {
  assert.equal(buildReplay(events, 3).duration, 10);
});

test("readout at a moment: last swing, damage taken so far, the attack in progress", () => {
  const m = buildReplay(events, 12);
  const a = readoutAt(m, 4.5);
  assert.equal(a.swing.speed, 4.2);
  assert.equal(a.swingsSoFar, 1);
  assert.equal(a.damageTaken, 12.5);
  assert.equal(a.attack.attack, "Overhead"); // started at 2, 2.5 s ago: still inside the 3 s window
});

test("an attack is only 'current' for a few seconds after it starts", () => {
  const m = buildReplay(events, 12);
  assert.equal(readoutAt(m, 2 + ATTACK_WINDOW_SEC - 0.1).attack.attack, "Overhead");
  assert.equal(readoutAt(m, 2 + ATTACK_WINDOW_SEC + 0.5).attack, null);
  assert.equal(readoutAt(m, 2 + ATTACK_WINDOW_SEC + 0.5).lastAttack.attack, "Overhead");
  assert.equal(readoutAt(m, 1).attack, null);
});

test("before the first event the readout is empty, not zero-filled", () => {
  const r = readoutAt(buildReplay(events, 12), 0.5);
  assert.equal(r.swing, null);
  assert.equal(r.swingAgeSec, null);
  assert.equal(r.lastHurt, null);
  assert.equal(r.damageTaken, 0);
});

test("neighbourTime walks every lane in order", () => {
  const m = buildReplay(events, 12);
  assert.equal(neighbourTime(m, 0, 1), 2);
  assert.equal(neighbourTime(m, 2, 1), 3);
  assert.equal(neighbourTime(m, 10, 1), null);
  assert.equal(neighbourTime(m, 10, -1), 7);
  assert.equal(neighbourTime(m, 2, -1), null);
});

test("an empty fight is reported as empty and described in words", () => {
  const m = buildReplay([ev("session_start", 0), ev("hp", 1, { playerHp: 1 })], 5);
  assert.equal(m.isEmpty, true);
  assert.match(replaySummary(m), /No swings/);
  assert.match(replaySummary(buildReplay(events, 12)), /2 landed swings, 1 monster attack, 1 weapon clash, 2 times the player took damage/);
  assert.equal(buildReplay(null).isEmpty, true);
});
