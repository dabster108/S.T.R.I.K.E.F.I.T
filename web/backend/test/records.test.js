const test = require("node:test");
const assert = require("node:assert/strict");
const records = require("../records");
const effort = require("../effort");

let n = 0;
const row = (over = {}, values = {}) => ({
  id: `s${++n}`,
  level: "Easy",
  outcome: "victory",
  startedAt: new Date(Date.UTC(2026, 9, 1, 10, n)).toISOString(),
  ...over,
  values: { peakSwingSpeed: 6, damageDealt: 400, score: 900, fade: -10, ...values },
});

test("a first fight on a level gives no record", () => {
  const a = row();
  assert.deepEqual(records.newRecordsFor(a.id, [a]), []);
  // another level's history does not count as something to beat
  const hard = row({ level: "Hard" }, { score: 99999 });
  const b = row({}, { score: 5000 });
  assert.deepEqual(records.newRecordsFor(b.id, [hard, b]), []);
});

test("beating an earlier fight on the same level is a record, with the value it beat", () => {
  const a = row({}, { peakSwingSpeed: 6.2, damageDealt: 400, score: 900, fade: -10 });
  const b = row({}, { peakSwingSpeed: 7.9, damageDealt: 500, score: 1400, fade: -3 });
  assert.deepEqual(records.newRecordsFor(b.id, [a, b]), [
    { metric: "peakSwingSpeed", value: 7.9, previous: 6.2 },
    { metric: "damageDealt", value: 500, previous: 400 },
    { metric: "score", value: 1400, previous: 900 },
    { metric: "fade", value: -3, previous: -10 },
  ]);
});

test("each metric is compared on its own", () => {
  const a = row({}, { peakSwingSpeed: 7, score: 1000 });
  const b = row({}, { peakSwingSpeed: 6, score: 1100 });
  assert.deepEqual(records.newRecordsFor(b.id, [a, b]).map((r) => r.metric), ["score"]);
});

test("equal values are not records, and neither are lower ones", () => {
  const a = row({}, { peakSwingSpeed: 8, damageDealt: 500, score: 1000, fade: 2 });
  const same = row({}, { peakSwingSpeed: 8, damageDealt: 500, score: 1000, fade: 2 });
  const lower = row({}, { peakSwingSpeed: 5, damageDealt: 300, score: 700, fade: -20 });
  assert.deepEqual(records.newRecordsFor(same.id, [a, same]), []);
  assert.deepEqual(records.newRecordsFor(lower.id, [a, lower]), []);
  // a difference too small to show (below the rounding of the metric) is not a record either
  const tiny = row({}, { peakSwingSpeed: 8.001, damageDealt: 500.04, score: 1000.4, fade: 2.04 });
  assert.deepEqual(records.newRecordsFor(tiny.id, [a, tiny]), []);
});

test("abandoned fights never create records, and are not the thing to beat", () => {
  const a = row({}, { score: 500 });
  const abandoned = row({ outcome: "abandoned" }, { peakSwingSpeed: 8, damageDealt: 9999, score: 9999, fade: 50 });
  assert.deepEqual(records.newRecordsFor(abandoned.id, [a, abandoned]), []);
  const b = row({}, { score: 600 });
  // the abandoned fight sits between a and b but its big numbers do not stop b from setting its record
  assert.deepEqual(records.newRecordsFor(b.id, [a, abandoned, b]).map((r) => r.metric), ["score"]);
  // an abandoned fight alone is no baseline: a victory after it is a "first" fight
  const only = row({ outcome: "abandoned" });
  const next = row({}, { score: 1 });
  assert.deepEqual(records.newRecordsFor(next.id, [only, next]), []);
});

test("only earlier fights count, so reading a session later gives the same answer", () => {
  const a = row({}, { score: 1000 });
  const b = row({}, { score: 1500 });
  const later = row({}, { score: 9000 });
  const first = records.newRecordsFor(b.id, [a, b]);
  assert.deepEqual(records.newRecordsFor(b.id, [a, b, later]), first); // a fight played afterwards changes nothing about b
  assert.deepEqual(records.newRecordsFor(b.id, [later, b, a]), first); // and the order the rows arrive in does not matter
  // the one after them beats the best of both
  assert.deepEqual(records.newRecordsFor(later.id, [a, b, later]).find((r) => r.metric === "score"), { metric: "score", value: 9000, previous: 1500 });
});

test("a metric nobody measured before is skipped, not invented", () => {
  const a = row({}, { peakSwingSpeed: null, fade: null });
  const b = row({}, { peakSwingSpeed: 7, fade: 3, score: 5000 });
  // the damage is equal (400 and 400), the speed and the fade had nothing to compare with: only the score is a record
  assert.deepEqual(records.newRecordsFor(b.id, [a, b]).map((r) => r.metric), ["score"]);
});

test("the fade value only exists when there were enough swings", () => {
  const hits = (speeds) => speeds.map((speed, i) => ({ t: i * 5, speed }));
  const ok = effort.fade(hits([6, 6, 6, 5, 5, 5, 4, 4, 4]), 45);
  const few = effort.fade(hits([6, 6]), 10);
  assert.equal(records.recordValues({ score: 1, peakSpeed: 3, damageDealt: 4 }, ok).fade, ok.fadePct);
  assert.equal(records.recordValues({ score: 1, peakSpeed: 3, damageDealt: 4 }, few).fade, null);
  assert.equal(records.recordValues({ score: 1 }, null).peakSwingSpeed, null);
});

test("recordFlags gives, for every session, what newRecordsFor gives", () => {
  const rows = [
    row({}, { score: 500, peakSwingSpeed: 5 }),
    row({}, { score: 700, peakSwingSpeed: 4 }),
    row({ level: "Hard" }, { score: 100 }),
    row({ outcome: "abandoned" }, { score: 9999 }),
    row({}, { score: 650, peakSwingSpeed: 7 }),
    row({ level: "Hard" }, { score: 300 }),
  ];
  const flags = records.recordFlags(rows);
  for (const r of rows) assert.deepEqual(flags[r.id] || [], records.newRecordsFor(r.id, rows).map((x) => x.metric), r.id);
  assert.deepEqual(flags, { [rows[1].id]: ["score"], [rows[4].id]: ["peakSwingSpeed"], [rows[5].id]: ["score"] });
});
