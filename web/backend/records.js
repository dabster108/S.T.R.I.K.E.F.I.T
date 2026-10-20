// Personal-best alerts: did this finished fight beat everything the player did earlier on the same level? Pure functions (no
// database, no clock), tested in test/records.test.js; recordsStore.js loads the rows.
//
// A row is one finished session: { id, level, outcome, startedAt, values: { peakSwingSpeed, damageDealt, score, fade } } where a
// value that cannot be measured is null. "Earlier" means started earlier (ties by id), so asking again later gives the same answer
// and nothing is stored: a record is a function of the rows.

const { METRICS, DECIDED_OUTCOMES, MIN_EARLIER_FIGHTS } = require("./config/records");

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round = (v, d) => Math.round(v * 10 ** d) / 10 ** d;
const decided = (outcome) => DECIDED_OUTCOMES.includes(outcome);
const byStart = (a, b) => new Date(a.startedAt) - new Date(b.startedAt) || String(a.id).localeCompare(String(b.id));

/** The four comparable values of a session: fact = engage.sessionFacts row, fadeResult = effort.fade(...) (or null). */
function recordValues(fact, fadeResult) {
  return {
    peakSwingSpeed: isNum(fact.peakSpeed) ? fact.peakSpeed : null,
    damageDealt: isNum(fact.damageDealt) ? fact.damageDealt : null,
    score: isNum(fact.score) ? fact.score : null,
    fade: fadeResult && fadeResult.status === "ok" && isNum(fadeResult.fadePct) ? fadeResult.fadePct : null,
  };
}

/** The best value of each metric among rows (null where no row has one), rounded as the comparison rounds. */
function bests(rows) {
  const out = {};
  for (const m of METRICS) {
    const vals = rows.map((r) => r.values[m.key]).filter(isNum);
    out[m.key] = vals.length ? round(Math.max(...vals), m.decimals) : null;
  }
  return out;
}

function compare(values, previous) {
  const records = [];
  for (const m of METRICS) {
    const now = values[m.key];
    const before = previous[m.key];
    if (!isNum(now) || before === null || before === undefined) continue; // nothing measured, or nothing to beat for this metric
    if (round(now, m.decimals) > before) records.push({ metric: m.key, value: round(now, m.decimals), previous: before });
  }
  return records;
}

/**
 * The records one session set: [{ metric, value, previous }]. `rows` are all the player's finished sessions. A record needs at least
 * MIN_EARLIER_FIGHTS decided fights earlier on the same level, and an abandoned session never sets one.
 */
function newRecordsFor(sessionId, rows) {
  const target = rows.find((r) => r.id === sessionId);
  if (!target || !decided(target.outcome)) return [];
  const earlier = rows.filter((r) => r.id !== target.id && r.level === target.level && decided(r.outcome) && byStart(r, target) < 0);
  if (earlier.length < MIN_EARLIER_FIGHTS) return [];
  return compare(target.values, bests(earlier));
}

/**
 * For every session of one player, the metrics it set records in (the PB chip in the lists): { [sessionId]: [metric, ...] }.
 * One pass in play order with a running best per level; it gives exactly what newRecordsFor gives for each session.
 */
function recordFlags(rows) {
  const out = {};
  const state = new Map(); // level -> { n, best }
  for (const row of [...rows].sort(byStart)) {
    if (!decided(row.outcome)) continue;
    const s = state.get(row.level) || { n: 0, best: Object.fromEntries(METRICS.map((m) => [m.key, null])) };
    if (s.n >= MIN_EARLIER_FIGHTS) {
      const found = compare(row.values, s.best);
      if (found.length) out[row.id] = found.map((r) => r.metric);
    }
    for (const m of METRICS) {
      const v = row.values[m.key];
      if (isNum(v)) {
        const r = round(v, m.decimals);
        if (s.best[m.key] === null || r > s.best[m.key]) s.best[m.key] = r;
      }
    }
    s.n++;
    state.set(row.level, s);
  }
  return out;
}

module.exports = { METRICS, recordValues, bests, newRecordsFor, recordFlags };
