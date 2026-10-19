// A scripted fight for DEMO MODE: the same fight the dev simulator plays (backend/scripts/simulate-live.js), computed ahead of time on a
// virtual clock with a seeded random generator, so the Live Arena can loop it with no server at all. Pure (no JSON import, no clock, no
// network): makeFightBuilder() takes the monsters' attack table as an argument, like lib/arena.js; fightApp.js binds the real file.
// Everything the arena reads is produced here exactly as the headset sends it: swing events with speed, tier, damage and HP snapshots,
// monster attacks with their wind-ups, hurts and dodges, guards and clashes, then the result.

// From the scenes / prefabs, as in simulate-live.js (demo/demo.test.js checks the two agree).
export const CFG = {
  Easy: { hp: 500, monsterHp: 500, base: 12, min: 1.2, cap: 8, maxMul: 2.5, weaponMul: 1.15, tiers: [1.5, 3.0, 5.0], xpHit: 10, xpFull: 25, monsterDmg: 8.8, gap: [1.2, 2.2] },
  Medium: { hp: 800, monsterHp: 800, base: 11, min: 1.2, cap: 8, maxMul: 2.2, weaponMul: 1.0, tiers: [1.4, 2.8, 4.6], xpHit: 15, xpFull: 40, monsterDmg: 16.8, gap: [0.8, 1.6] },
  Hard: { hp: 1200, monsterHp: 5600, base: 14, min: 1.6, cap: 8, maxMul: 2.6, weaponMul: 1.0, tiers: [1.4, 2.8, 4.6], xpHit: 15, xpFull: 40, monsterDmg: 26, gap: [0.5, 1.2] },
};
const CATEGORY_DAMAGE = { Standard: 1, Heavy: 1.5, IdleSlam: 2, Special: 1.7, Combo: 0.8 };
export const TICK = 0.4; // seconds of fight time per step

const r2 = (n) => Math.round(n * 100) / 100;
const lerp = (a, b, t) => a + (b - a) * Math.min(1, Math.max(0, t));

export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// The numeric payload keys that make up a live session's latest state (backend/server.js LIVE_KEYS).
export const LIVE_KEYS = ["playerHp", "playerMaxHp", "monsterHp", "monsterMaxHp", "xp", "score", "hits", "damageTotal", "fullSwings", "t", "timeLeft", "stamina", "maxStamina", "combo", "effort", "kcal"];

/**
 * buildFight({ playerName, levelKey, seconds, outcome, seed }) -> { playerName, levelKey, outcome, seconds, durationSec, steps }
 * steps: [{ at, events: [{ type, message, payload }], state, session }] in fight seconds; `state` is the live state after the step's
 * events, `session` the running totals (score, hitCount, damageDealt, xp, fullSwings). The last step carries `final: true`.
 */
export function makeFightBuilder({ attackTable }) {
  return function buildFight({ playerName, levelKey, seconds = 45, outcome = "victory", seed = 1 }) {
    const cfg = CFG[levelKey];
    if (!cfg) throw new Error(`unknown level ${levelKey}`);
    const attacks = Object.entries(attackTable.levels[levelKey]).map(([name, a]) => ({ name, ...a }));
    const rnd = mulberry32(seed);
    const skill = lerp(0.8, 1.15, rnd());
    const s = { playerHp: cfg.hp, monsterHp: cfg.monsterHp, xp: 0, score: 0, hits: 0, damageTotal: 0, fullSwings: 0, combo: 0, kcal: 0 };
    let clock = 0;
    const live = {}; // the folded live state, like the server's mergeLiveState
    const steps = [];

    const snapshot = () => ({
      playerHp: r2(s.playerHp), playerMaxHp: cfg.hp, monsterHp: r2(s.monsterHp), monsterMaxHp: cfg.monsterHp, xp: s.xp, score: s.score, hits: s.hits,
      damageTotal: r2(s.damageTotal), fullSwings: s.fullSwings, combo: s.combo, effort: r2(Math.min(1, 0.35 + s.combo * 0.08)), kcal: r2(s.kcal),
      stamina: 100, maxStamina: 100, timeLeft: Math.max(0, 40 - (clock % 40)),
    });
    let queue = [];
    const ev = (type, message, payload) => {
      const p = { t: r2(clock), ...payload };
      queue.push({ type, message, payload: p });
      for (const k of LIVE_KEYS) if (typeof p[k] === "number" && Number.isFinite(p[k])) live[k] = p[k];
      if (type !== "hp") live.lastEvent = { type, message };
    };
    const flush = (extra = {}) => {
      if (!queue.length && !extra.final) return;
      steps.push({
        at: r2(clock), events: queue, state: { ...live },
        session: { score: s.score, hitCount: s.hits, tapCount: s.hits, damageDealt: r2(s.damageTotal), xp: s.xp, fullSwings: s.fullSwings, playerHpRemaining: r2(s.playerHp), monsterHpRemaining: r2(s.monsterHp) },
        ...extra,
      });
      queue = [];
    };

    ev("session_start", `${playerName} started ${levelKey}`, { level: levelKey, ...snapshot() });
    flush();

    let nextHp = 1, nextAttack = 2.2, nextGuard = 9 + rnd() * 6, guardUntil = 0, guardBlocked = 0, lastHitAt = -9, done = null;
    const pending = [];
    const abandonAt = outcome === "abandoned" ? seconds * 0.6 : Infinity;

    while (!done) {
      clock += TICK;
      const t = clock;
      const progress = t / seconds;
      const enraged = levelKey === "Hard" && s.monsterHp / cfg.monsterHp <= 0.33;
      const fatigue = lerp(1, 0.82, progress);
      // Pace both sides with feedback on the HP fractions so the fight ends when the script says it should (see simulate-live.js).
      const monsterTarget = outcome === "victory" ? Math.max(0, 1 - 1.1 * progress) : Math.max(0.45, 1 - 0.45 * progress);
      const playerTarget = outcome === "defeat" ? Math.max(0, 1 - 1.1 * progress) : Math.max(0.4, 1 - 0.5 * progress);
      const pace = Math.min(3, Math.max(0.06, 1 + (s.monsterHp / cfg.monsterHp - monsterTarget) * 7));
      const hurtPace = Math.min(3, Math.max(0.12, 1 + (s.playerHp / cfg.hp - playerTarget) * 7));

      for (let i = Math.floor(rnd() * 3.4); i > 0; i--) {
        const raw = rnd() < 0.1 ? 8 + rnd() * 6 : (rnd() * 5.5 + 0.8) * skill * fatigue;
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

      if (t >= nextGuard && !guardUntil) {
        guardUntil = t + 2.2 + rnd() * 1.6;
        guardBlocked = 0;
        ev("monster_defend", "Monster raised its guard", {});
      }
      if (guardUntil && t >= guardUntil) {
        ev("monster_guard", guardBlocked ? `Monster HIGH guard vs UPPERCUT: blocked ${guardBlocked}` : "Monster HIGH guard vs UPPERCUT", {
          swingClass: "UPPERCUT", guardType: "HIGH", blocked: guardBlocked > 0, blockedHits: guardBlocked, damagePrevented: r2(guardBlocked * 14), openings: 0, reactive: false, broken: false, seconds: 2.8,
        });
        guardUntil = 0;
        nextGuard = t + 10 + rnd() * 8;
      }

      if (t >= nextAttack && t >= guardUntil) {
        const a = attacks[Math.floor(rnd() * attacks.length)];
        const tempo = enraged ? attackTable.enragedTempo : 1;
        ev("monster_attack", `Monster ${a.name} (${a.category})`, { attack: a.name, category: a.category });
        pending.push({ at: t + a.windUp / tempo, attack: a, hits: a.category === "Combo" ? 2 : 1 });
        const [lo, hi] = cfg.gap;
        nextAttack = t + (a.windUp + lo + rnd() * (hi - lo)) / tempo;
      }
      for (let i = pending.length - 1; i >= 0; i--) {
        const p = pending[i];
        if (t < p.at) continue;
        pending.splice(i, 1);
        if (rnd() < (outcome === "defeat" ? 0.85 : 0.5)) {
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
      else if (t >= seconds * 1.2) done = outcome === "victory" ? "victory" : outcome;
      flush();
    }

    let final = done;
    if (final === "victory") s.score += 500;
    ev("session_complete", `${playerName} finished ${levelKey}: ${final} — score ${s.score}`, { outcome: final, score: s.score });
    flush({ final: true, outcome: final });
    return { playerName, levelKey, outcome: final, seconds, durationSec: r2(clock), steps };
  };
}
