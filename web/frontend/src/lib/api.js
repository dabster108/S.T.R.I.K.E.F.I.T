// Thin client for web/backend. Empty base = same origin (Vite dev proxy, or the
// backend serving the built dashboard). Set VITE_API_URL to point elsewhere (Vercel -> Render).
export const API_BASE = (import.meta.env.VITE_API_URL || "").trim().replace(/\/$/, "");

// Render's free tier sleeps; a cold start can take up to a minute. Any request
// slower than this marks the server as "waking" until it answers.
const SLOW_AFTER_MS = 3500;

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

const wake = { slow: 0, listeners: new Set() };
const emitWake = () => wake.listeners.forEach((l) => l());
export const wakeStore = {
  subscribe(l) {
    wake.listeners.add(l);
    return () => wake.listeners.delete(l);
  },
  isWaking: () => wake.slow > 0,
};

async function request(path, { method = "GET", body, timeoutMs = 75000 } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  let slow = false;
  const slowTimer = setTimeout(() => {
    slow = true;
    wake.slow++;
    emitWake();
  }, SLOW_AFTER_MS);
  try {
    const res = await fetch(`${API_BASE}${path}`, {
      method,
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });
    const data = await res.json().catch(() => null);
    if (!res.ok) throw new ApiError(data?.error || `${method} ${path} failed (${res.status})`, res.status);
    return data;
  } catch (err) {
    if (err.name === "AbortError") throw new ApiError(`${method} ${path} timed out`, 0);
    if (err instanceof ApiError) throw err;
    throw new ApiError("Backend unreachable", 0);
  } finally {
    clearTimeout(timer);
    clearTimeout(slowTimer);
    if (slow) {
      wake.slow--;
      emitWake();
    }
  }
}

const qs = (params = {}) => {
  const s = new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== "" && v !== "all")).toString();
  return s ? `?${s}` : "";
};
const enc = encodeURIComponent;

export const api = {
  health: () => request("/api/health", { timeoutMs: 10000 }),
  sessions: (filters) => request(`/api/sessions${qs(filters)}`).then((d) => d.sessions || []),
  session: (id) => request(`/api/sessions/${enc(id)}`),
  // Accepts a limit (original signature) or a filter object.
  events: (arg = 200) => request(`/api/events${qs(typeof arg === "number" ? { limit: arg } : arg)}`).then((d) => d.events || []),
  deleteSession: (id) => request(`/api/sessions/${enc(id)}`, { method: "DELETE" }),
  clearAll: () => request("/api/sessions", { method: "DELETE" }),
  ping: (message) => request("/api/events", { method: "POST", body: { type: "ping", playerName: "Dashboard", message } }),

  live: (player) => request(`/api/live${qs({ player })}`),
  players: () => request("/api/players").then((d) => d.players || []),
  player: (name) => request(`/api/players/${enc(name)}`),
  overview: () => request(`/api/overview${qs({ tz: Intl.DateTimeFormat().resolvedOptions().timeZone })}`),
  publicStats: () => request("/api/public-stats"),
  analytics: (filters) => request(`/api/analytics${qs(filters)}`),
  exportUrl: (kind, filters) => `${API_BASE}/api/export/${kind}.csv${qs(filters)}`,
  streamUrl: () => `${API_BASE}/api/stream`,
};
