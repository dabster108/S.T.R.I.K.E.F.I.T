// The milestone timeline of one player: first fight, first victory (and on each level), first full-power swing, 100 / 500 / 1,000 counted
// swings, 10 fights, 3- and 7-day play streaks and each rank reached, each with the moment it was reached; plus the next few not yet
// reached with how far along they are. Pure functions (no database, no clock reads except a passed `now`), tested in
// test/milestones.test.js; milestonesStore.js loads the sessions.
//
// Everything is worked out from the player's finished sessions in play order. Swing-count milestones use the running total at the time
// of each session, not the total today, so "500 swings" is dated to the session that crossed 500. A milestone's time is when that
// session ended.

const fitness = require("./fitness");
const ranks = require("./ranks");
const { LEVELS } = require("./levels");
const cfg = require("./config/milestones");

const when = (f) => f.endedAt || f.startedAt;
const iso = (v) => new Date(v).toISOString();
const chronological = (facts) => [...facts].sort((a, b) => new Date(a.startedAt) - new Date(b.startedAt) || String(a.id).localeCompare(String(b.id)));
const num = new Intl.NumberFormat("en-US");

// ---------------------------------------------------------------------------------------------------------------- play streaks

/**
 * Walks the play days (YYYY-MM-DD, any order, duplicates fine) with the streak rule and reports how long the streaks got.
 * -> { longest, reachedOn(length) } where reachedOn is the play day a streak first reached `length` days, or null.
 * A gap of g missed days between two play days is forgiven when g <= forgivenMissedDays and the days forgiven before it, within the
 * previous perDays - 1 days, plus g, stay within forgivenMissedDays. A longer gap, or a second rest day in a week, starts a new streak.
 */
function playStreaks(days, rule = cfg.STREAK_RULE) {
  const nums = [...new Set(days)].map(fitness.dayNum).sort((a, b) => a - b);
  const forgiven = []; // the missed day numbers that were forgiven
  const reached = new Map();
  let run = 0;
  let longest = 0;
  let prev = null;
  for (const n of nums) {
    if (prev !== null) {
      const missed = n - prev - 1;
      if (missed === 0) run++;
      else {
        const first = prev + 1; // the first missed day
        const recent = forgiven.filter((d) => d >= first - (rule.perDays - 1)).length; // days already forgiven in the week before it
        if (missed <= rule.forgivenMissedDays && recent + missed <= rule.forgivenMissedDays) {
          for (let d = first; d < n; d++) forgiven.push(d);
          run++;
        } else run = 1;
      }
    } else run = 1;
    longest = Math.max(longest, run);
    if (!reached.has(run)) reached.set(run, n);
    prev = n;
  }
  return { longest, reachedOn: (length) => (reached.has(length) ? fitness.dayFromNum(reached.get(length)) : null) };
}

// ---------------------------------------------------------------------------------------------------------------- the definitions

const levelEntries = () => LEVELS.map((l) => ({ key: l.key, number: l.number }));

/** Every milestone there can be, in the order they are listed: { id, kind, icon, title, line }. */
function definitions() {
  const defs = [
    { id: "first_fight", kind: "fight", title: "First fight", line: "You finished your first fight." },
    { id: "first_victory", kind: "victory", title: "First victory", line: "A monster went down." },
    ...levelEntries().map((l) => ({ id: `victory_${l.key.toLowerCase()}`, kind: "victory", title: `Level ${l.number} cleared`, line: `First victory on Level ${l.number}${l.key === "Hard" ? " (Hard)" : ""}.` })),
    { id: "first_full_power", kind: "swing", title: "First full-power swing", line: "A swing at full power landed." },
    ...cfg.SWING_COUNTS.map((n) => ({ id: `swings_${n}`, kind: "swing", title: `${num.format(n)} swings`, line: `${num.format(n)} counted swings in total.` })),
    { id: `fights_${cfg.FIGHT_COUNT}`, kind: "fight", title: `${cfg.FIGHT_COUNT} fights`, line: `${cfg.FIGHT_COUNT} finished fights.` },
    ...cfg.STREAK_DAYS.map((n) => ({ id: `streak_${n}`, kind: "streak", title: `${n}-day streak`, line: `Played on ${n} days in a row (one rest day a week is allowed).` })),
    ...ranks.RANKS.slice(1).map((r) => ({ id: `rank_${r.key}`, kind: "rank", title: `Reached ${r.name}`, line: `${num.format(r.from)} effort points.` })),
  ];
  return defs;
}

// ---------------------------------------------------------------------------------------------------------------- the answer

/**
 * facts: all of one player's finished sessions (engage.sessionFacts: id, level, outcome, day, startedAt, endedAt, swings, fullSwings).
 * -> { achieved: [milestone with reachedAt, day, sessionId, progress: 1], upcoming: [milestone with progress 0..1], totals }
 */
function buildMilestones({ playerName, facts, zone, now = new Date() }) {
  const sessions = chronological(facts);
  const found = new Map(); // id -> { at, sessionId }
  const reach = (id, f, at = when(f)) => {
    if (!found.has(id)) found.set(id, { at, sessionId: f.id });
  };

  let swings = 0;
  let fights = 0;
  let sawFullPower = false;
  for (const f of sessions) {
    fights++;
    swings += Math.max(0, Number(f.swings) || 0);
    if (fights === 1) reach("first_fight", f);
    if (f.outcome === "victory") {
      reach("first_victory", f);
      reach(`victory_${String(f.level).toLowerCase()}`, f);
    }
    if (!sawFullPower && Number(f.fullSwings) > 0) {
      sawFullPower = true;
      reach("first_full_power", f);
    }
    for (const n of cfg.SWING_COUNTS) if (swings >= n) reach(`swings_${n}`, f); // the first session at or past the count
    if (fights >= cfg.FIGHT_COUNT) reach(`fights_${cfg.FIGHT_COUNT}`, f);
  }

  const streaks = playStreaks(sessions.map((f) => f.day));
  for (const n of cfg.STREAK_DAYS) {
    const day = streaks.reachedOn(n);
    const f = day && sessions.find((s) => s.day === day);
    if (f) reach(`streak_${n}`, f);
  }

  const timeline = ranks.rankTimeline(sessions);
  for (const step of timeline) {
    const f = sessions.find((s) => s.id === step.sessionId);
    if (f) reach(`rank_${step.rank.key}`, f, step.at);
  }
  const points = ranks.totalPoints(sessions);

  // how far along a milestone that is not reached yet is, 0 to 1
  const progressOf = (d) => {
    const m = /^swings_(\d+)$/.exec(d.id);
    if (m) return Math.min(1, swings / Number(m[1]));
    if (d.id === `fights_${cfg.FIGHT_COUNT}`) return Math.min(1, fights / cfg.FIGHT_COUNT);
    const s = /^streak_(\d+)$/.exec(d.id);
    if (s) return Math.min(1, streaks.longest / Number(s[1]));
    const r = /^rank_(.+)$/.exec(d.id);
    if (r) return Math.min(1, points / ranks.RANKS.find((x) => x.key === r[1]).from);
    return 0; // a "first" has no meter
  };

  const defs = definitions();
  const order = new Map(defs.map((d, i) => [d.id, i]));
  const achieved = defs
    .filter((d) => found.has(d.id))
    .map((d) => ({ ...d, reachedAt: iso(found.get(d.id).at), day: fitness.dayIn(new Date(found.get(d.id).at), zone), sessionId: found.get(d.id).sessionId, progress: 1 }))
    .sort((a, b) => (a.reachedAt < b.reachedAt ? -1 : a.reachedAt > b.reachedAt ? 1 : order.get(a.id) - order.get(b.id)));
  const upcoming = defs
    .filter((d) => !found.has(d.id))
    .map((d) => ({ ...d, reachedAt: null, progress: Math.round(progressOf(d) * 1000) / 1000 }))
    .sort((a, b) => b.progress - a.progress || order.get(a.id) - order.get(b.id))
    .slice(0, cfg.UPCOMING);

  return {
    ok: true,
    playerName,
    timeZone: zone,
    achieved,
    upcoming,
    totals: { fights, swings, effortPoints: Math.round(points), longestStreak: streaks.longest },
    streakRule: `A play day is a day with at least one finished fight (${zone}). ${cfg.STREAK_RULE.forgivenMissedDays} missed day is forgiven in any ${cfg.STREAK_RULE.perDays} days.`,
    generatedAt: iso(now),
  };
}

module.exports = { playStreaks, definitions, buildMilestones };
