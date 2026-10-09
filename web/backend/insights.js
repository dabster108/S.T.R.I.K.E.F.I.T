// Plain-language insights for one player. Each sentence is produced only when the data behind it reaches a stated
// minimum; otherwise the rule is listed under `pending` with what it needs and what exists. Pure functions, no
// database: analytics.playerInsights() feeds them, test/insights.test.js checks them.
//
// Rules (all minimums are in MIN):
//   effort_trend   which third of their fights the player swings fastest in (mean hit swing speed per third, averaged
//                  over fights). Needs MIN.trendSessions fights with at least MIN.trendHitsPerSession hits each and
//                  MIN.trendHits hits in total. A third must lead the mean of the other two by MIN.trendMargin to be
//                  named; otherwise the sentence says the effort stays level.
//   full_share     share of tiered hits that were full power. Needs MIN.tierHits tiered hits.
//   best_worst     best and hardest level by win rate (victories / decided fights). Needs MIN.levels levels with at
//                  least MIN.levelSessions decided fights each, and different win rates.
//   guard          share of hits the monster's guard blocked, and the openings found, from the monster_guard and
//                  opening_hit telemetry. Needs MIN.guardHits hits in fights that carry that telemetry.

const MIN = { trendSessions: 3, trendHitsPerSession: 10, trendHits: 30, trendMargin: 0.1, tierHits: 20, levelSessions: 2, levels: 2, guardHits: 20 };
const THIRDS = ["first", "middle", "last"];
const LEVEL_LABEL = { Easy: "Level 1", Medium: "Level 2", Hard: "Level 3" };

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round = (v, d = 1) => Math.round(v * 10 ** d) / 10 ** d;
const pct = (v) => Math.round(v * 100);
const label = (level) => LEVEL_LABEL[level] || level;

/** sessions: [{ durationSec, hits: [{ t, speed }] }] -> mean speed per third, or a reason it is not enough. */
function effortTrend(sessions) {
  const usable = [];
  let hits = 0;
  for (const s of sessions || []) {
    const list = (s.hits || []).filter((h) => isNum(h.speed) && isNum(h.t));
    if (list.length < MIN.trendHitsPerSession) continue;
    const dur = Math.max(isNum(s.durationSec) ? s.durationSec : 0, ...list.map((h) => h.t), 0.001);
    const sums = [0, 0, 0], ns = [0, 0, 0];
    for (const h of list) {
      const k = Math.min(2, Math.floor((h.t / dur) * 3));
      sums[k] += h.speed; ns[k]++;
    }
    if (ns.some((n) => n === 0)) continue; // a fight with an empty third says nothing about that third
    usable.push(sums.map((x, i) => x / ns[i]));
    hits += list.length;
  }
  const have = { fights: usable.length, hits };
  if (usable.length < MIN.trendSessions || hits < MIN.trendHits) return { ok: false, have };
  const mean = [0, 1, 2].map((i) => usable.reduce((a, v) => a + v[i], 0) / usable.length);
  const top = mean.indexOf(Math.max(...mean));
  const others = mean.filter((_, i) => i !== top);
  const rest = (others[0] + others[1]) / 2;
  const lead = rest > 0 ? (mean[top] - rest) / rest : 0;
  return { ok: true, have, perThird: mean.map((v) => round(v, 2)), top: lead >= MIN.trendMargin ? THIRDS[top] : null, lead: round(lead * 100, 0), others: round(rest, 2) };
}

/** tiers: ["tap" | "light" | "medium" | "full" | null, ...] */
function fullShare(tiers) {
  const tiered = (tiers || []).filter((t) => typeof t === "string" && t);
  const full = tiered.filter((t) => t.toLowerCase() === "full").length;
  const have = { tieredHits: tiered.length };
  if (tiered.length < MIN.tierHits) return { ok: false, have };
  return { ok: true, have, full, tiered: tiered.length, share: full / tiered.length };
}

/** sessions: [{ level, outcome }] (live sessions are ignored by the caller) */
function bestWorstLevel(sessions) {
  const by = new Map();
  for (const s of sessions || []) {
    if (s.outcome !== "victory" && s.outcome !== "defeat") continue;
    const e = by.get(s.level) || { level: s.level, wins: 0, decided: 0 };
    e.decided++;
    if (s.outcome === "victory") e.wins++;
    by.set(s.level, e);
  }
  const levels = [...by.values()].filter((e) => e.decided >= MIN.levelSessions).map((e) => ({ ...e, winRate: e.wins / e.decided }));
  const have = { levelsWithEnough: levels.length };
  if (levels.length < MIN.levels) return { ok: false, have };
  const sorted = [...levels].sort((a, b) => b.winRate - a.winRate || b.decided - a.decided);
  const best = sorted[0], worst = sorted[sorted.length - 1];
  if (best.winRate === worst.winRate) return { ok: false, have: { ...have, sameWinRate: true } };
  return { ok: true, have, best, worst };
}

/** sessions: [{ hits, guardsBlocked, openings }] for fights that carry guard telemetry */
function guardSummary(sessions) {
  const list = sessions || [];
  const hits = list.reduce((a, s) => a + (s.hits || 0), 0);
  const blocked = list.reduce((a, s) => a + (s.guardsBlocked || 0), 0);
  const openings = list.reduce((a, s) => a + (s.openings || 0), 0);
  const have = { fights: list.length, hits };
  if (hits < MIN.guardHits) return { ok: false, have };
  return { ok: true, have, hits, blocked, openings, share: blocked / hits };
}

/**
 * input: { trendSessions, tiers, outcomes, guardSessions } (see the rule functions)
 * -> { insights: [{ id, text, data, minimum }], pending: [{ id, minimum, have }], minimums: MIN }
 */
function playerInsights(input) {
  const insights = [], pending = [];
  const trend = effortTrend(input.trendSessions);
  const trendMin = `${MIN.trendSessions} fights with ${MIN.trendHitsPerSession}+ hits each, ${MIN.trendHits}+ hits in total`;
  if (trend.ok) {
    insights.push({
      id: "effort_trend",
      text: trend.top
        ? `You hit hardest in the ${trend.top} third of your fights: ${trend.perThird[THIRDS.indexOf(trend.top)]} m/s on average, ${trend.lead}% above the other thirds.`
        : `Your swing speed stays level from start to finish (no third more than ${pct(MIN.trendMargin)}% above the others).`,
      data: { perThird: trend.perThird, fights: trend.have.fights, hits: trend.have.hits },
      minimum: trendMin,
    });
  } else pending.push({ id: "effort_trend", minimum: trendMin, have: trend.have });

  const share = fullShare(input.tiers);
  const shareMin = `${MIN.tierHits} hits with a strength tier`;
  if (share.ok) insights.push({ id: "full_share", text: `${pct(share.share)}% of your hits were full power (${share.full} of ${share.tiered}).`, data: { full: share.full, tiered: share.tiered }, minimum: shareMin });
  else pending.push({ id: "full_share", minimum: shareMin, have: share.have });

  const lv = bestWorstLevel(input.outcomes);
  const lvMin = `${MIN.levels} levels with ${MIN.levelSessions}+ won or lost fights each, with different win rates`;
  if (lv.ok) {
    insights.push({
      id: "best_worst",
      text: `Your best level is ${label(lv.best.level)} (won ${lv.best.wins} of ${lv.best.decided}); your hardest is ${label(lv.worst.level)} (won ${lv.worst.wins} of ${lv.worst.decided}).`,
      data: { best: lv.best, worst: lv.worst },
      minimum: lvMin,
    });
  } else pending.push({ id: "best_worst", minimum: lvMin, have: lv.have });

  const g = guardSummary(input.guardSessions);
  const gMin = `${MIN.guardHits} hits in fights recorded with guard telemetry`;
  if (g.ok) {
    insights.push({
      id: "guard",
      text: `The monster guarded ${pct(g.share)}% of your swings; you found ${g.openings} opening${g.openings === 1 ? "" : "s"}.`,
      data: { hits: g.hits, blocked: g.blocked, openings: g.openings, fights: g.have.fights },
      minimum: gMin,
    });
  } else pending.push({ id: "guard", minimum: gMin, have: g.have });

  return { insights, pending, minimums: MIN };
}

module.exports = { MIN, effortTrend, fullShare, bestWorstLevel, guardSummary, playerInsights };
