// node render.mjs <query-string> <out.png>
// e.g. node render.mjs "model=/assets/Models/Monsters/Hip%20Hop%20Dancing.fbx&t=0.2" out/l1_t0.2.png
import { openPage } from "./cdp.mjs";
import fs from "node:fs";
import path from "node:path";

const qs = process.argv[2];
const out = process.argv[3];
const PORT = 9956;
const page = await openPage(PORT);
try {
  await page.send("Page.navigate", { url: `http://localhost:8899/render.html?${qs}` });
  let dataUrl = null, error = null, meta = null;
  for (let i = 0; i < 150; i++) {
    dataUrl = await page.evaluate("window.__dataUrl");
    error = await page.evaluate("window.__error");
    if (dataUrl || error) break;
    await new Promise((r) => setTimeout(r, 200));
  }
  meta = await page.evaluate("window.__meta");
  if (error) { console.error("ERROR:", error); console.error("console:", page.consoleLines.join("\n")); process.exit(1); }
  if (!dataUrl) { console.error("TIMED OUT"); console.error("console:", page.consoleLines.join("\n")); process.exit(1); }
  const b64 = dataUrl.replace(/^data:image\/png;base64,/, "");
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, Buffer.from(b64, "base64"));
  console.log("wrote", out, JSON.stringify(meta));
} finally {
  page.close();
}
