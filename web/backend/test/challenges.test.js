// Challenge rules against fixed fixtures (no database, no clock). Run: npm test
const test = require("node:test");
const assert = require("node:assert/strict");
const ch = require("../challenges");

const ZONE = ch.safeZone("Asia/Kathmandu");
const fact = (extra = {}) => ({ level: "Easy", outcome: "defeat", fullSwings: 0, kcal: null, activeMinutes: 0, bestCombo: null, damagePercent: null, ...extra });
const periods = (day) => ch.periodsAt(new Date(`${day}T06:00:00Z`), ZONE);
const byId = (id) => ch.CHALLENGES.find((c) => c.id === id);

test("config: ids unique, metrics and periods valid, every daily tier has a pool, bonus XP positive", () => {
  const ids = ch.CHALLENGES.map((c) => c.id);
  assert.equal(new Set(ids).size, ids.length);
  for (const c of ch.CHALLENGES) {
    assert.ok(ch.METRICS.includes(c.metric), c.id);
    assert.ok(["daily", "weekly"].includes(c.period), c.id);
    if (c.period === "daily") assert.ok(ch.DAILY_TIERS.includes(c.tier), c.id);
    assert.ok(c.bonusXp > 0, c.id);
    assert.ok(c.target > 0, c.id);
  }
  for (const tier of ch.DAILY_TIERS) assert.ok(ch.CHALLENGES.some((c) => c.period === "daily" && c.tier === tier), tier);
});

test("rotation is deterministic: the same date always gives the same 3 daily + 1 weekly", () => {
  const a = ch.rotationFor("2026-10-15");
  const b = ch.rotationFor("2026-10-15");
  assert.deepEqual(a.daily.map((c) => c.id), b.daily.map((c) => c.id));
  assert.deepEqual(a.weekly.map((c) => c.id), b.weekly.map((c) => c.id));
  assert.equal(a.daily.length, 3);
  assert.deepEqual(a.daily.map((c) => c.tier), ["easy", "medium", "stretch"]);
  assert.equal(a.weekly.length, 1);
});

test("rotation: one weekly challenge for the whole Monday-to-Sunday week, and the days do vary", () => {
  const week = ["2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15", "2026-10-16", "2026-10-17", "2026-10-18"];
  const weekly = new Set(week.map((d) => ch.rotationFor(d).weekly[0].id));
  assert.equal(weekly.size, 1);
  assert.equal(ch.rotationFor("2026-10-18").weekStart, "2026-10-12");
  assert.equal(ch.rotationFor("2026-10-19").weekStart, "2026-10-19");
  const dailySets = new Set();
  for (let i = 0; i < 30; i++) dailySets.add(ch.rotationFor(new Date(Date.UTC(2026, 9, 1 + i)).toISOString().slice(0, 10)).daily.map((c) => c.id).join(","));
  assert.ok(dailySets.size > 3, `only ${dailySets.size} distinct daily sets in 30 days`);
});

test("rotation: the three daily cards ask for different metrics when the pools allow it", () => {
  for (let i = 0; i < 60; i++) {
    const day = new Date(Date.UTC(2026, 8, 1 + i)).toISOString().slice(0, 10);
    const metrics = ch.rotationFor(day).daily.map((c) => c.metric);
    assert.equal(new Set(metrics).size, 3, `${day}: ${metrics}`);
  }
});

test("day boundary: Asia/Kathmandu midnight (18:15Z) moves the day, the daily key and the rotation seed", () => {
  const before = ch.periodsAt(new Date("2026-10-14T18:14:00Z"), ZONE);
  const after = ch.periodsAt(new Date("2026-10-14T18:15:00Z"), ZONE);
  assert.equal(before.day, "2026-10-14");
  assert.equal(after.day, "2026-10-15");
  const c = byId("d-show-up");
  assert.equal(ch.challengeKey(c.id, ch.periodStartFor(c, before)), "d-show-up@2026-10-14");
  assert.equal(ch.challengeKey(c.id, ch.periodStartFor(c, after)), "d-show-up@2026-10-15");
  // Sunday 23:59 local and Monday 00:00 local are different weeks.
  assert.equal(ch.periodsAt(new Date("2026-10-18T18:14:00Z"), ZONE).weekStart, "2026-10-12");
  assert.equal(ch.periodsAt(new Date("2026-10-18T18:15:00Z"), ZONE).weekStart, "2026-10-19");
});

test("caps: every target is clamped whatever the median says", () => {
  assert.equal(ch.clampToCaps("sessions", "daily", 10), ch.SAFETY_CAPS.daily.sessions.max);
  assert.equal(ch.clampToCaps("activeMinutes", "daily", 500), ch.SAFETY_CAPS.daily.activeMinutes.max);
  assert.equal(ch.clampToCaps("kcal", "daily", 0.2), ch.SAFETY_CAPS.daily.kcal.min);
  assert.equal(ch.clampToCaps("sessions", "weekly", 40), ch.SAFETY_CAPS.weekly.sessions.max);
  assert.equal(ch.clampToCaps("clearLevel", "daily", 9), 1);
  assert.equal(ch.clampDamagePercent(5), ch.DAMAGE_PERCENT_CAP.min);
  // A huge median still lands on the cap.
  const medians = { daily: { source: "data", values: { fullSwings: 5000, activeMinutes: 900 } }, weekly: { source: "data", values: {} } };
  assert.equal(ch.targetFor(byId("d-full-power"), medians).target, ch.SAFETY_CAPS.daily.fullSwings.max);
  assert.equal(ch.targetFor(byId("d-active-stretch"), medians).target, ch.SAFETY_CAPS.daily.activeMinutes.max);
  for (const c of ch.CHALLENGES) {
    const t = ch.targetFor(c, medians).target;
    const cap = ch.SAFETY_CAPS[c.period][c.metric];
    if (cap) assert.ok(t >= cap.min && t <= cap.max, `${c.id}: ${t}`);
  }
});

test("targets: scaled from real medians, fixed fallback (labelled) without data, no-scale challenges are fixed", () => {
  const medians = { daily: { source: "data", values: { fullSwings: 40 } }, weekly: { source: "fallback", values: {} } };
  assert.deepEqual(ch.targetFor(byId("d-full-power"), medians), { target: 40, targetSource: "data", median: 40 });
  assert.equal(ch.targetFor(byId("d-full-stretch"), medians).target, 60);
  assert.equal(ch.targetFor(byId("w-full-power"), medians).targetSource, "fallback");
  assert.equal(ch.targetFor(byId("w-full-power"), medians).target, 60);
  assert.equal(ch.targetFor(byId("d-show-up"), medians).targetSource, "fixed");
  assert.equal(ch.targetFor(byId("d-combo"), { daily: { source: "data", values: { bestCombo: null } } }).targetSource, "fallback");
});

test("medianBasis: only players with 3+ active days, only days before the cutoff, needs 2 such players", () => {
  const row = (playerName, day, fullSwings) => ({ playerName, day, fullSwings, kcal: 10, sessions: 1, activeMinutes: 2, bestCombo: null });
  const rows = [
    row("A", "2026-10-01", 10), row("A", "2026-10-02", 20), row("A", "2026-10-03", 30),
    row("B", "2026-10-01", 40), row("B", "2026-10-02", 50), row("B", "2026-10-03", 60),
    row("C", "2026-10-01", 999), // 1 active day: not eligible
    row("A", "2026-10-15", 5000), // on the cutoff day: ignored
  ];
  const basis = ch.medianBasis(rows, "2026-10-15");
  assert.equal(basis.source, "data");
  assert.equal(basis.playersUsed, 2);
  assert.equal(basis.daily.fullSwings, 35);
  assert.equal(basis.daily.bestCombo, null);
  assert.equal(ch.medianBasis(rows.filter((r) => r.playerName !== "B"), "2026-10-15").source, "fallback");
});

test("progress: every metric, computed on read", () => {
  const p = periods("2026-10-15");
  const facts = [
    fact({ fullSwings: 12, kcal: 4.5, activeMinutes: 2, bestCombo: 6 }),
    fact({ level: "Medium", outcome: "victory", fullSwings: 9, kcal: 6, activeMinutes: 3, bestCombo: 11, damagePercent: 45 }),
  ];
  const ev = (id) => ch.evaluate(byId(id), facts, { periods: p, medians: null });
  assert.equal(ev("d-full-power").value, 21);
  assert.equal(ev("d-burn").value, 10.5);
  assert.equal(ev("d-show-up").value, 2);
  assert.equal(ev("d-move-a-bit").value, 5);
  assert.equal(ev("d-combo").value, 11);
  assert.equal(ev("d-clear-l2").met, true);
  assert.equal(ev("d-clear-l1").met, false);
  assert.equal(ev("d-guarded-win").met, true); // 45% <= 50%
  assert.equal(ev("w-careful-win").met, false); // 45% > 40%
  assert.equal(ev("d-burn").description, "Reach 10 kcal (estimated)");
  assert.equal(ev("d-show-up").resets, "Resets tomorrow");
  assert.equal(ev("w-three-fights").resets, "Resets on Monday");
});

test("progress: no combo data says tracking, an unmeasured win says unmeasured, never a made-up number", () => {
  const p = periods("2026-10-15");
  const combo = ch.evaluate(byId("d-combo"), [fact({ fullSwings: 3 })], { periods: p });
  assert.equal(combo.value, 0);
  assert.equal(combo.tracking, true);
  const dmg = ch.evaluate(byId("d-guarded-win"), [fact({ outcome: "victory" })], { periods: p });
  assert.equal(dmg.met, false);
  assert.equal(dmg.unmeasured, true);
});

test("idempotent completion: a recorded key is never 'newly met' again, however often it is evaluated", () => {
  const p = periods("2026-10-15");
  const facts = [fact({ fullSwings: 50 })];
  const card = ch.evaluate(byId("d-full-power"), facts, { periods: p });
  assert.equal(card.met, true);
  assert.equal(ch.newlyMet([card]).length, 1);
  const completions = new Map([[card.key, { completedAt: "2026-10-15T05:00:00.000Z", bonusXp: card.bonusXp }]]);
  for (let i = 0; i < 3; i++) {
    const again = ch.evaluate(byId("d-full-power"), facts, { periods: p, completions });
    assert.equal(again.completed, true);
    assert.equal(ch.newlyMet([again]).length, 0);
  }
  // The next day is a new key, so it can be earned again then.
  const tomorrow = ch.evaluate(byId("d-full-power"), facts, { periods: periods("2026-10-16"), completions });
  assert.equal(tomorrow.key, "d-full-power@2026-10-16");
  assert.equal(tomorrow.completed, false);
});

test("rest note: 5+ consecutive active days (ending today or yesterday) shows it; a gap resets the count", () => {
  const days = ["2026-10-11", "2026-10-12", "2026-10-13", "2026-10-14", "2026-10-15"];
  assert.equal(ch.consecutiveActiveDays(days, "2026-10-15"), 5);
  assert.equal(ch.restNote(days, "2026-10-15").show, true);
  assert.equal(ch.restNote(days, "2026-10-16").show, true); // nothing yet today: yesterday's run still counts
  assert.equal(ch.restNote(days, "2026-10-17").consecutiveActiveDays, 0);
  assert.equal(ch.restNote(days.slice(1), "2026-10-15").show, false);
  assert.equal(ch.restNote(["2026-10-10", ...days.slice(1)], "2026-10-15").consecutiveActiveDays, 4);
  assert.equal(ch.SAFETY_LINE, "Stop if you feel dizzy or in pain.");
});
