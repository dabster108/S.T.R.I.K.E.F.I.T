// Post-session survey storage (table session_feedback, see schema.sql). Validation and the
// statistics live in feedbackCore.js so they can be tested without a database.

const { query } = require("./db");
const fitness = require("./fitness");
const core = require("./feedbackCore");

const num = (v) => (v === null || v === undefined || !Number.isFinite(Number(v)) ? null : Number(v));

const toApi = (r) => ({
  sessionId: r.session_id,
  enjoyment: r.enjoyment,
  rpe: r.rpe,
  comment: r.comment,
  createdAt: new Date(r.created_at).toISOString(),
});

// One rating per session. Returns the saved rating, or null when that session already has one
// (first write wins: with no authentication, a later request must not overwrite someone's answer).
async function saveFeedback(sessionId, { enjoyment, rpe, comment }) {
  const { rows } = await query(
    `INSERT INTO session_feedback (session_id, enjoyment, rpe, comment) VALUES ($1, $2, $3, $4)
     ON CONFLICT (session_id) DO NOTHING RETURNING *`,
    [sessionId, enjoyment, rpe, comment]
  );
  return rows.length ? toApi(rows[0]) : null;
}

async function getFeedback(sessionId) {
  const { rows } = await query("SELECT * FROM session_feedback WHERE session_id = $1", [sessionId]);
  return rows.length ? toApi(rows[0]) : null;
}

// sessionId -> { enjoyment, rpe, comment }, for the CSV export.
async function feedbackBySession() {
  const { rows } = await query("SELECT session_id, enjoyment, rpe, comment FROM session_feedback");
  return new Map(rows.map((r) => [r.session_id, r]));
}

// Aggregate ratings plus, for each rated session, the measured effort next to the perceived one.
// No player names leave this function.
async function feedbackAnalytics() {
  const sessions = (await query(
    `SELECT s.id, s.level, s.status, s.started_at AS "startedAt", s.score, s.duration_sec AS "durationSec",
            s.hit_count AS "hitCount", f.enjoyment, f.rpe
     FROM session_feedback f JOIN sessions s ON s.id = f.session_id`
  )).rows;
  const hits = (await query(
    `SELECT e.session_ref AS session_id, lower(e.payload->>'tier') AS tier,
            (e.payload->>'swingSpeed')::float8 AS speed,
            CASE WHEN json_typeof(e.payload->'t') = 'number' THEN (e.payload->>'t')::float8 END AS t
     FROM events e JOIN session_feedback f ON f.session_id = e.session_ref
     WHERE e.type = 'hit' AND json_typeof(e.payload->'swingSpeed') = 'number'`
  )).rows;
  const bySession = new Map();
  for (const h of hits) {
    if (!bySession.has(h.session_id)) bySession.set(h.session_id, []);
    bySession.get(h.session_id).push({ tier: h.tier, speed: num(h.speed), t: num(h.t) });
  }
  const rows = sessions.map((s) => {
    const summary = fitness.sessionSummary(s, bySession.get(s.id) || []);
    const minutes = (Number(s.durationSec) || 0) / 60;
    return {
      sessionId: s.id,
      level: s.level,
      enjoyment: s.enjoyment,
      rpe: s.rpe,
      effort: summary.tiers.avgTierWeight,
      kcalPerMin: summary.estimatedCalories !== null && minutes > 0 ? summary.estimatedCalories / minutes : null,
    };
  });
  return core.summarizeFeedback(rows);
}

module.exports = { saveFeedback, getFeedback, feedbackBySession, feedbackAnalytics };
