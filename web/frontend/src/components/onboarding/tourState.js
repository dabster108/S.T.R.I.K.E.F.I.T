// Tour flags, kept apart from Tour.jsx so the header and the app shell can use them without loading the tour itself.
// The rules live in lib/tourFlag.js (pure, tested); this binds them to the real storages.
import { AUTO_START_DELAY_MS, createTourFlag, tourAutoStartAllowed } from "../../lib/tourFlag.js";

export { AUTO_START_DELAY_MS, tourAutoStartAllowed };
export const TOUR_EVENT = "strikefit:tour";
export const openTour = () => window.dispatchEvent(new Event(TOUR_EVENT));

// Touching window.localStorage itself can throw (blocked site data), so the storages are looked up inside a try.
const storage = (name) => {
  try {
    return window[name];
  } catch {
    return null;
  }
};
const flag = () => createTourFlag({ local: storage("localStorage"), session: storage("sessionStorage") });

export const hasSeenTour = () => flag().seen();
// Saves the flag the first time the tour has really been on screen; a replay (already seen) writes nothing.
export const markTourSeen = () => flag().mark();

// Another dialog is open (the command palette, the mobile menu, a confirm): the tour must not start over it.
// The tour's own dialog is marked data-tour-dialog and does not count.
export const anotherModalOpen = () =>
  typeof document !== "undefined" && Boolean(document.querySelector('[aria-modal="true"]:not([data-tour-dialog])'));
