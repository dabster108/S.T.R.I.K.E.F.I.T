// Fails when a shipped copy of the story differs from content/story.json. Run: node --test content/story.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const sync = require("./sync-story.js");

test("content/story.json is valid", () => {
  assert.deepEqual(sync.validate(JSON.parse(fs.readFileSync(sync.SOURCE, "utf8"))), []);
});

test("the game and website copies are byte-identical to content/story.json", () => {
  assert.deepEqual(sync.check(fs.readFileSync(sync.SOURCE, "utf8")), [], "run: node content/sync-story.js");
});

test("new fields are present: 3 guardians whose names match levels[], interludes, plain ASCII", () => {
  const story = JSON.parse(fs.readFileSync(sync.SOURCE, "utf8"));
  assert.equal(story.guardians.length, 3);
  assert.deepEqual(story.guardians.map((g) => g.level), [1, 2, 3]);
  assert.deepEqual(story.guardians.map((g) => g.name), story.levels.map((l) => l.guardian));
  assert.ok(story.interludes.ending.length >= 1);
  assert.equal(/[^\x00-\x7e]/.test(JSON.stringify(story)), false, "no curly quotes, dashes or accents (the Quest font has no glyph for them)");
});

test("the validator rejects a bad guardian, a missing interlude and a non-ASCII quote", () => {
  const good = JSON.parse(fs.readFileSync(sync.SOURCE, "utf8"));
  const noTips = JSON.parse(JSON.stringify(good)); noTips.guardians[0].tips = [];
  assert.ok(sync.validate(noTips).some((e) => e.includes("tips")));
  const noDefeat = JSON.parse(JSON.stringify(good)); delete noDefeat.interludes.defeat;
  assert.ok(sync.validate(noDefeat).some((e) => e.includes("defeat")));
  const curly = JSON.parse(JSON.stringify(good)); curly.guardians[1].lore = "He watches \u2019you\u2019.";
  assert.ok(sync.validate(curly).some((e) => e.includes("non-ASCII")));
  const renamed = JSON.parse(JSON.stringify(good)); renamed.guardians[2].name = "Someone Else";
  assert.ok(sync.validate(renamed).some((e) => e.includes("differs")));
});

test("existing fields are untouched by the additions (readers that ignore unknown fields see the same story)", () => {
  const story = JSON.parse(fs.readFileSync(sync.SOURCE, "utf8"));
  assert.equal(story.slides.length, 3);
  assert.equal(story.slides[0].id, "guardians");
  assert.equal(story.levels[1].guardian, "The Crimson Warden");
  assert.equal(story.slideSeconds, 7);
});

test("guardian portraits: three PNGs, 1024 px at most, under 6 MB together, and the game copies are byte-identical", () => {
  assert.deepEqual(sync.validatePortraits(), []);
  const total = sync.PORTRAITS.reduce((a, p) => a + fs.statSync(p.from).size, 0);
  assert.ok(total < 6 * 1024 * 1024, `portraits ${total} bytes`);
  for (const p of sync.PORTRAITS) assert.ok(fs.readFileSync(p.from).equals(fs.readFileSync(p.to)), p.to);
});
