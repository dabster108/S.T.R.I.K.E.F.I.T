import test from "node:test";
import assert from "node:assert/strict";
import { DEMO_CLIP, SHOT_TYPES, SHOWCASE_SHOTS, shotType } from "../content/showcaseShots.js";

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

test("the demo clip: mp4 + webm + poster under video/, labelled as scripted Editor footage, used by one video shot", () => {
  assert.match(DEMO_CLIP.mp4, /^video\/.+\.mp4$/);
  assert.match(DEMO_CLIP.webm, /^video\/.+\.webm$/);
  assert.match(DEMO_CLIP.poster, /^video\/.+\.jpg$/);
  assert.match(DEMO_CLIP.note, /Scripted demo footage recorded in the Unity Editor \(not a live player\)/);
  assert.ok(["Easy", "Medium", "Hard"].includes(DEMO_CLIP.level));
  const uses = SHOWCASE_SHOTS.filter((s) => s.path === DEMO_CLIP.mp4);
  assert.equal(uses.length, 1);
  assert.equal(shotType(uses[0]), "video");
  assert.equal(uses[0].credit, "demo");
  assert.ok(uses[0].seconds > 0);
});
