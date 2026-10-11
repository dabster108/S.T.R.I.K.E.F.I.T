// Minimal headless-Brave driver over the DevTools protocol, reused by inspect.mjs and render.mjs.
import { spawn } from "node:child_process";
import http from "node:http";
import { WebSocket } from "ws";

const BRAVE = "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser";
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const getJson = (u) => new Promise((res, rej) => http.get(u, (r) => { let d = ""; r.on("data", (c) => (d += c)); r.on("end", () => { try { res(JSON.parse(d)); } catch (e) { rej(e); } }); }).on("error", rej));

export async function openPage(port) {
  const fs = await import("node:fs");
  const dir = fs.mkdtempSync("/tmp/brave-portraits-");
  const proc = spawn(BRAVE, ["--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${dir}`, "--no-first-run", "--hide-scrollbars", "--enable-webgl", "--use-gl=angle", "--use-angle=metal", "--ignore-gpu-blocklist", "about:blank"], { stdio: "ignore" });
  let targets;
  for (let i = 0; i < 60; i++) { try { targets = await getJson(`http://127.0.0.1:${port}/json`); break; } catch { await sleep(250); } }
  const page = targets.find((t) => t.type === "page");
  const ws = new WebSocket(page.webSocketDebuggerUrl, { perMessageDeflate: false });
  await new Promise((r) => ws.on("open", r));
  let id = 0;
  const pending = new Map();
  const consoleLines = [];
  ws.on("message", (m) => {
    const msg = JSON.parse(m);
    if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); return; }
    if (msg.method === "Runtime.consoleAPICalled") consoleLines.push(msg.params.args.map((a) => a.value ?? a.description ?? "").join(" "));
    if (msg.method === "Runtime.exceptionThrown") consoleLines.push("EXCEPTION: " + (msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails.text));
  });
  const send = (method, params = {}) => new Promise((r) => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async (expr, awaitPromise = true) => (await send("Runtime.evaluate", { expression: expr, awaitPromise, returnByValue: true })).result?.result?.value;
  await send("Page.enable");
  await send("Runtime.enable");
  return { proc, ws, send, evaluate, consoleLines, close: () => { ws.close(); proc.kill(); } };
}
