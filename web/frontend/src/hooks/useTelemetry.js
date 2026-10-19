import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { api } from "../lib/api.js";
import { liveStore, startLive } from "../live/liveStore.js";
import { shouldRefetchOnConnect } from "../lib/startup.js";

const HEALTH_INTERVAL_MS = 15000;
const MAX_EVENTS = 300;
// Stored, but too frequent for lists of "what happened" (one per second per fight).
const HIDDEN_TYPES = new Set(["ping", "hp"]);

// The stream store has four states; the original views know three.
const CONNECTION = { connected: "live", connecting: "connecting", reconnecting: "connecting", offline: "offline" };

// Owns the session/event lists for the original views. REST loads the snapshot;
// the one shared Server-Sent Events connection (live/liveStore.js) keeps it current.
// Every (re)connect refetches, so a backend restart never leaves stale data.
// withEvents: whether the (large) recent-events list is needed by the page on screen. Only the Overview shows it, so every other
// page skips that download; it is fetched the first time it is wanted, and the stream keeps it current from then on.
export function useTelemetry({ onSession, onConnectionChange, withEvents = true } = {}) {
  const [sessions, setSessions] = useState([]);
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [eventsReady, setEventsReady] = useState(false);   // the (large) events list arrives after the sessions: the page shows without waiting for it
  const [loadError, setLoadError] = useState(null);
  const [health, setHealth] = useState(null);
  const [lastUpdate, setLastUpdate] = useState(null);
  const connection = CONNECTION[useSyncExternalStore(liveStore.subscribeConnection, liveStore.getConnection)];

  const callbacks = useRef({ onSession, onConnectionChange });
  callbacks.current = { onSession, onConnectionChange };
  const pingWaiters = useRef(new Map());

  const loadedOk = useRef(false);
  const refreshing = useRef(false);
  const everLive = useRef(false);
  const wantEvents = useRef(withEvents);
  wantEvents.current = withEvents;
  const eventsLoaded = useRef(false);
  const eventsInFlight = useRef(false);

  const refresh = useCallback(async () => {
    refreshing.current = true;
    try {
      // Both requests start together; the page is shown as soon as the sessions are in (the events list is about four times as
      // big and only feeds "Latest activity").
      const eventsRequest = wantEvents.current ? api.events(MAX_EVENTS) : null;
      eventsInFlight.current = Boolean(eventsRequest);
      eventsRequest?.catch(() => {});   // handled below, after the sessions
      const s = await api.sessions();
      setSessions(s.filter((x) => x.status !== "live"));
      setLastUpdate(new Date().toISOString());
      loadedOk.current = true;
      setLoading(false);
      if (eventsRequest) {
        const e = await eventsRequest;
        setEvents(e.filter((ev) => !HIDDEN_TYPES.has(ev.type)));
        eventsLoaded.current = true;
        setEventsReady(true);
      }
      setLoadError(null);
    } catch (err) {
      setLoadError(err.message);
    } finally {
      refreshing.current = false;
      eventsInFlight.current = false;
      setLoading(false);
      if (wantEvents.current) setEventsReady(true);   // also after a failed request: the list then shows its empty state, not a skeleton forever
    }
  }, []);

  // The first time a page that shows the events list is opened (after the sessions were loaded without it).
  useEffect(() => {
    if (!withEvents || loading || eventsLoaded.current || eventsInFlight.current) return;
    eventsInFlight.current = true;
    setEventsReady(false);
    api
      .events(MAX_EVENTS)
      .then((e) => {
        setEvents((prev) => {
          const have = new Set(prev.map((x) => x.id));
          return [...prev, ...e.filter((ev) => !HIDDEN_TYPES.has(ev.type) && !have.has(ev.id))].sort((a, b) => new Date(b.at) - new Date(a.at)).slice(0, MAX_EVENTS);
        });
        eventsLoaded.current = true;
      })
      .catch(() => {})
      .finally(() => {
        eventsInFlight.current = false;
        setEventsReady(true); // a failed request shows the empty state, not a skeleton forever
      });
  }, [withEvents, loading]);

  const checkHealth = useCallback(async () => {
    const t0 = performance.now();
    try {
      const data = await api.health();
      setHealth({ ...data, latencyMs: Math.round(performance.now() - t0), checkedAt: new Date().toISOString() });
      return true;
    } catch (err) {
      setHealth({ ok: false, error: err.message, checkedAt: new Date().toISOString() });
      return false;
    }
  }, []);

  useEffect(() => {
    startLive();
    refresh();
    checkHealth();
    const timer = setInterval(checkHealth, HEALTH_INTERVAL_MS);

    const off = liveStore.onMessage((type, payload) => {
      if (type === "event_created") {
        const waiter = payload.type === "ping" && pingWaiters.current.get(payload.message);
        if (waiter) {
          waiter(payload);
          pingWaiters.current.delete(payload.message);
        }
        if (HIDDEN_TYPES.has(payload.type)) return;
        setEvents((prev) => [payload, ...prev.filter((e) => e.id !== payload.id)].slice(0, MAX_EVENTS));
        setLastUpdate(new Date().toISOString());
      } else if (type === "session_finished") {
        // These lists hold finished battles (as before live sessions existed); fights in
        // progress, and their many session_updated messages, belong to the live components.
        const s = payload.session;
        setSessions((prev) => [s, ...prev.filter((x) => x.id !== s.id)]);
        setLastUpdate(new Date().toISOString());
        callbacks.current.onSession?.(s);
      } else if (type === "player_renamed") {
        const rename = (x) => (x.playerName === payload.oldName ? { ...x, playerName: payload.newName } : x);
        setSessions((prev) => prev.map(rename));
        setEvents((prev) => prev.map(rename));
        setLastUpdate(new Date().toISOString());
      } else if (type === "session_deleted") {
        if (payload.all) {
          setSessions([]);
          setEvents([]);
        } else {
          setSessions((prev) => prev.filter((s) => s.id !== payload.id));
          setEvents((prev) => prev.filter((e) => e.sessionId !== payload.id));
        }
      }
    });

    return () => {
      clearInterval(timer);
      off();
    };
  }, [refresh, checkHealth]);

  // Reconnected after a drop: refetch, and tell the shell (toasts).
  const prevConnection = useRef(connection);
  useEffect(() => {
    const prev = prevConnection.current;
    prevConnection.current = connection;
    if (prev === connection) return;
    callbacks.current.onConnectionChange?.(connection, prev);
    if (connection === "live" && prev !== "live") {
      // Not on the very first connect while the first load is running: that repeated the same five requests ~1 s later.
      if (shouldRefetchOnConnect({ prev, everLive: everLive.current, loadedOk: loadedOk.current, refreshing: refreshing.current })) {
        refresh();
        checkHealth();
      }
      everLive.current = true;
    }
  }, [connection, refresh, checkHealth]);

  // Full round trip: POST /api/events -> backend -> stream broadcast -> back here.
  const pingRoundTrip = useCallback(async () => {
    const token = `ping-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
    const t0 = performance.now();
    const arrived = new Promise((resolve, reject) => {
      pingWaiters.current.set(token, resolve);
      setTimeout(() => {
        pingWaiters.current.delete(token);
        reject(new Error("No realtime echo within 5s"));
      }, 5000);
    });
    arrived.catch(() => {}); // surfaced by the await below; avoids an unhandled rejection if the POST fails
    const posted = await api.ping(token);
    const httpMs = Math.round(performance.now() - t0);
    await arrived;
    return { httpMs, socketMs: Math.round(performance.now() - t0), eventId: posted.event.id };
  }, []);

  return { sessions, events, eventsReady, loading, loadError, connection, health, lastUpdate, refresh, checkHealth, pingRoundTrip };
}
