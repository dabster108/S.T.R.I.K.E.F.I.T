// node inspect.mjs <fbxUrlPath>  -- e.g. node inspect.mjs "/assets/Models/Monsters/Hip Hop Dancing.fbx"
import { openPage } from "./cdp.mjs";

const rel = process.argv[2];
const PORT = 9955;
const page = await openPage(PORT);
try {
  await page.send("Page.navigate", { url: `http://localhost:8899/inspect.html?url=${encodeURIComponent(rel)}` });
  let report = null, error = null;
  for (let i = 0; i < 100; i++) {
    report = await page.evaluate("window.__report");
    error = await page.evaluate("window.__error");
    if (report || error) break;
    await new Promise((r) => setTimeout(r, 200));
  }
  console.log(JSON.stringify({ report, error, console: page.consoleLines }, null, 2));
} finally {
  page.close();
}
