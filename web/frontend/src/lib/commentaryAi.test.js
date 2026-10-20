import test from "node:test";
import assert from "node:assert/strict";
import { COMMENTARY } from "../config/commentary.js";
import { aiStatus, askCommentator, askSummary, createBudget, familyOf, fetchVoice, lineRequest, resetAiStatus, worthAsking } from "./commentaryAi.js";
import { createVoice } from "./commentaryVoice.js";
import { createState, step } from "./commentary.js";

const LINE = { situation: "combo5", text: "Combo x5!", facts: { base: "Combo x5!", monster: "The Crimson Warden", player: "Ava", level: "Hard", monsterPct: 41, playerPct: 80, combo: 5, phase: 2 } };

test("lines made by step carry the facts the commentator is given", () => {
  let s = createState({ levelKey: "Hard", playerName: "Ava", monsterName: "The Harbor Tyrant", initial: { monsterHp: 700, monsterMaxHp: 1000, playerHp: 90, playerMaxHp: 100 } });
  const out = step({ type: "hit", payload: { combo: 5, monsterHp: 640, monsterMaxHp: 1000, playerHp: 90, playerMaxHp: 100 }, at: 0 }, s);
  const line = out.lines.find((l) => l.situation === "combo5");
  assert.ok(line);
  assert.equal(line.facts.base, line.text);
  assert.equal(line.facts.monsterPct, 64);
  assert.equal(line.facts.playerPct, 90);
  assert.equal(line.facts.combo, 5);
  assert.equal(line.facts.player, "Ava");
  assert.equal(line.facts.monster, "The Harbor Tyrant");
});

test("lineRequest sends the event, the picture of the fight and the recent lines", () => {
  const r = lineRequest(LINE, ["one", "two"]);
  assert.equal(r.situation, "combo5");
  assert.equal(r.base, "Combo x5!");
  assert.equal(r.monsterPct, 41);
  assert.deepEqual(r.recent, ["one", "two"]);
});

test("askCommentator returns the sentence, or null for anything else", async () => {
  assert.equal(await askCommentator(LINE, [], { send: async () => ({ ok: true, text: "  What a move!  " }) }), "What a move!");
  assert.equal(await askCommentator(LINE, [], { send: async () => ({ ok: false }) }), null);
  assert.equal(await askCommentator(LINE, [], { send: async () => ({ ok: true, text: "  " }) }), null);
  assert.equal(await askCommentator(LINE, [], { send: async () => { throw new Error("offline"); } }), null);
});

test("aiStatus asks once when the answer came, and retries a failure only after a while", async () => {
  resetAiStatus();
  let t = 0;
  let calls = 0;
  const ok = async () => (calls++, { ai: true, voice: false });
  assert.deepEqual(await aiStatus({ now: () => t, ask: ok }), { ai: true, voice: false });
  assert.deepEqual(await aiStatus({ now: () => t, ask: ok }), { ai: true, voice: false });
  assert.equal(calls, 1);
  resetAiStatus();
  const bad = async () => (calls++, Promise.reject(new Error("down")));
  assert.deepEqual(await aiStatus({ now: () => t, ask: bad }), { ai: false, voice: false });
  assert.equal(calls, 2);
  await aiStatus({ now: () => t + 1000, ask: bad });
  assert.equal(calls, 2); // too soon
  t += COMMENTARY.ai.statusRetryMs + 1;
  assert.deepEqual(await aiStatus({ now: () => t, ask: ok }), { ai: true, voice: false });
  assert.equal(calls, 3);
  resetAiStatus();
});

test("fetchVoice returns an object URL for audio and null for failures", async () => {
  const audio = { ok: true, blob: async () => ({ size: 12 }) };
  assert.equal(await fetchVoice("hi there", { base: "", fetchImpl: async () => audio, makeUrl: () => "blob:x" }), "blob:x");
  assert.equal(await fetchVoice("hi there", { base: "", fetchImpl: async () => ({ ok: false }) }), null);
  assert.equal(await fetchVoice("hi there", { base: "", fetchImpl: async () => ({ ok: true, blob: async () => ({ size: 0 }) }) }), null);
  assert.equal(await fetchVoice("hi there", { base: "", fetchImpl: async () => { throw new Error("offline"); } }), null);
});

// --- the voice -------------------------------------------------------------------------------------------------------
function rig({ playFails = false } = {}) {
  const log = [];
  class FakeAudio {
    constructor(url) { this.url = url; FakeAudio.all.push(this); }
    async play() { if (playFails) throw new Error("blocked"); log.push(`play ${this.url}`); }
    pause() { log.push(`pause ${this.url}`); }
  }
  FakeAudio.all = [];
  const synth = { cancel: () => log.push("synth cancel"), speak: (u) => log.push(`synth ${u.text}`) };
  class Utt { constructor(text) { this.text = text; } }
  let t = 0;
  const voice = createVoice({ AudioCtor: FakeAudio, synth, Utterance: Utt, revoke: (u) => log.push(`revoke ${u}`), now: () => t });
  return { voice, log, FakeAudio, tick: (ms) => (t += ms) };
}
const flush = () => new Promise((r) => setTimeout(r, 0));

test("voice: plays the Google audio, and revokes it when done", async () => {
  const { voice, log, FakeAudio } = rig();
  voice.say("What a goal!", Promise.resolve("blob:1"));
  await flush();
  assert.deepEqual(log.filter((l) => l.startsWith("play")), ["play blob:1"]);
  assert.equal(voice.playing(), true);
  FakeAudio.all[0].onended();
  assert.equal(voice.playing(), false);
  assert.ok(log.includes("revoke blob:1"));
});

test("voice: no audio, or a browser that blocks it, falls back to the browser voice with the same sentence", async () => {
  const a = rig();
  a.voice.say("What a goal!", Promise.resolve(null));
  await flush();
  assert.ok(a.log.includes("synth What a goal!"));
  const b = rig({ playFails: true });
  b.voice.say("What a goal!", Promise.resolve("blob:2"));
  await flush();
  assert.ok(b.log.includes("synth What a goal!"));
  assert.ok(b.log.includes("revoke blob:2"));
});

test("voice: a sentence finishes before the next starts, only the newest waits, and the result interrupts", async () => {
  const { voice, log, FakeAudio } = rig();
  voice.say("one", Promise.resolve("blob:1"));
  await flush();
  voice.say("two", Promise.resolve("blob:2"));
  voice.say("three", Promise.resolve("blob:3"));
  await flush();
  assert.deepEqual(log.filter((l) => l.startsWith("play")), ["play blob:1"]); // still the first
  FakeAudio.all[0].onended();
  await flush();
  assert.deepEqual(log.filter((l) => l.startsWith("play")), ["play blob:1", "play blob:3"]); // "two" was replaced
  assert.ok(log.includes("revoke blob:2"));
  voice.say("It is over!", Promise.resolve("blob:4"), { interrupt: true });
  await flush();
  assert.ok(log.includes("pause blob:3"));
  assert.equal(log.filter((l) => l.startsWith("play")).pop(), "play blob:4");
});

test("voice: a waiting sentence that waited too long is skipped", async () => {
  const { voice, log, FakeAudio, tick } = rig();
  voice.say("one", Promise.resolve("blob:1"));
  await flush();
  voice.say("two", Promise.resolve("blob:2"));
  tick(COMMENTARY.ai.voiceStaleMs + 1);
  FakeAudio.all[0].onended();
  await flush();
  assert.deepEqual(log.filter((l) => l.startsWith("play")), ["play blob:1"]);
  assert.ok(log.includes("revoke blob:2"));
});

test("voice: stop silences everything, including a sentence still being fetched", async () => {
  const { voice, log } = rig();
  let resolve;
  voice.say("late", new Promise((r) => (resolve = r)));
  voice.stop();
  resolve("blob:9");
  await flush();
  assert.equal(log.some((l) => l.startsWith("play")), false);
  assert.ok(log.includes("revoke blob:9"));
});

test("the AI is asked about colourful moments only, inside a per-minute allowance; big moments are always asked", () => {
  let t = 0;
  const budget = createBudget({ perMinute: 2, now: () => t });
  const rest = (situation, priority = COMMENTARY.priority.windUp) => ({ situation, priority });
  assert.equal(familyOf("combo10"), "combo");
  assert.equal(worthAsking(rest("idle", COMMENTARY.priority.rest), budget), false); // advice stays as written
  assert.equal(worthAsking(rest("guardUp", COMMENTARY.priority.rest), budget), false);
  assert.equal(worthAsking(rest("windUp"), budget), true);
  assert.equal(worthAsking(rest("combo5", COMMENTARY.priority.combo), budget), true);
  assert.equal(worthAsking(rest("windUp"), budget), false); // allowance used up
  assert.equal(worthAsking(rest("victory", COMMENTARY.priority.result), budget), true); // ... but not for the big moments
  assert.equal(worthAsking(rest("playerLow", COMMENTARY.priority.playerLow), budget), true);
  t += 61000;
  assert.equal(worthAsking(rest("windUp"), budget), true); // a minute later it is fresh
});

test("every situation named for the AI exists", async () => {
  const { SITUATIONS } = await import("../config/commentary.js");
  for (const s of COMMENTARY.ai.situations) assert.ok(SITUATIONS[s], s);
});

test("askSummary returns the report, or null for anything else", async () => {
  assert.equal(await askSummary({ base: "Full time: Ava won." }, { send: async () => ({ ok: true, text: " Full time! " }) }), "Full time!");
  assert.equal(await askSummary({}, { send: async () => ({ ok: false }) }), null);
  assert.equal(await askSummary({}, { send: async () => { throw new Error("offline"); } }), null);
});
