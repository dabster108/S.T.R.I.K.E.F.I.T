import test from "node:test";
import assert from "node:assert/strict";
import { BOTH, VIEW_KEY, arenaLink, effectiveChoice, pickStage, playersOnStage, readChoice, writeChoice } from "./arenaView.js";

const A1 = { id: "s1", playerName: "ZZTEST-ann", level: "Easy", live: true };
const B1 = { id: "s2", playerName: "ZZTEST-bo", level: "Hard", live: true };
const A2 = { id: "s3", playerName: "ZZTEST-ann", level: "Medium", live: false };
const store = (init = {}) => {
  const m = { ...init };
  return { getItem: (k) => (k in m ? m[k] : null), setItem: (k, v) => void (m[k] = v) };
};

test("playersOnStage keeps one entry per player and prefers the live fight", () => {
  assert.deepEqual(playersOnStage([A2, B1, A1]).map((e) => e.id), ["s1", "s2"]);
  assert.deepEqual(playersOnStage([A1, A2]).map((e) => e.id), ["s1"]);
});

test("pickStage: both shows everyone, a name shows that player, an absent name falls back to everyone", () => {
  assert.deepEqual(pickStage([A1, B1], BOTH), ["s1", "s2"]);
  assert.deepEqual(pickStage([A1, B1], undefined), ["s1", "s2"]);
  assert.deepEqual(pickStage([A1, B1], "ZZTEST-bo"), ["s2"]);
  assert.deepEqual(pickStage([A1, A2, B1], "ZZTEST-ann"), ["s1", "s3"]);
  assert.deepEqual(pickStage([A1, B1], "ZZTEST-gone"), ["s1", "s2"]);
});

test("effectiveChoice only keeps a name that is on stage", () => {
  assert.equal(effectiveChoice([A1, B1], "ZZTEST-bo"), "ZZTEST-bo");
  assert.equal(effectiveChoice([A1], "ZZTEST-bo"), BOTH);
  assert.equal(effectiveChoice([A1], BOTH), BOTH);
});

test("the choice is remembered; a ?player= link wins over the stored one; a blocked store means both", () => {
  const s = store();
  assert.equal(readChoice(s, ""), BOTH);
  writeChoice(s, "ZZTEST-bo");
  assert.equal(s.getItem(VIEW_KEY), "ZZTEST-bo");
  assert.equal(readChoice(s, ""), "ZZTEST-bo");
  assert.equal(readChoice(s, "?player=ZZTEST-ann"), "ZZTEST-ann");
  assert.equal(readChoice({ getItem() { throw new Error("blocked"); } }, ""), BOTH);
  assert.doesNotThrow(() => writeChoice({ setItem() { throw new Error("blocked"); } }, "x"));
});

test("arenaLink adds the player only when one is chosen, and encodes it", () => {
  assert.equal(arenaLink("https://x.test", BOTH), "https://x.test/dashboard/live");
  assert.equal(arenaLink("https://x.test", "ZZTEST ann"), "https://x.test/dashboard/live?player=ZZTEST+ann");
});
