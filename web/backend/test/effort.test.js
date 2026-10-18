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
