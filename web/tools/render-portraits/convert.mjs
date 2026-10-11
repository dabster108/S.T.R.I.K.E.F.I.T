// node convert.mjs — resizes the three rendered portraits to WebP/AVIF at 480/960/1440 wide, keeps
// transparency, and writes a tiny base64 blur placeholder per level into manifest.json.
// Output: web/frontend/public/monsters/level{1,2,3}-{480,960,1440}.{webp,avif}
import sharp from "sharp";
import fs from "node:fs";
import path from "node:path";

const OUT = path.resolve("../../frontend/public/monsters");
fs.mkdirSync(OUT, { recursive: true });
const WIDTHS = [480, 960, 1440];
const LEVELS = ["level1", "level2", "level3"];
const manifest = {};

for (const level of LEVELS) {
  const src = `final/${level}.png`;
  const img = sharp(src);
  const meta = await img.metadata();
  const files = {};
  for (const w of WIDTHS) {
    const h = Math.round((meta.height / meta.width) * w);
    for (const fmt of ["webp", "avif"]) {
      const outPath = path.join(OUT, `${level}-${w}.${fmt}`);
      await sharp(src).resize(w, h).toFormat(fmt, fmt === "webp" ? { quality: 88, alphaQuality: 90 } : { quality: 60 }).toFile(outPath);
      files[`${fmt}${w}`] = `/monsters/${level}-${w}.${fmt}`;
    }
  }
  // Tiny blurred placeholder: 24px wide webp, inlined as a data URI (a few hundred bytes).
  const placeholderBuf = await sharp(src).resize(24).blur(3).webp({ quality: 40 }).toBuffer();
  const placeholder = `data:image/webp;base64,${placeholderBuf.toString("base64")}`;
  manifest[level] = { width: meta.width, height: meta.height, aspect: +(meta.width / meta.height).toFixed(4), placeholder, ...files };
  console.log(level, meta.width, "x", meta.height, "placeholder", placeholderBuf.length, "bytes");
}

fs.writeFileSync(path.join(OUT, "manifest.json"), JSON.stringify(manifest, null, 2));
console.log("wrote", path.join(OUT, "manifest.json"));
