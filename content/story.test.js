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
