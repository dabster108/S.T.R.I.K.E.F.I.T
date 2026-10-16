// Regression check for the "stuck page" bug: after full-page navigations the server must still see ONE live-stream client.
//
// Cause it guards against: the browser's back/forward cache kept old pages alive with their EventSource open, which filled the
// browser's six connections per host and starved the next page (blank page, or a skeleton that never filled). liveStore.js now
// closes the stream on `pagehide` (lib/pagelife.js).
//
// Usage (backend and a built dashboard must be running, Brave or Chrome installed):
//   node scripts/check-stream-release.mjs [http://localhost:4000] [pages=8]
// Exit code 1 when the stream-client count grew by more than 1 over the baseline.
import { spawn } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const { WebSocket } = require("../../backend/node_modules/ws");
const BASE = (process.argv[2] || "http://localhost:4000").replace(/\/$/, "");
const PAGES = Number(process.argv[3]) || 8;
const BROWSERS = ["/Applications/Brave Browser.app/Contents/MacOS/Brave Browser", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/usr/bin/google-chrome", "/usr/bin/chromium"];
const exe = BROWSERS.find((p) => fs.existsSync(p));
if (!exe) throw new Error("No Chrome-family browser found");

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const getJson = (u) => new Promise((res, rej) => http.get(u, (r) => { let d = ""; r.on("data", (c) => (d += c)); r.on("end", () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } }); }).on("error", rej));
const clients = async () => (await fetch(`${BASE}/api/health`).then((r) => r.json())).streamClients;

const port = 9500 + Math.floor(Math.random() * 400);
const proc = spawn(exe, ["--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${fs.mkdtempSync(path.join(os.tmpdir(), "sf-stream-"))}`, "--no-first-run", "about:blank"], { stdio: "ignore" });
try {
  let targets;
  for (let i = 0; i < 80; i++) { try { targets = await getJson(`http://127.0.0.1:${port}/json`); if (targets.some((t) => t.type === "page")) break; } catch { /* starting */ } await sleep(250); }
  const ws = new WebSocket(targets.find((t) => t.type === "page").webSocketDebuggerUrl, { perMessageDeflate: false });
  await new Promise((r) => ws.on("open", r));
  let id = 0;
  const waiting = new Map();
  ws.on("message", (m) => { const msg = JSON.parse(m); if (msg.id && waiting.has(msg.id)) { waiting.get(msg.id)(msg); waiting.delete(msg.id); } });
  const send = (method, params = {}) => new Promise((r) => { const i = ++id; waiting.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });

  await send("Page.enable");
  await send("Page.navigate", { url: `${BASE}/dashboard/guide` });
  await sleep(2500);
  await send("Runtime.evaluate", { expression: "localStorage.setItem('strikefit.tour.done','1')" });
  const base = await clients();
  const routes = ["/dashboard", "/dashboard/engagement", "/dashboard/leaderboard", "/dashboard/sessions", "/dashboard/analytics", "/dashboard/compare", "/dashboard/live", "/dashboard/players"];
  const seen = [];
  for (let i = 0; i < PAGES; i++) {
    await send("Page.navigate", { url: BASE + routes[i % routes.length] });
    await sleep(1800);
    seen.push(await clients());
  }
  const worst = Math.max(...seen);
  const ok = worst - base <= 1;
  console.log(`stream clients: baseline ${base} (before this browser), after each of ${PAGES} full navigations: ${seen.join(", ")}`);
  console.log(ok ? "PASS: the old pages released their streams" : `FAIL: grew to ${worst} (baseline ${base}); cached pages are still holding their event streams`);
  ws.close();
  process.exitCode = ok ? 0 : 1;
} finally {
  proc.kill();
}
