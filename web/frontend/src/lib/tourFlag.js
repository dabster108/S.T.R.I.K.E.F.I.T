// The guided tour's "seen" flag and the rule for when it may start by itself. Pure (storages are passed in), so
// tourFlag.test.js can run it without a browser.
//
// Flag: localStorage "strikefit_tour_seen_v1" (bump the version to show the tour to everyone again). If localStorage is
// unavailable (private mode: reads or writes throw) the flag falls back to sessionStorage, so the tour does not come back on
// every page load within one visit. The old key "strikefit.tour.done" (first version of the tour) counts as seen, so
// nobody who already finished it is shown it again.
export const TOUR_SEEN_KEY = "strikefit_tour_seen_v1";
export const LEGACY_TOUR_KEY = "strikefit.tour.done";
export const AUTO_START_DELAY_MS = 800; // after the Overview has painted

const attempt = (fn, fallback) => {
  try {
    return fn();
  } catch {
    return fallback;
  }
};

export function createTourFlag({ local = null, session = null } = {}) {
  const readFrom = (store) => attempt(() => Boolean(store && (store.getItem(TOUR_SEEN_KEY) || store.getItem(LEGACY_TOUR_KEY))), false);
  const writeTo = (store) => attempt(() => (store.setItem(TOUR_SEEN_KEY, "1"), readFrom(store)), false);
  const seen = () => readFrom(local) || readFrom(session);
  return {
    seen,
    // Saves the flag once. Already seen: nothing is written (a replay never changes it). Returns where it went.
    mark() {
      if (seen()) return "already";
      if (local && writeTo(local)) return "local";
      if (session && writeTo(session)) return "session";
      return "none";
    },
  };
}

const normalize = (p) => String(p || "").replace(/\/+$/, "") || "/";

// May the tour start by itself right now? Only on the dashboard Overview, only when it has not been seen, never over
// another modal. (The Guide button does not use this: it always opens the tour.)
export function tourAutoStartAllowed({ pathname, seen, modalOpen }) {
  return normalize(pathname) === "/dashboard" && !seen && !modalOpen;
}
