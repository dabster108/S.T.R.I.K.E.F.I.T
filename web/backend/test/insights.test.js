// Run: npm test   (node --test, no database needed)
const test = require("node:test");
const assert = require("node:assert/strict");
const ins = require("../insights");

// A fight of `n` hits spread evenly over `dur` seconds, speed chosen per third.
const fight = (dur, n, [a, b, c]) => ({
  durationSec: dur,
  hits: Array.from({ length: n }, (_, i) => {
    const t = ((i + 0.5) / n) * dur;
    return { t, speed: t < dur / 3 ? a : t < (2 * dur) / 3 ? b : c };
  }),
});

test("effort trend: names the last third when it leads by 10 % or more", () => {
  const r = ins.effortTrend([fight(60, 12, [3, 3, 4]), fight(60, 12, [3, 3, 4]), fight(60, 12, [3, 3, 4])]);
  assert.equal(r.ok, true);
  assert.equal(r.top, "last");
  assert.deepEqual(r.perThird, [3, 3, 4]);
});

test("effort trend: level effort gives no third", () => {
  const r = ins.effortTrend([fight(60, 12, [3, 3, 3.1]), fight(60, 12, [3, 3, 3.1]), fight(60, 12, [3, 3, 3.1])]);
  assert.equal(r.ok, true);
  assert.equal(r.top, null);
});

test("effort trend: below the minimum (2 fights, or fights under 10 hits) is not enough", () => {
  assert.equal(ins.effortTrend([fight(60, 20, [3, 3, 4]), fight(60, 20, [3, 3, 4])]).ok, false);
  const r = ins.effortTrend([fight(60, 9, [3, 3, 4]), fight(60, 9, [3, 3, 4]), fight(60, 9, [3, 3, 4]), fight(60, 9, [3, 3, 4])]);
  assert.equal(r.ok, false);
  assert.equal(r.have.fights, 0);
});

test("full share: needs 20 tiered hits, ignores untiered", () => {
  assert.equal(ins.fullShare(Array(19).fill("full")).ok, false);
  const r = ins.fullShare([...Array(5).fill("full"), ...Array(15).fill("light"), null, null]);
  assert.equal(r.ok, true);
  assert.equal(r.share, 0.25);
  assert.equal(r.tiered, 20);
});

test("best / worst level: win rate over decided fights, abandoned fights ignored", () => {
  const out = [
    { level: "Easy", outcome: "victory" }, { level: "Easy", outcome: "victory" }, { level: "Easy", outcome: "abandoned" },
    { level: "Hard", outcome: "defeat" }, { level: "Hard", outcome: "victory" }, { level: "Hard", outcome: "defeat" },
  ];
  const r = ins.bestWorstLevel(out);
  assert.equal(r.ok, true);
  assert.equal(r.best.level, "Easy");
  assert.equal(r.worst.level, "Hard");
  assert.equal(r.worst.wins, 1);
  assert.equal(r.worst.decided, 3);
});

test("best / worst level: one level, or equal win rates, is not enough", () => {
  assert.equal(ins.bestWorstLevel([{ level: "Easy", outcome: "victory" }, { level: "Easy", outcome: "defeat" }]).ok, false);
  const same = ins.bestWorstLevel([
    { level: "Easy", outcome: "victory" }, { level: "Easy", outcome: "defeat" },
    { level: "Medium", outcome: "victory" }, { level: "Medium", outcome: "defeat" },
  ]);
  assert.equal(same.ok, false);
  assert.equal(same.have.sameWinRate, true);
});

test("guard summary: share of hits blocked and openings, from 20 hits", () => {
  assert.equal(ins.guardSummary([{ hits: 19, guardsBlocked: 3, openings: 1 }]).ok, false);
  const r = ins.guardSummary([{ hits: 15, guardsBlocked: 3, openings: 1 }, { hits: 10, guardsBlocked: 2, openings: 2 }]);
  assert.equal(r.ok, true);
  assert.equal(r.share, 0.2);
  assert.equal(r.openings, 3);
});

test("playerInsights: every sentence has data and a minimum; the rest are pending", () => {
  const r = ins.playerInsights({
    trendSessions: [fight(60, 12, [4, 3, 3]), fight(60, 12, [4, 3, 3]), fight(60, 12, [4, 3, 3])],
    tiers: Array(10).fill("full"),
    outcomes: [],
    guardSessions: [],
  });
  assert.deepEqual(r.insights.map((i) => i.id), ["effort_trend"]);
  assert.match(r.insights[0].text, /first third/);
  for (const i of r.insights) { assert.ok(i.data); assert.ok(i.minimum); }
  assert.deepEqual(r.pending.map((p) => p.id), ["full_share", "best_worst", "guard"]);
  assert.equal(r.pending[0].have.tieredHits, 10);
});
