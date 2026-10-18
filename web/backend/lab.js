// Thesis Lab (GET /api/lab and GET /api/lab/sessions.csv): return rate, perceived exertion against swing speed, a level-by-level
// table and a research CSV. Read-only; every number comes from the sessions / events / session_feedback tables through the shared
// rules (outcomes.js for win rate, effort.js for fade, correlation and return rate). Research prototype: the sample is small and
// the page says so. Names are already pseudonyms, so nothing is masked, but the CSV carries no IP address and no device
// information (the database holds none).

const effort = require("./effort");
const outcomes = require("./outcomes");
const levelsInfo = require("./levels");

const NOTE = "Research prototype. Small sample. Correlation is not causation.";
const CACHE_MS = 30_000;
const LEVEL_ORDER = levelsInfo.LEVELS.map((l) => l.key);
const levelRank = (l) => (LEVEL_ORDER.indexOf(l) === -1 ? 99 : LEVEL_ORDER.indexOf(l));

const round = effort.round;
const meanOf = (xs) => (xs.length ? effort.mean(xs) : null);

// One row per finished or abandoned session, with the hit-derived figures the Lab needs.
async function loadRows() {
  // Required here, not at the top, so buildLab / toCsv can be tested without a database connection.
  const { query } = require("./db");
  const analytics = require("./analytics");
  const sessions = (await query(
    `SELECT s.id, s.player_name AS player, s.level, s.outcome, s.score, s.hit_count AS hits, s.duration_sec AS "durationSec",
            s.started_at AS "startedAt", f.rpe
     FROM sessions s LEFT JOIN session_feedback f ON f.session_id = s.id
     WHERE s.status <> 'live' ORDER BY s.started_at, s.seq`
  )).rows;
  const hitsBy = await analytics.hitsForSessions(sessions.map((s) => s.id));
  return sessions.map((s) => {
    const hits = hitsBy.get(s.id) || [];
    const speeds = hits.map((h) => h.speed).filter((v) => typeof v === "number");
    return {
      ...s,
      startedAt: new Date(s.startedAt).toISOString(),
      score: Number(s.score) || 0,
      hits: Number(s.hits) || 0,
      durationSec: Number(s.durationSec) || 0,
      avgSpeed: speeds.length ? effort.mean(speeds) : null, // already clamped to the game's cap
      swings: speeds.length,
      speeds,
      fade: effort.fade(hits, Number(s.durationSec) || 0),
    };
  });
}

function buildLab(rows, nowMs = Date.now()) {
  const levels = [...new Set([...LEVEL_ORDER, ...rows.map((r) => r.level)])].sort((a, b) => levelRank(a) - levelRank(b));
  const rpePoints = rows.filter((r) => typeof r.rpe === "number" && r.avgSpeed !== null).map((r) => ({ sessionId: r.id, level: r.level, rpe: r.rpe, avgSpeed: round(r.avgSpeed, 2) }));
  return {
    ok: true,
    generatedAt: new Date(nowMs).toISOString(),
    note: NOTE,
    rule: outcomes.RULE,
    speedCap: levelsInfo.SWING_SPEED_CAP,
    sample: {
      sessions: rows.length,
      players: new Set(rows.map((r) => r.player)).size,
      sessionsWithSwingData: rows.filter((r) => r.swings > 0).length,
      sessionsRated: rows.filter((r) => typeof r.rpe === "number").length,
    },
    returnRate: {
      definition: "Share of players who played another fight within 1, 3 or 7 days of their first one. A window only counts players whose first fight is at least that old.",
      smallSampleBelow: effort.SMALL_SAMPLE,
      ...effort.returnRates(rows.map((r) => ({ player: r.player, startedAt: r.startedAt })), nowMs),
    },
    rpe: {
      definition: "Borg RPE (6 to 20, asked after the fight) against that fight's average swing speed. Pearson r needs at least 8 rated fights with swing data.",
      points: rpePoints,
      ...effort.rpeCorrelation(rpePoints),
    },
    levels: levels.map((level) => {
      const list = rows.filter((r) => r.level === level);
      const o = outcomes.countOutcomes(list.map((r) => r.outcome));
      const decided = list.filter((r) => outcomes.isDecided(r.outcome));
      const speeds = list.flatMap((r) => r.speeds);
      const fades = list.filter((r) => r.fade.status === "ok").map((r) => r.fade.fadePct);
      const status = (levelsInfo.LEVELS.find((l) => l.key === level) || {}).status || null;
      return {
        level,
        levelStatus: status,
        sessions: list.length,
        victories: o.victories,
        defeats: o.defeats,
        abandoned: o.abandoned,
        decided: o.decided,
        winRate: o.winRate,
        avgScore: decided.length ? Math.round(effort.mean(decided.map((r) => r.score))) : null,
        avgSwingSpeed: speeds.length ? round(effort.mean(speeds), 2) : null,
        swings: speeds.length,
        avgFadePct: fades.length ? round(effort.mean(fades), 1) : null,
        fadeSessions: fades.length,
        avgDurationSec: list.some((r) => r.durationSec > 0) ? round(meanOf(list.filter((r) => r.durationSec > 0).map((r) => r.durationSec)), 1) : null,
      };
    }),
  };
}

let cache = null;
async function lab() {
  if (cache && Date.now() - cache.at < CACHE_MS) return { ...cache.value, cached: true };
  const value = buildLab(await loadRows());
  cache = { at: Date.now(), value };
  return { ...value, cached: false };
}

// ---------------------------------------------------------------------------
// CSV
// ---------------------------------------------------------------------------

const CSV_COLS = ["player", "level", "outcome", "score", "hits", "avg_swing", "fade_pct", "duration_sec", "started_at"];

// Quote when needed; a cell that starts with = + - @ is prefixed with ' so a spreadsheet can't run a typed player name as a formula.
function csvCell(v) {
  if (v === null || v === undefined) return "";
  let s = String(v);
  if (/^[=+\-@\t\r]/.test(s) && !/^-?\d+(\.\d+)?$/.test(s)) s = `'${s}`;
  return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

/** rows from loadRows() -> the CSV text. */
function toCsv(rows) {
  const lines = rows.map((r) =>
    [
      r.player,
      r.level,
      r.outcome,
      r.score,
      r.hits,
      r.avgSpeed === null ? null : round(r.avgSpeed, 2),
      r.fade.status === "ok" ? r.fade.fadePct : null,
      r.durationSec ? round(r.durationSec, 1) : null,
      r.startedAt,
    ].map(csvCell).join(",")
  );
  return [CSV_COLS.join(","), ...lines].join("\n") + "\n";
}

function mount(app, { route }) {
  app.get("/api/lab", route(async (_req, res) => {
    res.set("Cache-Control", "private, max-age=15");
    res.json(await lab());
  }));
  app.get("/api/lab/sessions.csv", route(async (_req, res) => {
    const rows = await loadRows();
    res.set({
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": `attachment; filename="strikefit-lab-sessions-${new Date().toISOString().slice(0, 10)}.csv"`,
      "X-Row-Count": String(rows.length),
      "Access-Control-Expose-Headers": "X-Row-Count, Content-Disposition",
    });
    res.send(toCsv(rows));
  }));
}

function clearCache() {
  cache = null;
}

module.exports = { NOTE, CSV_COLS, buildLab, toCsv, csvCell, loadRows, mount, clearCache };
