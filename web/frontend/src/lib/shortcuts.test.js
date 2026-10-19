import test from "node:test";
import assert from "node:assert/strict";
import { GO_KEYS, RECENT_MAX, addRecent, goHint, goTarget, readRecent, writeRecent } from "./shortcuts.js";

const store = (init = {}) => {
  const m = { ...init };
  return { getItem: (k) => (k in m ? m[k] : null), setItem: (k, v) => void (m[k] = v) };
};

test("goTarget maps a letter (any case) to a dashboard route, unknown to null", () => {
  assert.equal(goTarget("l"), "/dashboard/live");
  assert.equal(goTarget("L"), "/dashboard/live");
  assert.equal(goTarget("z"), null);
  assert.equal(goTarget(undefined), null);
});

test("goHint is the inverse of GO_KEYS", () => {
  for (const [k, v] of Object.entries(GO_KEYS)) assert.deepEqual(goHint(v.to), ["G", k.toUpperCase()]);
  assert.equal(goHint("/dashboard/guide"), null);
});

test("addRecent puts the newest first, drops duplicates and caps the list", () => {
  let list = [];
  for (let i = 0; i < RECENT_MAX + 2; i++) list = addRecent(list, { id: `p${i}`, label: `P${i}`, to: `/x/${i}` });
  assert.equal(list.length, RECENT_MAX);
  assert.equal(list[0].id, `p${RECENT_MAX + 1}`);
  list = addRecent(list, { id: "p3", label: "P3", to: "/x/3" });
  assert.equal(list[0].id, "p3");
  assert.equal(list.filter((x) => x.id === "p3").length, 1);
});

test("readRecent survives junk and round-trips", () => {
  assert.deepEqual(readRecent(store({ "strikefit.recent": "{not json" })), []);
  assert.deepEqual(readRecent(store({ "strikefit.recent": '{"a":1}' })), []);
  assert.deepEqual(readRecent(store({ "strikefit.recent": '[{"id":1},{"id":"a","label":"A","to":"/a"}]' })), [{ id: "a", label: "A", to: "/a" }]);
  const s = store();
  writeRecent(s, [{ id: "a", label: "A", to: "/a" }]);
  assert.deepEqual(readRecent(s), [{ id: "a", label: "A", to: "/a" }]);
  assert.deepEqual(readRecent({ getItem() { throw new Error("blocked"); } }), []);
});
