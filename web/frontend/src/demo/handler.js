// The demo's stand-in for the whole HTTP API. A request goes in, a recorded answer comes out; nothing touches the network.
//   - GETs are answered from snapshot.json (recorded from the local dev backend by web/tools/make-demo-snapshot.mjs), with every
//     timestamp moved forward by whole days so the newest fight is "recent" whenever the demo is opened;
//   - /api/live, /api/events and /api/health also reflect the scripted fights the engine is playing;
//   - the study flow works in memory (consent, a code, both forms) so the viva can show it with no server;
//   - anything that would change data answers "the demo is read-only".
// Pure apart from the injected clock and the engine, so it is tested without a browser (demo.test.js).

const DAY = 864e5;
const ISO = /(\d{4})-(\d{2})-(\d{2})(T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)/g;
const BARE = /"(\d{4})-(\d{2})-(\d{2})"/g;

const shiftDate = (y, m, d, days) => new Date(Date.UTC(+y, +m - 1, +d) + days * DAY).toISOString().slice(0, 10);

/** Moves every ISO timestamp and bare "YYYY-MM-DD" string in `text` forward by `days` whole days. */
export function shiftTimes(text, days) {
  if (!days) return text;
  return text.replace(ISO, (_, y, m, d, rest) => shiftDate(y, m, d, days) + rest).replace(BARE, (_, y, m, d) => `"${shiftDate(y, m, d, days)}"`);
}

/** Whole days between the snapshot and now (never negative). */
export const daysBetween = (capturedAt, nowMs) => Math.max(0, Math.floor((nowMs - capturedAt) / DAY));

/** "/path?b=2&a=1&tz=X&level=all" -> "/path?a=1&b=2": the key snapshots are stored under (tz, empty and "all" values dropped, sorted). */
export function keyOf(pathAndQuery) {
  const u = new URL(pathAndQuery, "http://demo.invalid");
  const q = [...u.searchParams].filter(([k, v]) => k !== "tz" && v !== "" && v !== "all").sort(([a], [b]) => a.localeCompare(b));
  return u.pathname + (q.length ? `?${q.map(([k, v]) => `${k}=${v}`).join("&")}` : "");
}

const json = (body, status = 200) => ({ status, body: JSON.stringify(body), type: "application/json" });
const err = (status, error) => json({ ok: false, error }, status);
const READ_ONLY = "The demo is read-only: nothing you change here is saved.";

export function createDemoApi({ snapshot, engine, now = Date.now, storage = null }) {
  const days = daysBetween(snapshot.capturedAt, now());
  const data = JSON.parse(shiftTimes(JSON.stringify(snapshot.json), days));
  const csv = Object.fromEntries(Object.entries(snapshot.csv || {}).map(([k, v]) => [k, shiftTimes(v, days)]));

  // The study flow, in memory (and in sessionStorage, so a reload keeps it).
  const KEY = "strikefit.demo.study";
  const load = () => {
    try {
      return JSON.parse(storage?.getItem(KEY) || "null");
    } catch {
      return null;
    }
  };
  let study = load();
  const save = () => {
    try {
      storage?.setItem(KEY, JSON.stringify(study));
    } catch {
      /* private mode */
    }
  };
  const studyStatus = () => ({
    ok: true,
    code: study.code,
    consentedAt: study.consentedAt,
    preDone: Boolean(study.preAt),
    postDone: Boolean(study.postAt),
    // the demo's "fight" is the passage of a few seconds after the first form
    fights: { total: 1, finished: study.preAt && now() - study.preAt > 7000 ? 1 : 0, live: Boolean(study.preAt) && now() - study.preAt <= 7000, latestFinishedId: "demo-study-fight" },
  });

  function get(url) {
    const u = new URL(url, "http://demo.invalid");
    const path = u.pathname;
    const key = keyOf(url);

    if (path === "/api/live") {
      const sessions = engine ? engine.liveSessions() : [];
      return json({ sessions, count: sessions.length, liveTimeoutSec: 60, serverTime: new Date(now()).toISOString() });
    }
    if (path === "/api/health") {
      const h = data["/api/health"] || {};
      return json({ ...h, ok: true, time: new Date(now()).toISOString(), clients: 0, liveSessions: engine ? engine.liveSessions().length : 0 });
    }
    if (path === "/api/events") {
      const all = data["/api/events?limit=300"]?.events || [];
      const q = u.searchParams;
      const limit = Number(q.get("limit")) > 0 ? Math.trunc(Number(q.get("limit"))) : 200;
      const fromEngine = engine ? engine.recentEvents(limit) : [];
      let list = [...fromEngine, ...all];
      for (const [param, field] of [["player", "playerName"], ["sessionId", "sessionId"], ["type", "type"]]) if (q.get(param)) list = list.filter((e) => e[field] === q.get(param));
      return json({ events: list.slice(0, limit), count: Math.min(limit, list.length) });
    }
    if (path === "/api/sessions") {
      const all = data["/api/sessions"]?.sessions || [];
      const q = u.searchParams;
      let list = all;
      for (const [param, field] of [["player", "playerName"], ["level", "level"], ["outcome", "outcome"], ["status", "status"]]) if (q.get(param)) list = list.filter((s) => s[field] === q.get(param));
      const limit = Number(q.get("limit"));
      if (limit > 0) list = list.slice(0, Math.trunc(limit));
      return json({ sessions: list, count: list.length });
    }
    const csvMatch = /^\/api\/(export\/(?:sessions|events)|lab\/sessions|study\/export)\.csv$/.exec(path);
    if (csvMatch) {
      const name = `${csvMatch[1]}.csv`;
      const body = csv[name];
      if (!body) return err(404, "This export is not part of the demo data.");
      return { status: 200, type: "text/csv; charset=utf-8", body, headers: { "X-Row-Count": String(Math.max(0, body.split("\n").length - 2)), "Content-Disposition": `attachment; filename="strikefit-demo-${name.replace("/", "-")}"`, "Access-Control-Expose-Headers": "X-Row-Count, Content-Disposition" } };
    }
    if (/^\/api\/study\/participants\/[^/]+$/.test(path)) {
      return study && decodeURIComponent(path.split("/").pop()) === study.code ? json(studyStatus()) : err(404, "Unknown participant code.");
    }
    if (key in data) return json(data[key]);
    if (path in data) return json(data[path]);
    return err(404, "This page is not part of the demo data.");
  }

  function post(url, body) {
    const path = new URL(url, "http://demo.invalid").pathname;
    if (path === "/api/study/participants") {
      if (!body || body.consent !== true) return err(400, "Consent is required before a participant code is issued.");
      study = { code: "P11", consentedAt: new Date(now()).toISOString(), preAt: 0, postAt: 0 };
      save();
      return json({ ok: true, code: study.code, consentedAt: study.consentedAt, consentVersion: "demo" }, 201);
    }
    const m = /^\/api\/study\/participants\/([^/]+)\/(pre|post)$/.exec(path);
    if (m) {
      if (!study || decodeURIComponent(m[1]) !== study.code) return err(404, "Unknown participant code.");
      if (m[2] === "pre") study.preAt = now();
      else {
        if (!study.preAt) return err(409, "Answer the before-you-play form first.");
        study.postAt = now();
      }
      save();
      return json({ ok: true }, 201);
    }
    if (/^\/api\/sessions\/[^/]+\/feedback$/.test(path)) return json({ ok: true, feedback: { sessionId: path.split("/")[3], rpe: body?.rpe ?? null } }, 201);
    if (path === "/api/live/react" || path === "/api/events") return json({ ok: true });
    return err(403, READ_ONLY);
  }

  return {
    days,
    handle(method, url, body) {
      const m = String(method || "GET").toUpperCase();
      if (m === "GET" || m === "HEAD") return get(url);
      if (m === "POST") return post(url, body);
      return err(403, READ_ONLY);
    },
  };
}
