// Data shaping for the Compare page. Both modes (two players, two levels) are reduced
// to two "sides" of the same shape, so every panel is written once. Nothing is
// estimated: a value a side has no data for is null and the panels show "—".

const num = (v) => (Number.isFinite(Number(v)) ? Number(v) : 0);
export const isNum = (v) => typeof v === "number" && Number.isFinite(v);

// YYYY-MM-DD in the viewer's time zone (the same day the Overview's per-day chart uses).
const pad2 = (n) => String(n).padStart(2, "0");
const dayKey = (d) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
export function localDay(iso) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? null : dayKey(d);
}

// One finished fight. `speed` and `peak` stay null unless the fight has measured swings.
const fight = (s) => ({
  id: s.id,
  level: s.level,
  outcome: s.outcome,
  startedAt: s.startedAt,
  who: s.playerName || null,
  score: num(s.score),
  damage: num(s.damageDealt),
  hits: num(s.hitCount),
  durationSec: num(s.durationSec),
  speed: s.swingSamples > 0 && isNum(s.avgSwingSpeed) ? s.avgSwingSpeed : null,
  peak: s.swingSamples > 0 && isNum(s.peakSwingSpeed) ? s.peakSwingSpeed : null,
});

const byStart = (x, y) => new Date(x.startedAt) - new Date(y.startedAt);

function side(label, fights, swing) {
  return {
    label,
    fights, // oldest → newest
    // Early builds reported neither hits nor damage; their zeros are "not recorded", not "none landed".
    hasHits: fights.some((f) => f.hits > 0),
    tiers: swing.samples > 0 ? swing.tiers : null,
    swingSamples: swing.samples || 0,
    peakSwingSpeed: swing.samples > 0 && isNum(swing.peak) ? swing.peak : null,
  };
}

/** A player profile (api.player). `series` already carries each fight's measured swing speed. */
export function playerSide(p) {
  const fights = (p.series || []).filter((s) => s.status !== "live").map(fight).sort(byStart);
  return { ...side(p.playerName, fights, { samples: p.totals.swingSamples, tiers: p.tiers, peak: p.totals.peakSwingSpeed }), byLevel: p.byLevel || [] };
}

/** One level across every player: fights from api.sessions(), swing figures from api.analytics(). */
export function levelSide(level, label, sessions, analytics) {
  const fights = (sessions || []).filter((s) => s.level === level && s.status !== "live").map(fight).sort(byStart);
  const row = analytics.levelComparison.find((l) => l.level === level);
  const tiers = analytics.tiers.find((t) => t.level === level)?.counts;
  return side(label, fights, { samples: row?.swingSamples, tiers, peak: row?.peakSwingSpeed });
}

/** Tally of head-to-head rows ({ label, a, b, fmt }): who is higher on how many, the widest gap and the narrowest. */
export function leadSummary(rows) {
  const out = { a: 0, b: 0, tied: 0, missing: 0 };
  const gaps = [];
  for (const row of rows) {
    if (!isNum(row.a) || !isNum(row.b)) out.missing++;
    else if (row.a === row.b) out.tied++;
    else {
      const lead = row.a > row.b ? "a" : "b";
      out[lead]++;
      const hi = Math.max(row.a, row.b);
      const lo = Math.min(row.a, row.b);
      // share = the gap as a part of the higher value: bounded, so a side at 0 still ranks.
      gaps.push({ row, lead, hi, lo, share: (hi - lo) / hi, ratio: lo > 0 ? hi / lo : null });
    }
  }
  gaps.sort((x, y) => y.share - x.share);
  return { ...out, compared: out.a + out.b + out.tied, widest: gaps[0] || null, closest: gaps.length > 1 ? gaps[gaps.length - 1] : null };
}

/** "45% higher", "3.2× as high", or null when the lower side is at zero. */
export function gapText(gap) {
  if (!gap || gap.ratio === null) return null;
  return gap.ratio >= 2 ? `${gap.ratio.toFixed(1)}× as high` : `${Math.max(1, Math.round((gap.ratio - 1) * 100))}% higher`;
}

export function outcomeCounts(fights) {
  const victory = fights.filter((f) => f.outcome === "victory").length;
  const defeat = fights.filter((f) => f.outcome === "defeat").length;
  return { victory, defeat, other: fights.length - victory - defeat };
}

// Longest run of victories, oldest to newest. Null until a fight has been decided.
function bestStreak(fights) {
  if (!fights.some((f) => f.outcome === "victory" || f.outcome === "defeat")) return null;
  let best = 0;
  let run = 0;
  for (const f of fights) {
    run = f.outcome === "victory" ? run + 1 : 0;
    best = Math.max(best, run);
  }
  return best;
}

/** Personal bests of one side, keyed by record. Each is { value, fight } or null when no fight qualifies. */
export function bests(s) {
  const pick = (list, get, lower = false) => {
    const f = list.reduce((m, x) => (!m || (lower ? get(x) < get(m) : get(x) > get(m)) ? x : m), null);
    return f ? { value: get(f), fight: f } : null;
  };
  const timed = s.fights.filter((f) => f.durationSec > 0);
  const swung = s.fights.filter((f) => f.peak !== null);
  const streak = bestStreak(s.fights);
  return {
    score: pick(s.fights.filter((f) => f.score > 0), (f) => f.score),
    damage: pick(s.fights.filter((f) => f.damage > 0), (f) => f.damage),
    hits: pick(s.fights.filter((f) => f.hits > 0), (f) => f.hits),
    longest: pick(timed, (f) => f.durationSec),
    fastestWin: pick(timed.filter((f) => f.outcome === "victory"), (f) => f.durationSec, true),
    // Per-fight peaks exist for players; a level only has its overall peak.
    peak: swung.length ? pick(swung, (f) => f.peak) : s.peakSwingSpeed !== null ? { value: s.peakSwingSpeed, fight: null } : null,
    streak: streak === null ? null : { value: streak, fight: null },
  };
}

const MAX_BUCKETS = 14;
const monday = (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate() - ((d.getDay() + 6) % 7));
const UNITS = {
  day: { start: (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate()), next: (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1) },
  week: { start: monday, next: (d) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + 7) },
  month: { start: (d) => new Date(d.getFullYear(), d.getMonth(), 1), next: (d) => new Date(d.getFullYear(), d.getMonth() + 1, 1) },
};

/**
 * Fights started per day by each side, from the first fight of either side to the last.
 * Widens to weeks, then months, so there are never more than 14 columns; `clipped`
 * is true when even months overflow and only the latest 14 are kept.
 */
export function activity(a, b) {
  const dates = (s) => s.fights.map((f) => new Date(f.startedAt)).filter((d) => !Number.isNaN(d.getTime()));
  const [da, db] = [dates(a), dates(b)];
  const all = [...da, ...db];
  if (!all.length) return { unit: "day", buckets: [], clipped: false };
  const first = new Date(Math.min(...all));
  const last = new Date(Math.max(...all));

  const build = (unit) => {
    const u = UNITS[unit];
    const end = dayKey(u.start(last));
    const buckets = [];
    for (let d = u.start(first); buckets.length < 600; d = u.next(d)) {
      buckets.push({ key: dayKey(d), date: d, a: 0, b: 0 });
      if (dayKey(d) === end) break;
    }
    const index = new Map(buckets.map((x) => [x.key, x]));
    for (const [list, k] of [[da, "a"], [db, "b"]]) {
      for (const d of list) {
        const bucket = index.get(dayKey(u.start(d)));
        if (bucket) bucket[k]++;
      }
    }
    return buckets;
  };

  const unit = ["day", "week", "month"].find((u) => u === "month" || build(u).length <= MAX_BUCKETS);
  const buckets = build(unit);
  return { unit, buckets: buckets.slice(-MAX_BUCKETS), clipped: buckets.length > MAX_BUCKETS };
}

/** Days with at least one fight, and the most recent one, for the activity caption. */
export function daysPlayed(s) {
  const days = [...new Set(s.fights.map((f) => localDay(f.startedAt)).filter(Boolean))].sort();
  return { count: days.length, last: s.fights.length ? s.fights[s.fights.length - 1].startedAt : null };
}
