import test from "node:test";
import assert from "node:assert/strict";
import { buildSlides, dayLabel, exportFileName, hasFights, keyAction, stepIndex, weekLabel, weekParam, wrappedPath } from "./wrapped.js";

const full = {
  fights: 5, decidedFights: 4, wins: 3, defeats: 1, winRate: 75, swings: 1232, xpGained: 5400, activeMinutes: 9, longestWinStreak: 3,
  fastestSwing: 8, speedCap: 8, fullPowerShare: 0.31, averageFade: { fadePct: -4.2, label: "steady", labelText: "Held steady", sessionsUsed: 4 },
  bestDay: { day: "2026-10-15", swings: 440 }, favouriteMonster: { level: "Hard", levelNumber: 3, fights: 3, damageDealt: 8324.5 },
  personality: { status: "ok", key: "stamina", name: "The Marathoner", reason: "Stamina is the highest of your axes (98).", axes: [{ key: "power", label: "Power", score: 49 }, { key: "stamina", label: "Stamina", score: 98 }, { key: "dodge", label: "Dodge", score: null }] },
};

test("week labels, including across a month and a year", () => {
  assert.equal(weekLabel("2026-10-12", "2026-10-18"), "12 to 18 Oct 2026");
  assert.equal(weekLabel("2026-09-28", "2026-10-04"), "28 Sep to 4 Oct 2026");
  assert.equal(weekLabel("2026-12-28", "2027-01-03"), "28 Dec 2026 to 3 Jan 2027");
  assert.equal(dayLabel("2026-10-15"), "Thursday 15 Oct");
});

test("five slides in a fixed order, each with a text alternative", () => {
  const slides = buildSlides(full, { monster: { name: "The Harbor Tyrant", weapon: "Twin axes" } });
  assert.deepEqual(slides.map((s) => s.id), ["swings", "fastest", "monster", "bestDay", "personality"]);
  for (const s of slides) assert.ok(s.alt.length > 20, s.id);
  assert.deepEqual([slides[0].value, slides[0].unit], ["1,232", "swings counted"]);
  assert.deepEqual([slides[1].value, slides[1].unit, slides[1].headline], ["8", "m/s", "Right at the game's limit."]);
  assert.equal(slides[2].value, "The Harbor Tyrant");
  assert.match(slides[2].lines.join("|"), /8,325 damage dealt/);
  assert.equal(slides[3].value, "Thursday 15 Oct");
  assert.equal(slides[4].value, "The Marathoner");
  assert.deepEqual(slides[4].axes.map((a) => a.key), ["power", "stamina"]); // an axis with no score is left off, never drawn as 0
});

test("a week with no fights has no slides", () => {
  assert.equal(hasFights({ fights: 0 }), false);
  assert.deepEqual(buildSlides({ fights: 0 }), []);
  assert.deepEqual(buildSlides(null), []);
});

test("missing data says so instead of showing zeros", () => {
  const thin = { ...full, fastestSwing: null, fullPowerShare: null, averageFade: null, bestDay: null, favouriteMonster: null, personality: { status: "insufficient", name: "Warming Up", reason: "x", axes: [] }, longestWinStreak: 0, decidedFights: 0, winRate: null };
  const s = buildSlides(thin);
  assert.equal(s[1].value, "—");
  assert.match(s[1].headline, /No swing speed/);
  assert.match(s[1].lines.join(" "), /not measured/);
  assert.match(s[1].lines.join(" "), /Not enough swings/);
  assert.equal(s[2].value, "—");
  assert.equal(s[3].value, "—");
  assert.equal(s[4].waiting, true);
  assert.equal(s[4].value, "Warming Up");
  assert.equal(s[0].lines[1], "No decided fights");
  assert.equal(s[0].note, null);
});

test("singular and plural wording", () => {
  const one = buildSlides({ ...full, fights: 1, swings: 1 });
  assert.equal(one[0].unit, "swing counted");
  assert.equal(one[0].lines[0], "1 fight");
});

test("story controls: the arrow keys step without wrapping", () => {
  assert.equal(stepIndex(0, -1, 5), 0);
  assert.equal(stepIndex(4, 1, 5), 4);
  assert.equal(stepIndex(2, 1, 5), 3);
  assert.deepEqual(["ArrowRight", "ArrowLeft", "ArrowDown", "PageUp", "a", "Enter"].map(keyAction), ["next", "prev", "next", "prev", null, null]);
});

test("links, the week parameter and the file name", () => {
  assert.equal(wrappedPath("Ava"), "/wrapped/Ava");
  assert.equal(wrappedPath("Ava B/1", "2026-10-12"), "/wrapped/Ava%20B%2F1?week=2026-10-12");
  assert.equal(weekParam("?week=2026-10-12"), "2026-10-12");
  assert.equal(weekParam("?week=soon"), null);
  assert.equal(weekParam(""), null);
  assert.equal(exportFileName("Ava B/1", "2026-10-12", "swings"), "strikefit-wrapped-Ava_B_1-2026-10-12-swings.png");
});
