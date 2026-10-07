// Post-session survey: validation and statistics. Pure functions, no database access.
//
// The two questions are standard measures:
//   enjoyment  1 to 5 (1 = not at all enjoyable, 5 = very enjoyable)
//   rpe        Borg Rating of Perceived Exertion, 6 to 20 (6 = no exertion, 20 = maximal)
// Borg GA. Psychophysical bases of perceived exertion. Medicine & Science in Sports & Exercise.
// 1982;14(5):377-381.  (CITATION: verify volume, issue and pages against the paper before it goes
// into the thesis; it is written here from memory.)
//
// The survey has no authentication: anyone who knows a player's name and session can rate it.
// That is a known limitation (see Assets/Documentation/project_progress.md).

const RPE_MIN = 6;
const RPE_MAX = 20;
const ENJOYMENT_MIN = 1;
const ENJOYMENT_MAX = 5;
const MAX_COMMENT = 500;
const SPEARMAN_MIN_N = 8;

const isNum = (v) => typeof v === "number" && Number.isFinite(v);
const round = (v, d = 2) => (isNum(v) ? Math.round(v * 10 ** d) / 10 ** d : null);

// -> { ok: true, value: { enjoyment, rpe, comment } } | { ok: false, error }
function validateFeedback(body) {
  const b = body && typeof body === "object" && !Array.isArray(body) ? body : {};
  const pick = (raw, min, max, label) => {
    if (raw === undefined || raw === null || raw === "") return { value: null };
    const n = typeof raw === "number" ? raw : typeof raw === "string" && raw.trim() !== "" ? Number(raw) : NaN;
    if (!Number.isInteger(n) || n < min || n > max) return { error: `${label} must be a whole number from ${min} to ${max}` };
    return { value: n };
  };
  const enjoyment = pick(b.enjoyment, ENJOYMENT_MIN, ENJOYMENT_MAX, "Enjoyment");
  if (enjoyment.error) return { ok: false, error: enjoyment.error };
  const rpe = pick(b.rpe, RPE_MIN, RPE_MAX, "Perceived exertion");
  if (rpe.error) return { ok: false, error: rpe.error };
  if (enjoyment.value === null && rpe.value === null) return { ok: false, error: "Give an enjoyment rating, a perceived exertion rating, or both" };

  let comment = null;
  if (b.comment !== undefined && b.comment !== null) {
    if (typeof b.comment !== "string") return { ok: false, error: "Comment must be text" };
    const text = b.comment.replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, "").trim();
    if (text.length > MAX_COMMENT) return { ok: false, error: `Comment must be ${MAX_COMMENT} characters or fewer` };
    comment = text || null;
  }
  return { ok: true, value: { enjoyment: enjoyment.value, rpe: rpe.value, comment } };
}

// Per-IP limiter held in memory (one Render instance). A deliberate small guard against
// spam, not a security boundary. Returns true when the request may go ahead.
function createRateLimiter({ max = 12, windowMs = 10 * 60_000, now = Date.now } = {}) {
  const hits = new Map(); // ip -> [timestamps]
  const sweep = setInterval(() => {
    const cutoff = now() - windowMs;
    for (const [ip, list] of hits) {
      const fresh = list.filter((t) => t > cutoff);
      if (fresh.length) hits.set(ip, fresh);
      else hits.delete(ip);
    }
  }, windowMs);
  sweep.unref();
  return {
    allow(ip) {
      const t = now();
      const list = (hits.get(ip) || []).filter((x) => x > t - windowMs);
      if (list.length >= max) {
        hits.set(ip, list);
        return false;
      }
      list.push(t);
      hits.set(ip, list);
      return true;
    },
  };
}

// Average ranks (ties share the mean of the ranks they span).
function ranks(values) {
  const order = values.map((v, i) => [v, i]).sort((a, b) => a[0] - b[0]);
  const out = new Array(values.length);
  for (let i = 0; i < order.length; ) {
    let j = i;
    while (j + 1 < order.length && order[j + 1][0] === order[i][0]) j++;
    const rank = (i + j) / 2 + 1;
    for (let k = i; k <= j; k++) out[order[k][1]] = rank;
    i = j + 1;
  }
  return out;
}

// Spearman rank correlation = Pearson correlation of the ranks. null when it can't be defined
// (fewer than 2 pairs, or one variable is constant).
function spearman(xs, ys) {
  if (xs.length !== ys.length || xs.length < 2) return null;
  const rx = ranks(xs);
  const ry = ranks(ys);
  const n = xs.length;
  const mx = rx.reduce((a, v) => a + v, 0) / n;
  const my = ry.reduce((a, v) => a + v, 0) / n;
  let sxy = 0, sxx = 0, syy = 0;
  for (let i = 0; i < n; i++) {
    sxy += (rx[i] - mx) * (ry[i] - my);
    sxx += (rx[i] - mx) ** 2;
    syy += (ry[i] - my) ** 2;
  }
  return sxx && syy ? sxy / Math.sqrt(sxx * syy) : null;
}

const mean = (list) => (list.length ? list.reduce((a, v) => a + v, 0) / list.length : null);

// rows: [{ level, enjoyment, rpe, effort, kcalPerMin, sessionId }] one per rated session.
// effort = mean swing intensity (1 tap .. 4 full power), kcalPerMin = estimated kcal / minutes;
// both null when the session has no recorded swings.
function summarizeFeedback(rows) {
  const enj = rows.filter((r) => isNum(r.enjoyment));
  const rpe = rows.filter((r) => isNum(r.rpe));
  const levels = [...new Set(rows.map((r) => r.level))];
  const paired = rpe.filter((r) => isNum(r.effort));
  const enough = paired.length >= SPEARMAN_MIN_N;
  const rho = enough ? spearman(paired.map((r) => r.rpe), paired.map((r) => r.effort)) : null;
  return {
    n: rows.length,
    enjoyment: {
      n: enj.length,
      mean: round(mean(enj.map((r) => r.enjoyment))),
      distribution: Object.fromEntries([1, 2, 3, 4, 5].map((v) => [v, enj.filter((r) => r.enjoyment === v).length])),
    },
    rpe: {
      n: rpe.length,
      mean: round(mean(rpe.map((r) => r.rpe))),
      distribution: Object.fromEntries(Array.from({ length: RPE_MAX - RPE_MIN + 1 }, (_, i) => [RPE_MIN + i, rpe.filter((r) => r.rpe === RPE_MIN + i).length])),
    },
    byLevel: levels.map((level) => {
      const list = rows.filter((r) => r.level === level);
      return {
        level,
        n: list.length,
        meanEnjoyment: round(mean(list.filter((r) => isNum(r.enjoyment)).map((r) => r.enjoyment))),
        meanRpe: round(mean(list.filter((r) => isNum(r.rpe)).map((r) => r.rpe))),
      };
    }),
    points: rpe.map((r) => ({ sessionId: r.sessionId, level: r.level, rpe: r.rpe, enjoyment: r.enjoyment, effort: round(r.effort, 3), kcalPerMin: round(r.kcalPerMin, 2) })),
    correlation: {
      status: enough ? "ok" : "insufficient_data",
      method: "Spearman rank correlation, perceived exertion (RPE) against measured swing intensity",
      n: paired.length,
      minN: SPEARMAN_MIN_N,
      rho: round(rho, 3),
      note: enough
        ? "Descriptive only: a small, self-selected sample with no authentication, so it describes these ratings, not players in general. Estimated kcal/min is a fixed function of swing intensity, so it is not independent evidence."
        : `Needs at least ${SPEARMAN_MIN_N} rated sessions with recorded swings; ${paired.length} so far. No coefficient is shown below that.`,
    },
  };
}

module.exports = { RPE_MIN, RPE_MAX, ENJOYMENT_MIN, ENJOYMENT_MAX, MAX_COMMENT, SPEARMAN_MIN_N, validateFeedback, createRateLimiter, ranks, spearman, summarizeFeedback };
