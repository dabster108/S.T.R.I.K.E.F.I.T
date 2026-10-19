import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { postBody, postMissing, preMissing, stepFor } from "./study.js";
import * as content from "../content/studyContent.js";

const backend = createRequire(import.meta.url)("../../../backend/studyCore.js");

test("the page and the server agree on the consent version and every option value", () => {
  assert.equal(content.CONSENT_VERSION, backend.CONSENT_VERSION);
  assert.deepEqual(content.AGE_BANDS.map((o) => o[0]), backend.AGE_BANDS);
  assert.deepEqual(content.VR_EXPERIENCE.map((o) => o[0]), backend.VR_EXPERIENCE);
  assert.deepEqual(content.WEEKLY_EXERCISE.map((o) => o[0]), backend.WEEKLY_EXERCISE);
  assert.deepEqual(content.PLAY_AGAIN.map((o) => o[0]), backend.PLAY_AGAIN);
  assert.equal(content.SUS_ITEMS.length, backend.SUS_ITEMS);
  assert.equal(content.ENJOYMENT_ITEMS.length, backend.ENJOYMENT_ITEMS);
});

test("the Borg scale runs 6 to 20 without gaps", () => {
  assert.deepEqual(content.BORG.map((b) => b[0]), Array.from({ length: 15 }, (_, i) => i + 6));
});

test("SUS statements alternate positive and negative, as the scoring assumes", () => {
  const negative = [1, 3, 5, 7, 9]; // 0-based indexes of the even-numbered items: 2, 4, 6, 8, 10
  const text = content.SUS_ITEMS;
  assert.match(text[1], /unnecessarily complex/);
  assert.match(text[3], /support of a technical person/);
  assert.match(text[5], /inconsistency/);
  assert.match(text[7], /cumbersome/);
  assert.match(text[9], /learn a lot of things/);
  assert.ok(negative.every((i) => !/easy|confident|quickly|frequently|well integrated/.test(text[i])));
});

test("stepFor follows the participant through the study", () => {
  assert.equal(stepFor(null), "consent");
  assert.equal(stepFor({ preDone: false, postDone: false, fights: { finished: 0 } }), "pre");
  assert.equal(stepFor({ preDone: true, postDone: false, fights: { finished: 0 } }), "play");
  assert.equal(stepFor({ preDone: true, postDone: false, fights: { finished: 1 } }), "post");
  assert.equal(stepFor({ preDone: true, postDone: true, fights: { finished: 3 } }), "done");
});

test("the forms say exactly what is still missing", () => {
  assert.deepEqual(preMissing({}), ["age band", "VR experience", "weekly exercise"]);
  assert.deepEqual(preMissing({ ageBand: "25-34", vrExperience: "never", weeklyExercise: "none" }), []);
  const full = { rpe: "13", enjoyment: ["4", "4", "4", "4", "4"], effortVsUsual: "3", playAgain: "yes", sus: Array(10).fill("3") };
  assert.deepEqual(postMissing(full), []);
  assert.deepEqual(postMissing({ ...full, rpe: "" }), ["how hard it felt (Borg scale)"]);
  assert.deepEqual(postMissing({ ...full, enjoyment: ["4", "", "4"], sus: ["3"] }), ["3 enjoyment statements", "9 usability statements"]);
  assert.deepEqual(postMissing({}).length, 5);
});

test("postBody sends numbers", () => {
  const body = postBody({ enjoyment: ["4", "3", "5", "2", "1"], effortVsUsual: "4", playAgain: "maybe", sus: Array(10).fill("3") });
  assert.deepEqual(body.enjoyment, [4, 3, 5, 2, 1]);
  assert.equal(body.effortVsUsual, 4);
  assert.equal(body.sus.length, 10);
  assert.ok(body.sus.every((n) => n === 3));
});
