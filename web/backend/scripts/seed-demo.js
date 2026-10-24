// Fills the website with a few demo players and fights, so the dashboard has something to show: Dikshanta, Gaurav and Pratyush with
// 4 to 5 fights each, spread over the last ten days, on all three levels. It goes through the backend's HTTP API (the same way the game
// and the simulator do), so every page, rank, record and milestone is worked out by the site itself.
//
//   node scripts/seed-demo.js --url https://<your-render-url>      add the demo fights (a player that already exists is skipped)
//   node scripts/seed-demo.js --url ... --force                    add them even if the player exists
//   node scripts/seed-demo.js --url ... --remove                   delete exactly what this script added
//
// A host other than this machine needs ALLOW_PROD_DB=1 (the simulator's own safety rule), so on purpose:
//   ALLOW_PROD_DB=1 node scripts/seed-demo.js --url https://s-t-r-i-k-e-f-i-t.onrender.com
//
// The fights are marked source "simulator" and notes "simulated", which is how --remove finds them: it never deletes a fight that
// came from the game. A real player with one of these names would share the same page, so pick other names for real players.

const { runFight, parseArgs, assertSafeTarget } = require("./simulate-live");

// player, skill (how hard they swing, 0.6 to 1.3), then fights: level, outcome, seconds, days ago
const DEMO = [
  { player: "Dikshanta", skill: 1.05, fights: [["Easy", "victory", 40, 9.3], ["Easy", "victory", 46, 7.6], ["Medium", "defeat", 72, 5.2], ["Medium", "victory", 64, 3.4], ["Hard", "defeat", 110, 1.1]] },
  { player: "Gaurav", skill: 0.95, fights: [["Easy", "victory", 42, 8.4], ["Medium", "victory", 66, 6.1], ["Medium", "victory", 58, 4.3], ["Hard", "defeat", 100, 2.2]] },
  { player: "Pratyush", skill: 0.88, fights: [["Easy", "defeat", 36, 7.2], ["Easy", "victory", 44, 5.5], ["Medium", "defeat", 70, 3.1], ["Medium", "victory", 60, 0.8]] },
];

const same = (a, b) => String(a).toLowerCase() === String(b).toLowerCase();
const get = async (base, path) => {
  const res = await fetch(base + path);
  if (!res.ok) throw new Error(`GET ${path} -> ${res.status}`);
  return res.json();
};

async function seed(base, { force }) {
  const have = (await get(base, "/api/players")).players.map((p) => p.playerName);
  let total = 0;
  let n = 0;
  for (const { player, skill, fights } of DEMO) {
    if (have.some((h) => same(h, player)) && !force) {
      console.log(`[demo] ${player} already has fights: skipped (use --force to add more)`);
      continue;
    }
    for (const [level, outcome, seconds, daysAgo] of fights) {
      const r = await runFight({ url: base, player, level, outcome, seconds, daysAgo, skill, fast: true, keep: true, quiet: true, noSpikes: true, allowAnyName: true, seed: ++n * 104729 });
      total++;
      console.log(`[demo] ${player.padEnd(10)} ${level.padEnd(6)} ${r.outcome.padEnd(8)} score ${r.score}`);
    }
  }
  console.log(total ? `[demo] done: ${total} fights added` : "[demo] nothing added");
}

async function remove(base) {
  let removed = 0;
  for (const { player } of DEMO) {
    const { sessions } = await get(base, `/api/sessions?player=${encodeURIComponent(player)}&limit=500`);
    for (const s of sessions.filter((x) => x.source === "simulator" && same(x.playerName, player))) {
      const res = await fetch(`${base}/api/sessions/${s.id}`, { method: "DELETE" });
      if (res.ok) removed++;
    }
  }
  console.log(`[demo] removed ${removed} demo fights`);
}

module.exports = { DEMO };

if (require.main === module) {
  (async () => {
    const args = parseArgs(process.argv.slice(2));
    if (!args.url || args.url === true) throw new Error("give the backend address: --url https://<your-render-url> (or http://localhost:4000)");
    const base = String(args.url).replace(/\/+$/, "");
    assertSafeTarget(base, DEMO[0].player, process.env, { allowAnyName: true });
    const health = await get(base, "/api/health").catch(() => null);
    if (!health || !health.ok) throw new Error(`no healthy backend with a working database at ${base}`);
    if (args.remove) await remove(base);
    else await seed(base, { force: Boolean(args.force) });
  })().catch((e) => {
    console.error("[demo] failed:", e.message);
    process.exit(1);
  });
}
