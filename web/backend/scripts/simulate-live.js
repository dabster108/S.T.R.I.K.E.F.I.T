// Plays a fake fight against the API exactly the way the Unity SessionReporter does (live session POST, batched events,
// 1 s HP snapshots, final POST), so the dashboard's live views can be tested without a headset.
//
//   npm run simulate -- --url http://localhost:4000 --player "ZZTEST-ann" --level Hard --seconds 70
//
//   --level    Easy | Medium | Hard          (Level 1, 2, 3; attacks, categories and wind-ups come from the real monster prefabs,
//                                              frontend/src/content/monsterAttacks.json)
//   --outcome  victory | defeat | abandoned   (default victory; abandoned stops sending and never finishes, the server's
//                                              60 s sweeper then marks it abandoned, like a headset taken off)
//   --seconds  how long the fight should last (the damage is paced so it ends around 90 % of this)
//   --fast     no waiting: a virtual clock, for seeding. --days-ago N back-dates the fight; --seed N repeats it exactly
//   --keep     keep the finished session (default: it is DELETED at the end so it never reaches the statistics)
//   --linger   seconds to keep a finished (non --keep) session before deleting it
//   --skill    how hard this player swings, 0.6 to 1.3 (default: a seeded random 0.8 to 1.15)
//   --no-spikes  no raw speeds above the 8 m/s cap (the headset reports some; this keeps a fight's peak below the cap)
//
//   --scenario pb | rankup | phase   a whole story instead of one fight (scripts/scenarios.js):
//                pb      two weaker fights, then a live fight that sets new personal bests (shows the "New PB" banner)
//                rankup  quiet fights until the player is a few points short of the next rank, then a live fight that crosses it
//                phase   a Level 3 fight that goes through both boss phase breaks (66% and 33% HP)
//                --player NAME (ZZTEST...), --cleanup deletes that player's simulated fights afterwards
//
// SAFETY: this posts real rows to whatever backend --url points at. It refuses any host that is not this machine unless
// ALLOW_PROD_DB=1, and every player name must start with ZZTEST so test rows can be found and removed by prefix.

const fs = require("fs");
const path = require("path");

const ATTACK_TABLE = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "..", "frontend", "src", "content", "monsterAttacks.json"), "utf8"));

// From the scenes / prefabs: HitDetection, PlayerCombat, XPManager, LevelManager.GetParams, MonsterHealth.
const CFG = {
  Easy: { hp: 500, monsterHp: 500, base: 12, min: 1.2, cap: 8, maxMul: 2.5, weaponMul: 1.15, tiers: [1.5, 3.0, 5.0], xpHit: 10, xpFull: 25, monsterDmg: 8.8, gap: [1.2, 2.2] },
  Medium: { hp: 800, monsterHp: 800, base: 11, min: 1.2, cap: 8, maxMul: 2.2, weaponMul: 1.0, tiers: [1.4, 2.8, 4.6], xpHit: 15, xpFull: 40, monsterDmg: 16.8, gap: [0.8, 1.6] },
  Hard: { hp: 1200, monsterHp: 5600, base: 14, min: 1.6, cap: 8, maxMul: 2.6, weaponMul: 1.0, tiers: [1.4, 2.8, 4.6], xpHit: 15, xpFull: 40, monsterDmg: 26, gap: [0.5, 1.2] },
};
const CATEGORY_DAMAGE = { Standard: 1, Heavy: 1.5, IdleSlam: 2, Special: 1.7, Combo: 0.8 };
const TICK = 0.4; // seconds of fight time per loop

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const r2 = (n) => Math.round(n * 100) / 100;
const lerp = (a, b, t) => a + (b - a) * Math.min(1, Math.max(0, t));

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// allowAnyName: only the demo seed script (scripts/seed-demo.js) passes it, to post its named demo players; the host rule still holds.
function assertSafeTarget(base, player, env = process.env, { allowAnyName = false } = {}) {
  const host = new URL(base).hostname;
  const local = ["localhost", "127.0.0.1", "::1", "[::1]"].includes(host);
  if (!local && env.ALLOW_PROD_DB !== "1") throw new Error(`refusing to post to ${host}: only this machine is allowed (ALLOW_PROD_DB=1 to override)`);
  if (!allowAnyName && !/^ZZTEST/.test(player)) throw new Error(`player name "${player}" must start with ZZTEST so the test rows can be found and removed`);
}

async function runFight(opts = {}) {
  const BASE = String(opts.url || "http://localhost:4000").replace(/\/+$/, "");
  const PLAYER = String(opts.player || "ZZTEST-sim");
  const LEVEL = ["Easy", "Medium", "Hard"].includes(opts.level) ? opts.level : "Easy";
  const SECONDS = Number(opts.seconds) || 30;
  const OUTCOME = ["victory", "defeat", "abandoned"].includes(opts.outcome) ? opts.outcome : "victory";
  const FAST = Boolean(opts.fast);
  const KEEP = Boolean(opts.keep);
  const quiet = Boolean(opts.quiet);
  assertSafeTarget(BASE, PLAYER, process.env, { allowAnyName: opts.allowAnyName === true });

  const cfg = CFG[LEVEL];
  const attacks = Object.entries(ATTACK_TABLE.levels[LEVEL]).map(([name, a]) => ({ name, ...a }));
  const rnd = mulberry32(Number.isFinite(Number(opts.seed)) ? Number(opts.seed) : Math.floor(Math.random() * 2 ** 31));
  const skill = Number.isFinite(Number(opts.skill)) ? Number(opts.skill) : lerp(0.8, 1.15, rnd()); // how hard this player swings overall
  const spikes = !opts.noSpikes;

  const post = async (p, body) => {
    const res = await fetch(BASE + p, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
    if (!res.ok) throw new Error(`${p} -> ${res.status} ${await res.text()}`);
    return res.json();
  };

  const id = [...Array(16)].map(() => Math.floor(rnd() * 256).toString(16).padStart(2, "0")).join("");
  const s = { playerHp: cfg.hp, monsterHp: cfg.monsterHp, xp: 0, score: 0, hits: 0, damageTotal: 0, fullSwings: 0, combo: 0, kcal: 0 };
  let clock = 0; // fight seconds
  const realStart = Date.now();
  const now = () => (FAST ? clock : (Date.now() - realStart) / 1000);
  const queue = [];
  const ev = (type, message, payload) => queue.push({ type, sessionId: id, playerName: PLAYER, message, payload: { t: r2(now()), ...payload } });
  const snapshot = () => ({
    playerHp: r2(s.playerHp), playerMaxHp: cfg.hp, monsterHp: r2(s.monsterHp), monsterMaxHp: cfg.monsterHp, xp: s.xp, score: s.score, hits: s.hits,
    damageTotal: r2(s.damageTotal), fullSwings: s.fullSwings, combo: s.combo, effort: r2(Math.min(1, 0.35 + s.combo * 0.08)), kcal: r2(s.kcal),
    stamina: 100, maxStamina: 100, timeLeft: Math.max(0, 40 - (now() % 40)),
  });

  const daysAgo = Number(opts.daysAgo) || 0;
  const startMs = Date.now() - daysAgo * 864e5 - (FAST ? SECONDS * 1000 : 0);
  const stamp = (offsetSec) => new Date(startMs + offsetSec * 1000).toISOString();

  if (!quiet) console.log(`[sim] ${PLAYER} · ${LEVEL} · ${OUTCOME} · ${SECONDS}s${FAST ? " (fast)" : ""} → ${BASE} (session ${id})`);
  await post("/api/sessions", { id, playerName: PLAYER, level: LEVEL, status: "live", startedAt: stamp(0), source: "simulator", ...snapshot() });
  ev("session_start", `${PLAYER} started ${LEVEL}`, { level: LEVEL, ...snapshot() });

  let nextHp = 1;
  let nextAttack = 2.2;
  let nextGuard = 9 + rnd() * 6;
  let guardUntil = 0;
  let guardBlocked = 0;
  const pending = []; // attack resolutions: { at, attack, hits }
  let lastHitAt = -9;
  let done = null;
  const abandonAt = OUTCOME === "abandoned" ? SECONDS * 0.6 : Infinity;

  while (!done) {
    if (FAST) clock += TICK;
    else await sleep(TICK * 1000);
    const t = now();
    const progress = t / SECONDS;
    const enraged = LEVEL === "Hard" && s.monsterHp / cfg.monsterHp <= 0.33;

    // Swings: 0-3 per tick; effort fades a little late in the fight.
    const fatigue = lerp(1, 0.82, progress);
    // Pace both sides with a feedback loop on the HP fractions: a victory has the monster reach 0 near 90 % of the fight and the player
    // keep at least a third; a defeat the other way round; an abandoned fight stays close and open.
    const monsterTarget = OUTCOME === "victory" ? Math.max(0, 1 - 1.1 * progress) : Math.max(0.45, 1 - 0.45 * progress);
    const playerTarget = OUTCOME === "defeat" ? Math.max(0, 1 - 1.1 * progress) : Math.max(0.4, 1 - 0.5 * progress);
    const pace = Math.min(3, Math.max(0.06, 1 + (s.monsterHp / cfg.monsterHp - monsterTarget) * 7));
    const hurtPace = Math.min(3, Math.max(0.12, 1 + (s.playerHp / cfg.hp - playerTarget) * 7));
    for (let i = Math.floor(rnd() * 3.4); i > 0; i--) {
      // 10 % of swings are raw speeds above the game's 8 m/s cap, like the real headset reports.
      const raw = spikes && rnd() < 0.1 ? 8 + rnd() * 6 : (rnd() * 5.5 + 0.8) * skill * fatigue;
      const speed = r2(Math.max(0.4, raw));
      const hand = rnd() < 0.5 ? "right" : "left";
      if (speed < cfg.min) {
        ev("weak_swing", `Swing too slow (${speed} m/s)`, { swingSpeed: speed, hand, min: cfg.min });
        continue;
      }
      const guarded = t < guardUntil;
      const mul = lerp(1, cfg.maxMul, (Math.min(speed, cfg.cap) - cfg.min) / (cfg.cap - cfg.min));
      let damage = (cfg.base * mul + Math.min(speed, cfg.cap) * 0.08) * cfg.weaponMul * pace * (guarded ? 0.3 : 1);
      damage = r2(Math.min(damage, cfg.monsterHp * 0.12));
      const tier = speed >= cfg.tiers[2] ? "full" : speed >= cfg.tiers[1] ? "medium" : speed >= cfg.tiers[0] ? "light" : "tap";
      let xp = cfg.xpHit + Math.round(damage * 0.5);
      if (tier === "full") { xp += cfg.xpFull; s.fullSwings++; } else if (tier === "medium") xp += Math.round(cfg.xpFull * 0.5); else if (tier === "tap") xp = Math.max(1, Math.floor(xp / 2));
      s.combo = t - lastHitAt <= 2 ? s.combo + 1 : 1;
      lastHitAt = t;
      s.hits++; s.xp += xp; s.score += Math.round(damage); s.damageTotal += damage; s.kcal += 0.09;
      s.monsterHp = Math.max(0, s.monsterHp - damage);
      if (guarded) guardBlocked++;
      ev("hit", `${tier.toUpperCase()} hit for ${damage.toFixed(1)} (${speed} m/s)`, { damage, swingSpeed: speed, tier, hand, defended: guarded, hitIndex: s.hits, ...snapshot() });
      if (rnd() < 0.07) ev("clash", `Weapon clash (${speed} m/s)`, { swingSpeed: speed, hand });
    }

    // The monster guards now and then: monster_defend when it raises its guard, monster_guard (a report) when it drops it.
    if (t >= nextGuard && !guardUntil) {
      guardUntil = t + 2.2 + rnd() * 1.6;
      guardBlocked = 0;
      ev("monster_defend", "Monster raised its guard", {});
    }
    if (guardUntil && t >= guardUntil) {
      ev("monster_guard", guardBlocked ? `Monster HIGH guard vs UPPERCUT: blocked ${guardBlocked}` : "Monster HIGH guard vs UPPERCUT", {
        swingClass: "UPPERCUT", guardType: "HIGH", blocked: guardBlocked > 0, blockedHits: guardBlocked, damagePrevented: r2(guardBlocked * 14), openings: 0, reactive: false, broken: false, seconds: r2(2.8),
      });
      guardUntil = 0;
      nextGuard = t + 10 + rnd() * 8;
    }

    // A monster attack starts (monster_attack), and resolves one wind-up later: it hurts the player, or it is dodged.
    if (t >= nextAttack && t >= guardUntil) {
      const a = attacks[Math.floor(rnd() * attacks.length)];
      const tempo = enraged ? ATTACK_TABLE.enragedTempo : 1;
      ev("monster_attack", `Monster ${a.name} (${a.category})`, { attack: a.name, category: a.category });
      pending.push({ at: t + a.windUp / tempo, attack: a, hits: a.category === "Combo" ? 2 : 1 });
      const [lo, hi] = cfg.gap;
      nextAttack = t + (a.windUp + lo + rnd() * (hi - lo)) / tempo;
    }
    for (let i = pending.length - 1; i >= 0; i--) {
      const p = pending[i];
      if (t < p.at) continue;
      pending.splice(i, 1);
      const lands = rnd() < (OUTCOME === "defeat" ? 0.85 : 0.5);
      if (lands) {
        for (let k = 0; k < p.hits; k++) {
          const amount = r2(cfg.monsterDmg * (CATEGORY_DAMAGE[p.attack.category] || 1) * lerp(0.8, 1.2, rnd()) * hurtPace / p.hits);
          s.playerHp = Math.max(0, s.playerHp - amount);
          ev("player_hurt", `Took ${amount.toFixed(1)} damage`, { amount, blocked: false, attack: p.attack.name, category: p.attack.category, ...snapshot() });
        }
      } else ev("dodge", `Avoided ${p.attack.name}`, { attack: p.attack.name, category: p.attack.category });
    }

    if (t >= nextHp) { ev("hp", "HP snapshot", snapshot()); nextHp += 1; }

    if (s.monsterHp <= 0) done = "victory";
    else if (s.playerHp <= 0) done = "defeat";
    else if (t >= abandonAt) done = "abandoned";
    else if (t >= SECONDS * 1.2) done = OUTCOME === "victory" ? "victory" : OUTCOME;

    if (queue.length) {
      const batch = queue.splice(0, queue.length);
      for (let i = 0; i < batch.length; i += 200) await post("/api/events", { events: batch.slice(i, i + 200) });
      if (!quiet && !FAST) process.stdout.write(`\r[sim] t=${t.toFixed(1)}s hits=${s.hits} xp=${s.xp} monster=${s.monsterHp.toFixed(0)} player=${s.playerHp.toFixed(0)}   `);
    }
  }

  if (done === "abandoned") {
    if (!quiet) console.log(`\n[sim] abandoned at ${now().toFixed(1)}s: no final POST, the server marks it abandoned after its 60 s timeout`);
    return { id, outcome: "abandoned", score: s.score, hits: s.hits };
  }
  const outcome = done;
  if (outcome === "victory") s.score += 500;
  const duration = r2(now());
  await post("/api/sessions", {
    id, playerName: PLAYER, level: LEVEL, outcome, status: "finished", score: s.score, pointsEarned: s.score,
    damageDealt: r2(s.damageTotal), hitCount: s.hits, tapCount: s.hits, durationSec: duration, xp: s.xp, fullSwings: s.fullSwings,
    playerHpRemaining: r2(s.playerHp), monsterHpRemaining: r2(s.monsterHp), startedAt: stamp(0), endedAt: stamp(duration), source: "simulator", notes: "simulated",
  });
  if (!quiet) console.log(`\n[sim] ${outcome} — score ${s.score}, ${s.hits} hits, ${duration}s`);

  if (!KEEP) {
    await sleep((Number(opts.linger) || 0) * 1000);
    const res = await fetch(`${BASE}/api/sessions/${id}`, { method: "DELETE" });
    if (!quiet) console.log(`[sim] cleaned up simulated session (${res.status})`);
  }
  return { id, outcome, score: s.score, hits: s.hits, durationSec: duration };
}

function parseArgs(argv) {
  return Object.fromEntries(
    argv.reduce((acc, a, i, all) => {
      if (a.startsWith("--")) acc.push([a.slice(2).replace(/-([a-z])/g, (_, c) => c.toUpperCase()), all[i + 1] && !all[i + 1].startsWith("--") ? all[i + 1] : true]);
      return acc;
    }, [])
  );
}

// The command line's options: never the seed script's name exemption (a typed --allow-any-name must not switch the ZZTEST rule off).
function cliOptions(argv) {
  const args = parseArgs(argv);
  delete args.allowAnyName;
  return args;
}

module.exports = { runFight, parseArgs, cliOptions, assertSafeTarget, mulberry32, CFG };

if (require.main === module) {
  const args = cliOptions(process.argv.slice(2));
  const run = args.scenario ? require("./scenarios").runScenario(String(args.scenario), args) : runFight(args);
  run.catch((err) => {
    console.error("\n[sim] failed:", err.message);
    process.exitCode = 1;
  });
}
