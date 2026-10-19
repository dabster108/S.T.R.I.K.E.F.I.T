// Playtest study: validation, SUS scoring and the aggregates the Thesis Lab shows. Pure functions (test/study.test.js), no database.
//
// The study: a participant gives consent on /study, is issued a code (P01..P20) that they type into the headset as their player
// name, answers a short pre-play form, plays, then answers the post-play questionnaire. The Borg RPE is collected through the
// existing survey endpoint (session_feedback); the rest is stored by study.js.

const effort = require("./effort");

const CONSENT_VERSION = "2026-10-v1";
const MAX_PARTICIPANTS = 20;
const CODE_RE = /^(ZZTEST-)?P[0-9]{2}$/;
const TEST_PREFIX = "ZZTEST-";
const isCode = (c) => typeof c === "string" && CODE_RE.test(c);
const isTestCode = (c) => typeof c === "string" && c.startsWith(TEST_PREFIX);
/** The code for slot n (1..20): "P07". */
const codeFor = (n) => `P${String(n).padStart(2, "0")}`;
/** All real codes, P01..P20. */
const ALL_CODES = Array.from({ length: MAX_PARTICIPANTS }, (_, i) => codeFor(i + 1));
/** The lowest unused real code, or null when all twenty are taken. */
const nextFreeCode = (taken) => ALL_CODES.find((c) => !new Set(taken).has(c)) || null;

const AGE_BANDS = ["18-24", "25-34", "35-44", "45-54", "55-64", "65+", "prefer-not"];
const VR_EXPERIENCE = ["never", "once-or-twice", "occasionally", "regularly"];
const WEEKLY_EXERCISE = ["none", "under-1h", "1-3h", "3-5h", "over-5h"];
const PLAY_AGAIN = ["yes", "maybe", "no"];
const ENJOYMENT_ITEMS = 5;
const SUS_ITEMS = 10;

// The headset name picker may differ in case from the code (p07 / P07): match on upper case.
const owner = (s) => String(s?.player ?? "").toUpperCase();
const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round = effort.round;
const mean = (xs) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : null);

const likertArray = (raw, n, label) => {
  if (!Array.isArray(raw) || raw.length !== n) return { error: `${label}: answer all ${n} items` };
  const out = [];
  for (const v of raw) {
    const x = typeof v === "string" && v.trim() !== "" ? Number(v) : v;
    if (!Number.isInteger(x) || x < 1 || x > 5) return { error: `${label}: every answer must be a whole number from 1 to 5` };
    out.push(x);
  }
  return { value: out };
};
const oneOf = (raw, list, label) => (list.includes(raw) ? { value: raw } : { error: `${label}: pick one of the options` });

/** Pre-play form -> { ok, value: { ageBand, vrExperience, weeklyExercise } } | { ok: false, error } */
function validatePre(body) {
  const b = body && typeof body === "object" ? body : {};
  const a = oneOf(b.ageBand, AGE_BANDS, "Age band");
  if (a.error) return { ok: false, error: a.error };
  const v = oneOf(b.vrExperience, VR_EXPERIENCE, "VR experience");
  if (v.error) return { ok: false, error: v.error };
  const w = oneOf(b.weeklyExercise, WEEKLY_EXERCISE, "Weekly exercise");
  if (w.error) return { ok: false, error: w.error };
  return { ok: true, value: { ageBand: a.value, vrExperience: v.value, weeklyExercise: w.value } };
}

/** Post-play form (without the Borg RPE, which has its own endpoint) -> { ok, value } | { ok: false, error } */
function validatePost(body) {
  const b = body && typeof body === "object" ? body : {};
  const e = likertArray(b.enjoyment, ENJOYMENT_ITEMS, "Enjoyment");
  if (e.error) return { ok: false, error: e.error };
  const eff = likertArray([b.effortVsUsual], 1, "Effort compared with a usual workout");
  if (eff.error) return { ok: false, error: eff.error };
  const p = oneOf(b.playAgain, PLAY_AGAIN, "Play again");
  if (p.error) return { ok: false, error: p.error };
  const s = likertArray(b.sus, SUS_ITEMS, "Usability");
  if (s.error) return { ok: false, error: s.error };
  return { ok: true, value: { enjoyment: e.value, effortVsUsual: eff.value[0], playAgain: p.value, sus: s.value, susScore: susScore(s.value) } };
}

/**
 * System Usability Scale (Brooke, 1996). Ten items answered 1 to 5. Odd items are positively worded: contribution = answer - 1.
 * Even items are negatively worded: contribution = 5 - answer. The sum (0 to 40) times 2.5 gives 0 to 100.
 * (CITATION: verify against Brooke J. SUS: a "quick and dirty" usability scale. In: Usability Evaluation in Industry, 1996.)
 */
function susScore(answers) {
  if (!Array.isArray(answers) || answers.length !== SUS_ITEMS || answers.some((a) => !Number.isInteger(a) || a < 1 || a > 5)) return null;
  const sum = answers.reduce((s, a, i) => s + ((i + 1) % 2 === 1 ? a - 1 : 5 - a), 0);
  return round(sum * 2.5, 1);
}

// Interpretation bands. 68 is the commonly quoted average over many studies (Sauro & Lewis); the adjective labels follow Bangor,
// Kortum & Miller (2009). (CITATION: verify both before they go into the thesis; written from memory.)
const SUS_BANDS = [
  { from: 0, to: 51, label: "Poor", note: "Usability problems are likely to be serious." },
  { from: 51, to: 68, label: "Below average", note: "Usable, but under the usual benchmark of 68." },
  { from: 68, to: 80.3, label: "Good", note: "At or above the usual benchmark of 68." },
  { from: 80.3, to: 90, label: "Excellent", note: "Participants found it easy to use." },
  { from: 90, to: 100.01, label: "Best imaginable", note: "Almost no usability friction reported." },
];
const susBand = (score) => (isNum(score) ? SUS_BANDS.find((b) => score >= b.from && score < b.to) || null : null);

const SMALL = effort.SMALL_SAMPLE;

/**
 * Everything the Lab's study section shows.
 *   data:     { participants: [{ code, ageBand, vrExperience, weeklyExercise, consentedAt }],
 *               responses: [{ code, enjoyment[5], effortVsUsual, playAgain, sus[10], susScore }] }
 *   sessions: the Lab's session rows (player = the participant code in the headset): { id, player, level, outcome, avgSpeed, fade, rpe, swings }
 */
function buildStudy(data, sessions) {
  const participants = data?.participants || [];
  const responses = data?.responses || [];
  const byCode = new Map(participants.map((p) => [p.code, p]));
  const mine = (sessions || []).filter((s) => byCode.has(owner(s)) && s.outcome !== "live");
  const respBy = new Map(responses.map((r) => [r.code, r]));

  const susScores = responses.map((r) => r.susScore).filter(isNum);
  const susMean = mean(susScores);
  const enjoymentMeans = responses.map((r) => mean(r.enjoyment));
  const itemMeans = Array.from({ length: ENJOYMENT_ITEMS }, (_, i) => round(mean(responses.map((r) => r.enjoyment[i])), 2));
  const again = { yes: 0, maybe: 0, no: 0 };
  for (const r of responses) if (again[r.playAgain] !== undefined) again[r.playAgain] += 1;

  const rpePoints = mine
    .filter((s) => isNum(s.rpe) && isNum(s.avgSpeed))
    .map((s) => ({ sessionId: s.id, code: owner(s), level: s.level, rpe: s.rpe, avgSpeed: round(s.avgSpeed, 2) }));

  const perParticipant = participants
    .map((p) => {
      const list = mine.filter((s) => owner(s) === p.code);
      const fades = list.filter((s) => s.fade && s.fade.status === "ok").map((s) => s.fade.fadePct);
      const speeds = list.filter((s) => isNum(s.avgSpeed)).map((s) => s.avgSpeed);
      const rpes = list.filter((s) => isNum(s.rpe)).map((s) => s.rpe);
      const r = respBy.get(p.code);
      return {
        code: p.code,
        sessions: list.length,
        avgSpeed: speeds.length ? round(mean(speeds), 2) : null,
        fadePct: fades.length ? round(mean(fades), 1) : null,
        rpe: rpes.length ? round(mean(rpes), 1) : null,
        sus: r ? r.susScore : null,
        enjoyment: r ? round(mean(r.enjoyment), 2) : null,
        playAgain: r ? r.playAgain : null,
      };
    })
    .sort((a, b) => a.code.localeCompare(b.code));

  return {
    sample: {
      participants: participants.length,
      withFights: new Set(mine.map(owner)).size,
      completed: responses.length,
    },
    sus: {
      n: susScores.length,
      mean: susMean === null ? null : round(susMean, 1),
      band: susBand(susMean),
      bands: SUS_BANDS,
      scores: responses.map((r) => ({ code: r.code, score: r.susScore, band: susBand(r.susScore)?.label || null })).sort((a, b) => a.code.localeCompare(b.code)),
      smallSample: susScores.length < SMALL,
    },
    enjoyment: {
      n: responses.length,
      mean: responses.length ? round(mean(enjoymentMeans), 2) : null,
      items: itemMeans,
      smallSample: responses.length < SMALL,
    },
    effortVsUsual: {
      n: responses.length,
      mean: responses.length ? round(mean(responses.map((r) => r.effortVsUsual)), 2) : null,
      smallSample: responses.length < SMALL,
    },
    playAgain: {
      n: responses.length,
      ...again,
      yesPct: responses.length ? round((again.yes / responses.length) * 100, 1) : null,
      smallSample: responses.length < SMALL,
    },
    rpe: { points: rpePoints, ...effort.rpeCorrelation(rpePoints) },
    perParticipant,
  };
}

// ---------------------------------------------------------------------------
// CSV: sessions joined to the study answers by participant code. No IP address, no device information (the database holds none).
// ---------------------------------------------------------------------------

const STUDY_CSV_COLS = [
  "participant", "consent_at", "age_band", "vr_experience", "weekly_exercise",
  "level", "outcome", "score", "hits", "avg_swing", "fade_pct", "duration_sec", "started_at", "rpe",
  ...Array.from({ length: ENJOYMENT_ITEMS }, (_, i) => `enjoyment_${i + 1}`), "enjoyment_mean", "effort_vs_usual", "play_again",
  ...Array.from({ length: SUS_ITEMS }, (_, i) => `sus_${i + 1}`), "sus_score",
];

/** One row per fight (a participant with no fight yet still gets one row), the participant's answers repeated on each. */
function studyCsvRows(data, sessions, csvCell) {
  const respBy = new Map((data.responses || []).map((r) => [r.code, r]));
  const lines = [];
  for (const p of [...(data.participants || [])].sort((a, b) => a.code.localeCompare(b.code))) {
    const r = respBy.get(p.code);
    const list = (sessions || []).filter((s) => owner(s) === p.code).sort((a, b) => String(a.startedAt).localeCompare(String(b.startedAt)));
    const answers = [
      ...(r ? r.enjoyment : Array(ENJOYMENT_ITEMS).fill(null)),
      r ? round(mean(r.enjoyment), 2) : null,
      r ? r.effortVsUsual : null,
      r ? r.playAgain : null,
      ...(r ? r.sus : Array(SUS_ITEMS).fill(null)),
      r ? r.susScore : null,
    ];
    const head = [p.code, p.consentedAt || null, p.ageBand, p.vrExperience, p.weeklyExercise];
    const fight = (s) =>
      s
        ? [s.level, s.outcome, s.score, s.hits, isNum(s.avgSpeed) ? round(s.avgSpeed, 2) : null, s.fade && s.fade.status === "ok" ? s.fade.fadePct : null, s.durationSec ? round(s.durationSec, 1) : null, s.startedAt, isNum(s.rpe) ? s.rpe : null]
        : Array(9).fill(null);
    for (const s of list.length ? list : [null]) lines.push([...head, ...fight(s), ...answers].map(csvCell).join(","));
  }
  return [STUDY_CSV_COLS.join(","), ...lines].join("\n") + "\n";
}

module.exports = {
  CONSENT_VERSION, MAX_PARTICIPANTS, CODE_RE, TEST_PREFIX, ALL_CODES, AGE_BANDS, VR_EXPERIENCE, WEEKLY_EXERCISE, PLAY_AGAIN,
  ENJOYMENT_ITEMS, SUS_ITEMS, SUS_BANDS, STUDY_CSV_COLS,
  isCode, isTestCode, codeFor, nextFreeCode, validatePre, validatePost, susScore, susBand, buildStudy, studyCsvRows,
};
