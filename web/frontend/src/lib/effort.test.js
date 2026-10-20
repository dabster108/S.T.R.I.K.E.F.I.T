import test from "node:test";
import assert from "node:assert/strict";
import { formatFade, speakFade, movingAverage, smoothPath, nearestByTime, clockText } from "./effort.js";

test("fade is shown signed, with a real minus sign", () => {
  assert.equal(formatFade(-12.4), "−12%");
  assert.equal(formatFade(7.6), "+8%");
  assert.equal(formatFade(0.2), "0%");
  assert.equal(formatFade(null), "—");
  assert.equal(speakFade(-12), "minus 12 percent");
  assert.equal(speakFade(5), "plus 5 percent");
});

test("moving average smooths without changing the length or the x values", () => {
  const pts = [4, 8, 4, 8, 4].map((y, i) => ({ x: i, y }));
  const m = movingAverage(pts, 1);
  assert.equal(m.length, 5);
  assert.deepEqual(m.map((p) => p.x), [0, 1, 2, 3, 4]);
  assert.equal(m[1].y, (4 + 8 + 4) / 3);
  assert.equal(m[0].y, 6);
});

test("smoothPath never overshoots the data and handles tiny inputs", () => {
  assert.equal(smoothPath([]), "");
  assert.equal(smoothPath([{ x: 1, y: 2 }]), "M1,2");
  assert.match(smoothPath([{ x: 0, y: 0 }, { x: 10, y: 10 }]), /^M0,0C/);
  const pts = [{ x: 0, y: 10 }, { x: 10, y: 0 }, { x: 20, y: 10 }, { x: 30, y: 10 }, { x: 40, y: 2 }];
  const d = smoothPath(pts);
  // every Bezier control point's y stays inside the data's y range, so the curve cannot leave it
  const ys = [...d.matchAll(/[MC ]\s*-?[\d.]+,(-?[\d.]+)/g)].map((m) => Number(m[1]));
  assert.ok(ys.length >= 5 && ys.every((y) => y >= 0 - 1e-9 && y <= 10 + 1e-9), d);
  assert.equal(smoothPath([{ x: 5, y: 1 }, { x: 5, y: 9 }, { x: 8, y: 3 }]).split("C").length - 1, 1); // the repeated x is merged
});

test("nearestByTime and clockText", () => {
  assert.equal(nearestByTime([{ t: 1 }, { t: 5 }, { t: 9 }], 6).t, 5);
  assert.equal(nearestByTime([], 3), null);
  assert.equal(clockText(65), "01:05");
});
