// How many players land in each rank? Reads the LOCAL dev API (it refuses any other host, like the simulator), so the rank thresholds
// in config/ranks.js can be sanity-checked against simulated data without ever touching production.
//
//   node scripts/rank-distribution.js --url http://localhost:4000
//   --prefix ZZTEST-rankup   leave out players whose name starts with this (scenario test players)

const { assertSafeTarget } = require("./simulate-live");

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > -1 && process.argv[i + 1] && !process.argv[i + 1].startsWith("--") ? process.argv[i + 1] : fallback;
};

(async () => {
  const base = String(arg("url", "http://localhost:4000")).replace(/\/+$/, "");
  assertSafeTarget(base, "ZZTEST-distribution"); // local hosts only
  const skip = arg("prefix", null);
  const players = (await (await fetch(`${base}/api/players`)).json()).players.map((p) => p.playerName).filter((n) => !skip || !n.startsWith(skip));
  const all = {};
  for (let i = 0; i < players.length; i += 50) {
    const chunk = players.slice(i, i + 50);
    Object.assign(all, (await (await fetch(`${base}/api/ranks?players=${encodeURIComponent(chunk.join(","))}`)).json()).ranks);
  }
  const ladder = (await (await fetch(`${base}/api/players/${encodeURIComponent(players[0])}/rank`)).json()).ladder;
  const counts = Object.fromEntries(ladder.map((r) => [r.name, 0]));
  for (const info of Object.values(all)) counts[info.rank.name]++;
  console.log(`${Object.keys(all).length} players on ${base}`);
  for (const r of ladder) console.log(`  ${r.name.padEnd(9)} from ${String(r.from).padStart(5)} points: ${counts[r.name]}`);
  const pts = Object.values(all).map((i) => i.effortPoints).sort((a, b) => a - b);
  console.log(`  effort points: min ${pts[0]}, median ${pts[Math.floor(pts.length / 2)]}, max ${pts[pts.length - 1]}`);
})().catch((err) => {
  console.error(err.message);
  process.exitCode = 1;
});
