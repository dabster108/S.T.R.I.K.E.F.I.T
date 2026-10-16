// Tour flags, kept apart from Tour.jsx so the header and the app shell can use them without loading the tour itself.
export const TOUR_KEY = "strikefit.tour.done";
export const TOUR_EVENT = "strikefit:tour";
export const openTour = () => window.dispatchEvent(new Event(TOUR_EVENT));

export function shouldAutoStart() {
  try {
    return !localStorage.getItem(TOUR_KEY);
  } catch {
    return false;
  }
}
export function markTourDone() {
  try {
    localStorage.setItem(TOUR_KEY, "1");
  } catch {
    /* not remembered: it may show again next visit */
  }
}
