// Captures the frames for the README hero GIF: landing page at 1280x720, hold on the hero, scroll to "How it works",
// hold, scroll to "Monsters", hold. Frames go to --out (a temp dir); build-gif.py turns them into the GIF.
//   node hero-frames.mjs --url http://localhost:5180 --out /tmp/hero-frames --fps 13
import fs from "node:fs";
import path from "node:path";
import { openBrowser, sleep } from "./cdp.mjs";

const arg = (k, d) => { const i = process.argv.indexOf("--" + k); return i > 0 ? process.argv[i + 1] : d; };
const URL_ = arg("url", "http://localhost:5180");
const OUT = arg("out", "/tmp/hero-frames");
const FPS = Number(arg("fps", 13));
// seconds: hold on hero, scroll to how, hold, scroll to monsters, hold
const T = { hero: 1.6, toHow: 2.6, how: 1.6, toMonsters: 2.4, monsters: 1.8 };

fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(OUT, { recursive: true });
const b = await openBrowser(9361);
await b.size(1280, 720);
// Reduced motion makes the reveal-on-scroll animations land instantly, so no frame catches a half-faded section.
await b.send("Emulation.setEmulatedMedia", { features: [{ name: "prefers-reduced-motion", value: "reduce" }] });
await b.go(URL_ + "/", 3500);
// Lazy images (the monster portraits) must be loaded before the camera gets there.
await b.evaluate("document.querySelectorAll('img[loading=lazy]').forEach((i) => (i.loading = 'eager'))");
await b.waitFor("[...document.images].every((i) => i.complete)", 15000);
await sleep(800);

const navH = await b.evaluate("parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--nav-h')) || 64");
const top = (id) => b.evaluate(`document.getElementById(${JSON.stringify(id)}).getBoundingClientRect().top + scrollY`);
const howY = Math.round((await top("how")) - navH - 8);
// a little past the heading so the three portraits are in view
const monstersY = Math.round((await top("monsters")) - navH + 110);
const ease = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

let n = 0;
const frame = async (y) => {
  await b.evaluate(`window.scrollTo({ top: ${Math.round(y)}, behavior: "instant" })`);
  await sleep(35);
  await b.shot(path.join(OUT, `f${String(n++).padStart(4, "0")}.png`));
};
const hold = async (y, sec) => { for (let i = 0; i < Math.round(sec * FPS); i++) await frame(y); };
const move = async (a, c, sec) => { const k = Math.round(sec * FPS); for (let i = 1; i <= k; i++) await frame(a + (c - a) * ease(i / k)); };

await hold(0, T.hero);
await move(0, howY, T.toHow);
await hold(howY, T.how);
await move(howY, monstersY, T.toMonsters);
await hold(monstersY, T.monsters);
console.log(JSON.stringify({ frames: n, fps: FPS, seconds: +(n / FPS).toFixed(1), howY, monstersY, errors: b.errors }));
b.close();
