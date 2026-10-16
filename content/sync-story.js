#!/usr/bin/env node
// Copies content/story.json (the single source of truth for the story) to the two places that ship it:
//   Assets/Resources/story.json            read by the game (Resources.Load<TextAsset>("story"))
//   web/frontend/src/content/story.json    imported by the website's /story page
// Usage (repo root): node content/sync-story.js          copy, then verify
//                    node content/sync-story.js --check  verify only; exit 1 when a copy differs (CI / before a build)
// The file is checked for the fields both readers use before anything is written.
const fs = require("fs");
const path = require("path");

const ROOT = path.join(__dirname, "..");
const SOURCE = path.join(__dirname, "story.json");
const COPIES = [path.join(ROOT, "Assets", "Resources", "story.json"), path.join(ROOT, "web", "frontend", "src", "content", "story.json")];

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
  return errors;
}

function check(sourceText) {
  return COPIES.filter((p) => !fs.existsSync(p) || fs.readFileSync(p, "utf8") !== sourceText).map((p) => path.relative(ROOT, p));
}

function main() {
  const text = fs.readFileSync(SOURCE, "utf8");
  const errors = validate(JSON.parse(text));
  if (errors.length) {
    console.error("content/story.json is invalid:\n  " + errors.join("\n  "));
    process.exit(1);
  }
  if (!process.argv.includes("--check")) {
    for (const p of COPIES) {
      fs.mkdirSync(path.dirname(p), { recursive: true });
      fs.writeFileSync(p, text);
    }
  }
  const stale = check(text);
  if (stale.length) {
    console.error("Out of date (run: node content/sync-story.js):\n  " + stale.join("\n  "));
    process.exit(1);
  }
  console.log(`story.json in sync: ${COPIES.map((p) => path.relative(ROOT, p)).join(", ")}`);
}

if (require.main === module) main();
module.exports = { SOURCE, COPIES, validate, check };
