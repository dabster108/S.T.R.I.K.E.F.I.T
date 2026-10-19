import { createDemoApi } from "./handler.js";
import { createEngine } from "./engine.js";
import { createFakeEventSource } from "./fakeSource.js";

// Switches the page to demo data: every /api request is answered by the recorded snapshot (handler.js) and the live stream is the
// scripted fights (engine.js). Imported only when demo mode is on, so a normal visit never downloads any of it (snapshot.json is the
// big part). Runs before the app renders.
export async function installDemo() {
  const [{ default: snapshot }, { buildFight }] = await Promise.all([import("./snapshot.json"), import("./fightApp.js")]);
  const engine = createEngine({ buildFight });
  let storage = null;
  try {
    storage = sessionStorage;
  } catch {
    /* blocked */
  }
  const api = createDemoApi({ snapshot, engine, storage });

  const realFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const raw = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    let url;
    try {
      url = new URL(raw, window.location.href);
    } catch {
      return realFetch(input, init);
    }
    if (!url.pathname.startsWith("/api/")) return realFetch(input, init);
    let body = null;
    if (typeof init.body === "string") {
      try {
        body = JSON.parse(init.body);
      } catch {
        body = null;
      }
    }
    const out = api.handle(init.method || (input instanceof Request ? input.method : "GET"), url.pathname + url.search, body);
    return new Response(out.body, { status: out.status, headers: { "Content-Type": out.type, ...(out.headers || {}) } });
  };
  window.EventSource = createFakeEventSource(engine);
  window.__demo = { days: api.days };
}
