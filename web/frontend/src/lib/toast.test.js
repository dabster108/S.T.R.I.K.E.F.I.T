import test, { beforeEach } from "node:test";
import assert from "node:assert/strict";
import { MAX_TOASTS, clearToasts, dismiss, getToasts, push, subscribe, toast } from "./toast.js";

beforeEach(() => clearToasts());

test("push adds a toast and notifies subscribers; dismiss removes it", () => {
  let calls = 0;
  const off = subscribe(() => calls++);
  const id = push({ tone: "good", title: "Saved", timeout: 0 });
  assert.equal(getToasts().length, 1);
  assert.equal(getToasts()[0].title, "Saved");
  dismiss(id);
  assert.equal(getToasts().length, 0);
  assert.equal(calls, 2);
  off();
});

test("only the newest MAX_TOASTS stay", () => {
  for (let i = 0; i < MAX_TOASTS + 3; i++) push({ title: `t${i}`, timeout: 0 });
  assert.equal(getToasts().length, MAX_TOASTS);
  assert.equal(getToasts().at(-1).title, `t${MAX_TOASTS + 2}`);
});

test("the same message twice replaces itself", () => {
  push({ tone: "good", title: "Link copied", timeout: 0 });
  push({ tone: "good", title: "Link copied", timeout: 0 });
  assert.equal(getToasts().length, 1);
});

test("toast.bad stays longer than toast.good and sets the tone", () => {
  toast.bad("Export failed", "the server answered 500");
  assert.equal(getToasts()[0].tone, "bad");
  clearToasts();
});
