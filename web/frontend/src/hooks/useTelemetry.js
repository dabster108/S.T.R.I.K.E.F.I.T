import { useCallback, useEffect, useRef, useState } from "react";
import { io } from "socket.io-client";
import { API_BASE, api } from "../lib/api.js";

const HEALTH_INTERVAL_MS = 15000;
const MAX_EVENTS = 300;

// Owns all backend data. REST loads the full snapshot; the socket keeps it live.
// Every (re)connect refetches, so a backend restart never leaves stale data.
export function useTelemetry({ onSession, onConnectionChange } = {}) {
  const [sessions, setSessions] = useState([]);
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [connection, setConnection] = useState("connecting"); // connecting | live | offline
  const [health, setHealth] = useState(null);
  const [lastUpdate, setLastUpdate] = useState(null);

  const callbacks = useRef({ onSession, onConnectionChange });
  callbacks.current = { onSession, onConnectionChange };
  const socketRef = useRef(null);
  const pingWaiters = useRef(new Map());

  const refresh = useCallback(async () => {
    try {
      const [s, e] = await Promise.all([api.sessions(), api.events(MAX_EVENTS)]);
      setSessions(s);
      setEvents(e);
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
    refresh();
    checkHealth();
    const timer = setInterval(checkHealth, HEALTH_INTERVAL_MS);

    const socket = io(API_BASE || undefined, {
      transports: ["websocket", "polling"],
      timeout: 6000,
      reconnectionDelayMax: 5000,
    });
    socketRef.current = socket;

    const setConn = (state) => {
      setConnection((prev) => {
        if (prev !== state) callbacks.current.onConnectionChange?.(state, prev);
        return state;
      });
    };

    socket.on("connect", () => {
      setConn("live");
      refresh();
      checkHealth();
    });
    socket.on("disconnect", () => setConn("offline"));
    socket.on("connect_error", () => setConn("offline"));

    socket.on("session", (session) => {
      setSessions((prev) => [session, ...prev.filter((s) => s.id !== session.id)]);
      setLastUpdate(new Date().toISOString());
      callbacks.current.onSession?.(session);
    });

    socket.on("event", (event) => {
      const waiter = event.type === "ping" && pingWaiters.current.get(event.message);
      if (waiter) {
        waiter(event);
        pingWaiters.current.delete(event.message);
      }
      setEvents((prev) => [event, ...prev.filter((e) => e.id !== event.id)].slice(0, MAX_EVENTS));
      setLastUpdate(new Date().toISOString());
    });

    socket.on("session_deleted", ({ id }) => {
      setSessions((prev) => prev.filter((s) => s.id !== id));
      setEvents((prev) => prev.filter((e) => e.sessionId !== id));
    });

    socket.on("cleared", () => {
      setSessions([]);
      setEvents([]);
      setLastUpdate(new Date().toISOString());
    });

    return () => {
      clearInterval(timer);
      socket.disconnect();
      socketRef.current = null;
    };
  }, [refresh, checkHealth]);

  // Full round trip: POST /api/events → backend → socket broadcast → back here.
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

  return {
    sessions,
    events: events.filter((e) => e.type !== "ping"),
    loading,
    loadError,
    connection,
    health,
    lastUpdate,
    refresh,
    checkHealth,
    pingRoundTrip,
  };
}
