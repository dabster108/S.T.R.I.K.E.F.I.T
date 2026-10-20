const test = require("node:test");
const assert = require("node:assert/strict");
const ranks = require("../ranks");

const fact = (over = {}) => ({ id: "s" + Math.random().toString(36).slice(2, 8), playerName: "Ava", level: "Easy", outcome: "defeat", startedAt: "2026-10-01T10:00:00.000Z", endedAt: "2026-10-01T10:05:00.000Z", swings: 0, fullSwings: 0, ...over });

test("the ladder is Rookie 0, Brawler 300, Warrior 1200, Guardian 3500", () => {
  assert.deepEqual(ranks.ladder().map((r) => [r.name, r.from]), [["Rookie", 0], ["Brawler", 300], ["Warrior", 1200], ["Guardian", 3500]]);
});

test("effort points: swings x1, full-power swings x2, victories x40, Hard victories 60 extra", () => {
  assert.equal(ranks.sessionPoints(fact({ swings: 50 })), 50);
  assert.equal(ranks.sessionPoints(fact({ swings: 50, fullSwings: 10 })), 70);
  assert.equal(ranks.sessionPoints(fact({ swings: 50, fullSwings: 10, outcome: "victory" })), 110);
  assert.equal(ranks.sessionPoints(fact({ swings: 50, fullSwings: 10, outcome: "victory", level: "Hard" })), 170);
  assert.equal(ranks.sessionPoints(fact({ swings: 50, outcome: "defeat", level: "Hard" })), 50); // no bonus without the win
  assert.equal(ranks.sessionPoints(fact({ swings: 50, outcome: "abandoned" })), 50);
  assert.equal(ranks.sessionPoints(fact({ swings: -5, fullSwings: undefined })), 0); // junk never goes negative
});

test("rank boundaries: one below, exactly on, one above each threshold", () => {
  const cases = [[0, "Rookie"], [299, "Rookie"], [300, "Brawler"], [301, "Brawler"], [1199, "Brawler"], [1200, "Warrior"], [1201, "Warrior"], [3499, "Warrior"], [3500, "Guardian"], [3501, "Guardian"], [999999, "Guardian"]];
  for (const [points, name] of cases) assert.equal(ranks.rankFor(points).name, name, `${points} points`);
  assert.equal(ranks.rankFor(-20).name, "Rookie");
  assert.equal(ranks.rankFor(NaN).name, "Rookie");
});

test("rankInfo: next rank, points to go and progress 0 to 1", () => {
  let r = ranks.rankInfo(0);
  assert.deepEqual([r.rank.name, r.nextRank.name, r.pointsToNext, r.progress], ["Rookie", "Brawler", 300, 0]);
  r = ranks.rankInfo(150);
  assert.deepEqual([r.pointsToNext, r.progress], [150, 0.5]);
  r = ranks.rankInfo(300); // exactly on the threshold: Brawler, with none of the rung done
  assert.deepEqual([r.rank.name, r.nextRank.name, r.pointsToNext, r.progress], ["Brawler", "Warrior", 900, 0]);
  r = ranks.rankInfo(299);
  assert.equal(r.rank.name, "Rookie");
  assert.equal(r.pointsToNext, 1);
  assert.ok(r.progress > 0.99 && r.progress < 1);
  r = ranks.rankInfo(3500);
  assert.deepEqual([r.rank.name, r.nextRank, r.pointsToNext, r.progress], ["Guardian", null, null, 1]); // max rank: nothing next
  r = ranks.rankInfo(9000);
  assert.deepEqual([r.rank.name, r.nextRank, r.progress, r.effortPoints], ["Guardian", null, 1, 9000]);
});

test("the timeline lists each rank reached with the session that reached it, in play order", () => {
  const facts = [
    fact({ id: "c", startedAt: "2026-10-03T10:00:00.000Z", endedAt: "2026-10-03T10:09:00.000Z", swings: 900, outcome: "victory", level: "Hard" }), // a: 250 points, b: 350 in all, c: 350 + 900 swings + 40 victory + 60 Hard = 1350
    fact({ id: "a", startedAt: "2026-10-01T10:00:00.000Z", endedAt: "2026-10-01T10:05:00.000Z", swings: 250 }),
    fact({ id: "b", startedAt: "2026-10-02T10:00:00.000Z", endedAt: "2026-10-02T10:05:00.000Z", swings: 100 }),
  ];
  const t = ranks.rankTimeline(facts);
  // a: 250 (Rookie). b: 350 -> Brawler. c: 350 + 900 + 100 = 1350 -> Warrior.
  assert.deepEqual(t.map((s) => [s.rank.name, s.sessionId, s.points]), [["Brawler", "b", 350], ["Warrior", "c", 1350]]);
  assert.equal(t[0].at, "2026-10-02T10:05:00.000Z");
  assert.deepEqual(ranks.rankTimeline([]), []);
});

test("one big session can pass two rungs, and both count as reached", () => {
  const t = ranks.rankTimeline([fact({ id: "x", swings: 1500 })]);
  assert.deepEqual(t.map((s) => s.rank.name), ["Brawler", "Warrior"]);
});

test("rankUpFor: before and after one session", () => {
  const facts = [fact({ id: "a", startedAt: "2026-10-01T10:00:00.000Z", swings: 250 }), fact({ id: "b", startedAt: "2026-10-02T10:00:00.000Z", swings: 100 }), fact({ id: "c", startedAt: "2026-10-03T10:00:00.000Z", swings: 10 })];
  assert.equal(ranks.rankUpFor(facts, "a"), null);
  const up = ranks.rankUpFor(facts, "b");
  assert.deepEqual([up.from.name, up.to.name, up.effortPoints], ["Rookie", "Brawler", 350]);
  assert.equal(ranks.rankUpFor(facts, "c"), null); // already Brawler before it
  assert.equal(ranks.rankUpFor(facts, "nope"), null);
  // sessions that started LATER do not change the answer for an earlier one
  assert.deepEqual(ranks.rankUpFor(facts, "b"), ranks.rankUpFor(facts.slice(0, 2), "b"));
});

test("rank-up feed rows are idempotent: the same row id however often the same session is counted or the feed is rebuilt", () => {
  const facts = [fact({ id: "a", swings: 250 }), fact({ id: "b", startedAt: "2026-10-02T10:00:00.000Z", endedAt: "2026-10-02T10:05:00.000Z", swings: 100 })];
  const once = ranks.rankUpEntries(new Map([["Ava", facts]]));
  assert.equal(once.length, 1);
  assert.equal(once[0].id, "rank:Ava:brawler");
  assert.equal(once[0].label, "Brawler");
  // the same session reported twice (the finished POST arrives again): still one entry
  const doubled = ranks.rankUpEntries(new Map([["Ava", [...facts, facts[1]]]]));
  assert.equal(doubled.filter((r) => r.id === "rank:Ava:brawler").length, 1);
  // a rebuild gives the same rows
  assert.deepEqual(ranks.rankUpEntries(new Map([["Ava", facts]])), once);
  // old rank-ups can be filtered out
  assert.equal(ranks.rankUpEntries(new Map([["Ava", facts]]), "2026-10-03T00:00:00.000Z").length, 0);
});
