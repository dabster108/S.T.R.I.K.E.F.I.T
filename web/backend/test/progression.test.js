// Run: npm test   (node --test, no database needed)
const test = require("node:test");
const assert = require("node:assert/strict");
const p = require("../progression");
const f = require("../feedbackCore");
const fitness = require("../fitness");

const TODAY = "2026-10-07"; // a Wednesday

test("streak: plain consecutive days", () => {
  const s = p.streakWithFreeze(["2026-10-05", "2026-10-06", "2026-10-07"], TODAY);
  assert.equal(s.current, 3);
  assert.equal(s.longest, 3);
  assert.equal(s.freezeUsed, false);
});

test("streak: alive if last session was yesterday, dead after a full missed day with nothing to forgive", () => {
  assert.equal(p.streakWithFreeze(["2026-10-05", "2026-10-06"], TODAY).current, 2);
  // last session Oct 4, today Oct 7: three days gone, nothing to forgive
  const s = p.streakWithFreeze(["2026-10-03", "2026-10-04"], TODAY);
  assert.equal(s.current, 0);
  assert.equal(s.longest, 2);
});

test("streak: one missed day between played days is forgiven and adds nothing", () => {
  const s = p.streakWithFreeze(["2026-10-03", "2026-10-05", "2026-10-06", "2026-10-07"], TODAY);
  assert.equal(s.current, 4); // played Oct 3, 5, 6, 7: four played days, the frozen Oct 4 adds nothing
  assert.equal(s.freezeUsed, true);
  assert.deepEqual(s.freezeDays, ["2026-10-04"]);
});

test("streak: a second freeze within 7 days is refused, so the streak resets", () => {
  // freeze on Oct 2, then Oct 5 missed again within 7 days of the first freeze
  const s = p.streakWithFreeze(["2026-10-01", "2026-10-03", "2026-10-04", "2026-10-06", "2026-10-07"], TODAY);
  assert.equal(s.current, 2); // restarted on Oct 6
  assert.equal(s.freezeUsed, false);
  assert.equal(s.longest, 3); // Oct 1, (2 frozen), 3, 4
});

test("streak: a freeze is available again 7 days after the last one", () => {
  const s = p.streakWithFreeze(["2026-09-28", "2026-09-30", "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05", "2026-10-07"], TODAY);
  // freeze on Sep 29, next missed day Oct 6 is 7 days later -> allowed
  assert.equal(s.freezeDays.length, 2);
  assert.equal(s.current, 8);
});

test("streak: two missed days in a row always break it", () => {
  const s = p.streakWithFreeze(["2026-10-02", "2026-10-05", "2026-10-06", "2026-10-07"], TODAY);
  assert.equal(s.current, 3);
  assert.equal(s.freezeUsed, false);
});

test("streak: last session two days ago stays alive as freezePending when a freeze is free", () => {
  const s = p.streakWithFreeze(["2026-10-04", "2026-10-05"], TODAY);
  assert.equal(s.current, 2);
  assert.equal(s.freezePending, true);
  assert.equal(s.freezeUsed, false);
});

test("streak: no sessions", () => {
  const s = p.streakWithFreeze([], TODAY);
  assert.deepEqual([s.current, s.longest], [0, 0]);
});

test("streak: reachedOn gives the first day a length was hit", () => {
  const s = p.streakWithFreeze(["2026-10-01", "2026-10-02", "2026-10-03", "2026-10-07"], TODAY);
  assert.equal(s.reachedOn(3), "2026-10-03");
  assert.equal(s.reachedOn(4), null);
});

test("weekly goal: Monday-start week, target 3", () => {
  const g = p.weeklyGoal(["2026-10-04", "2026-10-05", "2026-10-06", "2026-10-07"], TODAY);
  assert.equal(g.weekStart, "2026-10-05");
  assert.equal(g.weekEnd, "2026-10-11");
  assert.equal(g.sessionsThisWeek, 3); // Sunday Oct 4 belongs to the previous week
  assert.equal(g.met, true);
  assert.equal(p.weeklyGoal(["2026-10-05"], TODAY).met, false);
});

test("level curve: XP_BASE x (L-1)^2", () => {
  assert.equal(p.levelFromXp(0).level, 1);
  assert.equal(p.levelFromXp(249).level, 1);
  assert.equal(p.levelFromXp(250).level, 2);
  assert.equal(p.levelFromXp(999).level, 2);
  assert.equal(p.levelFromXp(1000).level, 3);
  const l = p.levelFromXp(1500);
  assert.equal(l.level, 3);
  assert.equal(l.xpToNext, 750); // to 2250
  assert.equal(l.title, "Brawler");
  assert.equal(p.levelFromXp(12250).title, "Warlord");
  assert.equal(p.levelFromXp(-5).level, 1);
});

// ---- badges ---------------------------------------------------------------------------------
const session = (id, day, over = {}) => ({
  id, startedAt: `${day}T10:00:00Z`, endedAt: `${day}T10:05:00Z`, level: "Easy", outcome: "victory",
  hitCount: 20, fullSwings: 0, playerHpRemaining: 100, liveState: { playerMaxHp: 500 }, durationSec: 300, ...over,
});
const summaryFor = (s, counts = {}) =>
  fitness.sessionSummary(s, ["tap", "light", "medium", "full"].flatMap((t) => Array.from({ length: counts[t] || 0 }, () => ({ tier: t, speed: 3, t: 10 }))));

function ctxFor(sessions, { counts = {}, combo = new Map(), hurt = new Map(), maxHp = new Map(), events = new Set() } = {}) {
  const days = new Map(sessions.map((s) => [s.id, s.startedAt.slice(0, 10)]));
  const summaryById = new Map(sessions.map((s) => [s.id, summaryFor(s, counts[s.id])]));
  const streak = p.streakWithFreeze([...days.values()], TODAY);
  const weekly = p.weeklyGoal([...days.values()], TODAY);
  return { sessions, summaryById, comboById: combo, hurtById: hurt, maxHpById: maxHp, eventSessions: events, days, streak, weeklyTarget: 3, thisWeekSessions: weekly.sessionsThisWeek };
}
const byId = (list, id) => list.find((b) => b.id === id);

test("badges: new player has everything locked and combo is 'tracking'", () => {
  const b = p.evaluateBadges(ctxFor([]));
  assert.equal(b.filter((x) => x.unlocked).length, 0);
  assert.equal(byId(b, "combo_10").tracking, true);
  assert.match(byId(b, "combo_10").trackingNote, /after the game update/);
});

test("badges: First Blood, Full Power x50 crossing session, kcal, streak", () => {
  const s = [session("a", "2026-10-05", { durationSec: 60 }), session("b", "2026-10-06", { durationSec: 60 }), session("c", "2026-10-07", { durationSec: 60 })];
  const b = p.evaluateBadges(ctxFor(s, { counts: { a: { full: 20 }, b: { full: 20 }, c: { full: 20, tap: 5 } } }));
  assert.equal(byId(b, "first_blood").sessionId, "a");
  const fp = byId(b, "full_power_50");
  assert.equal(fp.unlocked, true);
  assert.equal(fp.sessionId, "c"); // 20+20+20 crosses 50 in the third session
  assert.equal(byId(b, "streak_3").unlocked, true);
  assert.equal(byId(b, "streak_3").sessionId, "c");
  assert.equal(byId(b, "weekly_goal").unlocked, true);
  const kcal = byId(b, "burn_100");
  assert.equal(kcal.unlocked, false);
  assert.ok(kcal.progress.current > 0 && kcal.progress.target === 100);
});

test("badges: Full Power progress when locked", () => {
  const b = p.evaluateBadges(ctxFor([session("a", "2026-10-05")], { counts: { a: { full: 12 } } }));
  assert.deepEqual(byId(b, "full_power_50").progress, { current: 12, target: 50 });
  assert.equal(byId(b, "full_power_50").unlocked, false);
});

test("badges: combo uses real combo data once present", () => {
  const s = [session("a", "2026-10-05"), session("b", "2026-10-06")];
  const b = p.evaluateBadges(ctxFor(s, { combo: new Map([["a", 4], ["b", 11]]) }));
  const c = byId(b, "combo_10");
  assert.equal(c.tracking, false);
  assert.equal(c.unlocked, true);
  assert.equal(c.sessionId, "b");
  const low = byId(p.evaluateBadges(ctxFor(s, { combo: new Map([["a", 4]]) })), "combo_10");
  assert.deepEqual([low.unlocked, low.progress.current], [false, 4]);
});

test("badges: Untouched needs a Medium victory with zero damage", () => {
  const hurtSession = session("m1", "2026-10-05", { level: "Medium", playerHpRemaining: 700, liveState: { playerMaxHp: 800 } });
  const cleanSession = session("m2", "2026-10-06", { level: "Medium", playerHpRemaining: 800, liveState: { playerMaxHp: 800 } });
  const easyClean = session("e1", "2026-10-07", { level: "Easy", playerHpRemaining: 500 });
  assert.equal(byId(p.evaluateBadges(ctxFor([hurtSession, easyClean])), "flawless_medium").unlocked, false);
  const ok = byId(p.evaluateBadges(ctxFor([hurtSession, cleanSession])), "flawless_medium");
  assert.deepEqual([ok.unlocked, ok.sessionId], [true, "m2"]);
  const lost = session("m3", "2026-10-07", { level: "Medium", outcome: "defeat", playerHpRemaining: 0 });
  assert.equal(byId(p.evaluateBadges(ctxFor([lost])), "flawless_medium").unlocked, false);
});

test("badges: Untouched is not guessed when damage can't be told", () => {
  const unknown = session("m1", "2026-10-05", { level: "Medium", liveState: null, playerHpRemaining: 0 });
  assert.equal(byId(p.evaluateBadges(ctxFor([unknown])), "flawless_medium").unlocked, false);
  const viaEvents = p.evaluateBadges(ctxFor([unknown], { hurt: new Map([["m1", 0]]), events: new Set(["m1"]) }));
  assert.equal(byId(viaEvents, "flawless_medium").unlocked, true);
});

test("badges: Untouched uses the max HP from events when the session has no live state", () => {
  const lostHp = session("m1", "2026-10-05", { level: "Medium", liveState: null, playerHpRemaining: 640 });
  const ctx = { hurt: new Map([["m1", 0]]), events: new Set(["m1"]), maxHp: new Map([["m1", 800]]) };
  assert.equal(byId(p.evaluateBadges(ctxFor([lostHp], ctx)), "flawless_medium").unlocked, false); // lost 160 HP
  const clean = session("m2", "2026-10-06", { level: "Medium", liveState: null, playerHpRemaining: 800 });
  const ok = byId(p.evaluateBadges(ctxFor([clean], { ...ctx, maxHp: new Map([["m2", 800]]), hurt: new Map([["m2", 0]]), events: new Set(["m2"]) })), "flawless_medium");
  assert.equal(ok.unlocked, true);
});

test("personal bests held by a session need at least two sessions", () => {
  const a = session("a", "2026-10-05", { score: 100 });
  const b = session("b", "2026-10-06", { score: 300 });
  const sa = summaryFor(a, { full: 20 });
  const sb = summaryFor(b, { tap: 20 });
  assert.deepEqual(p.personalBestsSet("a", [sa]), []);
  const set = p.personalBestsSet("b", [sa, sb]);
  assert.ok(set.some((x) => x.key === "highestScore"));
  assert.ok(!p.personalBestsSet("a", [sa, sb]).some((x) => x.key === "highestScore"));
});

// ---- survey ---------------------------------------------------------------------------------
test("feedback validation", () => {
  assert.equal(f.validateFeedback({ enjoyment: 5, rpe: 13 }).ok, true);
  assert.equal(f.validateFeedback({ enjoyment: 3 }).ok, true);
  assert.equal(f.validateFeedback({ rpe: 6, comment: "  fun  " }).value.comment, "fun");
  for (const bad of [{}, { enjoyment: 0 }, { enjoyment: 6 }, { rpe: 5 }, { rpe: 21 }, { enjoyment: 2.5 }, { rpe: "abc" }, { enjoyment: 3, comment: 7 }, { enjoyment: 3, comment: "x".repeat(501) }, null, []]) {
    assert.equal(f.validateFeedback(bad).ok, false, JSON.stringify(bad));
  }
  assert.equal(f.validateFeedback({ enjoyment: "4", rpe: "15" }).value.rpe, 15);
});

test("rate limiter allows max per window then refuses, per ip", () => {
  let t = 0;
  const rl = f.createRateLimiter({ max: 3, windowMs: 1000, now: () => t });
  assert.deepEqual([1, 2, 3, 4].map(() => rl.allow("a")), [true, true, true, false]);
  assert.equal(rl.allow("b"), true);
  t = 1500;
  assert.equal(rl.allow("a"), true);
});

test("spearman: known values and ties", () => {
  assert.equal(f.spearman([1, 2, 3, 4, 5], [10, 20, 30, 40, 50]), 1);
  assert.equal(f.spearman([1, 2, 3, 4, 5], [50, 40, 30, 20, 10]), -1);
  assert.equal(f.spearman([1, 1, 1], [1, 2, 3]), null); // constant variable
  // classic textbook example, rho = -0.1757575...
  const rho = f.spearman([106, 86, 100, 101, 99, 103, 97, 113, 112, 110], [7, 0, 27, 50, 28, 29, 20, 12, 6, 17]);
  assert.ok(Math.abs(rho - -0.17575757) < 1e-6, String(rho));
  assert.deepEqual(f.ranks([10, 20, 20, 30]), [1, 2.5, 2.5, 4]);
});

test("feedback summary: below 8 pairs gives no coefficient, at 8 it does", () => {
  const row = (i) => ({ sessionId: `s${i}`, level: i % 2 ? "Easy" : "Medium", enjoyment: (i % 5) + 1, rpe: 6 + i, effort: 1 + i * 0.1, kcalPerMin: 4 + i });
  const few = f.summarizeFeedback(Array.from({ length: 7 }, (_, i) => row(i)));
  assert.equal(few.correlation.status, "insufficient_data");
  assert.equal(few.correlation.rho, null);
  assert.equal(few.n, 7);
  const enough = f.summarizeFeedback(Array.from({ length: 8 }, (_, i) => row(i)));
  assert.equal(enough.correlation.status, "ok");
  assert.equal(enough.correlation.rho, 1);
  assert.equal(enough.byLevel.length, 2);
  assert.equal(enough.rpe.distribution[6], 1);
  const empty = f.summarizeFeedback([]);
  assert.deepEqual([empty.n, empty.enjoyment.mean, empty.rpe.mean], [0, null, null]);
});
