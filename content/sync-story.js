#!/usr/bin/env node
// Copies content/story.json (the single source of truth for the story) to the two places that ship it:
//   Assets/Resources/story.json            read by the game (Resources.Load<TextAsset>("story"))
//   web/frontend/src/content/story.json    imported by the website's /story page
// and the guardian portraits content/guardians/level{1,2,3}.png (1024 px PNGs made from the same renders as the website's
// /monsters images: web/tools/render-portraits/final) to Assets/Resources/Guardians/ (the game's chapter cards and Guardians
// screen). The website keeps serving the AVIF / WebP versions convert.mjs makes from those same renders.
// Usage (repo root): node content/sync-story.js          copy, then verify
//                    node content/sync-story.js --check  verify only; exit 1 when a copy differs (CI / before a build)
// The file is checked for the fields both readers use before anything is written.
const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..");
const SOURCE = path.join(__dirname, "story.json");
const COPIES = [path.join(ROOT, "Assets", "Resources", "story.json"), path.join(ROOT, "web", "frontend", "src", "content", "story.json")];
const PORTRAITS = [1, 2, 3].map((n) => ({
  from: path.join(__dirname, "guardians", `level${n}.png`),
  to: path.join(ROOT, "Assets", "Resources", "Guardians", `level${n}.png`),
}));
const MAX_PORTRAIT_BYTES = 2 * 1024 * 1024; // each; 6 MB for the three is the budget

function validate(story) {
  const errors = [];
  if (!Array.isArray(story.slides) || story.slides.length === 0) errors.push("slides: at least one");
  (story.slides || []).forEach((s, i) => {
    if (!s || typeof s.text !== "string" || !s.text.trim()) errors.push(`slides[${i}].text missing`);
    if (!s || typeof s.id !== "string" || !s.id.trim()) errors.push(`slides[${i}].id missing`);
  });
  if (!(Number(story.slideSeconds) > 0)) errors.push("slideSeconds must be > 0");
  if (Number(story.slideSeconds) * (story.slides || []).length >= 45) errors.push("story must stay under 45 s");
  const keys = (story.levels || []).map((l) => l && l.key);
  for (const k of ["Easy", "Medium", "Hard"]) if (!keys.includes(k)) errors.push(`levels: ${k} missing`);
  (story.levels || []).forEach((l, i) => {
    for (const f of ["guardian", "intro", "scene"]) if (!l || typeof l[f] !== "string" || !l[f].trim()) errors.push(`levels[${i}].${f} missing`);
  });
  // Guardians (lore cards, Guardians screen) and interludes (story-run text between the fights).
  const guardians = story.guardians || [];
  if (guardians.length !== 3) errors.push("guardians: exactly 3");
  guardians.forEach((g, i) => {
    for (const f of ["id", "name", "weapon", "lore"]) if (!g || typeof g[f] !== "string" || !g[f].trim()) errors.push(`guardians[${i}].${f} missing`);
    if (!g || ![1, 2, 3].includes(g.level)) errors.push(`guardians[${i}].level must be 1, 2 or 3`);
    if (!g || !Array.isArray(g.tips) || g.tips.length < 1 || g.tips.some((t) => typeof t !== "string" || !t.trim())) errors.push(`guardians[${i}].tips: at least one text`);
    const lv = (story.levels || []).find((l) => l && g && l.level === g.level);
    if (lv && g && lv.guardian !== g.name) errors.push(`guardians[${i}].name "${g.name}" differs from levels[].guardian "${lv.guardian}"`);
  });
  const it = story.interludes || {};
  for (const f of ["afterLevel1", "afterLevel2", "defeat"]) if (typeof it[f] !== "string" || !it[f].trim()) errors.push(`interludes.${f} missing`);
  if (!Array.isArray(it.ending) || it.ending.length < 1 || it.ending.some((t) => typeof t !== "string" || !t.trim())) errors.push("interludes.ending: at least one text");
  // Plain ASCII everywhere: the Quest font has no glyph for curly quotes or dashes (a box is shown instead).
  const nonAscii = (JSON.stringify(story).match(/[^\x00-\x7e]/g) || []);
  if (nonAscii.length) errors.push(`non-ASCII characters in the story: ${[...new Set(nonAscii)].map((c) => "U+" + c.codePointAt(0).toString(16)).join(", ")}`);
  return errors;
}

function check(sourceText) {
  const stale = COPIES.filter((p) => !fs.existsSync(p) || fs.readFileSync(p, "utf8") !== sourceText).map((p) => path.relative(ROOT, p));
  for (const { from, to } of PORTRAITS)
    if (!fs.existsSync(from) || !fs.existsSync(to) || !fs.readFileSync(from).equals(fs.readFileSync(to))) stale.push(path.relative(ROOT, to));
  return stale;
}

// PNG width / height from the IHDR chunk (no image library needed).
function pngSize(file) {
  const b = fs.readFileSync(file);
  if (b.readUInt32BE(0) !== 0x89504e47) return null;
  return { width: b.readUInt32BE(16), height: b.readUInt32BE(20), bytes: b.length };
}

function validatePortraits() {
  const errors = [];
  for (const { from } of PORTRAITS) {
    if (!fs.existsSync(from)) { errors.push(`${path.relative(ROOT, from)} missing`); continue; }
    const s = pngSize(from);
    if (!s) errors.push(`${path.relative(ROOT, from)} is not a PNG`);
    else {
      if (Math.max(s.width, s.height) > 1024) errors.push(`${path.relative(ROOT, from)} is ${s.width}x${s.height}: 1024 px at most`);
      if (s.bytes > MAX_PORTRAIT_BYTES) errors.push(`${path.relative(ROOT, from)} is ${s.bytes} bytes: 2 MB at most`);
    }
  }
  return errors;
}

function main() {
  const text = fs.readFileSync(SOURCE, "utf8");
  const errors = validate(JSON.parse(text)).concat(validatePortraits());
  if (errors.length) {
    console.error("content/story.json is invalid:\n  " + errors.join("\n  "));
    process.exit(1);
  }
  if (!process.argv.includes("--check")) {
    for (const p of COPIES) {
      fs.mkdirSync(path.dirname(p), { recursive: true });
      fs.writeFileSync(p, text);
    }
    for (const { from, to } of PORTRAITS) {
      fs.mkdirSync(path.dirname(to), { recursive: true });
      fs.copyFileSync(from, to);
    }
  }
  const stale = check(text);
  if (stale.length) {
    console.error("Out of date (run: node content/sync-story.js):\n  " + stale.join("\n  "));
    process.exit(1);
  }
  console.log(`story.json in sync: ${COPIES.map((p) => path.relative(ROOT, p)).join(", ")}; portraits: ${PORTRAITS.map((p) => path.relative(ROOT, p.to)).join(", ")}`);
}

if (require.main === module) main();
module.exports = { SOURCE, COPIES, PORTRAITS, validate, validatePortraits, check, pngSize };
