// Builds frontend/src/demo/snapshot.json: the recorded API answers the site serves in DEMO MODE (?demo=1), so the whole dashboard runs
// with no backend and no internet. It reads a LOCAL dev backend that holds the seeded ZZTEST fights (npm run dev:safe, then
// npm run dev:seed-study) and renames the ZZTEST players to friendly demo names. It refuses any backend that is not on this machine.
//
//   node web/tools/make-demo-snapshot.mjs [--url http://localhost:4100]
//
// Run it again after changing the seed data. The output is committed; it is only loaded when demo mode is on.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => (process.argv.includes(`--${k}`) ? process.argv[process.argv.indexOf(`--${k}`) + 1] : d);
const BASE = String(arg("url", "http://localhost:4000")).replace(/\/+$/, "");
const host = new URL(BASE).hostname;
if (!["localhost", "127.0.0.1", "::1", "[::1]"].includes(host)) {
  console.error(`refusing: ${host} is not this machine. The demo snapshot is only ever taken from the local dev backend.`);
  process.exit(1);
}

const NAMES = { "ZZTEST-ann": "Ava", "ZZTEST-bo": "Ben", "ZZTEST-cy": "Cleo", "ZZTEST-dee": "Dev" };
const TZ = "Asia/Kathmandu";
const get = async (p, text = false) => {
  const r = await fetch(BASE + p);
  if (!r.ok) return null;
  return text ? r.text() : r.json();
};
const rename = (s) => {
  let out = s;
  for (const [from, to] of Object.entries(NAMES)) out = out.split(from).join(to).split(encodeURIComponent(from)).join(to);
  return out.replace(/ZZTEST-P(\d\d)/g, "P$1");
};
// The key the browser-side handler will look up: path plus the query without tz and without empty / "all" values, sorted.
const keyOf = (p) => {
  const u = new URL(p, "http://x");
  const q = [...u.searchParams].filter(([k, v]) => k !== "tz" && v !== "" && v !== "all").sort(([a], [b]) => a.localeCompare(b));
  return u.pathname + (q.length ? "?" + q.map(([k, v]) => `${k}=${v}`).join("&") : "");
};

const out = { capturedAt: Date.now(), json: {}, csv: {} };
const put = async (p) => {
  const data = await get(p);
  if (data) out.json[keyOf(rename(p))] = JSON.parse(rename(JSON.stringify(data)));
  else console.log("  (no answer)", p);
};

const GLOBAL = [
  "/api/health", "/api/stats", "/api/players", `/api/overview?tz=${TZ}`, "/api/public-stats", "/api/analytics", "/api/analytics?level=Easy", "/api/analytics?level=Medium", "/api/analytics?level=Hard",
  "/api/analytics/feedback", `/api/insights/calories?tz=${TZ}`, "/api/lab", "/api/calendar?weeks=26", "/api/activity?limit=20", "/api/activity?limit=10",
  "/api/leaderboard?level=all&limit=10", "/api/leaderboard?level=Easy&limit=10", "/api/leaderboard?level=Medium&limit=10", "/api/leaderboard?level=Hard&limit=10",
  "/api/live/reactions", "/api/sessions", "/api/events?limit=300",
];
const PLAYER = ["", "/rank", "/milestones", "/records", "/engagement", "/achievements", "/goal/today", "/goals?days=14", "/challenges", "/challenges/history?days=30", "/calendar?weeks=26", "/week", "/week?week=last", "/best", "/fade?last=10", "/fingerprint", "/insights", "/checklist",
  "/best-pace?level=Easy", "/best-pace?level=Medium", "/best-pace?level=Hard"];

for (const p of GLOBAL) await put(p);
for (const old of Object.keys(NAMES)) for (const s of PLAYER) await put(`/api/players/${encodeURIComponent(old)}${s}`);
// STRIKEFIT Wrapped: every week a player has fights in (under its original date, the handler maps a requested week back to it), and the
// default answer is their LATEST week with fights, so the demo opens on something to show whatever day it is.
for (const old of Object.keys(NAMES)) {
  const base = `/api/players/${encodeURIComponent(old)}/wrapped`;
  const first = await get(base);
  if (!first) continue;
  const weeks = (first.weeks || []).slice(0, 8);
  for (const w of weeks) await put(`${base}?weekStart=${w.weekStart}`);
  if (weeks.length) {
    const latest = await get(`${base}?weekStart=${weeks[0].weekStart}`);
    out.json[keyOf(rename(base))] = JSON.parse(rename(JSON.stringify(latest)));
  } else await put(base);
}
const sessions = (await get("/api/sessions")).sessions;
// A fight's detail page carries every event of the fight (about 200 kB each), so only a handful have one: the newest fight of each named player and a few more.
const shown = [...new Map(sessions.filter((s) => s.playerName in NAMES).map((s) => [s.playerName, s])).values(), ...sessions.filter((s) => s.playerName in NAMES).slice(0, 8)].filter((s, i, a) => a.indexOf(s) === i).slice(0, 8);
for (const s of shown) for (const sfx of ["", "/fade"]) await put(`/api/sessions/${s.id}${sfx}`);
for (const s of shown) await put(`/api/challenge/${s.id}`);

for (const [k, p] of [["export/sessions.csv", "/api/export/sessions.csv"], ["export/events.csv", "/api/export/events.csv"], ["lab/sessions.csv", "/api/lab/sessions.csv"], ["study/export.csv", "/api/study/export.csv"]]) {
  const t = await get(p, true);
  if (t) out.csv[k] = rename(t);
}
// A session detail keeps every other event (the replay still plays, just coarser).
for (const k of Object.keys(out.json)) {
  const d = out.json[k];
  if (/^\/api\/sessions\/[0-9a-f]{32}$/.test(k) && Array.isArray(d.events)) d.events = d.events.filter((_, i) => i % 2 === 0);
}
for (const k of Object.keys(out.csv)) if (k === "export/events.csv") out.csv[k] = out.csv[k].split("\n").slice(0, 500).join("\n") + "\n";
// Keep the events list modest: the demo's activity lists never show more than a screenful.
for (const k of Object.keys(out.json)) if (k.startsWith("/api/events") && Array.isArray(out.json[k].events)) out.json[k].events = out.json[k].events.slice(0, 120);
const file = path.join(here, "..", "frontend", "src", "demo", "snapshot.json");
fs.mkdirSync(path.dirname(file), { recursive: true });
fs.writeFileSync(file, JSON.stringify(out));
const kb = (fs.statSync(file).size / 1024).toFixed(0);
console.log(`wrote ${Object.keys(out.json).length} answers + ${Object.keys(out.csv).length} CSVs, ${kb} kB → frontend/src/demo/snapshot.json`);
