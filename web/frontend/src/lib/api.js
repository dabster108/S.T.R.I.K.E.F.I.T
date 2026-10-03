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

// quiet: never reported to the wake store (the public landing page shows no server state).
async function request(path, { method = "GET", body, timeoutMs = 75000, quiet = false } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  let slow = false;
  const slowTimer = quiet
    ? null
    : setTimeout(() => {
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
  playerEngagement: (name) => request(`/api/players/${enc(name)}/engagement${qs({ tz: Intl.DateTimeFormat().resolvedOptions().timeZone })}`),
  // Returns the renamed player's profile (same shape as player()).
  renamePlayer: (name, newName) => request(`/api/players/${enc(name)}`, { method: "PATCH", body: { newName }, timeoutMs: 20000 }),
  overview: () => request(`/api/overview${qs({ tz: Intl.DateTimeFormat().resolvedOptions().timeZone })}`),
  calorieInsights: () => request(`/api/insights/calories${qs({ tz: Intl.DateTimeFormat().resolvedOptions().timeZone })}`),
  publicStats: () => request("/api/public-stats"),
  // Landing page: short timeouts, no wake-up signalling, callers swallow failures.
  liveQuiet: () => request("/api/live", { quiet: true, timeoutMs: 12000 }),
  publicStatsQuiet: () => request("/api/public-stats", { quiet: true, timeoutMs: 20000 }),
  analytics: (filters) => request(`/api/analytics${qs(filters)}`),
  exportUrl: (kind, filters) => `${API_BASE}/api/export/${kind}.csv${qs(filters)}`,
  streamUrl: () => `${API_BASE}/api/stream`,
};

// Fire-and-forget request that makes a sleeping Render instance start booting
// while the visitor reads the landing page. Never throws, never reports.
let warmed = false;
export function warmUp() {
  if (warmed || typeof fetch === "undefined") return;
  warmed = true;
  fetch(`${API_BASE}/api/health`, { cache: "no-store", keepalive: true }).catch(() => {});
}
