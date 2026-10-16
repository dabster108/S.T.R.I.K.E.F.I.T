// Regression tests for the Compare page that stayed on its skeleton: duplicate startup requests queued the page's own
// requests behind them. Run: npm test (node --test src/lib/*.test.js)
import test from "node:test";
import assert from "node:assert/strict";
import { createCoalescer } from "./inflight.js";
import { RESYNC_MIN_GAP_MS, shouldRefetchOnConnect, shouldResync } from "./startup.js";

const deferred = () => {
  let resolve, reject;
  const promise = new Promise((res, rej) => ((resolve = res), (reject = rej)));
  return { promise, resolve, reject };
};

test("coalescer: concurrent callers of one URL share a single request", async () => {
  const run = createCoalescer();
  let calls = 0;
  const gate = deferred();
  const fetcher = () => (calls++, gate.promise);
  const a = run("/api/players", fetcher);
  const b = run("/api/players", fetcher);
  const c = run("/api/players", fetcher);
  gate.resolve({ players: [] });
  const [ra, rb, rc] = await Promise.all([a, b, c]);
  assert.equal(calls, 1);
  assert.equal(ra, rb);
  assert.equal(rb, rc);
});

test("coalescer: different URLs do not share; a settled request is not reused", async () => {
  const run = createCoalescer();
  let calls = 0;
  const fetcher = () => (calls++, Promise.resolve(calls));
  const [x, y] = await Promise.all([run("/api/a", fetcher), run("/api/b", fetcher)]);
  assert.equal(calls, 2);
  assert.notEqual(x, y);
  assert.equal(await run("/api/a", fetcher), 3); // the first /api/a settled, so this is a fresh request
});

test("coalescer: a failure is shared by the waiting callers, then forgotten (the next call retries)", async () => {
  const run = createCoalescer();
  let calls = 0;
  const gate = deferred();
  const fetcher = () => (calls++, calls === 1 ? gate.promise : Promise.resolve("ok"));
  const a = run("/api/x", fetcher).catch((e) => e.message);
  const b = run("/api/x", fetcher).catch((e) => e.message);
  gate.reject(new Error("boom"));
  assert.deepEqual(await Promise.all([a, b]), ["boom", "boom"]);
  assert.equal(calls, 1);
  assert.equal(await run("/api/x", fetcher), "ok");
  assert.equal(calls, 2);
});

test("coalescer: a synchronous throw becomes a rejection and does not wedge the key", async () => {
  const run = createCoalescer();
  await assert.rejects(run("/api/y", () => { throw new Error("sync"); }), /sync/);
  assert.equal(await run("/api/y", () => Promise.resolve(7)), 7);
});

test("startup: the first stream open right after the initial snapshot does not fetch it again", () => {
  assert.equal(shouldResync({ wasPolling: false, sinceLastMs: 900 }), false); // the 925 ms second wave seen in the trace
  assert.equal(shouldResync({ wasPolling: false, sinceLastMs: RESYNC_MIN_GAP_MS }), true);
});

test("startup: a first snapshot that never loaded is retried on the first open", () => {
  assert.equal(shouldResync({ wasPolling: false, sinceLastMs: 100, ready: false }), true);
});

test("startup: coming back from polling resyncs, unless polling just did", () => {
  assert.equal(shouldResync({ wasPolling: true, sinceLastMs: 5000 }), true);
  assert.equal(shouldResync({ wasPolling: true, sinceLastMs: 200 }), false);
});

test("startup: telemetry refetches on connect only after a failed first load or a real reconnect", () => {
  // first connect while the first load is still running: skip (this was the duplicate /api/sessions + events + health)
  assert.equal(shouldRefetchOnConnect({ prev: "connecting", everLive: false, loadedOk: false, refreshing: true }), false);
  // first connect, first load already succeeded: skip
  assert.equal(shouldRefetchOnConnect({ prev: "connecting", everLive: false, loadedOk: true, refreshing: false }), false);
  // server was asleep, the first load failed: refetch once the stream is up
  assert.equal(shouldRefetchOnConnect({ prev: "connecting", everLive: false, loadedOk: false, refreshing: false }), true);
  // a real drop and reconnect
  assert.equal(shouldRefetchOnConnect({ prev: "connecting", everLive: true, loadedOk: true, refreshing: false }), true);
  assert.equal(shouldRefetchOnConnect({ prev: "offline", everLive: true, loadedOk: true, refreshing: false }), true);
});
