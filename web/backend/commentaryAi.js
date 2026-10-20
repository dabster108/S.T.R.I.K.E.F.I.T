// The AI match commentator and its voice (server side, so the API keys never reach the browser).
//
//   POST /api/commentary/line     one game event in, one football-commentator sentence out (Groq chat model)
//   POST /api/commentary/summary  the totals of a finished fight in, a 2 to 3 sentence full-time report out (same checks)
//   POST /api/commentary/speech   a sentence in, an MP3 of it spoken out (Google Cloud Text-to-Speech)
//   GET  /api/commentary/status   { ai, voice }: which of the two are switched on
//
// The browser already has the plain line for every event ("Combo x6 on The Crimson Warden"); this module only dresses it up. So the model
// is given that line as the facts and its answer is checked before it is returned: one short sentence, no link, and no number that is
// not in the facts. Anything else, and any failure or timeout, returns an error and the browser keeps the plain line. Requests are
// rate limited (per IP and in total) and a run of failures switches the service off for a short while.

const crypto = require("crypto");
const config = require("./config/commentaryAi");
const { createBucket } = require("./engage");

const GROQ_KEY_NAMES = ["GROQ_API_KEY", "GORQ_API_KEY"]; // web/backend/.env spells it GORQ_API_KEY
const LEVELS = new Set(["Easy", "Medium", "Hard"]);
const SITUATIONS = new Set(config.situations);

const intIn = (v, lo, hi) => (v !== null && v !== "" && v !== undefined && Number.isFinite(Number(v)) && Number(v) >= lo && Number(v) <= hi ? Math.round(Number(v)) : null);
// Letters, digits, spaces and a few name characters only: a player name is typed by a stranger and goes into a prompt.
const cleanName = (v, max) => String(v ?? "").normalize("NFKC").replace(/[^\p{L}\p{N} .'_-]/gu, "").replace(/\s+/g, " ").trim().slice(0, max);
const cleanSentence = (v, max) => String(v ?? "").replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim().slice(0, max);
const isObj = (v) => Boolean(v) && typeof v === "object" && !Array.isArray(v);

/** The Groq key, whichever way it is spelled in the environment ("" when there is none). */
function groqKey(env = process.env) {
  for (const n of GROQ_KEY_NAMES) if (env[n] && String(env[n]).trim()) return String(env[n]).trim();
  return "";
}
const googleKey = (env = process.env) => String(env.GOOGLE_TTS_API_KEY || "").trim();

// ---------------------------------------------------------------------------------------------------------------- requests

/** Checks and cleans the body of POST /api/commentary/line. -> { ok, value } | { ok: false, error } */
function validateLine(body) {
  if (!isObj(body)) return { ok: false, error: "Send a JSON object." };
  if (!SITUATIONS.has(body.situation) && !/^combo\d{1,3}$/.test(String(body.situation))) return { ok: false, error: "Unknown situation." };
  const base = cleanSentence(body.base, config.input.baseMax);
  if (base.length < 3) return { ok: false, error: "base is missing." };
  const situation = /^combo\d/.test(body.situation) ? "combo" : body.situation;
  const recent = (Array.isArray(body.recent) ? body.recent : []).slice(0, config.input.recentMax).map((t) => cleanSentence(t, config.output.maxChars)).filter(Boolean);
  return {
    ok: true,
    value: {
      situation,
      base,
      monster: cleanName(body.monster, config.input.nameMax),
      player: cleanName(body.player, config.input.nameMax),
      level: LEVELS.has(body.level) ? body.level : null,
      monsterPct: intIn(body.monsterPct, 0, 100),
      playerPct: intIn(body.playerPct, 0, 100),
      combo: intIn(body.combo, 0, config.input.comboMax),
      phase: intIn(body.phase, 1, 3),
      recent,
    },
  };
}

/** The user message for the model: the facts, the mood and what was just said. */
function buildPrompt(v) {
  const facts = [
    `Event: ${v.base}`,
    `Mood: ${config.moods[v.situation] || "an ordinary passage of play"}`,
    v.player && `Player: ${v.player}`,
    v.monster && `Opponent: ${v.monster}`,
    v.level && `Difficulty: ${v.level}`,
    v.monsterPct !== null && `Opponent health: ${v.monsterPct}%`,
    v.playerPct !== null && `Player health: ${v.playerPct}%`,
    v.combo && `Current combo: ${v.combo}`,
    v.phase && `Boss phase: ${v.phase} of 3`,
  ].filter(Boolean);
  if (v.recent.length) facts.push(`Recent lines (do not repeat their wording): ${v.recent.map((t) => `"${t}"`).join(" | ")}`);
  return facts.join("\n");
}

const numbersIn = (text) => String(text).match(/\d+(?:[.,]\d+)?/g) || [];

// Spelled-out numbers ("forty-one") are turned back into digits so they are checked like any other number. "one" is left alone: it is
// everywhere in football talk ("no one", "one-touch").
const UNITS = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
const TENS = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
function spelledNumbers(text) {
  const words = String(text).toLowerCase().split(/[^a-z]+/).filter(Boolean);
  const found = [];
  for (let i = 0; i < words.length; i++) {
    const tens = TENS.indexOf(words[i]);
    const unit = UNITS.indexOf(words[i + 1]);
    if (tens > 1) {
      if (unit > 0 && unit < 10) {
        found.push(String(tens * 10 + unit));
        i++;
      } else found.push(String(tens * 10));
    } else if (words[i] === "hundred") found.push("100");
    else if (words[i] !== "one" && UNITS.includes(words[i])) found.push(String(UNITS.indexOf(words[i])));
  }
  return found;
}

/**
 * Cleans what the model said and decides whether it may be used. -> the sentence, or null.
 * Rejected: empty, too long, a link, a number that is not in the facts, or none of the opponent's name when the event names it.
 */
function acceptLine(raw, v, limits = config.output) {
  let t = String(raw ?? "").replace(/<think>[\s\S]*?<\/think>/gi, "").split(/\n/).map((s) => s.trim()).find(Boolean) || "";
  t = t.replace(/^["'“”‘’`*_\s]+|["'“”‘’`*_\s]+$/g, "").replace(/\s+/g, " ");
  if (!t || t.length > limits.maxChars || t.split(" ").length > limits.maxWords) return null;
  if (/https?:|www\.|@|#|\[|\]|\{|\}|<|>/.test(t) || /\p{Extended_Pictographic}/u.test(t)) return null;
  const allowed = new Set(numbersIn([v.base, v.monsterPct, v.playerPct, v.combo, v.phase, v.level, ...(v.numbers || []), ...v.recent].filter((x) => x !== null && x !== undefined).join(" ")));
  if ([...numbersIn(t), ...spelledNumbers(t)].some((n) => !allowed.has(n))) return null;
  const last = v.monster.split(" ").pop().toLowerCase(); // "Warden" from "The Crimson Warden": the event names the opponent, so the line must too
  if (last.length > 2 && v.base.toLowerCase().includes(last) && !t.toLowerCase().includes(last)) return null;
  return t;
}

const OUTCOMES = new Set(["victory", "defeat", "abandoned"]);
const SUMMARY_NUMBERS = ["durationSec", "score", "damageDealt", "hitCount", "fullSwings", "peakCombo"];

/** Checks and cleans the body of POST /api/commentary/summary (the totals of a finished fight). */
function validateSummary(body) {
  if (!isObj(body)) return { ok: false, error: "Send a JSON object." };
  if (!OUTCOMES.has(body.outcome)) return { ok: false, error: "Unknown outcome." };
  const base = cleanSentence(body.base, config.summary.baseMax);
  if (base.length < 10) return { ok: false, error: "base is missing." };
  const totals = {};
  for (const k of SUMMARY_NUMBERS) totals[k] = intIn(body[k], 0, 10_000_000);
  return {
    ok: true,
    value: {
      kind: "summary",
      situation: body.outcome,
      outcome: body.outcome,
      base,
      monster: cleanName(body.monster, config.input.nameMax),
      player: cleanName(body.player, config.input.nameMax),
      level: LEVELS.has(body.level) ? body.level : null,
      totals,
      numbers: Object.values(totals).filter((n) => n !== null),
      monsterPct: null,
      playerPct: null,
      combo: null,
      phase: null,
      recent: [],
    },
  };
}

function buildSummaryPrompt(v) {
  const t = v.totals;
  return [
    `Result: ${v.outcome}`,
    `Plain report: ${v.base}`,
    v.player && `Player: ${v.player}`,
    v.monster && `Opponent: ${v.monster}`,
    v.level && `Difficulty: ${v.level}`,
    t.durationSec !== null && `Fight time: ${t.durationSec} seconds`,
    t.score !== null && `Score: ${t.score}`,
    t.damageDealt !== null && `Damage dealt: ${t.damageDealt}`,
    t.hitCount !== null && `Hits landed: ${t.hitCount}`,
    t.fullSwings !== null && `Full-power hits: ${t.fullSwings}`,
    t.peakCombo !== null && `Best combo: ${t.peakCombo}`,
  ]
    .filter(Boolean)
    .join("\n");
}

/** Checks the body of POST /api/commentary/speech. */
function validateSpeech(body) {
  if (!isObj(body)) return { ok: false, error: "Send a JSON object." };
  const text = cleanSentence(body.text, config.speechMaxChars + 1);
  if (text.length < 3 || text.length > config.speechMaxChars) return { ok: false, error: `text must be 3 to ${config.speechMaxChars} characters.` };
  return { ok: true, value: { text } };
}

// ---------------------------------------------------------------------------------------------------------------- the service

/**
 * The commentator. `fetchImpl`, `now` and `env` can be replaced, which is how the tests run it without the internet.
 * Every method returns { status, body } (speech returns { status, audio, body }), never throws.
 */
function createCommentator({ env = process.env, fetchImpl = globalThis.fetch, now = Date.now, log = () => {} } = {}) {
  const buckets = {
    line: { ip: createBucket({ ...config.limits.line.perIp, now }), all: createBucket({ ...config.limits.line.global, now }) },
    speech: { ip: createBucket({ ...config.limits.speech.perIp, now }), all: createBucket({ ...config.limits.speech.global, now }) },
  };
  const breaker = { fails: 0, until: 0 };
  const speechCache = new Map(); // text hash -> Buffer, oldest first
  const lineCache = new Map(); // moment -> { at, promise } (the answer, or the answer on its way)
  const model = () => String(env.GROQ_COMMENTARY_MODEL || config.groq.model).trim();
  const voiceName = () => String(env.GOOGLE_TTS_VOICE || config.google.voice).trim();
  const aiOn = () => Boolean(groqKey(env)) && env.COMMENTARY_AI !== "off";
  const voiceOn = () => Boolean(googleKey(env)) && env.COMMENTARY_AI !== "off";
  const down = () => now() < breaker.until;
  const rateLimited = (retryAfter) => {
    const secs = Number(retryAfter);
    breaker.until = now() + Math.min(config.breaker.rateLimitedMaxMs, Number.isFinite(secs) && secs > 0 ? secs * 1000 : config.breaker.coolOffMs);
    breaker.fails = 0;
    log("[commentary] line: Groq rate limit reached, resting");
  };
  const failed = (what, err) => {
    breaker.fails++;
    if (breaker.fails >= config.breaker.failures) {
      breaker.until = now() + config.breaker.coolOffMs;
      breaker.fails = 0;
    }
    log(`[commentary] ${what} failed: ${err}`); // the message only: never a key, never the request
  };
  const ok = () => {
    breaker.fails = 0;
  };

  async function timed(url, init, ms) {
    const ctl = new AbortController();
    const t = setTimeout(() => ctl.abort(), ms);
    try {
      return await fetchImpl(url, { ...init, signal: ctl.signal });
    } finally {
      clearTimeout(t);
    }
  }

  async function askGroq(value) {
    const isSummary = value.kind === "summary";
    try {
      const res = await timed(
        config.groq.url,
        {
          method: "POST",
          headers: { Authorization: `Bearer ${groqKey(env)}`, "Content-Type": "application/json" },
          body: JSON.stringify({
            model: model(),
            temperature: config.groq.temperature,
            max_tokens: isSummary ? config.summary.maxTokens : config.groq.maxTokens,
            messages: [
              { role: "system", content: isSummary ? config.summary.system : config.system },
              { role: "user", content: isSummary ? buildSummaryPrompt(value) : buildPrompt(value) },
            ],
          }),
        },
        config.groq.timeoutMs
      );
      if (res.status === 429) {
        rateLimited(res.headers?.get?.("retry-after"));
        return { status: 503, body: { ok: false, error: "AI commentary is resting." } };
      }
      if (!res.ok) throw new Error(`Groq answered ${res.status}`);
      const data = await res.json();
      const text = acceptLine(data?.choices?.[0]?.message?.content, value, isSummary ? config.summary.output : config.output);
      ok();
      if (!text) return { status: 422, body: { ok: false, error: "The model's line was not usable." } };
      return { status: 200, body: { ok: true, text, source: "ai" } };
    } catch (err) {
      failed("line", err.name === "AbortError" ? "timed out" : err.message);
      return { status: 502, body: { ok: false, error: "The commentator is not available." } };
    }
  }

  // One checked request (line or summary) -> a sentence. Viewers of the same moment share one Groq request (the lines are chosen from the
  // fight's id, so they match); a failed answer is never shared.
  async function shared(v, ip) {
    if (!aiOn()) return { status: 503, body: { ok: false, error: "AI commentary is not set up." } };
    if (!v.ok) return { status: 400, body: { ok: false, error: v.error } };
    if (down()) return { status: 503, body: { ok: false, error: "AI commentary is resting." } };
    const key = crypto.createHash("sha1").update(JSON.stringify({ ...v.value, recent: 0 })).digest("hex");
    const hit = lineCache.get(key);
    if (hit && now() - hit.at < config.lineCacheMs) return hit.promise;
    if (!buckets.line.ip.take(ip) || !buckets.line.all.take()) return { status: 429, body: { ok: false, error: "Too many commentary requests." } };
    const promise = askGroq(v.value);
    lineCache.set(key, { at: now(), promise });
    while (lineCache.size > config.lineCacheMax) lineCache.delete(lineCache.keys().next().value);
    const out = await promise;
    if (out.status !== 200) lineCache.delete(key);
    return out;
  }

  return {
    status() {
      return { status: 200, body: { ok: true, ai: aiOn() && !down(), voice: voiceOn() && !down(), model: aiOn() ? model() : null, voiceName: voiceOn() ? voiceName() : null } };
    },

    line: (body, ip) => shared(validateLine(body), ip),
    summary: (body, ip) => shared(validateSummary(body), ip),

    async speech(body, ip) {
      if (!voiceOn()) return { status: 503, body: { ok: false, error: "Spoken commentary is not set up." } };
      const v = validateSpeech(body);
      if (!v.ok) return { status: 400, body: { ok: false, error: v.error } };
      const key = crypto.createHash("sha1").update(`${voiceName()}|${v.value.text}`).digest("hex");
      if (speechCache.has(key)) {
        const audio = speechCache.get(key);
        speechCache.delete(key);
        speechCache.set(key, audio); // most recently used goes last
        return { status: 200, audio };
      }
      if (down()) return { status: 503, body: { ok: false, error: "Spoken commentary is resting." } };
      if (!buckets.speech.ip.take(ip) || !buckets.speech.all.take()) return { status: 429, body: { ok: false, error: "Too many speech requests." } };
      try {
        const res = await timed(
          config.google.url,
          {
            method: "POST",
            headers: { "X-Goog-Api-Key": googleKey(env), "Content-Type": "application/json" },
            body: JSON.stringify({
              input: { text: v.value.text },
              voice: { languageCode: config.google.languageCode, name: voiceName() },
              audioConfig: { audioEncoding: "MP3", speakingRate: config.google.speakingRate, pitch: config.google.pitch },
            }),
          },
          config.google.timeoutMs
        );
        if (!res.ok) throw new Error(`Google answered ${res.status}`);
        const data = await res.json();
        if (!data?.audioContent) throw new Error("no audio in the answer");
        const audio = Buffer.from(data.audioContent, "base64");
        ok();
        speechCache.set(key, audio);
        while (speechCache.size > config.speechCacheMax) speechCache.delete(speechCache.keys().next().value);
        return { status: 200, audio };
      } catch (err) {
        failed("speech", err.name === "AbortError" ? "timed out" : err.message);
        return { status: 502, body: { ok: false, error: "The voice is not available." } };
      }
    },
  };
}

/** Mounts the three endpoints. `route` is server.js's async wrapper. */
function mount(app, { route, commentator = createCommentator({ log: (m) => console.warn(m) }) }) {
  app.get("/api/commentary/status", (_req, res) => {
    const out = commentator.status();
    res.set("Cache-Control", "no-cache").status(out.status).json(out.body);
  });
  app.post("/api/commentary/line", route(async (req, res) => {
    const out = await commentator.line(req.body, req.ip);
    res.set("Cache-Control", "no-store").status(out.status).json(out.body);
  }));
  app.post("/api/commentary/summary", route(async (req, res) => {
    const out = await commentator.summary(req.body, req.ip);
    res.set("Cache-Control", "no-store").status(out.status).json(out.body);
  }));
  app.post("/api/commentary/speech", route(async (req, res) => {
    const out = await commentator.speech(req.body, req.ip);
    if (out.audio) return res.set({ "Content-Type": "audio/mpeg", "Cache-Control": `private, max-age=${config.speechCacheSeconds}` }).status(200).send(out.audio);
    res.set("Cache-Control", "no-store").status(out.status).json(out.body);
  }));
  return commentator;
}

module.exports = { createCommentator, mount, validateLine, validateSummary, buildSummaryPrompt, validateSpeech, buildPrompt, acceptLine, groqKey, googleKey };
