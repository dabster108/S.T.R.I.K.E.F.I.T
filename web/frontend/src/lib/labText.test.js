import test from "node:test";
import assert from "node:assert/strict";
import { correlationWord, levelsText, listText, returnRateText, rpeText } from "./labText.js";

const rr = (rows) => ({ windows: rows.map(([days, n, returned]) => ({ days, n, returned })) });

test("return rate in words, with the small-sample caveat under 10 players", () => {
  assert.equal(returnRateText(rr([[1, 7, 6], [3, 7, 6], [7, 7, 6]])), "6 of 7 players came back within a day, and nobody else returned later in the week. With 7 players this describes this group only, not players in general.");
  assert.equal(returnRateText(rr([[1, 12, 3], [3, 12, 5], [7, 10, 6]])), "3 of 12 players came back within a day, 6 of 10 within a week.");
  assert.match(returnRateText(rr([[1, 0, 0], [3, 0, 0], [7, 0, 0]])), /nothing to say/);
  assert.match(returnRateText(null), /nothing to say/);
});

test("correlation strength words", () => {
  assert.deepEqual([0.05, -0.2, 0.4, -0.7].map(correlationWord), ["no clear", "a weak", "a moderate", "a strong"]);
});

test("RPE sentence: direction, strength, and always 'not a cause'", () => {
  assert.equal(rpeText({ status: "ok", r: 0.58, n: 12 }), "There is a strong link (r = 0.58): fights with faster swings tended to feel harder. 12 rated fights is a small sample, and a link is not a cause.");
  assert.match(rpeText({ status: "ok", r: -0.35, n: 40 }), /moderate link .* feel easier\. A link is not a cause\./);
  assert.match(rpeText({ status: "ok", r: 0.02, n: 40 }), /^There is no clear link/);
  assert.equal(rpeText({ status: "insufficient", n: 1, minPairs: 8 }), "Not enough rated fights to say anything yet (1 of the 8 needed).");
});

test("levels sentence: first vs last level, fastest swings, rough rates flagged", () => {
  const levels = [
    { label: "Level 1", decided: 12, winRate: 83.3, avgSwingSpeed: 5.23 },
    { label: "Level 2", decided: 16, winRate: 68.8, avgSwingSpeed: 3.03 },
    { label: "Level 3", decided: 8, winRate: 37.5, avgSwingSpeed: 6.4 },
  ];
  assert.equal(levelsText(levels), "Players won 83% of decided fights on Level 1 and 38% on Level 3, so the later level is harder; swings were fastest on Level 3 (6.4 m/s on average). Level 3 has fewer than 10 decided fights, so treat its rate as rough.");
  assert.match(levelsText([{ label: "Level 1", decided: 0, winRate: null }]), /No level has/);
  assert.match(levelsText([{ label: "Level 1", decided: 20, winRate: 50, avgSwingSpeed: null }]), /^Players won 50% of decided fights on Level 1\.$/);
});

test("lists read as English", () => {
  assert.equal(listText(["Level 1"]), "Level 1");
  assert.equal(listText(["Level 1", "Level 2"]), "Level 1 and Level 2");
  assert.equal(listText(["Level 1", "Level 2", "Level 3"]), "Level 1, Level 2 and Level 3");
  assert.equal(listText([]), "");
});

import { enjoymentText, fadeText, playAgainText, susText } from "./labText.js";

const band = { label: "Good", note: "At or above the usual benchmark of 68." };

test("susText names the mean, the band, how many are under 68, and the small sample", () => {
  const t = susText({ n: 4, mean: 73.75, band, scores: [{ score: 80 }, { score: 60 }, { score: 90 }, { score: 65 }] });
  assert.match(t, /73\.8 out of 100/);
  assert.match(t, /“good”/);
  assert.match(t, /2 of 4 scored under the usual benchmark of 68/);
  assert.match(t, /describes this group only/);
  assert.match(susText({ n: 0 }), /no SUS score/);
  assert.doesNotMatch(susText({ n: 12, mean: 70, band, scores: Array(12).fill({ score: 70 }) }), /describes this group only/);
});

test("enjoymentText gives the mean and warns when the five statements differ", () => {
  assert.match(enjoymentText({ n: 3, mean: 4.2, items: [4.2, 4.2, 4.2, 4.2, 4.2] }), /4\.2 out of 5/);
  assert.doesNotMatch(enjoymentText({ n: 3, mean: 4, items: [4, 4, 4, 4, 4] }), /differ/);
  assert.match(enjoymentText({ n: 3, mean: 3, items: [1.5, 3, 3, 3, 4.5] }), /differ noticeably/);
  assert.match(enjoymentText({ n: 0 }), /No enjoyment/);
});

test("playAgainText counts yes, maybe and no, and says intent is not behaviour", () => {
  const t = playAgainText({ n: 10, yes: 6, maybe: 3, no: 1 });
  assert.match(t, /6 of 10 said they would play again/);
  assert.match(t, /3 said maybe/);
  assert.match(t, /1 said no/);
  assert.match(t, /intent, not what people actually do/);
  assert.match(playAgainText({ n: 2, yes: 2, maybe: 0, no: 0 }), /^2 of 2 said they would play again this week\./);
});

test("fadeText counts the slowed and the stronger participants", () => {
  const rows = [{ fadePct: -25 }, { fadePct: -12 }, { fadePct: 3 }, { fadePct: 15 }, { fadePct: null }];
  const t = fadeText(rows);
  assert.match(t, /2 of 4 participants swung at least 10% slower/);
  assert.match(t, /1 swung at least 10% faster/);
  assert.match(fadeText([]), /No participant has a fight/);
});
