// One clip at a time on a page, and when a clip may start by itself. Pure (the elements only need play / pause / paused), so the
// rules are unit tested; components/ui/DemoVideo.jsx is the one caller.
//
//   - A clip the visitor starts (Play button) takes the page: whatever else was playing is paused.
//   - A clip may start by itself only when it is on screen, autoplay is allowed, and no other clip is playing (an autoplaying clip
//     never interrupts one the visitor is watching).
//   - Autoplay is off under reduced motion (the OS setting or the site's Calm mode) and when the browser asks to save data.

let owner = null;

/** This element becomes the playing clip; the previous one is paused. */
export function claim(el) {
  if (owner && owner !== el && !owner.paused) owner.pause();
  owner = el;
}

/** The element stops being the owner (paused, unmounted). */
export function release(el) {
  if (owner === el) owner = null;
}

/** Another clip is playing right now. */
export function otherPlaying(el) {
  return owner !== null && owner !== el && !owner.paused;
}

/** navigator.connection.saveData (Chrome / Android "Data Saver", "Lite mode"). */
export function saveDataOn(nav = globalThis.navigator) {
  return Boolean(nav && nav.connection && nav.connection.saveData);
}

export function autoplayAllowed({ reduced, saveData }) {
  return !reduced && !saveData;
}

/**
 * Whether a clip should be playing now.
 *   choice: null (no choice yet), "play" or "pause" (the visitor pressed the button)
 */
export function shouldPlay({ active, choice, reduced, saveData }) {
  if (!active) return false;
  if (choice === "pause") return false;
  if (choice === "play") return true;
  return autoplayAllowed({ reduced, saveData });
}

/** Test hook: forget the owner. */
export function _reset() {
  owner = null;
}
