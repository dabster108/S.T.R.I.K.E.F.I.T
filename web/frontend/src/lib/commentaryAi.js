// The browser side of the AI commentator. The keys live on the server (backend/commentaryAi.js); this file only asks it three things:
// is it switched on, what would you say for this event, and what does that sound like. Every function here is safe to call at any time:
// none of them throws, and an answer of null always means "use the plain line" (or the browser's own voice).

import { COMMENTARY } from "../config/commentary.js";

// api.js is loaded when first needed (it is in the app's bundle anyway), so this file can be tested without the browser's build-time settings.
const client = () => import("./api.js");

const OFF = Object.freeze({ ai: false, voice: false });
let status = null; // { value, at }

/** { ai, voice }: what the server has switched on. Asked once, and again after statusRetryMs if the server could not be reached. */
export async function aiStatus({ now = Date.now, ask = async () => (await client()).api.commentaryStatus() } = {}) {
  if (status && (status.ok || now() - status.at < COMMENTARY.ai.statusRetryMs)) return status.value;
  try {
    const r = await ask();
    status = { ok: true, at: now(), value: { ai: Boolean(r?.ai), voice: Boolean(r?.voice) } };
  } catch {
    status = { ok: false, at: now(), value: OFF };
  }
  return status.value;
}
export const resetAiStatus = () => {
  status = null;
};

/** The request body for one line: the event, the fight's picture and the lines just shown. */
export function lineRequest(line, recent) {
  const f = line.facts || {};
  return { situation: line.situation, base: f.base ?? line.text, monster: f.monster, player: f.player, level: f.level, monsterPct: f.monsterPct, playerPct: f.playerPct, combo: f.combo, phase: f.phase, recent };
}

/** The AI commentator's sentence for a line, or null (off, slow, refused, rate limited, or not usable). */
export async function askCommentator(line, recent = [], { send = async (body, ms) => (await client()).api.commentaryLine(body, ms) } = {}) {
  try {
    const r = await send(lineRequest(line, recent), COMMENTARY.ai.requestTimeoutMs);
    return r?.ok && typeof r.text === "string" && r.text.trim() ? r.text.trim() : null;
  } catch {
    return null;
  }
}

/** The AI commentator's full-time report for a finished fight (facts as from summaryFacts() plus `base`, the plain report), or null. */
export async function askSummary(facts, { send = async (body, ms) => (await client()).api.commentarySummary(body, ms) } = {}) {
  try {
    const r = await send(facts, COMMENTARY.summary.aiWaitMs);
    return r?.ok && typeof r.text === "string" && r.text.trim() ? r.text.trim() : null;
  } catch {
    return null;
  }
}

/** An object URL for the spoken MP3 of a sentence, or null. The caller revokes it (the player below does). */
export async function fetchVoice(text, { base = null, fetchImpl = globalThis.fetch, makeUrl = (b) => URL.createObjectURL(b) } = {}) {
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), COMMENTARY.ai.voiceTimeoutMs);
  try {
    const prefix = base ?? (await client()).API_BASE;
    const res = await fetchImpl(`${prefix}/api/commentary/speech`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ text }), signal: ctl.signal });
    if (!res.ok) return null;
    const blob = await res.blob();
    return blob.size ? makeUrl(blob) : null;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

/** The family of a situation: "combo10" -> "combo". */
export const familyOf = (situation) => String(situation).replace(/\d+$/, "");

/** A per-minute allowance (sliding window): take() is true while fewer than `perMinute` requests were allowed in the last minute. */
export function createBudget({ perMinute = COMMENTARY.ai.maxPerMinute, now = () => performance.now() } = {}) {
  let stamps = [];
  return {
    take() {
      const t = now();
      stamps = stamps.filter((s) => t - s < 60000);
      if (stamps.length >= perMinute) return false;
      stamps.push(t);
      return true;
    },
  };
}

/** Is this queued line worth an AI request? (the right kind of moment, and either a big one or inside the budget) */
export function worthAsking(line, budget) {
  if (!COMMENTARY.ai.situations.includes(familyOf(line.situation))) return false;
  return line.priority >= COMMENTARY.ai.alwaysAskPriority || budget.take();
}
