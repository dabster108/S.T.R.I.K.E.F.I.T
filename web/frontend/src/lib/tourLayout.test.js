// Tour popover placement. Run: npm test (node --test src/lib/*.test.js)
import test from "node:test";
import assert from "node:assert/strict";
import { EDGE, GAP, placePopover, scrollDelta } from "./tourLayout.js";

const overlaps = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;
const box = (p, cardW, cardH) => ({ x: p.left, y: p.top, w: cardW, h: cardH });

test("small target with room below: popover goes under it", () => {
  const p = placePopover({ rect: { x: 4, y: 148, w: 231, h: 56 }, vw: 1440, vh: 900, cardW: 360, cardH: 236 });
  assert.equal(p.mode, "below");
  assert.equal(p.top, 148 + 56 + GAP);
});

test("target near the bottom: popover goes above it", () => {
  const p = placePopover({ rect: { x: 4, y: 820, w: 231, h: 56 }, vw: 1440, vh: 900, cardW: 360, cardH: 236 });
  assert.equal(p.mode, "above");
  assert.equal(p.top, 820 - GAP - 236);
});

test("tall target on a phone: docked sheet at the bottom, spotlight clipped above it, nothing covered", () => {
  const rect = { x: 8, y: 80, w: 374, h: 800 };
  const p = placePopover({ rect, vw: 390, vh: 844, cardW: 358, cardH: 236 });
  assert.equal(p.mode, "dock");
  assert.equal(p.top, 844 - EDGE - 236);
  assert.ok(p.spot.y + p.spot.h + GAP <= p.top, "spotlight ends above the sheet");
  assert.equal(p.spot.y, rect.y);
  assert.equal(overlaps(box(p, 358, 236), p.spot), false);
});

test("tall target on a wide screen: popover beside it, spotlight not clipped", () => {
  const rect = { x: 264, y: 80, w: 669, h: 700 };
  const p = placePopover({ rect, vw: 1440, vh: 900, cardW: 360, cardH: 255 });
  assert.equal(p.mode, "right");
  assert.equal(p.left, 264 + 669 + GAP);
  assert.deepEqual(p.spot, rect);
  const wideLeft = placePopover({ rect: { x: 1000, y: 80, w: 400, h: 700 }, vw: 1440, vh: 900, cardW: 360, cardH: 255 });
  assert.equal(wideLeft.mode, "left");
  assert.equal(wideLeft.left, 1000 - GAP - 360);
});

test("no target: centred, inside the viewport", () => {
  const p = placePopover({ rect: null, vw: 390, vh: 844, cardW: 358, cardH: 236 });
  assert.equal(p.mode, "center");
  assert.equal(p.spot, null);
  assert.ok(p.left >= EDGE && p.left + 358 <= 390 - EDGE);
});

test("property: for any target and viewport the popover stays on screen and never covers the spotlight", () => {
  const viewports = [[390, 844], [390, 600], [768, 1024], [1440, 900], [1024, 500]];
  for (const [vw, vh] of viewports) {
    const cardW = Math.min(360, vw - 32);
    const cardH = 236;
    for (const y of [0, 40, 80, 200, 400, vh - 60]) {
      for (const h of [40, 120, 300, 607, 900, 2000]) {
        for (const x of [0, 16, vw / 2 - 100]) {
          const rect = { x, y, w: Math.min(vw - x, 240), h };
          const p = placePopover({ rect, vw, vh, cardW, cardH });
          const where = JSON.stringify({ vw, vh, rect, mode: p.mode });
          assert.ok(p.left >= EDGE - 0.01 && p.left + cardW <= vw - EDGE + 0.01, `x in viewport ${where}`);
          assert.ok(p.top >= EDGE - 0.01 && p.top + cardH <= vh - EDGE + 0.01, `y in viewport ${where}`);
          if (p.spot && p.mode !== "dock") assert.equal(overlaps(box(p, cardW, cardH), p.spot), false, `no overlap ${where}`);
          // docked: the visible part of the target is above the sheet whenever the target starts above it
          if (p.mode === "dock" && rect.y + 24 + GAP <= p.top) assert.equal(overlaps(box(p, cardW, cardH), p.spot), false, `dock no overlap ${where}`);
        }
      }
    }
  }
});

test("scrollDelta: tall targets start under the header, short ones are centred in the free space, near-fine is left alone", () => {
  assert.equal(scrollDelta({ y: 500, h: 900, vh: 844, cardH: 236 }), 420); // scroll down 420 -> top at 80
  assert.equal(scrollDelta({ y: 82, h: 900, vh: 844, cardH: 236 }), 0);
  const d = scrollDelta({ y: 700, h: 100, vh: 844, cardH: 236 });
  assert.ok(d > 0 && d < 700);
  assert.ok(scrollDelta({ y: -300, h: 200, vh: 844, cardH: 236 }) < 0);
});
