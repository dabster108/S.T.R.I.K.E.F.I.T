// Guided tour flag and auto-start rules. Run: npm test (node --test src/lib/*.test.js)
import test from "node:test";
import assert from "node:assert/strict";
import { AUTO_START_DELAY_MS, createTourFlag, LEGACY_TOUR_KEY, TOUR_SEEN_KEY, tourAutoStartAllowed } from "./tourFlag.js";

const memory = (initial = {}) => {
  const data = { ...initial };
  return { getItem: (k) => (k in data ? data[k] : null), setItem: (k, v) => void (data[k] = String(v)), removeItem: (k) => void delete data[k], data };
};
const throwing = () => ({
  getItem() {
    throw new Error("SecurityError");
  },
  setItem() {
    throw new Error("QuotaExceededError");
  },
});

test("first visit: not seen; finishing saves the flag; a reload (new flag object, same storage) stays away", () => {
  const local = memory();
  assert.equal(createTourFlag({ local }).seen(), false);
  assert.equal(createTourFlag({ local }).mark(), "local");
  assert.equal(local.data[TOUR_SEEN_KEY], "1");
  assert.equal(createTourFlag({ local }).seen(), true);
});

test("a cleared flag brings the tour back", () => {
  const local = memory();
  createTourFlag({ local }).mark();
  local.removeItem(TOUR_SEEN_KEY);
  assert.equal(createTourFlag({ local }).seen(), false);
});

test("a replay never changes the flag: nothing is written once it is seen", () => {
  const local = memory();
  createTourFlag({ local }).mark();
  const writes = [];
  const spy = { ...local, setItem: (k, v) => (writes.push(k), local.setItem(k, v)) };
  assert.equal(createTourFlag({ local: spy }).mark(), "already");
  assert.deepEqual(writes, []);
});

test("the first version's key counts as seen (nobody who finished it sees it again) and is not rewritten", () => {
  const local = memory({ [LEGACY_TOUR_KEY]: "1" });
  const flag = createTourFlag({ local });
  assert.equal(flag.seen(), true);
  assert.equal(flag.mark(), "already");
  assert.equal(TOUR_SEEN_KEY in local.data, false);
});

test("private mode: localStorage throws, so the flag falls back to sessionStorage for the rest of the visit", () => {
  const session = memory();
  const flag = createTourFlag({ local: throwing(), session });
  assert.equal(flag.seen(), false);
  assert.equal(flag.mark(), "session");
  // another page load in the same visit: same sessionStorage, still throwing localStorage
  assert.equal(createTourFlag({ local: throwing(), session }).seen(), true);
  // a new visit (fresh sessionStorage) shows it once more
  assert.equal(createTourFlag({ local: throwing(), session: memory() }).seen(), false);
});

test("localStorage that accepts a write but cannot read it back falls through to sessionStorage", () => {
  const session = memory();
  const forgetful = { getItem: () => null, setItem() {} };
  assert.equal(createTourFlag({ local: forgetful, session }).mark(), "session");
});

test("no storage at all: nothing throws, the flag is simply never remembered", () => {
  const flag = createTourFlag({ local: throwing(), session: throwing() });
  assert.equal(flag.seen(), false);
  assert.equal(flag.mark(), "none");
  assert.equal(createTourFlag({}).mark(), "none");
});

test("auto-start only on the dashboard Overview, when unseen and no modal is open", () => {
  const ok = { pathname: "/dashboard", seen: false, modalOpen: false };
  assert.equal(tourAutoStartAllowed(ok), true);
  assert.equal(tourAutoStartAllowed({ ...ok, pathname: "/dashboard/" }), true);
  for (const pathname of ["/dashboard/players", "/dashboard/player/Iij", "/dashboard/live", "/", "/story", "/monsters", "/dashboard/story"]) {
    assert.equal(tourAutoStartAllowed({ ...ok, pathname }), false, pathname);
  }
  assert.equal(tourAutoStartAllowed({ ...ok, seen: true }), false);
  assert.equal(tourAutoStartAllowed({ ...ok, modalOpen: true }), false);
});

test("the delay is the 800 ms the Overview needs to paint", () => {
  assert.equal(AUTO_START_DELAY_MS, 800);
});
