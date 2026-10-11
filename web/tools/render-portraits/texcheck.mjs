import { openPage } from "./cdp.mjs";
const page = await openPage(9958);
await page.send("Page.navigate", { url: `http://localhost:8899/texcheck.html?model=${encodeURIComponent("/assets/Models/Monsters/Hip Hop Dancing.fbx")}` });
let r=null;
for (let i=0;i<60;i++){ r = await page.evaluate("window.__tex"); if(r) break; await new Promise(x=>setTimeout(x,200)); }
console.log(JSON.stringify(r));
console.log(page.consoleLines.join("\n"));
page.close();
