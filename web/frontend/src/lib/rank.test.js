import test from "node:test";
import assert from "node:assert/strict";
import { rankSentence } from "./rank.js";

const info = (over = {}) => ({ rank: { key: "brawler", name: "Brawler" }, effortPoints: 1043, nextRank: { key: "warrior", name: "Warrior", from: 1200 }, pointsToNext: 157, progress: 0.8256, ...over });

test("the rank sentence says the rank, the points and how far it is to the next rank", () => {
  assert.equal(rankSentence(info()), "Brawler, 1,043 effort points, 83% of the way to Warrior (157 points to go)");
});

test("the top rank has nothing next", () => {
  assert.equal(rankSentence(info({ rank: { key: "guardian", name: "Guardian" }, effortPoints: 4200, nextRank: null, pointsToNext: null, progress: 1 })), "Guardian, 4,200 effort points, the top rank");
});

test("no answer, no sentence", () => {
  assert.equal(rankSentence(null), "");
});
