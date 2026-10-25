import test from "node:test";
import assert from "node:assert/strict";
import { _reset, autoplayAllowed, claim, otherPlaying, release, saveDataOn, shouldPlay } from "./videoCoordinator.js";

const clip = () => ({ paused: true, play() { this.paused = false; }, pause() { this.paused = true; } });

test("claiming a clip pauses the one that was playing", () => {
  _reset();
  const a = clip(), b = clip();
  claim(a); a.play();
  claim(b); b.play();
  assert.equal(a.paused, true);
  assert.equal(b.paused, false);
  assert.equal(otherPlaying(a), true);
  assert.equal(otherPlaying(b), false);
});

test("release forgets the owner; a paused owner does not count as playing", () => {
  _reset();
  const a = clip(), b = clip();
  claim(a); a.play(); a.pause();
  assert.equal(otherPlaying(b), false);
  release(a);
  claim(b);
  assert.equal(otherPlaying(a), false);
});

test("autoplay: off under reduced motion / Calm mode and Data Saver", () => {
  assert.equal(autoplayAllowed({ reduced: false, saveData: false }), true);
  assert.equal(autoplayAllowed({ reduced: true, saveData: false }), false);
  assert.equal(autoplayAllowed({ reduced: false, saveData: true }), false);
  assert.equal(saveDataOn({ connection: { saveData: true } }), true);
  assert.equal(saveDataOn({ connection: {} }), false);
  assert.equal(saveDataOn({}), false);
  assert.equal(saveDataOn(undefined), false);
});

test("shouldPlay: every active clip autoplays wherever it is on the page; the visitor's choice wins", () => {
  const base = { active: true, choice: null, reduced: false, saveData: false };
  assert.equal(shouldPlay(base), true);
  assert.equal(shouldPlay({ ...base, active: false }), false);
  assert.equal(shouldPlay({ ...base, reduced: true }), false);
  assert.equal(shouldPlay({ ...base, saveData: true }), false);
  assert.equal(shouldPlay({ ...base, reduced: true, choice: "play" }), true);
  assert.equal(shouldPlay({ ...base, choice: "pause" }), false);
  assert.equal(shouldPlay({ ...base, active: false, choice: "play" }), false);
});
