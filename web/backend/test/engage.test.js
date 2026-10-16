// Mastery stars, calendar, weekly recap, best run and reaction limits against fixed fixtures. Run: npm test
const test = require("node:test");
const assert = require("node:assert/strict");
const engage = require("../engage");

const ZONE = "Asia/Kathmandu";
const row = (extra = {}) => ({
  id: "s1", playerName: "A", level: "Easy", outcome: "victory", score: 1000, startedAt: "2026-10-15T04:00:00.000Z", endedAt: "2026-10-15T04:01:00.000Z",
  durationSec: 60, hitCount: 20, fullSwings: 0, playerHpRemaining: 400, liveState: { playerMaxHp: 500 },
  tap: 0, light: 5, medium: 5, full: 10, minutes: 1, peak: 6.25, combo: 9, maxhp: null, hurt: 3, evented: true, ...extra,
});

test("sessionFacts: kcal via the fitness estimate, damage % from max HP, full swings, local day", () => {
  const f = engage.sessionFacts(row(), ZONE);
  assert.equal(f.day, "2026-10-15");
  assert.equal(f.damagePercent, 20);
  assert.equal(f.fullSwings, 10);
  assert.equal(f.fullRatio, 0.5);
  assert.equal(f.peakSpeed, 6.25);
  assert.ok(f.kcal > 0);
  // No max HP anywhere but events with zero hurts: 0% (the badge rules use the same evidence).
  assert.equal(engage.sessionFacts(row({ liveState: null, hurt: 0 }), ZONE).damagePercent, 0);
  assert.equal(engage.sessionFacts(row({ liveState: null, hurt: 2 }), ZONE).damagePercent, null);
  // No swings and no hits: no estimate (null), never 0.
  assert.equal(engage.sessionFacts(row({ light: 0, medium: 0, full: 0, hitCount: 0 }), ZONE).kcal, null);
  // Fewer than 10 tiered swings: no full-power ratio.
  assert.equal(engage.sessionFacts(row({ light: 1, medium: 1, full: 1 }), ZONE).fullRatio, null);
});

test("mastery: star 1 cleared, star 2 damage limit, star 3 time OR full-power ratio", () => {
  const cfg = engage.MASTERY.Easy;
  const f = (extra) => engage.sessionFacts(row(extra), ZONE);
  const none = engage.mastery([f({ outcome: "defeat" })]).find((l) => l.level === "Easy");
  assert.equal(none.starsEarned, 0);
  const one = engage.mastery([f({ playerHpRemaining: 100, durationSec: cfg.clearWithinSec + 30, full: 1 })]).find((l) => l.level === "Easy");
  assert.deepEqual(one.stars.map((s) => s.earned), [true, false, false]);
  const two = engage.mastery([f({ playerHpRemaining: 400, durationSec: cfg.clearWithinSec + 30, full: 1 })]).find((l) => l.level === "Easy");
  assert.deepEqual(two.stars.map((s) => s.earned), [true, true, false]);
  const fast = engage.mastery([f({ playerHpRemaining: 100, durationSec: cfg.clearWithinSec, full: 1 })]).find((l) => l.level === "Easy");
  assert.deepEqual(fast.stars.map((s) => s.earned), [true, false, true]);
  const power = engage.mastery([f({ playerHpRemaining: 100, durationSec: 999, full: 10 })]).find((l) => l.level === "Easy");
  assert.equal(power.stars[2].earned, true);
  assert.equal(power.best.fastestClearSec, 999);
  assert.equal(engage.mastery([]).length, 3);
});

test("calendar: Monday-aligned weeks ending today, future days omitted, kcal null when unknown", () => {
  const byDay = new Map([["2026-10-13", { sessions: 2, kcal: 7.25 }], ["2026-10-14", { sessions: 1, kcal: null }]]);
  const g = engage.calendarGrid(byDay, "2026-10-15", 2); // Thursday
  assert.equal(g.from, "2026-10-05"); // Monday of the previous week
  assert.equal(g.days[0].date, "2026-10-05");
  assert.equal(g.days[g.days.length - 1].date, "2026-10-15");
  assert.equal(g.days.length, 11);
  assert.equal(g.totals.sessions, 3);
  assert.equal(g.totals.activeDays, 2);
  assert.equal(g.days.find((d) => d.date === "2026-10-13").kcal, 7.3);
  assert.equal(g.days.find((d) => d.date === "2026-10-14").kcal, null);
  assert.equal(engage.clampWeeks(999), engage.CALENDAR_MAX_WEEKS);
  assert.equal(engage.clampWeeks("abc"), 26);
});

test("week summary and compare: only real figures, compare only when the previous week has a session", () => {
  const facts = [
    engage.sessionFacts(row({ id: "a", startedAt: "2026-10-13T04:00:00.000Z" }), ZONE),
    engage.sessionFacts(row({ id: "b", startedAt: "2026-10-14T04:00:00.000Z", level: "Medium", outcome: "defeat", peak: 7.1 }), ZONE),
    engage.sessionFacts(row({ id: "c", startedAt: "2026-10-06T04:00:00.000Z" }), ZONE),
  ];
  const w = engage.weekSummary(facts, "2026-10-12");
  assert.equal(w.sessions, 2);
  assert.equal(w.activeDays, 2);
  assert.deepEqual(w.levelsCleared, ["Easy"]);
  assert.equal(w.bestSwingSpeed, 7.1);
  assert.equal(w.fullSwings, 20);
  const prev = engage.weekSummary(facts, "2026-10-05");
  assert.equal(engage.weekCompare(w, prev).sessions, 1);
  assert.equal(engage.weekCompare(w, engage.weekSummary(facts, "2026-09-28")), null);
  assert.equal(engage.weekSummary([], "2026-10-12").estimatedCalories, null);
});

test("pickBest: decided fights with a score only; highest score, ties by shorter fight; 'before' excludes the session itself and later runs", () => {
  const s = (id, score, startedAt, durationSec = 60) => ({ id, score, startedAt, durationSec, status: "finished", outcome: "victory" });
  const runs = [s("a", 900, "2026-10-01T00:00:00Z"), s("b", 1200, "2026-10-02T00:00:00Z", 80), s("c", 1200, "2026-10-03T00:00:00Z", 50), s("d", 5000, "2026-10-04T00:00:00Z")];
  assert.equal(engage.pickBest(runs).id, "d");
  assert.equal(engage.pickBest(runs, { id: "d", startedAt: "2026-10-04T00:00:00Z" }).id, "c");
  assert.equal(engage.pickBest(runs, { id: "a", startedAt: "2026-10-01T00:00:00Z" }), null);
  assert.equal(engage.pickBest([{ ...s("x", 99999, "2026-10-01T00:00:00Z"), status: "live" }]), null);
  assert.equal(engage.pickBest([{ ...s("x", 99999, "2026-10-01T00:00:00Z"), outcome: "abandoned" }]), null);
  assert.equal(engage.pickBest([s("x", 0, "2026-10-01T00:00:00Z")]), null);
});

test("reactions: kinds validated; per-IP bucket 1 per 2 s with a burst of 5; global bucket 10 per second", () => {
  assert.equal(engage.validateReaction({ sessionId: "abc", kind: "fire" }).ok, true);
  assert.equal(engage.validateReaction({ sessionId: "abc", kind: "FIRE" }).value.kind, "fire");
  assert.equal(engage.validateReaction({ sessionId: "abc", kind: "heart" }).ok, false);
  assert.equal(engage.validateReaction({ kind: "fire" }).ok, false);
  assert.equal(engage.validateReaction(null).ok, false);
  let t = 0;
  const ip = engage.createBucket({ ...engage.REACTION_LIMITS.perIp, now: () => t });
  const allowed = Array.from({ length: 7 }, () => ip.take("1.2.3.4")).filter(Boolean).length;
  assert.equal(allowed, 5);
  assert.equal(ip.take("5.6.7.8"), true); // another IP has its own bucket
  t += 1999;
  assert.equal(ip.take("1.2.3.4"), false);
  t += 1;
  assert.equal(ip.take("1.2.3.4"), true);
  let g = 0;
  const global = engage.createBucket({ ...engage.REACTION_LIMITS.global, now: () => g });
  assert.equal(Array.from({ length: 15 }, () => global.take()).filter(Boolean).length, 10);
  g += 1000;
  assert.equal(Array.from({ length: 15 }, () => global.take()).filter(Boolean).length, 10);
  assert.equal(engage.reactionsEnabled({}), true);
  assert.equal(engage.reactionsEnabled({ REACTIONS_ENABLED: "false" }), false);
  assert.equal(engage.reactionsEnabled({ REACTIONS_ENABLED: "0" }), false);
  assert.equal(engage.reactionsEnabled({ REACTIONS_ENABLED: "true" }), true);
});
