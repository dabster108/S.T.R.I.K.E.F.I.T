// One delegated pointer listener for every [data-spotlight] element on the page (instead of one per card). It writes the
// pointer position, relative to the card, into --spot-x / --spot-y, at most once per animation frame. The highlight itself is a
// pseudo-element moved with transform (styles.css .spotlight::before), so nothing repaints. Pointer devices only: touch never moves it.

let started = false;

export function startSpotlight() {
  if (started || typeof window === "undefined" || !window.matchMedia) return;
  started = true;
  if (!window.matchMedia("(hover: hover) and (pointer: fine)").matches) return;
  let frame = 0;
  let next = null;
  document.addEventListener(
    "pointermove",
    (e) => {
      if (e.pointerType === "touch") return;
      const el = e.target instanceof Element ? e.target.closest("[data-spotlight]") : null;
      if (!el) return;
      next = { el, x: e.clientX, y: e.clientY };
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        const { el: card, x, y } = next;
        const r = card.getBoundingClientRect();
        card.style.setProperty("--spot-x", `${Math.round(x - r.left)}px`);
        card.style.setProperty("--spot-y", `${Math.round(y - r.top)}px`);
      });
    },
    { passive: true }
  );
}
