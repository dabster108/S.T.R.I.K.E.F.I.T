// Plays a fake fight against the API exactly the way the Unity SessionReporter
// does (live session POST, batched events, 1 s HP snapshots, final POST), so
// the dashboard's live views can be tested without a headset.
//
//   npm run simulate -- --url http://localhost:4000 --player "Sim Player" --level Medium --seconds 30
//
// The simulated session is DELETED at the end unless --keep is passed, so it
// never ends up in the statistics. Numbers follow the game's scene values
// (Level1_Easy / Level2_Medium) but the swings themselves are random.

const args = Object.fromEntries(
  process.argv.slice(2).reduce((acc, a, i, all) => {
    if (a.startsWith("--")) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith("--") ? all[i + 1] : true]);
    return acc;
  }, [])
);

const BASE = String(args.url || "http://localhost:4000").replace(/\/+$/, "");
const PLAYER = String(args.player || "Simulator");
const LEVEL = args.level === "Medium" ? "Medium" : "Easy";
const SECONDS = Number(args.seconds) || 30;
const KEEP = Boolean(args.keep);

// From the scenes: HitDetection, PlayerCombat, XPManager, LevelManager.GetParams.
const CFG = {
  Easy: { hp: 500, monsterHp: 500, base: 12, min: 1.2, cap: 8, maxMul: 2.5, weaponMul: 1.15, tiers: [1.5, 3.0, 5.0], xpHit: 10, xpFull: 25, monsterDmg: 8 * 1.1 },
  Medium: { hp: 800, monsterHp: 800, base: 11, min: 1.2, cap: 8, maxMul: 2.2, weaponMul: 1.0, tiers: [1.4, 2.8, 4.6], xpHit: 15, xpFull: 40, monsterDmg: 14 * 1.2 },
}[LEVEL];
const ATTACKS = LEVEL === "Easy"
  ? [["Slash", "Standard"], ["Heavy", "Heavy"], ["Special", "Special"]]
  : [["Overhead", "Standard"], ["Sweep", "Standard"], ["Ground Slam", "IdleSlam"], ["Spin", "Heavy"], ["Fire Combo", "Combo"]];

const lerp = (a, b, t) => a + (b - a) * Math.min(1, Math.max(0, t));
const tierOf = (v) => (v >= CFG.tiers[2] ? "full" : v >= CFG.tiers[1] ? "medium" : v >= CFG.tiers[0] ? "light" : "tap");
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const r2 = (n) => Math.round(n * 100) / 100;

async function post(path, body) {
  const res = await fetch(BASE + path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!res.ok) throw new Error(`${path} -> ${res.status} ${await res.text()}`);
  return res.json();
}

async function main() {
  const id = [...crypto.getRandomValues(new Uint8Array(16))].map((b) => b.toString(16).padStart(2, "0")).join("");
  const s = { playerHp: CFG.hp, monsterHp: CFG.monsterHp, xp: 0, score: 0, hits: 0, damageTotal: 0, fullSwings: 0 };
  const t0 = Date.now();
  const t = () => r2((Date.now() - t0) / 1000);
  const queue = [];
  const ev = (type, message, payload) => queue.push({ type, sessionId: id, playerName: PLAYER, message, payload: { t: t(), ...payload } });
  const snapshot = () => ({ playerHp: r2(s.playerHp), playerMaxHp: CFG.hp, monsterHp: r2(s.monsterHp), monsterMaxHp: CFG.monsterHp, xp: s.xp, score: s.score, hits: s.hits, damageTotal: r2(s.damageTotal), fullSwings: s.fullSwings, timeLeft: Math.max(0, 40 - t()) });

  console.log(`[sim] ${PLAYER} · ${LEVEL} · ${SECONDS}s → ${BASE} (session ${id})`);
  await post("/api/sessions", { id, playerName: PLAYER, level: LEVEL, status: "live", startedAt: new Date().toISOString(), source: "simulator", ...snapshot() });
  ev("session_start", `${PLAYER} started ${LEVEL}`, { level: LEVEL, ...snapshot() });

  let nextHp = 1000;
  let nextAttack = 2500;
  let outcome = null;
  while (!outcome) {
    await sleep(400);
    const ms = Date.now() - t0;

    // 0-3 swings per 0.4 s tick; effort fades a little late in the fight.
    const fatigue = lerp(1, 0.75, ms / (SECONDS * 1000));
    for (let i = Math.floor(Math.random() * 3); i > 0; i--) {
      const speed = r2(Math.max(0.4, (Math.random() * 5.5 + 0.8) * fatigue));
      const hand = Math.random() < 0.5 ? "right" : "left";
      if (speed < CFG.min) {
        ev("weak_swing", `Swing too slow (${speed} m/s)`, { swingSpeed: speed, hand, min: CFG.min });
        continue;
      }
      const mul = lerp(1, CFG.maxMul, (speed - CFG.min) / (CFG.cap - CFG.min));
      const damage = r2((CFG.base * mul + speed * 0.08) * CFG.weaponMul);
      const tier = tierOf(speed);
      let xp = CFG.xpHit + Math.round(damage * 0.5);
      if (tier === "full") { xp += CFG.xpFull; s.fullSwings++; } else if (tier === "medium") xp += Math.round(CFG.xpFull * 0.5); else if (tier === "tap") xp = Math.max(1, Math.floor(xp / 2));
      s.hits++; s.xp += xp; s.score += Math.round(damage); s.damageTotal += damage;
      s.monsterHp = Math.max(0, s.monsterHp - damage);
      ev("hit", `${tier.toUpperCase()} hit for ${damage.toFixed(1)} (${speed} m/s)`, { damage, swingSpeed: speed, tier, hand, defended: false, hitIndex: s.hits, ...snapshot() });
      if (Math.random() < 0.05) ev("clash", `Weapon clash (${speed} m/s)`, { swingSpeed: speed, hand });
    }

    if (ms >= nextAttack) {
      const [name, category] = ATTACKS[Math.floor(Math.random() * ATTACKS.length)];
      ev("monster_attack", `Monster ${name} (${category})`, { attack: name, category });
      if (Math.random() < 0.55) {
        const amount = r2(CFG.monsterDmg * (category === "Standard" ? 1 : 1.4));
        s.playerHp = Math.max(0, s.playerHp - amount);
        ev("player_hurt", `Took ${amount.toFixed(1)} damage`, { amount, blocked: false, ...snapshot() });
      } else ev("dodge", `Dodged ${name}`, { attack: name, category });
      nextAttack += LEVEL === "Easy" ? 3200 : 2200;
    }

    if (ms >= nextHp) {
      ev("hp", "HP snapshot", snapshot());
      nextHp += 1000;
    }

    if (s.monsterHp <= 0) outcome = "victory";
    else if (s.playerHp <= 0) outcome = "defeat";
    else if (ms >= SECONDS * 1000) outcome = s.monsterHp / CFG.monsterHp < s.playerHp / CFG.hp ? "victory" : "defeat";

    if (queue.length) {
      const batch = queue.splice(0, queue.length);
      await post("/api/events", { events: batch });
      process.stdout.write(`\r[sim] t=${t().toFixed(1)}s hits=${s.hits} xp=${s.xp} monster=${s.monsterHp.toFixed(0)} player=${s.playerHp.toFixed(0)}   `);
    }
  }

  if (outcome === "victory") s.score += 500;
  const duration = t();
  await post("/api/sessions", {
    id, playerName: PLAYER, level: LEVEL, outcome, status: "finished", score: s.score, pointsEarned: s.score,
    damageDealt: r2(s.damageTotal), hitCount: s.hits, tapCount: s.hits, durationSec: duration, xp: s.xp, fullSwings: s.fullSwings,
    playerHpRemaining: r2(s.playerHp), monsterHpRemaining: r2(s.monsterHp),
    startedAt: new Date(t0).toISOString(), endedAt: new Date().toISOString(), source: "simulator", notes: "simulated",
  });
  console.log(`\n[sim] ${outcome} — score ${s.score}, ${s.hits} hits, ${duration}s`);

  if (!KEEP) {
    await sleep(Number(args.linger) * 1000 || 0);
    const res = await fetch(`${BASE}/api/sessions/${id}`, { method: "DELETE" });
    console.log(`[sim] cleaned up simulated session (${res.status})`);
  }
}

main().catch((err) => {
  console.error("\n[sim] failed:", err.message);
  process.exitCode = 1;
});
