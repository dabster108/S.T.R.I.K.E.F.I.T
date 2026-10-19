// Fills the DEV database with simulated fights through the running dev backend (all player names start with ZZTEST).
//
//   npm run dev:seed                     seed if the database has no ZZTEST fights yet
//   npm run dev:seed -- --force          seed again on top
//   npm run dev:seed -- --url http://localhost:4000
//
// It talks to the backend's HTTP API in the simulator's fast mode (a virtual clock, back-dated over the last 12 days), so it
// takes a few seconds. It only talks to this machine (simulate-live.js refuses any other host), and it checks the backend
// is a development one by asking /api/health for its database host.

const { runFight, parseArgs } = require("./simulate-live");

const PLAYERS = ["ZZTEST-ann", "ZZTEST-bo", "ZZTEST-cy", "ZZTEST-dee"];
// player, level, outcome, seconds, daysAgo
const PLAN = [
  ["ZZTEST-ann", "Easy", "victory", 38, 11], ["ZZTEST-ann", "Easy", "victory", 44, 9], ["ZZTEST-ann", "Medium", "defeat", 70, 8], ["ZZTEST-ann", "Medium", "victory", 62, 6],
  ["ZZTEST-ann", "Hard", "defeat", 110, 4], ["ZZTEST-ann", "Hard", "victory", 120, 2], ["ZZTEST-ann", "Medium", "victory", 55, 1],
  ["ZZTEST-bo", "Easy", "victory", 40, 10], ["ZZTEST-bo", "Easy", "defeat", 50, 10], ["ZZTEST-bo", "Medium", "victory", 66, 7], ["ZZTEST-bo", "Medium", "victory", 58, 3],
  ["ZZTEST-cy", "Easy", "victory", 36, 9], ["ZZTEST-cy", "Medium", "defeat", 74, 5], ["ZZTEST-cy", "Hard", "defeat", 100, 1],
  ["ZZTEST-dee", "Easy", "victory", 42, 3],
];

(async () => {
  const args = parseArgs(process.argv.slice(2));
  const base = String(args.url || "http://localhost:4000").replace(/\/+$/, "");
  const health = await fetch(`${base}/api/health`).then((r) => r.json()).catch(() => null);
  if (!health || !health.ok) throw new Error(`no healthy backend at ${base}. Start it with npm run dev:safe first.`);
  const have = (await fetch(`${base}/api/players`).then((r) => r.json())).players.filter((p) => PLAYERS.includes(p.playerName)).length;
  if (have && !args.force) {
    console.log(`[seed] ${have} ZZTEST players already exist; nothing to do (use --force to add more)`);
    return;
  }
  let n = 0;
  for (const [player, level, outcome, seconds, daysAgo] of PLAN) {
    const r = await runFight({ url: base, player, level, outcome, seconds, daysAgo, fast: true, keep: true, quiet: true, seed: ++n * 7919 });
    console.log(`[seed] ${String(n).padStart(2)}/${PLAN.length} ${player.padEnd(11)} ${level.padEnd(6)} ${r.outcome.padEnd(8)} score ${r.score}`);
  }
  console.log(`[seed] done: ${PLAN.length} fights for ${PLAYERS.length} ZZTEST players`);
})().catch((e) => {
  console.error("[seed] failed:", e.message);
  process.exit(1);
});
