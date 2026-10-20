import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { COMMENTARY } from "../config/commentary.js";
import { api } from "../lib/api.js";
import { createQueue, createState, enqueue, hashSeed, nextReleaseDelay, release, step } from "../lib/commentary.js";
import { askCommentator, askSummary, createBudget, fetchVoice, worthAsking } from "../lib/commentaryAi.js";
import { speechSupported } from "../lib/commentaryPrefs.js";
import { createVoice } from "../lib/commentaryVoice.js";
import { plainSummary, summaryFacts } from "../lib/summary.js";
import { cumulativeDamage, liveNow, paceDelta } from "../lib/pace.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { useApi } from "./useApi.js";
import { useCommentaryPrefs } from "./useCommentaryPrefs.js";
import { useCommentaryStatus } from "./useCommentaryStatus.js";
import { useLiveHits } from "../components/live/useLiveHits.js";

const clock = () => performance.now();

/**
 * The commentary for ONE fight. It only listens to the shared live stream (the connection and the event shapes are untouched), turns
 * each event into lines with lib/commentary.js, and shows them through the pacing queue: one line per 1.2 s, three waiting at most.
 * Several fights on stage each call this once, so each has its own stream of lines.
 *
 *   id, levelKey, playerName, monsterName   the fight
 *   liveState                               the fight's state when the stage first shows it (so earlier phase changes are not re-announced)
 *   speak                                   true for the one fight whose lines are read aloud (when the viewer turned speech on)
 * When the server has the AI commentator (Groq) each queued line is sent off to be reworded like a football commentator; if the better
 * sentence is not back by the time the line's turn comes (or never comes) the plain template line is shown, so the pacing never changes.
 * Spoken lines use Google's voice when the server has it, otherwise the browser's own.
 * Returns { current, log, enabled, summary }: the line on the bar ({ text, tone, icon, key } or null), the last 20 lines, newest first, and
 * the full-time report ({ text, ai, key } or null) that appears a moment after the result line when the fight is over.
 */
export function useCommentary({ id, levelKey, playerName, monsterName, liveState, status, speak = false }) {
  const prefs = useCommentaryPrefs();
  const enabled = !prefs.hidden;
  const live = status === "live";
  const [current, setCurrent] = useState(null);
  const [log, setLog] = useState([]);
  const [summary, setSummary] = useState(null);
  const reported = useRef(null); // the fight id whose report has been started (a re-sent finish must not start a second one)
  const stateRef = useRef(null);
  const queueRef = useRef(createQueue());
  const timer = useRef(null);
  const seq = useRef(0);
  const speakRef = useRef(false);
  const server = useCommentaryStatus(); // what the server offers: { ai, voice }
  const aiRef = useRef(false);
  const voiceOnRef = useRef(false);
  aiRef.current = Boolean(enabled && server.ai && !prefs.plain);
  voiceOnRef.current = server.voice;
  speakRef.current = Boolean(speak && prefs.speech && enabled && (server.voice || speechSupported()));
  const voiceRef = useRef(null);
  if (!voiceRef.current) voiceRef.current = createVoice();
  const recentRef = useRef([]); // the last lines shown, newest first: sent so the model does not repeat their wording
  const inFlight = useRef(0);
  const budget = useRef(createBudget());
  const shownAt = useRef(-Infinity); // when the last line was put on the bar (performance.now), for the gap rule in present()
  const epoch = useRef(0); // a new fight (or switching commentary off) makes every answer still on its way out of date
  const liveStateRef = useRef(liveState);
  liveStateRef.current = liveState;

  // A new fight (or the viewer turning commentary back on) starts with fresh memory, taken from the fight as it is right now.
  useEffect(() => {
    if (!enabled) return undefined;
    stateRef.current = createState({ levelKey, playerName, monsterName, seed: hashSeed(id), initial: liveStateRef.current || {} });
    queueRef.current = createQueue();
    recentRef.current = [];
    inFlight.current = 0;
    budget.current = createBudget();
    shownAt.current = -Infinity;
    epoch.current++;
    reported.current = null;
    setCurrent(null);
    setLog([]);
    setSummary(null);
    const voice = voiceRef.current;
    return () => {
      clearTimeout(timer.current);
      epoch.current++;
      voice.stop();
    };
  }, [id, levelKey, playerName, monsterName, enabled]);

  useEffect(() => {
    if (!prefs.speech) voiceRef.current.stop();
  }, [prefs.speech]);

  // `audio`: a promise of the spoken version (already on its way when the AI sentence arrived) or null.
  const show = useCallback((line, audio) => {
    const shown = { ...line, key: ++seq.current, shownAt: Date.now() };
    setCurrent(shown);
    setLog((l) => [shown, ...l].slice(0, COMMENTARY.logMax));
    recentRef.current = [shown.text, ...recentRef.current].slice(0, COMMENTARY.ai.recentLines);
    if (speakRef.current) {
      const spoken = audio || (voiceOnRef.current ? fetchVoice(shown.text) : null); // no Google audio: the browser's voice reads it
      voiceRef.current.say(shown.text, spoken, { interrupt: shown.priority >= COMMENTARY.priority.result });
    }
  }, []);

  // The line's turn has come: show the AI sentence if it is here (or arrives within showWaitMs), otherwise the plain one. Waiting must
  // not squeeze two shown lines closer than minGapMs, so a line that is ready early still waits for the gap since the last one shown.
  const present = useCallback(
    async (line) => {
      const { aiPromise } = line;
      const plain = { ...line, aiPromise: undefined, audioPromise: undefined }; // the promises are not part of what is shown or logged
      const mine = epoch.current;
      let better = null;
      if (aiPromise) better = await Promise.race([aiPromise, new Promise((r) => setTimeout(() => r(null), COMMENTARY.ai.showWaitMs))]);
      const gap = shownAt.current + COMMENTARY.minGapMs - clock();
      if (gap > 0) await new Promise((r) => setTimeout(r, gap));
      if (mine !== epoch.current) return undefined;
      shownAt.current = clock();
      // the spoken version is started when the AI sentence arrives, so it is read from the line after the wait
      return better ? show({ ...plain, text: better, plainText: plain.text, ai: true }, line.audioPromise) : show(plain, null);
    },
    [show]
  );

  // Starts the AI request for a line that is waiting in the queue, so the sentence is usually back before the line's turn.
  const ask = useCallback((item) => {
    item.aiPromise = null;
    if (!aiRef.current || inFlight.current >= COMMENTARY.ai.maxInFlight || !worthAsking(item, budget.current)) return;
    const mine = epoch.current;
    inFlight.current++;
    item.aiPromise = askCommentator(item, recentRef.current).then((text) => {
      if (mine !== epoch.current) return null;
      inFlight.current--;
      if (text && speakRef.current && voiceOnRef.current) item.audioPromise = fetchVoice(text);
      return text;
    });
  }, []);

  const pump = useCallback(() => {
    clearTimeout(timer.current);
    const now = clock();
    const out = release(queueRef.current, now);
    queueRef.current = out.queue;
    if (out.line) present(out.line);
    const wait = nextReleaseDelay(queueRef.current, now);
    if (wait !== null) timer.current = setTimeout(pump, Math.max(30, wait + 10));
  }, [present]);

  const feed = useCallback(
    (event) => {
      if (!stateRef.current) return;
      const out = step({ ...event, at: clock() }, stateRef.current);
      stateRef.current = out.state;
      if (!out.lines.length) return;
      let q = queueRef.current;
      for (const line of out.lines) q = enqueue(q, line, clock());
      for (const item of q.items) if (item.aiPromise === undefined) ask(item); // only lines that made it into the queue cost a request
      queueRef.current = q;
      pump();
    },
    [pump, ask]
  );

  // The fight is over: the plain report at once, the AI's version if it arrives in time, shown after the result line and read aloud when
  // speech is on. The plain text is the fallback, so a finished fight always gets a summary.
  const report = useCallback(
    (session) => {
      if (reported.current === id) return;
      reported.current = id;
      const mine = epoch.current;
      const facts = summaryFacts(session, { playerName, monsterName, peakCombo: stateRef.current?.peakCombo });
      const base = plainSummary(facts);
      const wait = (ms) => new Promise((r) => setTimeout(r, ms));
      const better = aiRef.current ? askSummary({ ...facts, base }) : Promise.resolve(null);
      Promise.all([wait(COMMENTARY.summary.delayMs), Promise.race([better, wait(COMMENTARY.summary.aiWaitMs).then(() => null)])]).then(([, text]) => {
        if (mine !== epoch.current) return;
        const final = text || base;
        setSummary({ text: final, ai: Boolean(text), key: ++seq.current });
        if (speakRef.current) voiceRef.current.say(final, voiceOnRef.current ? fetchVoice(final) : null);
      });
    },
    [id, playerName, monsterName]
  );

  useLiveMessages((type, payload) => {
    if (!enabled || !id) return;
    if (type === "event_created" && payload?.sessionId === id) feed({ type: payload.type, payload: payload.payload });
    else if ((type === "session_started" || type === "session_updated") && payload?.session?.id === id && payload.liveState) feed({ type: "state", payload: payload.liveState });
    else if (type === "session_finished" && payload?.session?.id === id) {
      feed({ type: "result", payload: { outcome: payload.session.outcome } });
      report(payload.session);
    }
  });

  // Time passing: the idle nudge needs a clock, once a second, while the fight is live and the tab is visible.
  useEffect(() => {
    if (!enabled || !live) return undefined;
    const t = setInterval(() => {
      if (!document.hidden) feed({ type: "tick" });
    }, 1000);
    return () => clearInterval(t);
  }, [enabled, live, feed]);

  // Ghost Pace: how far ahead of (or behind) the player's best fight on this level this fight is. The same two sources the Ghost Pace
  // card uses; the commentary only reads the gap.
  const hits = useLiveHits(enabled && live ? id : null);
  const best = useApi(() => (enabled && live && ["Easy", "Medium", "Hard"].includes(levelKey) ? api.playerBestPace(playerName, levelKey) : Promise.resolve(null)), [enabled, live, playerName, levelKey]);
  const ghost = best.data && best.data.playerName === playerName && best.data.level === levelKey && best.data.best && !best.data.reason ? best.data.points : null;
  const livePoints = useMemo(() => cumulativeDamage([...hits.values()]), [hits]);
  const t = liveState?.t;
  useEffect(() => {
    if (!enabled || !live || !ghost || ghost.length < 2) return;
    if (livePoints.length < 2) return; // this fight's own hits have not loaded (or it has none yet): comparing nothing against the best would be a false claim
    const delta = paceDelta(livePoints, ghost, liveNow(t, livePoints));
    if (delta !== null) feed({ type: "pace", payload: { delta } });
  }, [enabled, live, ghost, livePoints, t, feed]);

  return { current, log, enabled, summary };
}
