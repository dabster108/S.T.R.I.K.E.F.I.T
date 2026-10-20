// Playtest study (the public /study page): consent, participant codes, the pre-play form, the post-play questionnaire, and the
// research CSV. Storage is in schema.sql (study_participants / study_consents / study_responses); validation, SUS scoring and
// the aggregates are in studyCore.js. Public and unauthenticated like the survey endpoint, so every write is rate limited per IP.
// The Borg RPE is not handled here: the page sends it to POST /api/sessions/:id/feedback, next to the fight it rates.

const { query, withTransaction } = require("./db");
const core = require("./studyCore");
const feedbackCore = require("./feedbackCore");

const limiter = feedbackCore.createRateLimiter({ max: 30, windowMs: 10 * 60_000 });

const iso = (v) => (v ? new Date(v).toISOString() : null);

/** The participant, their consent time and whether each form is done; null if the code is unknown. */
async function status(code) {
  const { rows } = await query(
    `SELECT p.code, p.age_band, p.pre_at, c.consented_at, c.version, r.created_at AS post_at
     FROM study_participants p
     LEFT JOIN study_consents c ON c.participant_code = p.code
     LEFT JOIN study_responses r ON r.participant_code = p.code
     WHERE p.code = $1`,
    [code]
  );
  if (!rows.length) return null;
  const p = rows[0];
  const fights = (await query(
    `SELECT id, status, outcome, ended_at FROM sessions WHERE upper(player_name) = $1 ORDER BY started_at DESC, seq DESC`,
    [code]
  )).rows;
  const finished = fights.filter((f) => f.status !== "live");
  return {
    code: p.code,
    consentedAt: iso(p.consented_at),
    preDone: Boolean(p.pre_at),
    postDone: Boolean(p.post_at),
    fights: { total: fights.length, finished: finished.length, live: fights.some((f) => f.status === "live"), latestFinishedId: finished[0]?.id || null },
  };
}

/** Everything the Lab and the CSV need: participants (with consent time) and their post-play answers. */
async function load() {
  const participants = (await query(
    `SELECT p.code, p.age_band, p.vr_experience, p.weekly_exercise, c.consented_at
     FROM study_participants p LEFT JOIN study_consents c ON c.participant_code = p.code ORDER BY p.code`
  )).rows.map((r) => ({ code: r.code, ageBand: r.age_band, vrExperience: r.vr_experience, weeklyExercise: r.weekly_exercise, consentedAt: iso(r.consented_at) }));
  const responses = (await query(`SELECT participant_code, enjoyment, effort_vs_usual, play_again, sus, sus_score FROM study_responses`)).rows.map((r) => ({
    code: r.participant_code,
    enjoyment: r.enjoyment.map(Number),
    effortVsUsual: Number(r.effort_vs_usual),
    playAgain: r.play_again,
    sus: r.sus.map(Number),
    susScore: Number(r.sus_score),
  }));
  return { participants, responses };
}

/** Issues the lowest free code P01..P20 and stores the consent in the same transaction. null when all twenty are taken. */
async function enroll() {
  for (let attempt = 0; attempt < 5; attempt++) {
    const out = await withTransaction(async (client) => {
      const taken = (await client.query(`SELECT code FROM study_participants WHERE code NOT LIKE 'ZZTEST-%'`)).rows.map((r) => r.code);
      const code = core.nextFreeCode(taken);
      if (!code) return { full: true };
      const ins = await client.query(`INSERT INTO study_participants (code) VALUES ($1) ON CONFLICT DO NOTHING RETURNING code`, [code]);
      if (!ins.rowCount) return { raced: true }; // someone took it between the select and the insert
      const c = await client.query(`INSERT INTO study_consents (participant_code, version) VALUES ($1, $2) RETURNING consented_at`, [code, core.CONSENT_VERSION]);
      return { code, consentedAt: iso(c.rows[0].consented_at) };
    });
    if (out.full) return null;
    if (!out.raced) return out;
  }
  return null;
}

function mount(app, { route }) {
  const guard = (req, res) => {
    if (limiter.allow(req.ip)) return true;
    res.status(429).json({ ok: false, error: "Too many requests from this connection. Try again in a few minutes." });
    return false;
  };
  const codeOf = (req, res) => {
    const code = String(req.params.code || "").toUpperCase();
    if (core.isCode(code)) return code;
    res.status(400).json({ ok: false, error: "That is not a participant code (it looks like P07)." });
    return null;
  };

  // Consent given -> a code. The body must say consent: true; nothing else is read.
  app.post("/api/study/participants", route(async (req, res) => {
    if (!guard(req, res)) return;
    if (!req.body || req.body.consent !== true) return res.status(400).json({ ok: false, error: "Consent is required before a participant code is issued." });
    const out = await enroll();
    if (!out) return res.status(409).json({ ok: false, error: `All ${core.MAX_PARTICIPANTS} participant codes are in use.` });
    res.status(201).json({ ok: true, code: out.code, consentedAt: out.consentedAt, consentVersion: core.CONSENT_VERSION });
  }));

  app.get("/api/study/participants/:code", route(async (req, res) => {
    const code = codeOf(req, res);
    if (!code) return;
    const s = await status(code);
    if (!s) return res.status(404).json({ ok: false, error: "Unknown participant code." });
    res.json({ ok: true, ...s });
  }));

  app.post("/api/study/participants/:code/pre", route(async (req, res) => {
    if (!guard(req, res)) return;
    const code = codeOf(req, res);
    if (!code) return;
    const checked = core.validatePre(req.body);
    if (!checked.ok) return res.status(400).json({ ok: false, error: checked.error });
    const v = checked.value;
    const r = await query(
      `UPDATE study_participants SET age_band = $2, vr_experience = $3, weekly_exercise = $4, pre_at = now()
       WHERE code = $1 AND pre_at IS NULL RETURNING code`,
      [code, v.ageBand, v.vrExperience, v.weeklyExercise]
    );
    if (!r.rowCount) {
      const s = await status(code);
      return s ? res.status(409).json({ ok: false, error: "The before-you-play form was already sent." }) : res.status(404).json({ ok: false, error: "Unknown participant code." });
    }
    res.status(201).json({ ok: true });
  }));

  app.post("/api/study/participants/:code/post", route(async (req, res) => {
    if (!guard(req, res)) return;
    const code = codeOf(req, res);
    if (!code) return;
    const checked = core.validatePost(req.body);
    if (!checked.ok) return res.status(400).json({ ok: false, error: checked.error });
    const s = await status(code);
    if (!s) return res.status(404).json({ ok: false, error: "Unknown participant code." });
    if (!s.preDone) return res.status(409).json({ ok: false, error: "Answer the before-you-play form first." });
    if (!s.fights.finished) return res.status(409).json({ ok: false, error: `Play a fight in the headset as ${code} first, then come back to this form.` });
    const v = checked.value;
    const r = await query(
      `INSERT INTO study_responses (participant_code, enjoyment, effort_vs_usual, play_again, sus, sus_score) VALUES ($1, $2, $3, $4, $5, $6)
       ON CONFLICT (participant_code) DO NOTHING RETURNING participant_code`,
      [code, v.enjoyment, v.effortVsUsual, v.playAgain, v.sus, v.susScore]
    );
    if (!r.rowCount) return res.status(409).json({ ok: false, error: "The after-you-play form was already sent." });
    res.status(201).json({ ok: true, susScore: v.susScore, band: core.susBand(v.susScore)?.label || null });
  }));

  // The research CSV: every fight by a participant joined to their answers. No IP address, no device information.
  app.get("/api/study/export.csv", route(async (_req, res) => {
    const lab = require("./lab");
    const [data, rows] = await Promise.all([load(), lab.loadRows()]);
    const body = core.studyCsvRows(data, rows, lab.csvCell);
    res.set({
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": `attachment; filename="strikefit-study-${new Date().toISOString().slice(0, 10)}.csv"`,
      "X-Row-Count": String(Math.max(0, body.split("\n").length - 2)),
      "Access-Control-Expose-Headers": "X-Row-Count, Content-Disposition",
    });
    res.send(body);
  }));
}

module.exports = { load, status, enroll, mount };
