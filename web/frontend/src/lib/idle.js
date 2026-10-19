// Resolves when the browser has nothing better to do (requestIdleCallback), or after `timeout` ms at the latest. Safari has no
// requestIdleCallback, so it falls back to a short timer. Used to start downloads that feed content below the fold
// (the Overview's events list, the live feed seed) after the first screen has painted, instead of racing the data it is waiting for.
export function whenIdle(timeout = 2000) {
  return new Promise((resolve) => {
    if (typeof window !== "undefined" && typeof window.requestIdleCallback === "function") window.requestIdleCallback(() => resolve(), { timeout });
    else setTimeout(resolve, Math.min(timeout, 400));
  });
}
