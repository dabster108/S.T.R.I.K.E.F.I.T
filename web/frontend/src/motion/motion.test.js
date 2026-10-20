import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { DUR, EASE, RISE, STAGGER, MAX_STAGGER_STEPS, revealStyle, staggerDelay } from "./tokens.js";

const css = fs.readFileSync(new URL("../styles/tokens.css", import.meta.url), "utf8");
const token = (name) => new RegExp(`--${name}:\\s*([^;]+);`).exec(css)?.[1].trim();

test("the JS and CSS motion tokens are the same numbers", () => {
  assert.equal(token("motion-fast"), `${DUR.fast}ms`);
  assert.equal(token("motion-base"), `${DUR.base}ms`);
  assert.equal(token("motion-slow"), `${DUR.slow}ms`);
  assert.equal(token("motion-ease"), EASE);
  assert.equal(token("motion-stagger"), `${STAGGER}ms`);
  assert.equal(token("motion-rise"), `${RISE}px`);
});

test("the three durations are 150 / 250 / 400 ms and the legacy tokens point at them", () => {
  assert.deepEqual(DUR, { fast: 150, base: 250, slow: 400 });
  assert.equal(token("dur-fast"), "var(--motion-fast)");
  assert.equal(token("dur"), "var(--motion-base)");
  assert.equal(token("dur-slow"), "var(--motion-slow)");
  assert.equal(token("ease-out"), "var(--motion-ease)");
});

test("stagger is one step per sibling, capped so a long list never waits seconds", () => {
  assert.equal(staggerDelay(0), 0);
  assert.equal(staggerDelay(3), 3 * STAGGER);
  assert.equal(staggerDelay(50), MAX_STAGGER_STEPS * STAGGER);
  assert.equal(staggerDelay(-2), 0);
  assert.equal(staggerDelay("x"), 0);
});

test("revealStyle sets --m-delay only when there is a delay", () => {
  assert.equal(revealStyle(), undefined);
  assert.deepEqual(revealStyle({ delay: 100 }), { "--m-delay": "100ms" });
  assert.deepEqual(revealStyle({ delay: 100, index: 2 }), { "--m-delay": `${100 + 2 * STAGGER}ms` });
});

test("every animation name used in the stylesheets has @keyframes (a missing one leaves its element stuck at its first frame)", () => {
  const css = ["../styles.css", "../styles/tokens.css", "../styles/fonts.css"].map((f) => fs.readFileSync(new URL(f, import.meta.url), "utf8")).join("\n");
  const defined = new Set([...css.matchAll(/@keyframes\s+([\w-]+)/g)].map((m) => m[1]));
  const IGNORE = new Set(["var", "linear", "ease", "infinite", "both", "forwards", "backwards", "none", "normal", "alternate", "paused", "running", "ease-in-out", "ease-out", "ease-in", "reverse", "initial", "inherit", "important"]);
  const used = new Set();
  for (const m of css.matchAll(/animation(?:-name)?\s*:\s*([^;}]+)/g)) {
    for (const tok of m[1].match(/(?<![\w-])[a-zA-Z][\w-]*(?![\w-]*\()/g) || []) if (!IGNORE.has(tok) && !/^\d/.test(tok)) used.add(tok);
  }
  assert.deepEqual([...used].filter((u) => !defined.has(u)).sort(), []);
});
