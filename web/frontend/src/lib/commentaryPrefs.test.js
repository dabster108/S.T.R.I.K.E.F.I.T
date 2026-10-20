import test from "node:test";
import assert from "node:assert/strict";
import { createPrefs } from "./commentaryPrefs.js";

const memory = () => {
  const m = new Map();
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), m };
};
const KEYS = { hidden: "h", speech: "s", plain: "p" };

test("commentary shows and stays silent by default", () => {
  const p = createPrefs(memory(), KEYS);
  assert.deepEqual(p.get(), { hidden: false, speech: false, plain: false });
});

test("the choices are remembered and listeners hear about them", () => {
  const storage = memory();
  const p = createPrefs(storage, KEYS);
  let heard = 0;
  const off = p.subscribe(() => heard++);
  p.setHidden(true);
  p.setSpeech(true);
  p.setPlain(true);
  assert.equal(heard, 3);
  assert.deepEqual(createPrefs(storage, KEYS).get(), { hidden: true, speech: true, plain: true }); // a new page load reads them back
  off();
  p.setSpeech(false);
  assert.equal(heard, 3);
  assert.equal(storage.m.get("s"), "0");
});

test("blocked storage does not break anything", () => {
  const broken = { getItem: () => { throw new Error("blocked"); }, setItem: () => { throw new Error("blocked"); } };
  const p = createPrefs(broken, KEYS);
  assert.deepEqual(p.get(), { hidden: false, speech: false, plain: false });
  p.setHidden(true); // lasts for this tab
  assert.equal(p.get().hidden, true);
  assert.deepEqual(createPrefs(null, KEYS).get(), { hidden: false, speech: false, plain: false });
});
