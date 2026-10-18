// Shared helpers for the effort features (fade, replay, fingerprint, ghost pace). The numbers themselves are computed by the
// backend (backend/effort.js); this file only formats and smooths them for display.

const isNum = (v) => typeof v === "number" && Number.isFinite(v);

/** −12% / +8% / 0%: a real minus sign, always signed except zero. */
export function formatFade(pct) {
  if (!isNum(pct)) return "—";
  const r = Math.round(pct);
  return `${r > 0 ? "+" : r < 0 ? "−" : ""}${Math.abs(r)}%`;
}

/** For screen readers: "minus 12 percent". */
export function speakFade(pct) {
  if (!isNum(pct)) return "not available";
  const r = Math.round(pct);
  return `${r > 0 ? "plus " : r < 0 ? "minus " : ""}${Math.abs(r)} percent`;
}

export const FADE_PLAIN = {
  steady: "Your swings stayed about as fast from start to finish.",
  slowed: "Your swings got slower toward the end of the fight.",
  stronger: "Your swings got faster toward the end of the fight.",
};

// Tone per label: colour is never the only signal (the label text and the sign are always shown with it).
export const FADE_TONE = {
  steady: { chip: "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent-ring)]", text: "text-[var(--accent-strong)]" },
  slowed: { chip: "bg-[var(--warn-soft)] text-[var(--warn-ink)] ring-amber-200", text: "text-[var(--warn-ink)]" },
  stronger: { chip: "bg-[var(--good-soft)] text-[var(--good-ink)] ring-emerald-200", text: "text-[var(--good-ink)]" },
};

/** Centered moving average over neighbouring swings (by order, not time): smooths the line without inventing points. */
export function movingAverage(points, radius = 2) {
  return points.map((p, i) => {
    const from = Math.max(0, i - radius);
    const to = Math.min(points.length - 1, i + radius);
    let sum = 0;
    for (let j = from; j <= to; j++) sum += points[j].y;
    return { x: p.x, y: sum / (to - from + 1) };
  });
}

/**
 * SVG path through pixel points as a monotone cubic curve (the same construction as d3's curveMonotoneX): smooth, and it never
 * overshoots between two neighbouring points, so a dip stays a dip. Points must be in increasing x; equal x's are merged.
 */
export function smoothPath(input) {
  const pts = input.filter((p, i) => i === 0 || p.x > input[i - 1].x);
  if (!pts.length) return "";
  if (pts.length === 1) return `M${pts[0].x},${pts[0].y}`;
  const n = pts.length;
  const sign = (v) => (v < 0 ? -1 : 1);
  const dx = (i) => pts[i + 1].x - pts[i].x;
  const secant = (i) => (pts[i + 1].y - pts[i].y) / dx(i);
  const tangent = new Array(n);
  for (let i = 1; i < n - 1; i++) {
    const s0 = secant(i - 1);
    const s1 = secant(i);
    const p = (s0 * dx(i) + s1 * dx(i - 1)) / (dx(i - 1) + dx(i));
    tangent[i] = (sign(s0) + sign(s1)) * Math.min(Math.abs(s0), Math.abs(s1), 0.5 * Math.abs(p)) || 0;
  }
  tangent[0] = n === 2 ? secant(0) : (3 * secant(0) - tangent[1]) / 2;
  tangent[n - 1] = n === 2 ? secant(0) : (3 * secant(n - 2) - tangent[n - 2]) / 2;
  let d = `M${pts[0].x},${pts[0].y}`;
  for (let i = 0; i < n - 1; i++) {
    const h = dx(i) / 3;
    d += `C${pts[i].x + h},${pts[i].y + h * tangent[i]} ${pts[i + 1].x - h},${pts[i + 1].y - h * tangent[i + 1]} ${pts[i + 1].x},${pts[i + 1].y}`;
  }
  return d;
}

/** The swing nearest to time t, or null. */
export function nearestByTime(points, t) {
  let best = null;
  for (const p of points) if (best === null || Math.abs(p.t - t) < Math.abs(best.t - t)) best = p;
  return best;
}

export const clockText = (s) => `${String(Math.floor(Math.max(0, s) / 60)).padStart(2, "0")}:${String(Math.floor(Math.max(0, s) % 60)).padStart(2, "0")}`;
