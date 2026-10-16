// Minimal headless-Brave driver over the DevTools protocol (screenshots and scripted page steps).
// Not shipped, not imported by the app. `ws` is resolved from web/backend/node_modules, so no new dependency is added.
import { spawn } from "node:child_process";
import http from "node:http";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const here = path.dirname(fileURLToPath(import.meta.url));
const { WebSocket } = createRequire(path.join(here, "../../backend/package.json"))("ws");
const BRAVE = process.env.BROWSER_BIN || "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser";

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const getJson = (u) =>
  new Promise((res, rej) =>
    http
      .get(u, (r) => {
        let d = "";
        r.on("data", (c) => (d += c));
        r.on("end", () => {
          try {
            res(JSON.parse(d));
          } catch (e) {
            rej(e);
          }
        });
      })
      .on("error", rej)
  );

export async function openBrowser(port = 9360) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "readme-media-"));
  const proc = spawn(BRAVE, ["--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${dir}`, "--no-first-run", "--hide-scrollbars", "--force-color-profile=srgb", "about:blank"], { stdio: "ignore" });
  let targets;
  for (let i = 0; i < 80; i++) {
    try {
      targets = await getJson(`http://127.0.0.1:${port}/json`);
      if (targets.find((t) => t.type === "page")) break;
    } catch {}
    await sleep(250);
  }
  const page = targets.find((t) => t.type === "page");
  const ws = new WebSocket(page.webSocketDebuggerUrl, { perMessageDeflate: false, maxPayload: 256 * 1024 * 1024 });
  await new Promise((r) => ws.on("open", r));
  let id = 0;
  const pending = new Map();
  const errors = [];
  ws.on("message", (m) => {
    const msg = JSON.parse(m);
    if (msg.id && pending.has(msg.id)) {
      pending.get(msg.id)(msg);
      pending.delete(msg.id);
      return;
    }
    if (msg.method === "Runtime.exceptionThrown") errors.push("exception: " + (msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails.text).slice(0, 200));
    if (msg.method === "Runtime.consoleAPICalled" && msg.params.type === "error") errors.push("console.error: " + msg.params.args.map((a) => a.value ?? a.description ?? "").join(" ").slice(0, 200));
  });
  const send = (method, params = {}) => new Promise((r) => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const evaluate = async (expr) => {
    const r = await send("Runtime.evaluate", { expression: expr, awaitPromise: true, returnByValue: true });
    if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description || "evaluate failed");
    return r.result?.result?.value;
  };
  await send("Page.enable");
  await send("Runtime.enable");
  return {
    errors,
    send,
    evaluate,
    size: (width, height) => send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile: false }),
    async go(url, waitMs = 2500) {
      await send("Page.navigate", { url });
      await sleep(waitMs);
    },
    async waitFor(expr, ms = 20000) {
      const t0 = Date.now();
      while (Date.now() - t0 < ms) {
        try {
          if (await evaluate(expr)) return true;
        } catch {}
        await sleep(250);
      }
      return false;
    },
    async shot(file, { full = false, clip } = {}) {
      const m = await send("Page.getLayoutMetrics");
      const w = Math.ceil(m.result.cssLayoutViewport.clientWidth);
      const h = Math.ceil(full ? m.result.cssContentSize.height : m.result.cssLayoutViewport.clientHeight);
      // clip is in document coordinates; without one the visible viewport is captured, wherever the page is scrolled.
      const params = { format: "png", captureBeyondViewport: full };
      if (clip) params.clip = { ...clip, scale: 1 };
      else if (full) params.clip = { x: 0, y: 0, width: w, height: h, scale: 1 };
      const r = await send("Page.captureScreenshot", params);
      fs.writeFileSync(file, Buffer.from(r.result.data, "base64"));
    },
    close() {
      ws.close();
      proc.kill();
    },
  };
}
