// Page lifecycle for long-lived connections (the live event stream).
//
// When a visitor navigates away, the browser may keep the old page in its back/forward cache. A page that still holds an
// open EventSource keeps that socket too, so a handful of cached pages used up the browser's six connections per host and
// the NEW page's script and API requests queued behind them (a blank page, or a skeleton that never filled, with no error;
// measured: the server's stream-client count rose by one per navigation). So: let go of the stream on `pagehide`, and
// open it again when the browser restores the page from the cache (`pageshow` with persisted = true).
export function bindPageLifecycle(target, { onHide, onShow }) {
  const hide = () => onHide();
  const show = (event) => {
    if (event && event.persisted) onShow();
  };
  target.addEventListener("pagehide", hide);
  target.addEventListener("pageshow", show);
  return () => {
    target.removeEventListener("pagehide", hide);
    target.removeEventListener("pageshow", show);
  };
}
