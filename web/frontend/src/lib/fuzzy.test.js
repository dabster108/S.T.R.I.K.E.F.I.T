import test from "node:test";
import assert from "node:assert/strict";
import { fuzzyFilter, score } from "./fuzzy.js";

test("prefix beats word start beats substring beats subsequence", () => {
  const p = score("Leaderboard", "lead");
  const w = score("Live Arena", "arena");
  const s = score("Dashboard overview", "boa");
  const f = score("Leaderboard", "ldb");
  assert.ok(p < w && w < s && s < f, [p, w, s, f].join(" "));
});

test("no match returns null; every word of a multi-word query must match", () => {
  assert.equal(score("Sessions", "zzz"), null);
  assert.notEqual(score("Export Thesis Lab CSV", "lab csv"), null);
  assert.equal(score("Export Thesis Lab CSV", "lab pdf"), null);
});

test("fuzzyFilter ranks and keeps order of ties; empty query returns everything", () => {
  const items = [{ label: "Players" }, { label: "Live Arena" }, { label: "Leaderboard" }, { label: "Analytics" }];
  assert.deepEqual(fuzzyFilter(items, "").length, 4);
  assert.deepEqual(fuzzyFilter(items, "l").map((x) => x.label), ["Live Arena", "Leaderboard", "Players", "Analytics"]);
  assert.deepEqual(fuzzyFilter(items, "ldb").map((x) => x.label), ["Leaderboard"]);
});

test("a subsequence needs 3+ characters and letters that sit close together", () => {
  assert.ok(score("Leaderboard", "ldb") > 2 && score("Leaderboard", "ldb") < 3);
  assert.equal(score("Leaderboard", "ld"), null); // too short to be a subsequence
  assert.equal(score("Leaderboard", "lrd"), null); // l..r..d are not near each other
  assert.equal(score("a b c d e f g", "adg"), null); // spread across the whole text
  assert.equal(score("Show keyboard shortcuts", "hard"), null); // h, a, r, d exist, but scattered
  assert.equal(score("Take the dashboard tour", "hard"), null);
});
