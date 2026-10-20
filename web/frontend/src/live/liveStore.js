// One shared real-time connection for the whole app.
//
// EventSource on /api/stream is the primary channel. If it drops, the store
// reconnects with exponential backoff and, until the stream is back, polls
// /api/live (and recent events) every 3 s so live views keep moving.
// Components subscribe to narrow slices (one session, the id list, the feed,
// the connection state) so an event only re-renders what it changed.

import { whenIdle } from "../lib/idle.js";
import { api } from "../lib/api.js";
import { shouldResync } from "../lib/startup.js";
import { bindPageLifecycle } from "../lib/pagelife.js";

const FEED_MAX = 250;
const POLL_MS = 3000;
const WATCHDOG_MS = 35_000; // server heartbeats every 15 s: two missed beats = dead
const BACKOFF = [1000, 2000, 4000, 8000, 15000, 30000];
const FINISHED_LINGER_MS = 30_000; // a finished fight stays in the store a little longer than the arena shows it (RESULT_HOLD_MS in lib/arena.js)
const QUIET_TYPES = new Set(["hp", "ping"]); // stored, but too frequent for the feed

const state = {
  connection: "connecting", // connecting | connected | reconnecting | offline
  mode: "stream", // stream | polling
  lastMessageAt: 0,
  ids: [], // live (and just-finished) session ids, oldest first
  sessions: new Map(), // id -> { session, liveState, finishedAt? }
  feed: [], // newest first
  ready: false, // first snapshot loaded
};

const listeners = { connection: new Set(), ids: new Set(), feed: new Set(), session: new Map(), message: new Set() };

function emit(set) {
  for (const l of set) l();
}
function emitSession(id) {
  const set = listeners.session.get(id);
  if (set) emit(set);
}

function setConnection(connection, mode = state.mode) {
  if (state.connection === connection && state.mode === mode) return;
  state.connection = connection;
  state.mode = mode;
  emit(listeners.connection);
}

// ---------------------------------------------------------------- sessions

function sameIds(a, b) {
  return a.length === b.length && a.every((x, i) => x === b[i]);
}

function recomputeIds() {
  const next = [...state.sessions.values()]
    .sort((a, b) => new Date(a.session.startedAt) - new Date(b.session.startedAt))
    .map((e) => e.session.id);
  if (!sameIds(next, state.ids)) {
    state.ids = next;
    emit(listeners.ids);
  }
}

const lingerTimers = new Map();

// extra: the fields a finished fight's message adds (newRecords: the personal bests it set, rankUp: a rank it reached). They are kept
// on the entry so the result moment can show them; later messages for the same fight do not wipe them.
function upsertSession(session, liveState, extra) {
  if (!session?.id) return;
  const prev = state.sessions.get(session.id);
  const entry = {
    session,
    liveState: liveState ?? prev?.liveState ?? null,
    newRecords: extra && "newRecords" in extra ? extra.newRecords : prev?.newRecords,
    rankUp: extra && "rankUp" in extra ? extra.rankUp : prev?.rankUp,
  };
  if (session.status !== "live") {
    if (!prev) return; // a finished fight we never saw live: nothing to show
    entry.finishedAt = prev.finishedAt || Date.now();
    clearTimeout(lingerTimers.get(session.id));
    lingerTimers.set(session.id, setTimeout(() => removeSession(session.id), FINISHED_LINGER_MS));
  }
  state.sessions.set(session.id, entry);
  emitSession(session.id);
  recomputeIds();
}

function removeSession(id) {
  clearTimeout(lingerTimers.get(id));
  lingerTimers.delete(id);
  if (state.sessions.delete(id)) {
    emitSession(id);
    recomputeIds();
  }
}

// Replaces the live set with a /api/live snapshot, keeping just-finished fights lingering.
function applySnapshot(list) {
  const seen = new Set();
  for (const s of list) {
    seen.add(s.id);
    const { liveState, ...session } = s;
    const prev = state.sessions.get(s.id);
    if (!prev || JSON.stringify(prev.liveState) !== JSON.stringify(liveState) || prev.session.status !== session.status) {
      upsertSession(session, liveState);
    }
  }
  for (const [id, entry] of state.sessions) {
    if (!seen.has(id) && !entry.finishedAt) {
      // Went away while we weren't listening: show it as finished briefly.
      upsertSession({ ...entry.session, status: "finished", outcome: entry.session.outcome === "in_progress" ? "unknown" : entry.session.outcome }, entry.liveState);
    }
  }
  if (!state.ready) {
    state.ready = true;
    emit(listeners.ids);
  }
}

// ---------------------------------------------------------------- feed

function pushFeed(events, { seeded = false } = {}) {
  const have = new Set(state.feed.map((e) => e.id));
  const fresh = events.filter((e) => e && !QUIET_TYPES.has(e.type) && !have.has(e.id)).map((e) => ({ ...e, seeded }));
  if (!fresh.length) return;
  state.feed = [...fresh, ...state.feed]
    .sort((a, b) => new Date(b.at) - new Date(a.at))
    .slice(0, FEED_MAX);
  emit(listeners.feed);
}

// ---------------------------------------------------------------- renames

// A rename is applied twice in the tab that made it: straight after the PATCH
// answers, then again when the stream echoes it. The second one is skipped.
const RENAME_DEDUPE_MS = 15_000;
const recentRenames = new Map(); // "old\u0000new" -> ms

function renameSeen(payload) {
  const key = `${payload.oldName}\u0000${payload.newName}`;
  const now = Date.now();
  for (const [k, t] of recentRenames) if (now - t > RENAME_DEDUPE_MS) recentRenames.delete(k);
  if (recentRenames.has(key)) return true;
  recentRenames.set(key, now);
  recentRenames.delete(`${payload.newName}\u0000${payload.oldName}`); // renaming back is a new change
  return false;
}

function applyRename({ oldName, newName }) {
  for (const [id, entry] of state.sessions) {
    if (entry.session.playerName !== oldName) continue;
    state.sessions.set(id, { ...entry, session: { ...entry.session, playerName: newName } });
    emitSession(id);
  }
  if (state.feed.some((e) => e.playerName === oldName)) {
    state.feed = state.feed.map((e) => (e.playerName === oldName ? { ...e, playerName: newName } : e));
    emit(listeners.feed);
  }
}

// ---------------------------------------------------------------- transport

let es = null;
let attempt = 0;
let reconnectTimer = null;
let pollTimer = null;
let watchdog = null;
let started = false;

function handle(type, payload) {
  state.lastMessageAt = Date.now();
  if (type === "player_renamed" && (!payload?.oldName || !payload?.newName || renameSeen(payload))) return;
  switch (type) {
    case "session_started":
    case "session_updated":
      upsertSession(payload.session, payload.liveState);
      break;
    case "session_finished":
      upsertSession(payload.session, payload.liveState, payload);
      break;
    case "event_created":
      pushFeed([payload]);
      break;
    case "session_deleted":
      if (payload.all) {
        for (const id of [...state.sessions.keys()]) removeSession(id);
        state.feed = [];
        emit(listeners.feed);
      } else {
        removeSession(payload.id);
        const before = state.feed.length;
        state.feed = state.feed.filter((e) => e.sessionId !== payload.id);
        if (state.feed.length !== before) emit(listeners.feed);
      }
      break;
    case "player_renamed":
      applyRename(payload);
      break;
    default:
      break;
  }
  for (const l of listeners.message) l(type, payload);
}

let lastResyncAt = 0;
async function resync({ seedFeed }) {
  lastResyncAt = Date.now();
  try {
    const live = await api.live();
    applySnapshot(live.sessions || []);
    if (seedFeed) {
      // The kill feed is only on screen on the Live Arena and Broadcast pages; elsewhere its 35 kB seed waits for an idle moment.
      if (!/^\/dashboard\/(live|broadcast)/.test(location.pathname)) await whenIdle();
      pushFeed(await api.events({ limit: 80 }), { seeded: true });
    }
  } catch {
    /* the connection state already tells the user */
  }
}

function startPolling() {
  if (pollTimer) return;
  // A tick that is still waiting on a slow server must not be joined by the next one every 3 s: stacked polls used up
  // the browser's six connections and starved the page's own requests.
  let ticking = false;
  const tick = async () => {
    if (ticking) return;
    ticking = true;
    lastResyncAt = Date.now();
    try {
      const [live, events] = await Promise.all([api.live(), api.events({ limit: 30 })]);
      applySnapshot(live.sessions || []);
      pushFeed(events);
      if (state.connection === "offline") setConnection("reconnecting", "polling");
    } catch {
      setConnection("offline", "polling");
    } finally {
      ticking = false;
    }
  };
  tick();
  pollTimer = setInterval(tick, POLL_MS);
}

function stopPolling() {
  clearInterval(pollTimer);
  pollTimer = null;
}

function armWatchdog() {
  clearTimeout(watchdog);
  watchdog = setTimeout(() => {
    // No heartbeat for too long: the connection is silently dead.
    if (es) dropped();
  }, WATCHDOG_MS);
}

function dropped() {
  if (es) {
    es.close();
    es = null;
  }
  clearTimeout(watchdog);
  setConnection(attempt >= 3 ? "offline" : "reconnecting", "polling");
  startPolling();
  const delay = BACKOFF[Math.min(attempt, BACKOFF.length - 1)];
  attempt++;
  clearTimeout(reconnectTimer);
  reconnectTimer = setTimeout(connect, delay);
}

// goal_set / goal_met were broadcast by the server but never subscribed here, so the goal card's live refresh never fired;
// challenge_done and reaction arrived with the engagement pass.
const TYPES = ["hello", "heartbeat", "session_started", "session_updated", "session_finished", "event_created", "session_deleted", "player_renamed", "feedback_saved", "goal_set", "goal_met", "challenge_done", "reaction"];

function connect() {
  // Only ever one connection: a pending backoff retry and a tab refocus can both land here.
  clearTimeout(reconnectTimer);
  if (es) {
    es.close();
    es = null;
  }
  if (typeof EventSource === "undefined") {
    setConnection("reconnecting", "polling");
    startPolling();
    return;
  }
  try {
    es = new EventSource(api.streamUrl());
  } catch {
    dropped();
    return;
  }
  const first = !state.ready;
  es.onopen = () => {
    const wasPolling = state.mode === "polling" || first;
    attempt = 0;
    stopPolling();
    setConnection("connected", "stream");
    armWatchdog();
    // Anything that happened while we were away arrives via the snapshot. The very first open comes about a second
    // after startLive() fetched one, so it is not fetched twice (startup.js).
    if (shouldResync({ wasPolling, sinceLastMs: Date.now() - lastResyncAt, ready: state.ready })) resync({ seedFeed: wasPolling || state.feed.length === 0 });
  };
  es.onerror = () => {
    // EventSource retries by itself, but not after an HTTP/CORS failure, and
    // its retries are invisible to us; take over so the indicator stays honest.
    dropped();
  };
  for (const type of TYPES) {
    es.addEventListener(type, (e) => {
      armWatchdog();
      try {
        const msg = JSON.parse(e.data);
        handle(type, msg.payload);
      } catch {
        /* ignore a malformed frame */
      }
    });
  }
}

// Called by the tab that renamed a player, so it updates without waiting for the stream.
export function announceRename(change) {
  handle("player_renamed", change);
}

// Lets go of the stream and the polling when the page is hidden for good or parked in the back/forward cache.
function releaseConnections() {
  clearTimeout(reconnectTimer);
  clearTimeout(watchdog);
  stopPolling();
  if (es) {
    es.close();
    es = null;
  }
}

export function startLive() {
  if (started) return;
  started = true;
  // A cached page must not keep its event stream open (it starved the next page of connections, see lib/pagelife.js);
  // when the browser restores it, reconnect (the open handler fetches a fresh snapshot).
  bindPageLifecycle(window, {
    onHide: releaseConnections,
    onShow: () => {
      attempt = 0;
      setConnection("connecting", "stream");
      connect();
    },
  });
  resync({ seedFeed: true }); // first paint shouldn't wait for the stream
  connect();
  // Coming back to a backgrounded tab: reconnect immediately instead of waiting out the backoff.
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && !es) {
      clearTimeout(reconnectTimer);
      connect();
    }
  });
}

// ---------------------------------------------------------------- subscriptions (for useSyncExternalStore)

function sub(set) {
  return (l) => {
    set.add(l);
    return () => set.delete(l);
  };
}

export const liveStore = {
  subscribeConnection: sub(listeners.connection),
  subscribeIds: sub(listeners.ids),
  subscribeFeed: sub(listeners.feed),
  subscribeSession(id) {
    return (l) => {
      if (!listeners.session.has(id)) listeners.session.set(id, new Set());
      listeners.session.get(id).add(l);
      return () => listeners.session.get(id)?.delete(l);
    };
  },
  onMessage(l) {
    listeners.message.add(l);
    return () => listeners.message.delete(l);
  },
  getConnection: () => state.connection,
  getMode: () => state.mode,
  getIds: () => state.ids,
  isReady: () => state.ready,
  getSession: (id) => state.sessions.get(id),
  getFeed: () => state.feed,
};
