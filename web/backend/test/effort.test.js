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
