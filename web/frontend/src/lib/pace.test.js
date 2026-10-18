import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { cumulativeDamage, damageAt, liveNow, paceChip, paceDelta } from "./pace.js";

const backend = createRequire(import.meta.url)("../../../backend/effort.js");

test("the web curve is built exactly like the backend one", () => {
  let seed = 11;
  const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  for (let i = 0; i < 100; i++) {
    const hits = Array.from({ length: Math.floor(rnd() * 400) }, () => ({ t: rnd() * 120, damage: rnd() > 0.1 ? rnd() * 60 : 0 }));
    assert.deepEqual(cumulativeDamage(hits), backend.cumulativeDamage(hits));
    const pts = cumulativeDamage(hits);
    for (const t of [0, 1, 17.3, 60, 200]) assert.equal(damageAt(pts, t), backend.damageAt(pts, t));
  }
});

test("ahead / behind chip", () => {
  const ghost = cumulativeDamage([{ t: 10, damage: 100 }, { t: 20, damage: 100 }, { t: 30, damage: 100 }]);
  const live = cumulativeDamage([{ t: 8, damage: 150 }, { t: 18, damage: 90 }]);
  // at t=20: live 240, ghost 200
  assert.equal(paceDelta(live, ghost, 20), 40);
  assert.deepEqual(paceChip(140), { tone: "ahead", text: "Ahead of your best by 140 dmg" });
  assert.deepEqual(paceChip(-90.4), { tone: "behind", text: "Behind by 90 dmg" });
  assert.deepEqual(paceChip(0.2), { tone: "level", text: "Level with your best" });
  // time passing with no hit makes the live line fall behind a ghost that keeps scoring: at t=30 ghost 300, live still 240
  assert.equal(paceDelta(live, ghost, 30), -60);
});

test("no ghost, no chip", () => {
  assert.equal(paceDelta(cumulativeDamage([]), null, 5), null);
  assert.equal(paceDelta(cumulativeDamage([]), [{ t: 0, damage: 0 }], 5), null); // a ghost with no hits is not a ghost
  assert.equal(paceChip(null), null);
});

test("liveNow uses the fight clock or the last hit, whichever is later", () => {
  const pts = cumulativeDamage([{ t: 12, damage: 5 }]);
  assert.equal(liveNow(9, pts), 12);
  assert.equal(liveNow(20, pts), 20);
  assert.equal(liveNow(undefined, []), 0);
});
