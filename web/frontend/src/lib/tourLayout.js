// Where the guided tour's popover goes relative to its spotlight, in viewport pixels. Pure, tested by tourLayout.test.js.
//
// The popover must never cover the part of the target it is explaining:
//   below  - the popover fits under the target,
//   above  - it fits over the target,
//   right / left - a tall target with room beside it (desktop): next to it, aligned with its top,
//   dock   - the target is too tall for either (a tall card on a phone): the popover becomes a sheet at the bottom of the
//            viewport and the spotlight is clipped to the part of the target that is visible above it,
//   center - there is no target on screen (e.g. the sidebar link is hidden on a phone).
export const GAP = 12; // between spotlight and popover
export const EDGE = 16; // from the viewport edge

export function placePopover({ rect, vw, vh, cardW, cardH }) {
  const clampLeft = (x) => Math.max(EDGE, Math.min(vw - cardW - EDGE, x));
  if (!rect) return { mode: "center", top: Math.max(EDGE, (vh - cardH) / 2), left: clampLeft((vw - cardW) / 2), spot: null };

  const left = clampLeft(rect.w > cardW ? rect.x : rect.x + rect.w / 2 - cardW / 2);
  const below = rect.y + rect.h + GAP;
  if (below + cardH + EDGE <= vh) return { mode: "below", top: below, left, spot: rect };
  const above = rect.y - GAP - cardH;
  if (above >= EDGE) return { mode: "above", top: above, left, spot: rect };

  // Tall target with room beside it (desktop): next to the target, aligned with its top, never over it.
  const sideTop = Math.max(EDGE, Math.min(rect.y, vh - EDGE - cardH));
  if (rect.x + rect.w + GAP + cardW + EDGE <= vw) return { mode: "right", top: sideTop, left: rect.x + rect.w + GAP, spot: rect };
  if (rect.x - GAP - cardW >= EDGE) return { mode: "left", top: sideTop, left: rect.x - GAP - cardW, spot: rect };

  // Too tall for any side: dock at the bottom and keep only the visible top of the target in the spotlight.
  const top = Math.max(EDGE, vh - EDGE - cardH);
  const room = Math.max(24, top - GAP - rect.y);
  return { mode: "dock", top, left: clampLeft((vw - cardW) / 2), spot: { ...rect, h: Math.min(rect.h, room) } };
}

// Where to scroll a target (viewport top y, height h) so it is readable above the popover: tall targets start just
// below the sticky header; short ones sit in the middle of the space the popover leaves. Returns the delta to scrollBy
// (positive = scroll down), 0 when it is already fine.
export const HEADER_GUTTER = 80;
export function scrollDelta({ y, h, vh, cardH }) {
  const free = vh - cardH - GAP - EDGE - HEADER_GUTTER; // space above a docked sheet
  const wanted = h > free ? HEADER_GUTTER : Math.max(HEADER_GUTTER, HEADER_GUTTER + (free - h) / 2);
  const delta = Math.round(y - wanted);
  return Math.abs(delta) < 12 ? 0 : delta;
}
