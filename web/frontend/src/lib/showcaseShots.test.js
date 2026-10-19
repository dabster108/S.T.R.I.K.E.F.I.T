import test from "node:test";
import assert from "node:assert/strict";
import { SHOT_TYPES, SHOWCASE_SHOTS, shotType } from "../content/showcaseShots.js";

test("every shot has a valid type, an alt text and a tab/title/caption", () => {
  assert.ok(SHOWCASE_SHOTS.length > 0);
  for (const s of SHOWCASE_SHOTS) {
    assert.ok(SHOT_TYPES.includes(shotType(s)), `type of "${s.title}"`);
    for (const k of ["alt", "tab", "title", "caption"]) assert.ok(typeof s[k] === "string" && s[k].length > 0, `${k} of "${s.title}"`);
    assert.ok(s.path === null || typeof s.path === "string");
  }
});

test("shotType defaults to image and only video is video", () => {
  assert.equal(shotType({}), "image");
  assert.equal(shotType({ type: "gif" }), "image");
  assert.equal(shotType({ type: "video" }), "video");
});
