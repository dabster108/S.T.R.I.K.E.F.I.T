const test = require("node:test");
const assert = require("node:assert/strict");
const milestones = require("../milestones");
const progression = require("../progression");
const fitness = require("../fitness");
const cfg = require("../config/milestones");

const ZONE = "Asia/Kathmandu";
let n = 0;
const fact = (day, over = {}) => ({
  id: `m${++n}`, level: "Easy", outcome: "defeat", day, startedAt: `${day}T0${(n % 9) + 1}:00:00.000Z`, endedAt: `${day}T0${(n % 9) + 1}:05:00.000Z`,
  swings: 0, fullSwings: 0, ...over,
});
const build = (facts) => milestones.buildMilestones({ playerName: "Ava", facts, zone: ZONE, now: new Date("2026-10-20T00:00:00Z") });
const ids = (r) => r.achieved.map((m) => m.id);
const byId = (r, id) => r.achieved.find((m) => m.id === id);

test("a player with no fights has nothing reached and the first three milestones to go for", () => {
  const r = build([]);
  assert.deepEqual(r.achieved, []);
  assert.deepEqual(r.upcoming.map((m) => m.id), ["first_fight", "first_victory", "victory_easy"]);
  assert.ok(r.upcoming.every((m) => m.progress === 0 && m.reachedAt === null));
});

test("first fight, first victory, first victory on each level and the first full-power swing", () => {
  const facts = [
    fact("2026-10-01", { swings: 20 }),
    fact("2026-10-02", { swings: 30, outcome: "victory", level: "Easy", fullSwings: 4 }),
    fact("2026-10-03", { swings: 30, outcome: "victory", level: "Hard" }),
    fact("2026-10-04", { swings: 30, outcome: "victory", level: "Easy" }),
  ];
  const r = build(facts);
  assert.deepEqual(ids(r).slice(0, 5), ["first_fight", "first_victory", "victory_easy", "first_full_power", "victory_hard"]);
  assert.equal(byId(r, "first_fight").sessionId, facts[0].id);
  assert.equal(byId(r, "first_victory").sessionId, facts[1].id); // not the later victories
  assert.equal(byId(r, "victory_hard").sessionId, facts[2].id);
  assert.equal(byId(r, "first_full_power").sessionId, facts[1].id);
  assert.equal(r.achieved.find((m) => m.id === "victory_medium"), undefined);
});

test("swing milestones use the running total at the time of each session, not today's total", () => {
  const facts = [fact("2026-10-01", { swings: 60 }), fact("2026-10-02", { swings: 60 }), fact("2026-10-03", { swings: 500 }), fact("2026-10-04", { swings: 600 })];
  const r = build(facts);
  assert.equal(byId(r, "swings_100").sessionId, facts[1].id); // 120 after the second session
  assert.equal(byId(r, "swings_500").sessionId, facts[2].id); // 620 after the third
  assert.equal(byId(r, "swings_1000").sessionId, facts[3].id); // 1220 after the fourth
  assert.equal(r.totals.swings, 1220);
  // exactly on the count counts, one short does not
  assert.ok(byId(build([fact("2026-10-01", { swings: 100 })]), "swings_100"));
  assert.equal(byId(build([fact("2026-10-01", { swings: 99 })]), "swings_100"), undefined);
});

test("the 10th fight, abandoned fights included", () => {
  const facts = Array.from({ length: 10 }, (_, i) => fact(`2026-10-${String(i + 1).padStart(2, "0")}`, { outcome: i % 3 === 0 ? "abandoned" : "defeat" }));
  const r = build(facts);
  assert.equal(byId(r, "fights_10").sessionId, facts[9].id);
  assert.equal(byId(build(facts.slice(0, 9)), "fights_10"), undefined);
});

test("each rank reached is a milestone, dated to the session that crossed it", () => {
  const facts = [fact("2026-10-01", { swings: 250 }), fact("2026-10-02", { swings: 100 }), fact("2026-10-03", { swings: 900, outcome: "victory", level: "Hard" })];
  const r = build(facts);
  assert.equal(byId(r, "rank_brawler").sessionId, facts[1].id);
  assert.equal(byId(r, "rank_warrior").sessionId, facts[2].id);
  assert.equal(byId(r, "rank_guardian"), undefined);
  assert.equal(byId(r, "rank_brawler").day, "2026-10-02");
});

test("the achieved list is a timeline: oldest first, ties in the listed order", () => {
  const facts = [fact("2026-10-02", { swings: 130, outcome: "victory", fullSwings: 3 })]; // one session reaches several at once
  const r = build(facts);
  assert.deepEqual(ids(r).slice(0, 5), ["first_fight", "first_victory", "victory_easy", "first_full_power", "swings_100"]);
  const times = r.achieved.map((m) => m.reachedAt);
  assert.deepEqual([...times].sort(), times);
});

test("a milestone's day is its Kathmandu calendar day", () => {
  // 20:00 UTC on the 18th is 01:45 on the 19th in Kathmandu
  const f = { ...fact("2026-10-19"), startedAt: "2026-10-18T19:50:00.000Z", endedAt: "2026-10-18T20:00:00.000Z" };
  assert.equal(byId(build([f]), "first_fight").day, "2026-10-19");
});

test("play streaks: consecutive days, one rest day a week forgiven, two in a row not", () => {
  const streak = (days) => milestones.playStreaks(days);
  assert.equal(streak(["2026-10-01", "2026-10-02", "2026-10-03"]).longest, 3);
  assert.equal(streak(["2026-10-01", "2026-10-03", "2026-10-04"]).longest, 3); // one rest day forgiven
  assert.equal(streak(["2026-10-01", "2026-10-04", "2026-10-05"]).longest, 2); // two missed days in a row break it
  assert.equal(streak(["2026-10-01", "2026-10-03", "2026-10-05"]).longest, 2); // a second rest day within the week breaks it
  assert.equal(streak(["2026-10-01", "2026-10-03", "2026-10-10", "2026-10-12"]).longest, 2); // a week apart: forgiven again, but 10-03 to 10-10 is a long gap
  assert.equal(streak(["2026-10-03", "2026-10-01", "2026-10-01"]).longest, 2); // order and duplicates do not matter
  assert.equal(streak([]).longest, 0);
  assert.equal(streak(["2026-10-01", "2026-10-02", "2026-10-03"]).reachedOn(3), "2026-10-03");
  assert.equal(streak(["2026-10-01", "2026-10-02"]).reachedOn(3), null);
});

test("the streak rule agrees with the 3-day streak badge's rule on any sequence of days", () => {
  // same pseudo-random day sequences through both implementations
  let seed = 12345;
  const rnd = () => ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff);
  for (let t = 0; t < 300; t++) {
    const start = fitness.dayNum("2026-09-01");
    const days = [];
    for (let d = 0; d < 40; d++) if (rnd() < 0.7) days.push(fitness.dayFromNum(start + d));
    if (!days.length) continue;
    const mine = milestones.playStreaks(days);
    const theirs = progression.streakWithFreeze(days, days[days.length - 1]);
    assert.equal(mine.longest, theirs.longest, days.join(","));
    for (const len of [3, 7]) assert.equal(mine.reachedOn(len), theirs.reachedOn(len), `${len}: ${days.join(",")}`);
  }
  assert.equal(cfg.STREAK_RULE.perDays, progression.FREEZE_WINDOW_DAYS);
});

test("streak milestones are dated to the day the streak reached its length", () => {
  const facts = ["2026-10-01", "2026-10-02", "2026-10-04", "2026-10-05"].map((d) => fact(d));
  const r = build(facts);
  assert.equal(byId(r, "streak_3").day, "2026-10-04"); // 1, 2, (rest), 4 is the third play day
  assert.equal(byId(r, "streak_3").sessionId, facts[2].id);
  assert.equal(byId(r, "streak_7"), undefined);
  assert.equal(r.totals.longestStreak, 4);
});

test("upcoming milestones come with how far along they are, closest first, three at most", () => {
  // after two fights on two days: 100 swings, 150 effort points (80 + 2 x 5 + 40 for the win, then 20 more), a 2-day streak
  const facts = [fact("2026-10-01", { swings: 80, outcome: "victory", fullSwings: 5 }), fact("2026-10-02", { swings: 20 })];
  const r = build(facts);
  assert.deepEqual(ids(r), ["first_fight", "first_victory", "victory_easy", "first_full_power", "swings_100"]);
  assert.equal(r.upcoming.length, 3);
  assert.deepEqual(r.upcoming.map((m) => [m.id, m.progress]), [["streak_3", 0.667], ["rank_brawler", 0.5], ["streak_7", 0.286]]);
});

test("a milestone not reached is never given a time", () => {
  const r = build([fact("2026-10-01")]);
  for (const m of r.upcoming) assert.equal(m.reachedAt, null);
  for (const m of r.achieved) assert.ok(m.reachedAt && m.sessionId);
});
