// Game facts used by the landing page and dashboard. Every number here is read
// from the Unity project (scene or prefab values override script defaults):
//   minimum speed, cap, damage multiplier, base damage  HitDetection on the player weapons
//                                                        (Weapon_Hammer.prefab, Level2_Medium.unity)
//   tier thresholds, XP awards                           XPManager in Level1_Easy / Level2_Medium
//   weapon multiplier, speed bonus                       PlayerCombat.ApplyFitnessModifiers
//   HP                                                   LevelManager.GetParams

export const LEVELS = {
  Easy: {
    key: "Easy",
    number: 1,
    name: "Level 1",
    difficulty: "Easy",
    weapons: "Dual hammers",
    environment: "Lite Dungeon",
    monster: "Sword-wielding monster",
    status: "playable",
    hp: 500,
    swing: { min: 1.2, cap: 8, maxMul: 2.5, base: 12, weaponMul: 1.15, tiers: { light: 1.5, medium: 3.0, full: 5.0 }, xpHit: 10, xpFull: 25 },
  },
  Medium: {
    key: "Medium",
    number: 2,
    name: "Level 2",
    difficulty: "Medium",
    weapons: "Dual swords",
    environment: "Dark dungeon",
    monster: "Two-handed axe monster",
    status: "playable",
    hp: 800,
    swing: { min: 1.2, cap: 8, maxMul: 2.2, base: 11, weaponMul: 1.0, tiers: { light: 1.4, medium: 2.8, full: 4.6 }, xpHit: 15, xpFull: 40 },
  },
  Hard: {
    key: "Hard",
    number: 3,
    name: "Level 3",
    difficulty: "Hard",
    weapons: "To be decided",
    environment: "Old Seaport",
    monster: "Hammer-wielding monster",
    status: "planned",
  },
};

export const LEVEL_KEYS = ["Easy", "Medium"];
export const levelName = (key) => LEVELS[key]?.name || key || "Unknown";
export const levelShort = (key) => (LEVELS[key] ? `L${LEVELS[key].number}` : key || "?");

export const TIERS = ["tap", "light", "medium", "full"];
export const TIER_LABEL = { none: "No hit", tap: "Tap", light: "Light", medium: "Medium", full: "Full power" };

// Mathf.RoundToInt: round half to even.
export function roundToInt(x) {
  const f = Math.floor(x);
  const d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

export function tierFor(speed, level = "Easy") {
  const s = LEVELS[level].swing;
  if (speed < s.min) return "none";
  if (speed >= s.tiers.full) return "full";
  if (speed >= s.tiers.medium) return "medium";
  if (speed >= s.tiers.light) return "light";
  return "tap";
}

// One hit as HitDetection computes it, for a side-on hit (facing ×1.0), no wrist-rotation
// bonus, stamina available, first hit of the round (no tap bonus).
export function simulateHit(speed, level = "Easy") {
  const s = LEVELS[level].swing;
  const tier = tierFor(speed, level);
  if (tier === "none") return { tier, counts: false, multiplier: 0, damage: 0, xp: 0 };
  const t = Math.min(1, Math.max(0, (speed - s.min) / (s.cap - s.min)));
  const multiplier = 1 + (s.maxMul - 1) * t;
  const damage = (s.base * multiplier + speed * 0.08) * s.weaponMul;
  let xp = s.xpHit + roundToInt(damage * 0.5);
  if (tier === "full") xp += s.xpFull;
  else if (tier === "medium") xp += roundToInt(s.xpFull * 0.5);
  else if (tier === "tap") xp = Math.max(1, Math.trunc(xp / 2));
  return { tier, counts: true, multiplier, damage, xp };
}

export const OUTCOME = {
  victory: { label: "Victory", tone: "good" },
  defeat: { label: "Defeat", tone: "bad" },
  abandoned: { label: "Abandoned", tone: "muted" },
  in_progress: { label: "In progress", tone: "live" },
  timeout: { label: "Time up", tone: "muted" },
  unknown: { label: "Unknown", tone: "muted" },
};
export const outcomeLabel = (o) => OUTCOME[o]?.label || o || "Unknown";
