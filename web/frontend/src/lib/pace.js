// Ghost pace (components/live/GhostPace.jsx): a live fight's cumulative damage against the player's personal-best fight on the same
// level. cumulativeDamage / damageAt mirror backend/effort.js (lib/pace.test.js checks they agree), so the live line and the ghost
// line are built the same way.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round = (v, d = 1) => Math.round(v * 10 ** d) / 10 ** d;

/** hits: [{ t, damage }] -> [{ t, damage }] cumulative from { 0, 0 }, at most maxPoints long (always keeps the last). */
export function cumulativeDamage(hits, maxPoints = 240) {
  const sorted = (hits || []).filter((h) => isNum(h.t) && h.t >= 0 && isNum(h.damage) && h.damage > 0).sort((a, b) => a.t - b.t);
  let total = 0;
  const points = [{ t: 0, damage: 0 }];
  for (const h of sorted) {
    total += h.damage;
    points.push({ t: round(h.t, 2), damage: round(total, 1) });
  }
  if (points.length <= maxPoints) return points;
  const step = (points.length - 1) / (maxPoints - 1);
  const out = [];
  for (let i = 0; i < maxPoints - 1; i++) out.push(points[Math.round(i * step)]);
  out.push(points[points.length - 1]);
  return out;
}

/** Cumulative damage at time t on a curve from cumulativeDamage (a hit counts from its own moment on). */
export function damageAt(points, t) {
  let v = 0;
  for (const p of points || []) {
    if (p.t <= t) v = p.damage;
    else break;
  }
  return v;
}

/** Damage ahead (+) or behind (-) the ghost at fight time t. null when there is no ghost to compare with. */
export function paceDelta(live, ghost, t) {
  if (!ghost || ghost.length < 2) return null;
  return damageAt(live, t) - damageAt(ghost, t);
}

/** "Ahead of your best by 140 dmg" / "Behind by 90 dmg" / "Level with your best". */
export function paceChip(delta) {
  if (delta === null || !isNum(delta)) return null;
  const n = Math.round(Math.abs(delta));
  if (n < 1) return { tone: "level", text: "Level with your best" };
  return delta > 0 ? { tone: "ahead", text: `Ahead of your best by ${n} dmg` } : { tone: "behind", text: `Behind by ${n} dmg` };
}

/** The moment the live fight is at: its own clock, or its last hit if that is later. */
export function liveNow(stateT, livePoints) {
  const last = livePoints.length ? livePoints[livePoints.length - 1].t : 0;
  return Math.max(isNum(stateT) ? stateT : 0, last);
}
