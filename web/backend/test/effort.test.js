const test = require("node:test");
const assert = require("node:assert/strict");
const effort = require("../effort");

// A 90 s fight with swings every 10 s at the given speeds.
const swings = (speeds, step = 10) => speeds.map((speed, i) => ({ t: 5 + i * step, speed }));

test("fade compares the average speed of the last third with the first third", () => {
  // 90 s: first third = t < 30 -> swings at 5, 15, 25; last third = t >= 60 -> 65, 75, 85
  const hits = swings([6, 6, 6, 5, 5, 5, 3, 3, 3], 10).map((h, i) => ({ ...h, t: 5 + i * 10 }));
  const r = effort.fade(hits, 90);
  assert.equal(r.status, "ok");
  assert.equal(r.firstAvg, 6);
  assert.equal(r.lastAvg, 3);
  assert.equal(r.fadePct, -50);
  assert.equal(r.label, "slowed");
  assert.equal(r.labelText, "Slowed down");
  assert.equal(r.firstSwings, 3);
  assert.equal(r.lastSwings, 3);
});

test("fade is (last - first) / first, to one decimal", () => {
  const hits = swings([5, 5, 5, 0, 0, 0, 4.4, 4.4, 4.4]);
  assert.equal(effort.fade(hits, 90).fadePct, -12); // the "Fade -12%" example
});

test("labels: held steady inside +/-5%, got stronger above it", () => {
  assert.equal(effort.fade(swings([5, 5, 5, 5, 5, 5, 5.1, 5.1, 5.1]), 90).label, "steady");
  assert.equal(effort.fade(swings([5, 5, 5, 5, 5, 5, 4.8, 4.8, 4.8]), 90).labelText, "Held steady");
  const up = effort.fade(swings([4, 4, 4, 5, 5, 5, 6, 6, 6]), 90);
  assert.equal(up.label, "stronger");
  assert.equal(up.labelText, "Got stronger");
  assert.equal(up.fadePct, 50);
});

test("fewer than 6 swings is 'Not enough swings', never a made-up number", () => {
  const r = effort.fade(swings([6, 5, 4, 3, 2]), 60);
  assert.equal(r.status, "insufficient");
  assert.match(r.reason, /^Not enough swings/);
  assert.equal(r.fadePct, undefined);
  assert.equal(effort.fade([], 60).status, "insufficient");
  assert.equal(effort.fade(null, 60).status, "insufficient");
});

test("6 swings that all land in one third are still not enough", () => {
  const r = effort.fade([1, 2, 3, 4, 5, 6].map((t) => ({ t, speed: 5 })), 90);
  assert.equal(r.status, "insufficient");
  assert.match(r.reason, /third of the fight/);
});

test("speeds are clamped to the 8 m/s cap before averaging", () => {
  const r = effort.fade(swings([40, 40, 40, 8, 8, 8, 8, 8, 8]), 90);
  assert.equal(r.firstAvg, 8);
  assert.equal(r.fadePct, 0);
  assert.equal(r.label, "steady");
});

test("swings without a fight-clock time or speed are ignored", () => {
  const r = effort.fade([...swings([5, 5, 5, 5, 5, 5, 5, 5, 5]), { speed: 9 }, { t: 3 }, { t: NaN, speed: 4 }], 90);
  assert.equal(r.swings, 9);
});

test("a fight shorter than its last swing uses the swing time as the length", () => {
  const r = effort.fade(swings([5, 5, 5, 5, 5, 5, 5, 5, 5]), 10);
  assert.equal(r.status, "ok");
  assert.equal(r.lengthSec, 85);
});

test("averageFade ignores fights without enough swings", () => {
  const a = effort.fade(swings([6, 6, 6, 5, 5, 5, 3, 3, 3]), 90); // -50
  const b = effort.fade(swings([4, 4, 4, 5, 5, 5, 6, 6, 6]), 90); // +50
  const bad = effort.fade(swings([5, 5]), 60);
  const avg = effort.averageFade([a, b, bad]);
  assert.equal(avg.status, "ok");
  assert.equal(avg.fadePct, 0);
  assert.equal(avg.sessionsUsed, 2);
  assert.equal(avg.sessionsConsidered, 3);
  assert.equal(effort.averageFade([bad]).status, "insufficient");
  assert.equal(effort.averageFade([]).status, "insufficient");
});

// ---------------------------------------------------------------- fingerprint

const fp = (over = {}) =>
  effort.fingerprint({
    hits: Array.from({ length: 20 }, () => ({ speed: 4, defended: false })),
    fades: [effort.fade(swings([5, 5, 5, 5, 5, 5, 4.4, 4.4, 4.4]), 90)],
    hurt: 5,
    attacks: 20,
    ...over,
  });
const axis = (r, key) => r.axes.find((a) => a.key === key).score;

test("Power = average swing speed / the 8 m/s cap", () => {
  assert.equal(axis(fp(), "power"), 50); // 4 / 8
  assert.equal(axis(fp({ hits: Array.from({ length: 12 }, () => ({ speed: 40 })) }), "power"), 100); // clamped to the cap first
});

test("Consistency = 100 - (std dev / mean x 100), clamped", () => {
  assert.equal(axis(fp(), "consistency"), 100); // all swings identical
  const mixed = Array.from({ length: 10 }, (_, i) => ({ speed: i % 2 ? 6 : 2 })); // mean 4, std 2 -> 50% -> 50
  assert.equal(axis(fp({ hits: mixed }), "consistency"), 50);
  const wild = Array.from({ length: 10 }, (_, i) => ({ speed: i === 0 ? 8 : 0.1 })); // std/mean > 1 -> clamped at 0
  assert.equal(axis(fp({ hits: wild }), "consistency"), 0);
});

test("Stamina = 100 + average fade %, clamped to 0-100", () => {
  assert.equal(axis(fp(), "stamina"), 88); // fade -12
  const up = effort.fade(swings([4, 4, 4, 5, 5, 5, 6, 6, 6]), 90); // +50
  assert.equal(axis(fp({ fades: [up] }), "stamina"), 100); // getting stronger cannot exceed 100
  const down = effort.fade(swings([8, 8, 8, 5, 5, 5, 0.4, 0.4, 0.4]), 90); // -95
  assert.equal(axis(fp({ fades: [down] }), "stamina"), 5);
  assert.equal(axis(fp({ fades: [effort.fade([], 60)] }), "stamina"), null);
});

test("Dodge = 1 - (hits taken / monster attacks), clamped, and omitted without enough attacks", () => {
  assert.equal(axis(fp(), "dodge"), 75); // 5 hurt in 20 attacks
  assert.equal(axis(fp({ hurt: 0 }), "dodge"), 100);
  assert.equal(axis(fp({ hurt: 40 }), "dodge"), 0); // a double cleave can hurt twice: never below 0
  assert.equal(axis(fp({ attacks: 3 }), "dodge"), null);
});

test("Guard timing = share of hits that landed while the monster was not guarding", () => {
  const hits = Array.from({ length: 20 }, (_, i) => ({ speed: 4, defended: i < 5 })); // 5 of 20 guarded
  assert.equal(axis(fp({ hits }), "guard"), 75);
  const noFlag = Array.from({ length: 20 }, () => ({ speed: 4 }));
  const r = fp({ hits: noFlag });
  assert.equal(axis(r, "guard"), null);
  assert.ok(r.omitted.includes("guard")); // the axis is dropped, not shown as 0
});

test("not enough data: no fake axes", () => {
  const r = effort.fingerprint({ hits: [{ speed: 5 }], fades: [], hurt: 0, attacks: 0 });
  assert.equal(r.status, "insufficient");
  assert.ok(r.axes.every((a) => a.score === null));
  assert.match(r.reason, /Not enough data/);
  assert.equal(effort.fingerprint({}).status, "insufficient");
});

test("every axis documents its formula", () => {
  assert.equal(effort.AXES.length, 5);
  for (const a of effort.AXES) assert.ok(a.formula.length > 10 && a.label);
});

// ---------------------------------------------------------------- Thesis Lab statistics

test("pearson r: perfect, inverse, none, and undefined cases", () => {
  assert.equal(effort.pearson([1, 2, 3, 4], [2, 4, 6, 8]), 1);
  assert.equal(effort.pearson([1, 2, 3, 4], [8, 6, 4, 2]), -1);
  assert.ok(Math.abs(effort.pearson([1, 2, 3, 4, 5], [2, 1, 4, 3, 5]) - 0.8) < 1e-9);
  assert.equal(effort.pearson([1, 2, 3], [5, 5, 5]), null); // no spread in y
  assert.equal(effort.pearson([1], [2]), null);
  assert.equal(effort.pearson([], []), null);
});

test("RPE vs swing speed needs at least 8 paired points before it reports an r", () => {
  const pts = (n) => Array.from({ length: n }, (_, i) => ({ rpe: 8 + i, avgSpeed: 2 + i * 0.5 }));
  const seven = effort.rpeCorrelation(pts(7));
  assert.equal(seven.status, "insufficient");
  assert.equal(seven.r, null);
  assert.equal(seven.n, 7);
  const eight = effort.rpeCorrelation(pts(8));
  assert.equal(eight.status, "ok");
  assert.equal(eight.r, 1);
  assert.equal(eight.minPairs, 8);
  // points missing either value are not pairs
  assert.equal(effort.rpeCorrelation([...pts(7), { rpe: 12, avgSpeed: null }, { rpe: null, avgSpeed: 3 }]).n, 7);
  assert.equal(effort.rpeCorrelation([]).status, "insufficient");
});

const DAY = 864e5;
const T0 = Date.parse("2026-10-01T10:00:00Z");
const at = (days) => new Date(T0 + days * DAY).toISOString();

test("return rate: second fight within 1, 3 and 7 days of the first", () => {
  const sessions = [
    { player: "a", startedAt: at(0) }, { player: "a", startedAt: at(0.5) }, // back the same day
    { player: "b", startedAt: at(0) }, { player: "b", startedAt: at(2) }, // back after 2 days
    { player: "c", startedAt: at(0) }, { player: "c", startedAt: at(6) }, // back after 6 days
    { player: "d", startedAt: at(0) }, // never came back
  ];
  const r = effort.returnRates(sessions, T0 + 30 * DAY);
  const w = Object.fromEntries(r.windows.map((x) => [x.days, x]));
  assert.deepEqual([w[1].n, w[1].returned, w[1].ratePct], [4, 1, 25]);
  assert.deepEqual([w[3].n, w[3].returned, w[3].ratePct], [4, 2, 50]);
  assert.deepEqual([w[7].n, w[7].returned, w[7].ratePct], [4, 3, 75]);
  assert.equal(r.players, 4);
});

test("return rate: a player whose first fight is too recent is not counted as 'did not return'", () => {
  const sessions = [{ player: "old", startedAt: at(0) }, { player: "old", startedAt: at(1) }, { player: "new", startedAt: at(9) }];
  const r = effort.returnRates(sessions, T0 + 10 * DAY); // "new" started 1 day ago: too early for the 3- and 7-day windows
  const w = Object.fromEntries(r.windows.map((x) => [x.days, x]));
  assert.equal(w[1].n, 2); // first fight at least 1 day old: old and new (exactly 1 day)
  assert.equal(w[3].n, 1);
  assert.equal(w[7].n, 1);
  assert.equal(effort.returnRates([], T0).windows[0].ratePct, null);
});

test("return rate flags a small sample below 10 players", () => {
  const nine = Array.from({ length: 9 }, (_, i) => ({ player: `p${i}`, startedAt: at(0) }));
  assert.equal(effort.returnRates(nine, T0 + 30 * DAY).windows[0].smallSample, true);
  const ten = Array.from({ length: 10 }, (_, i) => ({ player: `p${i}`, startedAt: at(0) }));
  assert.equal(effort.returnRates(ten, T0 + 30 * DAY).windows[0].smallSample, false);
  assert.equal(effort.SMALL_SAMPLE, 10);
});
