// Tiny static server for this local-only tool. Not shipped, not started by the app.
// Serves this directory at / and the Unity project's Assets (read-only) at /assets/,
// so the renderer can load the real FBX/texture files without copying ~150 MB of binaries.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const ASSETS_ROOT = path.resolve(here, "../../../Assets");
const PORT = Number(process.argv[2] || 8899);

const TYPES = {
  ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json",
  ".fbx": "application/octet-stream", ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
};

function send(res, filePath) {
  fs.readFile(filePath, (err, data) => {
    if (err) { res.writeHead(404); res.end("not found: " + filePath); return; }
    res.writeHead(200, { "Content-Type": TYPES[path.extname(filePath).toLowerCase()] || "application/octet-stream", "Access-Control-Allow-Origin": "*" });
    res.end(data);
  });
}

http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split("?")[0]);
  let filePath;
  if (url.startsWith("/assets/")) filePath = path.join(ASSETS_ROOT, url.slice("/assets/".length));
  else filePath = path.join(here, url === "/" ? "/index.html" : url);
  // keep both roots on their own side of the fence
  if (!filePath.startsWith(ASSETS_ROOT) && !filePath.startsWith(here)) { res.writeHead(403); res.end(); return; }
  send(res, filePath);
}).listen(PORT, () => console.log("render-portraits server on " + PORT));
