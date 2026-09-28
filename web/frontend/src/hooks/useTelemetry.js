import { useCallback, useEffect, useRef, useState, useSyncExternalStore } from "react";
import { api } from "../lib/api.js";
import { liveStore, startLive } from "../live/liveStore.js";

const HEALTH_INTERVAL_MS = 15000;
const MAX_EVENTS = 300;
// Stored, but too frequent for lists of "what happened" (one per second per fight).
const HIDDEN_TYPES = new Set(["ping", "hp"]);

// The stream store has four states; the original views know three.
const CONNECTION = { connected: "live", connecting: "connecting", reconnecting: "connecting", offline: "offline" };

// Owns the session/event lists for the original views. REST loads the snapshot;
// the one shared Server-Sent Events connection (live/liveStore.js) keeps it current.
// Every (re)connect refetches, so a backend restart never leaves stale data.
export function useTelemetry({ onSession, onConnectionChange } = {}) {
  const [sessions, setSessions] = useState([]);
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [health, setHealth] = useState(null);
  const [lastUpdate, setLastUpdate] = useState(null);
  const connection = CONNECTION[useSyncExternalStore(liveStore.subscribeConnection, liveStore.getConnection)];

  const callbacks = useRef({ onSession, onConnectionChange });
  callbacks.current = { onSession, onConnectionChange };
  const pingWaiters = useRef(new Map());

  const refresh = useCallback(async () => {
    try {
      const [s, e] = await Promise.all([api.sessions(), api.events(MAX_EVENTS)]);
      setSessions(s.filter((x) => x.status !== "live"));
      setEvents(e.filter((ev) => !HIDDEN_TYPES.has(ev.type)));
      setLoadError(null);
      setLastUpdate(new Date().toISOString());
    } catch (err) {
      setLoadError(err.message);
    } finally {
      setLoading(false);
    }
  }, []);

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
      refresh();
      checkHealth();
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

  return { sessions, events, loading, loadError, connection, health, lastUpdate, refresh, checkHealth, pingRoundTrip };
}
