// Whole stories for the simulator (npm run simulate -- --scenario pb|rankup|phase), so each new engagement feature can be seen on
// demand against the LOCAL dev database. Same safety as simulate-live.js: local hosts only, player names start with ZZTEST.
//
//   pb       two weaker lost fights (no raw speeds above the cap, so their peak is below 8 m/s), then a live victory: new records for
//            the peak swing speed, damage and score on that level
//   rankup   short fights until the player is a few points short of the next rank (read from GET /api/players/:name/rank),
//            then one live fight that takes them over it
//   phase    a Level 3 fight that goes through the boss's phase breaks at 66% and 33% HP (live commentary: "enters phase 2 / 3")
//
// Every story ends with the LIVE fight (so a browser on the Live Arena sees it happen); the fights before it are played on a virtual
// clock in a moment. Fights are kept so the player's pages have history; --cleanup removes that player's fights when the story ends.

const { runFight, assertSafeTarget } = require("./simulate-live");

const BASE = (o) => String(o.url || "http://localhost:4000").replace(/\/+$/, "");
const get = async (base, path) => {
  const res = await fetch(base + path);
  return { status: res.status, body: await res.json().catch(() => null) };
};

async function cleanup(base, player) {
  const { body } = await get(base, `/api/sessions?player=${encodeURIComponent(player)}`);
  for (const s of (body && body.sessions) || []) await fetch(`${base}/api/sessions/${s.id}`, { method: "DELETE" });
}

const SCENARIOS = {
  async pb(o) {
    const base = BASE(o);
    const player = o.player || "ZZTEST-pb";
    const level = ["Easy", "Medium", "Hard"].includes(o.level) ? o.level : "Easy";
    assertSafeTarget(base, player);
    if (!o.quiet) console.log(`[scenario pb] ${player} · ${level}: two weaker fights first`);
    for (const seed of [101, 102]) await runFight({ url: base, player, level, outcome: "defeat", seconds: 22, fast: true, keep: true, quiet: true, skill: 0.75, noSpikes: true, seed });
    if (!o.quiet) console.log("[scenario pb] now the live fight that should set new records");
    const last = await runFight({ url: base, player, level, outcome: "victory", seconds: Number(o.seconds) || 40, keep: true, skill: 1.15, seed: 103, quiet: o.quiet });
    if (o.cleanup) await cleanup(base, player);
    return last;
  },

  async rankup(o) {
    const base = BASE(o);
    const player = o.player || "ZZTEST-rankup";
    assertSafeTarget(base, player);
    const level = ["Easy", "Medium", "Hard"].includes(o.level) ? o.level : "Easy";
    const buffer = Number(o.buffer) || 110; // points short of the next rank when the live fight starts
    let seed = 200;
    for (let i = 0; i < 40; i++) {
      const r = await get(base, `/api/players/${encodeURIComponent(player)}/rank`);
      const next = r.status === 200 ? r.body.nextRank : null;
      const toGo = r.status === 200 ? r.body.pointsToNext : Infinity;
      if (r.status === 200 && (!next || toGo <= buffer)) break;
      await runFight({ url: base, player, level, outcome: "defeat", seconds: 8, fast: true, keep: true, quiet: true, skill: 0.9, noSpikes: true, seed: seed++ });
    }
    const r = await get(base, `/api/players/${encodeURIComponent(player)}/rank`);
    if (!o.quiet) console.log(`[scenario rankup] ${player} is ${r.body.rank.name} with ${r.body.effortPoints} points, ${r.body.pointsToNext} short of ${r.body.nextRank ? r.body.nextRank.name : "the top"}`);
    const last = await runFight({ url: base, player, level, outcome: "victory", seconds: Number(o.seconds) || 40, keep: true, skill: 1.1, seed: seed++, quiet: o.quiet });
    const after = await get(base, `/api/players/${encodeURIComponent(player)}/rank`);
    if (!o.quiet) console.log(`[scenario rankup] after the live fight: ${after.body.rank.name}, ${after.body.effortPoints} points`);
    if (o.cleanup) await cleanup(base, player);
    return { ...last, rankBefore: r.body.rank.name, rankAfter: after.body.rank.name };
  },

  async phase(o) {
    const base = BASE(o);
    const player = o.player || "ZZTEST-phase";
    assertSafeTarget(base, player);
    const last = await runFight({ url: base, player, level: "Hard", outcome: "victory", seconds: Number(o.seconds) || 80, keep: true, skill: 1.05, seed: 301, quiet: o.quiet });
    if (o.cleanup) await cleanup(base, player);
    return last;
  },
};

async function runScenario(name, opts = {}) {
  if (!SCENARIOS[name]) throw new Error(`unknown scenario "${name}": use ${Object.keys(SCENARIOS).join(", ")}`);
  return SCENARIOS[name](opts);
}

module.exports = { runScenario, SCENARIOS };
