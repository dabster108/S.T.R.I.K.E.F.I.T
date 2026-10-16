// Request coalescing: while a GET for a URL is in flight, every other caller of the same URL gets that same promise
// instead of opening another connection. A browser allows only 6 connections per host over HTTP/1.1 and the live stream
// holds one of them, so duplicate startup requests used to queue the page's own requests behind them (a slow database
// then left a page on its skeleton for tens of seconds). Failures are not remembered: the next call tries again.
export function createCoalescer() {
  const pending = new Map();
  return function run(key, start) {
    if (pending.has(key)) return pending.get(key);
    const p = Promise.resolve()
      .then(start)
      .finally(() => {
        if (pending.get(key) === p) pending.delete(key);
      });
    pending.set(key, p);
    return p;
  };
}
