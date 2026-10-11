import { openPage } from "./cdp.mjs";
const page = await openPage(9957);
const url = "/assets/Models/Monsters/Hip Hop Dancing.fbx";
await page.send("Page.navigate", { url: `http://localhost:8899/render.html?model=${encodeURIComponent(url)}&t=0.2&unlit=1&dbg=map` });
let info=null;
for (let i=0;i<60;i++){ info = await page.evaluate("window.__dbgInfo"); if(info) break; await new Promise(r=>setTimeout(r,200)); }
console.log(JSON.stringify(info));
console.log(page.consoleLines.join("\n"));
page.close();
