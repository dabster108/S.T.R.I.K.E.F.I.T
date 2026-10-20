const test = require("node:test");
const assert = require("node:assert/strict");
const wrapped = require("../wrapped");
const effort = require("../effort");

const ZONE = "Asia/Kathmandu";
let n = 0;
// day = the LOCAL start day (the facts carry it); startedAt only orders fights
const fact = (day, over = {}) => ({
  id: `w${++n}`, playerName: "Ava", level: "Easy", outcome: "victory", day, startedAt: `${day}T0${(n % 9) + 1}:00:00.000Z`, endedAt: `${day}T0${(n % 9) + 1}:05:00.000Z`,
  swings: 100, tieredSwings: 90, fullSwings: 30, activeMinutes: 2, peakSpeed: 6.5, damageDealt: 400, xp: 300, durationSec: 60, ...over,
});
const build = (facts, extra = {}) => wrapped.buildWrapped({ playerName: "Ava", facts, weekStart: "2026-10-12", zone: ZONE, now: new Date("2026-10-14T06:00:00Z"), ...extra });

test("the week runs Monday to Sunday and only counts fights that started in it", () => {
  const r = build([fact("2026-10-11"), fact("2026-10-12"), fact("2026-10-18"), fact("2026-10-19")]);
  assert.deepEqual([r.weekStart, r.weekEnd, r.fights], ["2026-10-12", "2026-10-18", 2]);
});

test("the default week is the current one in Kathmandu: Sunday evening UTC is already Monday there", () => {
  // 2026-10-18 is a Sunday; 20:00 UTC is 01:45 on Monday the 19th in Kathmandu (+05:45)
  assert.equal(wrapped.currentWeekStart(new Date("2026-10-18T20:00:00Z"), ZONE), "2026-10-19");
  assert.equal(wrapped.currentWeekStart(new Date("2026-10-18T17:00:00Z"), ZONE), "2026-10-12"); // 22:45 Sunday: still last week
  assert.equal(wrapped.currentWeekStart(new Date("2026-10-14T06:00:00Z"), ZONE), "2026-10-12");
});

test("weekStart is validated and normalised to a Monday", () => {
  assert.deepEqual(wrapped.parseWeekStart(undefined), { ok: true, value: null });
  assert.deepEqual(wrapped.parseWeekStart(""), { ok: true, value: null });
  assert.equal(wrapped.parseWeekStart("2026-10-15").value, "2026-10-12"); // a Thursday: its week's Monday
  assert.equal(wrapped.parseWeekStart("2026-10-12").value, "2026-10-12");
  assert.equal(wrapped.parseWeekStart("2026-10-18").value, "2026-10-12"); // a Sunday belongs to the week before the next Monday
  for (const bad of ["tomorrow", "2026-13-01", "2026-02-30", "12-10-2026", "2019-01-01", ["2026-10-12"], 20261012]) assert.equal(wrapped.parseWeekStart(bad).ok, false, String(bad));
});

test("fights, wins and the win rate follow the shared rule (abandoned fights are not decided)", () => {
  const r = build([fact("2026-10-12"), fact("2026-10-13"), fact("2026-10-13", { outcome: "defeat" }), fact("2026-10-14", { outcome: "abandoned" }), fact("2026-10-15", { outcome: "timeout" })]);
  assert.deepEqual([r.fights, r.decidedFights, r.wins, r.defeats, r.abandoned, r.winRate], [5, 3, 2, 1, 1, 66.7]);
  assert.equal(build([fact("2026-10-14", { outcome: "abandoned" })]).winRate, null); // nothing decided: no rate, never a made-up 0
});

test("swings add up, the fastest swing is clamped to 8 and the full-power share is full over tiered", () => {
  const r = build([fact("2026-10-12", { swings: 50, tieredSwings: 40, fullSwings: 10, peakSpeed: 7.2 }), fact("2026-10-13", { swings: 70, tieredSwings: 60, fullSwings: 30, peakSpeed: 11.6 })]);
  assert.equal(r.swings, 120);
  assert.equal(r.fastestSwing, 8);
  assert.equal(r.fullPowerShare, 0.4);
  assert.equal(r.fullPowerSwings, 40);
  assert.equal(build([fact("2026-10-12", { tieredSwings: 0, fullSwings: 0, peakSpeed: null })]).fastestSwing, null);
  assert.equal(build([fact("2026-10-12", { tieredSwings: 0, fullSwings: 0 })]).fullPowerShare, null);
  assert.equal(build([fact("2026-10-12", { tieredSwings: 10, fullSwings: 25 })]).fullPowerShare, 1); // never over 100%
});

test("active minutes and XP are summed", () => {
  const r = build([fact("2026-10-12", { activeMinutes: 3, xp: 120.4 }), fact("2026-10-13", { activeMinutes: 4, xp: 200.4 })]);
  assert.deepEqual([r.activeMinutes, r.xpGained], [7, 321]);
});

test("the best day is the one with the most swings, ties to the earlier day", () => {
  assert.deepEqual(wrapped.bestDay([fact("2026-10-12", { swings: 40 }), fact("2026-10-14", { swings: 30 }), fact("2026-10-14", { swings: 50 })]), { day: "2026-10-14", swings: 80 });
  assert.deepEqual(wrapped.bestDay([fact("2026-10-15", { swings: 60 }), fact("2026-10-13", { swings: 60 })]), { day: "2026-10-13", swings: 60 });
  assert.equal(wrapped.bestDay([fact("2026-10-12", { swings: 0 })]), null);
});

test("the favourite monster is the level fought most; a tie goes to the one that took more damage", () => {
  const fav = (facts) => wrapped.favouriteMonster(facts);
  assert.deepEqual(fav([fact("2026-10-12", { level: "Medium" }), fact("2026-10-13", { level: "Medium" }), fact("2026-10-14", { level: "Hard" })]).level, "Medium");
  const tie = fav([fact("2026-10-12", { level: "Easy", damageDealt: 400 }), fact("2026-10-13", { level: "Hard", damageDealt: 900 })]);
  assert.deepEqual([tie.level, tie.levelNumber, tie.fights, tie.damageDealt], ["Hard", 3, 1, 900]);
  assert.equal(fav([]), null);
});

test("the longest win streak ignores abandoned fights and breaks on a defeat", () => {
  const f = (outcome, i) => fact("2026-10-12", { outcome, startedAt: `2026-10-12T0${i}:00:00.000Z` });
  assert.equal(wrapped.longestWinStreak([f("victory", 1), f("victory", 2), f("abandoned", 3), f("victory", 4), f("defeat", 5), f("victory", 6)]), 3);
  assert.equal(wrapped.longestWinStreak([f("defeat", 1)]), 0);
  assert.equal(wrapped.longestWinStreak([f("victory", 3), f("defeat", 2), f("victory", 1)]), 1); // play order, not list order
});

test("the average fade needs enough swings, and says nothing otherwise", () => {
  const hits = (speeds) => speeds.map((speed, i) => ({ t: i * 5, speed }));
  const ok = effort.fade(hits([6, 6, 6, 5, 5, 5, 4, 4, 4]), 45);
  assert.equal(build([fact("2026-10-12")], { fades: [ok] }).averageFade.sessionsUsed, 1);
  assert.equal(build([fact("2026-10-12")], { fades: [effort.fade(hits([6]), 5)] }).averageFade, null);
  assert.equal(build([fact("2026-10-12")]).averageFade, null);
});

const fp = (scores) => ({ status: "ok", axes: [["power", "Power"], ["consistency", "Consistency"], ["stamina", "Stamina"], ["dodge", "Dodge"], ["guard", "Guard timing"]].map(([key, label], i) => ({ key, label, score: scores[i] })) });

test("the personality is named after the highest fingerprint axis", () => {
  const cases = [[[90, 60, 50, 40, 30], "The Hammer"], [[30, 90, 50, 40, 60], "The Metronome"], [[30, 40, 90, 50, 60], "The Marathoner"], [[30, 40, 50, 90, 60], "The Shadow"], [[30, 40, 50, 60, 90], "The Tactician"]];
  for (const [scores, name] of cases) assert.equal(wrapped.personalityFor(fp(scores)).name, name);
});

test("the top two axes within 5 points make an All-Rounder; 6 apart do not", () => {
  const close = wrapped.personalityFor(fp([80, 75, 40, 30, 20]));
  assert.deepEqual([close.name, close.key], ["The All-Rounder", "all_rounder"]);
  assert.match(close.reason, /Power \(80\).*Consistency \(75\)/);
  assert.equal(wrapped.personalityFor(fp([80, 74, 40, 30, 20])).name, "The Hammer");
  assert.equal(wrapped.personalityFor(fp([70, 70, 40, 30, 20])).name, "The All-Rounder"); // equal
});

test("an axis with no data cannot name the personality", () => {
  assert.equal(wrapped.personalityFor(fp([null, null, 55, 80, 20])).name, "The Shadow");
});

test("not enough data is Warming Up, never a guess", () => {
  const p = wrapped.personalityFor({ status: "insufficient", reason: "Not enough data yet", axes: [] });
  assert.deepEqual([p.status, p.name], ["insufficient", "Warming Up"]);
  assert.equal(wrapped.personalityFor(null).name, "Warming Up");
  assert.equal(build([]).personality.name, "Warming Up"); // an empty week
});

test("a week with no fights is empty, not zeros that look like results", () => {
  const r = build([fact("2026-10-05")]); // a fight, but in another week
  assert.equal(r.fights, 0);
  assert.deepEqual([r.winRate, r.fastestSwing, r.bestDay, r.favouriteMonster, r.fullPowerShare, r.averageFade], [null, null, null, null, null, null]);
  assert.equal(r.longestWinStreak, 0);
});

test("the picker lists the weeks that have fights, newest first, and the neighbours of the chosen week", () => {
  const r = build([fact("2026-09-29"), fact("2026-10-06"), fact("2026-10-07"), fact("2026-10-13")]);
  assert.deepEqual(r.weeks.map((w) => [w.weekStart, w.fights]), [["2026-10-12", 1], ["2026-10-05", 2], ["2026-09-28", 1]]);
  assert.deepEqual([r.previousWeek, r.nextWeek, r.isCurrentWeek], ["2026-10-05", null, true]);
  const old = build([fact("2026-10-06")], { weekStart: "2026-10-05" });
  assert.deepEqual([old.nextWeek, old.isCurrentWeek], ["2026-10-12", false]);
});

test("the answer holds the pseudonym and numbers only", () => {
  const r = build([fact("2026-10-12")]);
  const flat = JSON.stringify(r);
  assert.equal(r.playerName, "Ava");
  for (const key of ["email", "ip", "device", "birth", "address"]) assert.ok(!flat.toLowerCase().includes(`"${key}`), key);
});
