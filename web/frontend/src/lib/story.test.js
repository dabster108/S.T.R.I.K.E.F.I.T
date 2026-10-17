// The story helpers read the shared JSON. Run: npm test (node --test src/lib/*.test.js)
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { makeStory } from "./storyData.js";

const raw = JSON.parse(fs.readFileSync(new URL("../content/story.json", import.meta.url), "utf8"));
const { guardians, interludes, guardianForLevel, guardianForKey, guardianById, levelFor } = makeStory(raw);

test("three guardians, in level order, each with lore, weapon and at least one tip", () => {
  assert.deepEqual(guardians.map((g) => g.level), [1, 2, 3]);
  for (const g of guardians) {
    assert.ok(g.name && g.weapon && g.lore, g.id);
    assert.ok(g.tips.length >= 1);
  }
});

test("lookups agree: by level, by backend key and by id", () => {
  assert.equal(guardianForLevel(2).name, "The Crimson Warden");
  assert.equal(guardianForKey("Hard").id, "tyrant");
  assert.equal(guardianById("gatekeeper").level, 1);
  assert.equal(guardianForLevel(9), null);
  assert.equal(levelFor(1).guardian, guardianForLevel(1).name);
});

test("interludes have text between the fights, an ending and a defeat line", () => {
  assert.ok(interludes.afterLevel1 && interludes.afterLevel2 && interludes.defeat);
  assert.ok(interludes.ending.length >= 1);
});

test("all shared text is plain ASCII (the headset font has no glyph for curly quotes)", () => {
  const text = JSON.stringify({ guardians, interludes });
  assert.equal(/[^\x00-\x7e]/.test(text), false);
});

test("a file without the newer fields still gives empty, safe values", () => {
  const old = makeStory({ slides: [{ id: "a", text: "x" }], levels: [{ level: 1, key: "Easy", guardian: "G" }] });
  assert.deepEqual(old.guardians, []);
  assert.equal(old.guardianForLevel(1), null);
  assert.equal(old.interludes.defeat, "");
  assert.deepEqual(old.interludes.ending, []);
});
