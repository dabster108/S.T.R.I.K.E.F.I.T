import { CARD } from "../../config/wrapped.js";

// Waits for the web fonts, then renders a node to a PNG blob at the card's exact size (1080 x 1350). html-to-image is loaded on demand.
// The first pass can miss images and fonts that were still being fetched, so the second pass is the real one (as the recap card does).
export async function renderCardPng(node) {
  if (document.fonts?.ready) await document.fonts.ready;
  const { toBlob } = await import("html-to-image");
  const opts = { width: CARD.w, height: CARD.h, pixelRatio: 1, cacheBust: true };
  await toBlob(node, opts);
  return toBlob(node, opts);
}

export function downloadBlob(blob, name) {
  const url = URL.createObjectURL(blob);
  const a = Object.assign(document.createElement("a"), { href: url, download: name });
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
