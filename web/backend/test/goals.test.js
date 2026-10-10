// Daily goal rules against fixed fixtures (no database, no clock). Run: npm test
const test = require("node:test");
const assert = require("node:assert/strict");
const goals = require("../goals");
const progression = require("../progression");

const hit = (tier, t, speed = 4) => ({ tier, speed, t });
const session = (id, startedAt, extra = {}) => ({ id, startedAt, status: "finished", durationSec: 120, hitCount: 10, score: 100, xp: 100, fullSwings: 0, level: "Easy", ...extra });

test("validateGoal: metric must be one of the four, target inside the per-metric range", () => {
  assert.equal(goals.validateGoal({ metric: "kcal", target: 40 }).ok, true);
  assert.equal(goals.validateGoal({ metric: "kcal", target: "40" }).value.target, 40);
  assert.equal(goals.validateGoal({ metric: "steps", target: 40 }).ok, false);
  assert.equal(goals.validateGoal({ metric: "kcal", target: 4 }).ok, false);
  assert.equal(goals.validateGoal({ metric: "kcal", target: 501 }).ok, false);
  assert.equal(goals.validateGoal({ metric: "fullSwings", target: 9 }).ok, false);
  assert.equal(goals.validateGoal({ metric: "activeMinutes", target: 120 }).ok, true);
  assert.equal(goals.validateGoal({ metric: "activeMinutes", target: 121 }).ok, false);
  assert.equal(goals.validateGoal({ metric: "xp", target: "abc" }).ok, false);
  assert.equal(goals.validateGoal({ metric: "xp", target: NaN }).ok, false);
  assert.equal(goals.validateGoal(null).ok, false);
  assert.equal(goals.validateGoal({ metric: "xp" }).ok, false);
});

test("validateName rejects empty, long and control characters", () => {
  assert.equal(goals.validateName("Iij").ok, true);
  assert.equal(goals.validateName("   ").ok, false);
  assert.equal(goals.validateName("x".repeat(41)).ok, false);
  assert.equal(goals.validateName("a\nb").ok, false);
});

test("day boundary: a session counts for the local day it started on (Asia/Kathmandu is UTC+5:45)", () => {
  const zone = progression.safeZone("Asia/Kathmandu");
  // 18:14Z = 23:59 local; 18:15Z = 00:00 next day local.
  assert.equal(goals.dayIn(new Date("2026-10-09T18:14:00Z"), zone), "2026-10-09");
  assert.equal(goals.dayIn(new Date("2026-10-09T18:15:00Z"), zone), "2026-10-10");
  assert.equal(goals.dayIn(new Date("2026-10-09T18:16:00Z"), zone), "2026-10-10");
});

test("progress per metric", () => {
  const sessions = [
    session("a", "2026-10-09T05:00:00Z", { fullSwings: 4, xp: 300, durationSec: 100 }),
    session("b", "2026-10-09T06:00:00Z", { fullSwings: 0, xp: 150, durationSec: 100 }),
  ];
  const hits = new Map([
    ["a", [hit("full", 5), hit("full", 20), hit("medium", 70), hit("full", 71), hit("light", 200)]],
    ["b", [hit("tap", 3), hit("light", 12)]],
  ]);
  const t = goals.dayTotals(sessions, hits);
  assert.equal(t.sessions, 2);
  assert.equal(t.xp, 450);
  assert.equal(t.fullSwings, 4, "per session the larger of recorded full swings (4) and full-tier hit events (3), then summed with 0");
  assert.equal(t.activeMinutes, 3 + 1, "session a: minutes 0, 1, 3; session b: minute 0");
  assert.ok(t.kcal > 0);
  assert.equal(goals.progressFor({ metric: "xp", target: 900 }, t).percent, 50);
  assert.equal(goals.progressFor({ metric: "xp", target: 450 }, t).met, true);
  assert.equal(goals.progressFor({ metric: "fullSwings", target: 4 }, t).met, true);
  assert.equal(goals.activeMinutesOf([hit("tap", 59.9), hit("tap", 60), hit("tap", null)]), 2);
  assert.equal(goals.dayTotals([], new Map()).kcal, 0);
});

test("completion is evaluated once: a completed goal never completes again", () => {
  const totals = { kcal: 50, fullSwings: 0, activeMinutes: 0, xp: 0 };
  const open = { metric: "kcal", target: 40, completedAt: null };
  const first = goals.evaluateCompletion(open, totals);
  assert.deepEqual(first, { complete: true, bonusXp: goals.BONUS_XP });
  const done = { ...open, completedAt: "2026-10-09T10:00:00.000Z" };
  const again = goals.evaluateCompletion(done, totals);
  assert.equal(again.complete, false);
  assert.equal(again.bonusXp, 0);
  assert.equal(goals.evaluateCompletion(open, { ...totals, kcal: 39.9 }).complete, false);
  assert.equal(goals.evaluateCompletion(null, totals).complete, false);
});

test("presets: fallbacks with too little data, data-driven (easy < standard < hard, clamped) with enough", () => {
  const few = goals.suggestPresets(new Map([["A", [{ day: "d1", kcal: 10, fullSwings: 20, activeMinutes: 4, xp: 400 }]]]));
  assert.equal(few.source, "fallback");
  assert.deepEqual(few.presets.kcal, goals.FALLBACK_PRESETS.kcal);
  const day = (k) => ({ day: "x", kcal: k, fullSwings: k * 2, activeMinutes: Math.round(k / 4), xp: k * 20 });
  const rich = goals.suggestPresets(new Map([
    ["A", [day(20), day(30), day(40)]],
    ["B", [day(10), day(20), day(30), day(50)]],
  ]));
  assert.equal(rich.source, "data");
  const p = rich.presets.kcal;
  assert.ok(p.easy < p.standard && p.standard < p.hard, JSON.stringify(p));
  assert.equal(p.easy % 5, 0);
  assert.ok(p.hard <= goals.TARGET_RANGE.kcal.max);
  assert.equal(rich.medians.kcal, 30);
  assert.equal(p.easy, 30);        // median x1
  assert.equal(p.standard, 45);    // x1.5
  assert.equal(p.hard, 75);        // x2.5
  // a player with only 2 active days does not count
  const filtered = goals.suggestPresets(new Map([["A", [day(20), day(30), day(40)]], ["B", [day(500), day(600)]], ["C", [day(10), day(20), day(30)]]]));
  assert.equal(filtered.playersUsed, 2);
});

test("goal streak: consecutive days, alive until the end of the next day", () => {
  assert.deepEqual(goals.goalStreak(["2026-10-07", "2026-10-08", "2026-10-09"], "2026-10-09"), { current: 3, longest: 3, daysMet: 3 });
  assert.equal(goals.goalStreak(["2026-10-07", "2026-10-08", "2026-10-09"], "2026-10-10").current, 3);
  assert.equal(goals.goalStreak(["2026-10-07", "2026-10-08", "2026-10-09"], "2026-10-11").current, 0);
  assert.equal(goals.goalStreak(["2026-10-01", "2026-10-03", "2026-10-04"], "2026-10-04").current, 2);
  assert.equal(goals.goalStreak([], "2026-10-04").longest, 0);
});

test("badges: Goal Getter at 3 distinct days, Goal Week at 7; absent data stays locked", () => {
  const ids = progression.BADGES.map((b) => b.id);
  assert.ok(ids.includes("goal_getter") && ids.includes("goal_week"));
  const sessions = [{ id: "s1", startedAt: "2026-10-01T05:00:00Z", endedAt: "2026-10-01T05:10:00Z", level: "Easy", outcome: "victory", hitCount: 5, fullSwings: 0, playerHpRemaining: 100 }];
  const base = { sessions, summaryById: new Map([["s1", { tiers: { total: 5, counts: { full: 0 } }, estimatedCalories: 3 }]]), comboById: new Map(), hurtById: new Map(), maxHpById: new Map(), eventSessions: new Set(), days: new Map([["s1", "2026-10-01"]]), streak: progression.streakWithFreeze(["2026-10-01"], "2026-10-01"), weeklyTarget: 3, thisWeekSessions: 1 };
  const get = (ctx, id) => progression.evaluateBadges(ctx).find((b) => b.id === id);
  assert.equal(get(base, "goal_getter").unlocked, false);
  assert.equal(get({ ...base, goalDays: ["2026-10-01", "2026-10-02"] }, "goal_getter").unlocked, false);
  assert.equal(get({ ...base, goalDays: ["2026-10-01", "2026-10-02", "2026-10-02", "2026-10-04"] }, "goal_getter").unlocked, true);
  assert.equal(get({ ...base, goalDays: ["2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05", "2026-10-06"] }, "goal_week").unlocked, false);
  assert.equal(get({ ...base, goalDays: ["2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05", "2026-10-06", "2026-10-07"] }, "goal_week").unlocked, true);
});
