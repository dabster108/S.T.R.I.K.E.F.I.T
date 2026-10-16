// Engagement helper rules. Run: npm test (node --test src/lib/*.test.js)
import test from "node:test";
import assert from "node:assert/strict";
import { beatTips, calendarWeeks, cleanName, ghostSummary, level4, nextStep, rivalry, shortName, valueAt } from "./engage.js";

const lvl = (level, extra = {}) => ({
  level, played: 0, wins: 0,
  stars: [{ earned: false }, { earned: false }, { earned: false }],
  best: { score: null, lowestDamagePercent: null, fastestClearSec: null },
  thresholds: { maxDamagePercent: 40, clearWithinSec: 50, fullPowerRatio: 0.35 },
  ...extra,
});

test("names: control and bidi characters stripped, spaces collapsed, length capped", () => {
  assert.equal(cleanName("  Iij\u0000 \n  the‮ great "), "Iij the great");
  assert.equal(cleanName("x".repeat(80)).length, 40);
  assert.equal(shortName("abcdefghijklmnopqrstuvwxyz", 10), "abcdefghi…");
  assert.equal(cleanName("<script>alert(1)</script>"), "<script>alert(1)</script>"); // React escapes it on output
});

test("next step: rest note wins over everything; never suggests a session then", () => {
  const s = nextStep({ rest: { show: true, text: "5 active days in a row." }, mastery: [lvl("Easy")] });
  assert.equal(s.kind, "rest");
});

test("next step: a newcomer gets Level 1; after clearing it, the next unplayed level", () => {
  assert.equal(nextStep({ mastery: [lvl("Easy"), lvl("Medium"), lvl("Hard")] }).level, "Easy");
  const cleared = lvl("Easy", { played: 2, wins: 1, stars: [{ earned: true }, { earned: true }, { earned: true }], best: { score: 900 } });
  assert.equal(nextStep({ mastery: [cleared, lvl("Medium"), lvl("Hard")] }).level, "Medium");
});

test("next step: the nearest star (smallest relative gap) when every level is played", () => {
  const easy = lvl("Easy", { played: 3, wins: 2, stars: [{ earned: true }, { earned: false }, { earned: false }], best: { score: 900, lowestDamagePercent: 44, fastestClearSec: 100 } });
  const med = lvl("Medium", { played: 1, wins: 1, stars: [{ earned: true }, { earned: true }, { earned: true }], best: { score: 1200 } });
  const hard = lvl("Hard", { played: 1, wins: 0 });
  const s = nextStep({ mastery: [easy, med, hard] });
  assert.equal(s.kind, "star");
  assert.match(s.text, /44%/); // 44 vs 40 is closer than 100 s vs 50 s
});

test("next step: all stars earned falls through to a challenge, then a score to beat", () => {
  const full = (level, score) => lvl(level, { played: 1, wins: 1, stars: [{ earned: true }, { earned: true }, { earned: true }], best: { score } });
  const all = [full("Easy", 900), full("Medium", 1200), full("Hard", 3000)];
  const ch = { daily: [{ title: "Chain it", description: "Reach a 4-hit combo", resets: "Resets tomorrow", completed: false, met: false }], weekly: [] };
  assert.equal(nextStep({ mastery: all, challenges: ch }).kind, "challenge");
  const s = nextStep({ mastery: all });
  assert.equal(s.kind, "score");
  assert.equal(s.level, "Hard");
  for (const x of [nextStep({ mastery: all }), nextStep({ mastery: all, challenges: ch })]) assert.doesNotMatch(`${x.title} ${x.text}`, /hurry|miss|lose|last chance/i);
});

test("rivalry: wins per level by best score and the overall gap", () => {
  const p = (byLevel, best) => ({ byLevel, totals: { bestScore: best } });
  const r = rivalry(
    p([{ level: "Easy", sessions: 2, bestScore: 900 }, { level: "Medium", sessions: 1, bestScore: 1000 }], 1000),
    p([{ level: "Easy", sessions: 1, bestScore: 950 }, { level: "Hard", sessions: 1, bestScore: 3000 }], 3000)
  );
  assert.deepEqual(r.levels.map((l) => l.winner), ["b", "a", "b"]);
  assert.equal(r.winsA, 1);
  assert.equal(r.winsB, 2);
  assert.equal(r.levels[0].gap, 50);
  assert.deepEqual(r.bestGap, { a: 1000, b: 3000, gap: 2000, leader: "b" });
  assert.equal(rivalry(null, {}), null);
});

test("ghost summary: time only between two wins, swing speed when both have samples, null without a best run", () => {
  const best = { best: { outcome: "victory", durationSec: 60 }, samples: { hits: [{ speed: 4 }, { speed: 6 }] } };
  assert.equal(ghostSummary({ outcome: "victory", durationSec: 52.5, hits: [{ speed: 5.5 }] }, best), "7.5 s faster to clear, median swing speed up 10%");
  assert.equal(ghostSummary({ outcome: "defeat", durationSec: 52.5, hits: [{ speed: 4.5 }] }, best), "median swing speed down 10%");
  assert.equal(ghostSummary({ outcome: "victory", durationSec: 52.5, hits: [] }, { best: null }), null);
  assert.equal(valueAt([{ t: 0, f: 1 }, { t: 5, f: 0.5 }], 4.9, "f"), 1);
  assert.equal(valueAt([{ t: 0, f: 1 }, { t: 5, f: 0.5 }], 5, "f"), 0.5);
});

test("calendar helpers and tips", () => {
  assert.equal(level4(0, 10), 0);
  assert.equal(level4(1, 10), 1);
  assert.equal(level4(10, 10), 4);
  assert.equal(level4(5, 0), 0);
  assert.equal(calendarWeeks(Array.from({ length: 10 }, (_, i) => i)).length, 2);
  assert.ok(beatTips("Hard").some((t) => /forearm/.test(t)));
  assert.ok(beatTips("Unknown").length >= 3);
});
