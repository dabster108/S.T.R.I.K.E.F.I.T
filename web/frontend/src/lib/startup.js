// Decisions the live store and the telemetry hook make at connect time, kept pure so they can be tested.
// The dashboard shell used to load its snapshot (live fights, events, sessions, health) on mount AND again the moment the
// stream opened about a second later: the same five requests twice on every page load.

export const RESYNC_MIN_GAP_MS = 3000;

// The stream just opened: should the live store fetch a snapshot now?
//  - no snapshot has loaded yet (the first one failed, e.g. the server was asleep): yes,
//  - first connect right after startLive() already fetched one: no (it is seconds old at most),
//  - coming back from polling: yes, unless polling fetched one in the last second.
export function shouldResync({ wasPolling, sinceLastMs, ready = true }) {
  if (!ready) return true;
  return wasPolling ? sinceLastMs >= 1000 : sinceLastMs >= RESYNC_MIN_GAP_MS;
}

// The connection became "live": should useTelemetry refetch its lists and the health check?
//  - never while the first load is still running (it already has the data coming),
//  - yes when the first load never succeeded (server was asleep) or after a reconnect.
export function shouldRefetchOnConnect({ prev, everLive, loadedOk, refreshing }) {
  if (refreshing) return false;
  return !loadedOk || (everLive && prev !== "live");
}
