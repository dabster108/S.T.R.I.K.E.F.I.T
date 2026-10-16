// Dashboard screenshots for the README at 1440 px wide: Overview, Live Arena, Player page, Scoreboard.
//   node dashboard-shots.mjs --site http://localhost:5180 --api http://localhost:4010 --out ../../../docs/images
// SAFETY: the Live Arena shot starts a simulated fight (scripts/simulate-live.js), which WRITES sessions. So --api must be a
// local backend on a throwaway database (see README.md in this folder); the script refuses any other host.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { openBrowser, sleep } from "./cdp.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => { const i = process.argv.indexOf("--" + k); return i > 0 ? process.argv[i + 1] : d; };
const SITE = arg("site", "http://localhost:5180");
const API = arg("api", "http://localhost:4010");
const OUT = path.resolve(arg("out", path.join(here, "../../../docs/images")));
const PLAYER = arg("player", "Player 1");
if (!["localhost", "127.0.0.1"].includes(new URL(API).hostname)) throw new Error("refusing to run: --api must be a local throwaway backend");

fs.mkdirSync(OUT, { recursive: true });
const b = await openBrowser(9362);
await b.size(1440, 900);
await b.go(SITE + "/", 1500);
// The first-visit tour must not cover the page.
await b.evaluate("localStorage.setItem('strikefit_tour_seen_v1', '1')");

const text = (s) => `document.body.innerText.toLowerCase().includes(${JSON.stringify(s.toLowerCase())})`;
async function page(route, ready, file, { settle = 1800, full = false, scrollTo } = {}) {
  await b.go(SITE + route, 800);
  const ok = await b.waitFor(ready, 25000);
  await sleep(settle);
  if (scrollTo) {
    // bring a section to the top of the viewport (instant, the app sets smooth scrolling)
    await b.evaluate(`(() => { const el = [...document.querySelectorAll('h2,h3')].find((e) => e.textContent.trim() === ${JSON.stringify(scrollTo)}); if (el) window.scrollTo({ top: el.getBoundingClientRect().top + scrollY - 110, behavior: 'instant' }); })()`);
    await sleep(600);
  }
  await b.shot(path.join(OUT, file), { full });
  console.log(`${file}: ready=${ok}`);
}

await page("/dashboard", text("Performance overview") + " || " + text("battles"), "dashboard-overview.png");
await page("/dashboard/leaderboard", text("Leaderboard"), "dashboard-scoreboard.png");
await page(`/dashboard/player/${encodeURIComponent(PLAYER)}`, text("challenge"), "dashboard-player.png", { scrollTo: "Challenges" });

// Live Arena: a fight that stays in progress while the page is open (events posted the way the headset does), deleted after.
const SID = "readme-demo-" + Date.now().toString(36);
const post = (p, body) => fetch(API + p, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
let n = 0;
const speeds = [5.1, 3.4, 5.6, 4.2, 6.1, 2.8, 5.3, 4.7, 5.9, 3.9];
const tick = () => {
  const v = speeds[n % speeds.length];
  const tier = v >= 5 ? "full" : v >= 3 ? "medium" : "light";
  const ev = n === 0 ? "session_start" : "hit";
  n++;
  return post("/api/events", { events: [{ type: ev, sessionId: SID, playerName: PLAYER, message: "Simulated hit (demo data)", payload: { level: "Easy", t: n * 1.5, swingSpeed: v, tier, damage: Math.round(v * 5), combo: Math.min(9, n), hits: n, xp: n * 38, score: n * 90, playerHp: 500 - n * 6, playerMaxHp: 500, monsterHp: 500 - n * 21, monsterMaxHp: 500 } }] });
};
for (let i = 0; i < 12; i++) await tick();
const feeder = setInterval(tick, 1500);
await page("/dashboard/live", text("1 live"), "dashboard-live.png", { settle: 3500 });
clearInterval(feeder);
await fetch(API + "/api/sessions/" + SID, { method: "DELETE" });
console.log(JSON.stringify({ errors: b.errors }));
b.close();
