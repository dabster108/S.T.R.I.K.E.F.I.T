// Thin client for web/backend. Empty base = same origin (Vite dev proxy, or the
// backend serving the built dashboard). Set VITE_API_URL to point elsewhere.
export const API_BASE = (import.meta.env.VITE_API_URL || "").trim().replace(/\/$/, "");

export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.status = status;
  }
}

async function request(path, { method = "GET", body, timeoutMs = 8000 } = {}) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
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
  }
}

export const api = {
  health: () => request("/api/health", { timeoutMs: 4000 }),
  sessions: () => request("/api/sessions").then((d) => d.sessions || []),
  session: (id) => request(`/api/sessions/${encodeURIComponent(id)}`),
  events: (limit = 200) => request(`/api/events?limit=${limit}`).then((d) => d.events || []),
  deleteSession: (id) => request(`/api/sessions/${encodeURIComponent(id)}`, { method: "DELETE" }),
  clearAll: () => request("/api/sessions", { method: "DELETE" }),
  ping: (message) => request("/api/events", { method: "POST", body: { type: "ping", playerName: "Dashboard", message } }),
};
