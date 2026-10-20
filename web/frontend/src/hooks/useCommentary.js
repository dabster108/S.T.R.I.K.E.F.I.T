import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { COMMENTARY } from "../config/commentary.js";
import { api } from "../lib/api.js";
import { createQueue, createState, enqueue, hashSeed, nextReleaseDelay, release, step } from "../lib/commentary.js";
import { speechSupported } from "../lib/commentaryPrefs.js";
import { cumulativeDamage, liveNow, paceDelta } from "../lib/pace.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { useApi } from "./useApi.js";
import { useCommentaryPrefs } from "./useCommentaryPrefs.js";
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
 * Returns { current, log, enabled }: the line on the bar ({ text, tone, icon, key } or null) and the last 20 lines, newest first.
 */
export function useCommentary({ id, levelKey, playerName, monsterName, liveState, status, speak = false }) {
  const prefs = useCommentaryPrefs();
  const enabled = !prefs.hidden;
  const live = status === "live";
  const [current, setCurrent] = useState(null);
  const [log, setLog] = useState([]);
  const stateRef = useRef(null);
  const queueRef = useRef(createQueue());
  const timer = useRef(null);
  const seq = useRef(0);
  const speakRef = useRef(false);
  speakRef.current = Boolean(speak && prefs.speech && enabled && speechSupported());
  const liveStateRef = useRef(liveState);
  liveStateRef.current = liveState;

  // A new fight (or the viewer turning commentary back on) starts with fresh memory, taken from the fight as it is right now.
  useEffect(() => {
    if (!enabled) return undefined;
    stateRef.current = createState({ levelKey, playerName, monsterName, seed: hashSeed(id), initial: liveStateRef.current || {} });
    queueRef.current = createQueue();
    setCurrent(null);
    setLog([]);
    return () => {
      clearTimeout(timer.current);
      if (speechSupported()) window.speechSynthesis.cancel();
    };
  }, [id, levelKey, playerName, monsterName, enabled]);

  useEffect(() => {
    if (!prefs.speech && speechSupported()) window.speechSynthesis.cancel();
  }, [prefs.speech]);

  const show = useCallback((line) => {
    const shown = { ...line, key: ++seq.current, shownAt: Date.now() };
    setCurrent(shown);
    setLog((l) => [shown, ...l].slice(0, COMMENTARY.logMax));
    if (speakRef.current) {
      window.speechSynthesis.cancel(); // lines are at least 1.2 s apart, so this only ever clears a line that has not finished
      const u = new SpeechSynthesisUtterance(shown.text);
      u.rate = COMMENTARY.speechRate;
      window.speechSynthesis.speak(u);
    }
  }, []);

  const pump = useCallback(() => {
    clearTimeout(timer.current);
    const now = clock();
    const out = release(queueRef.current, now);
    queueRef.current = out.queue;
    if (out.line) show(out.line);
    const wait = nextReleaseDelay(queueRef.current, now);
    if (wait !== null) timer.current = setTimeout(pump, Math.max(30, wait + 10));
  }, [show]);

  const feed = useCallback(
    (event) => {
      if (!stateRef.current) return;
      const out = step({ ...event, at: clock() }, stateRef.current);
      stateRef.current = out.state;
      if (!out.lines.length) return;
      let q = queueRef.current;
      for (const line of out.lines) q = enqueue(q, line, clock());
      queueRef.current = q;
      pump();
    },
    [pump]
  );

  useLiveMessages((type, payload) => {
    if (!enabled || !id) return;
    if (type === "event_created" && payload?.sessionId === id) feed({ type: payload.type, payload: payload.payload });
    else if ((type === "session_started" || type === "session_updated") && payload?.session?.id === id && payload.liveState) feed({ type: "state", payload: payload.liveState });
    else if (type === "session_finished" && payload?.session?.id === id) feed({ type: "result", payload: { outcome: payload.session.outcome } });
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

  return { current, log, enabled };
}
