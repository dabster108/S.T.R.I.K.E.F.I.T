// Regression test for the "stuck page" bug: cached pages kept their event streams open and starved the next page's requests.
import test from "node:test";
import assert from "node:assert/strict";
import { bindPageLifecycle } from "./pagelife.js";

const pageEvent = (type, persisted) => Object.assign(new Event(type), { persisted });

test("pagehide closes the stream; a restore from the back/forward cache reopens it", () => {
  const target = new EventTarget();
  const calls = [];
  bindPageLifecycle(target, { onHide: () => calls.push("hide"), onShow: () => calls.push("show") });
  target.dispatchEvent(pageEvent("pagehide", true));
  assert.deepEqual(calls, ["hide"]);
  target.dispatchEvent(pageEvent("pageshow", true));
  assert.deepEqual(calls, ["hide", "show"]);
});

test("a normal first load (pageshow with persisted = false) does not reconnect twice", () => {
  const target = new EventTarget();
  let shows = 0;
  bindPageLifecycle(target, { onHide() {}, onShow: () => shows++ });
  target.dispatchEvent(pageEvent("pageshow", false));
  assert.equal(shows, 0);
});

test("the unbind function removes both listeners", () => {
  const target = new EventTarget();
  let n = 0;
  const off = bindPageLifecycle(target, { onHide: () => n++, onShow: () => n++ });
  off();
  target.dispatchEvent(pageEvent("pagehide", true));
  target.dispatchEvent(pageEvent("pageshow", true));
  assert.equal(n, 0);
});
