// The demo's stand-in for the live server: it plays scripted fights (fightScript.js) on a loop and speaks the same messages the real
// stream does (session_started, event_created, session_updated, session_finished). Several "lanes" (players) run side by side, each
// cycling through its own list of fights with a pause between them, so the Live Arena always has something happening and the player
// switcher has two players to switch between. Time is injected (`now`), so the whole thing is testable without waiting.

const iso = (ms) => new Date(ms).toISOString();

export const DEFAULT_PLAN = [
  { player: "Ava", offsetMs: 0, seed: 11, fights: [["Easy", "victory", 46], ["Medium", "victory", 58], ["Easy", "victory", 40]] },
  { player: "Ben", offsetMs: 9000, seed: 29, fights: [["Hard", "defeat", 78], ["Hard", "victory", 88], ["Medium", "victory", 60]] },
];
export const GAP_MS = 15_000; // between a fight ending and the same player's next one (the arena shows the result for 8 s of it)

// finishedExtras({ lane, run, step }) -> extra fields for a finished fight's message (newRecords, rankUp); none by default.
export function createEngine({ buildFight, plan = DEFAULT_PLAN, now = Date.now, gapMs = GAP_MS, finishedExtras = () => ({}) }) {
  const t0 = now();
  const lanes = plan.map((p, i) => ({ ...p, i, cursor: 0, run: null, startAt: t0 + (p.offsetMs || 0) }));
  const feed = []; // newest first, for /api/events
  let seq = 0;

  const sessionOf = (run, step, status = "live", extra = {}) => ({
    id: run.id, playerName: run.fight.playerName, level: run.fight.levelKey, outcome: "in_progress", ...step.session, durationSec: step.at, pointsEarned: step.session.score,
    startedAt: iso(run.startedAt), endedAt: iso(run.startedAt + step.at * 1000), source: "demo", notes: "", status, lastEventAt: iso(run.startedAt + step.at * 1000), ...extra,
  });

  /** Plays everything that is due at time `t` into emit(type, payload). Returns the ms until the next thing is due (null when nothing is). */
  function advance(emit, t = now()) {
    let nextDue = Infinity;
    for (const lane of lanes) {
      if (!lane.run && t >= lane.startAt) {
        const [levelKey, outcome, seconds] = lane.fights[lane.cursor % lane.fights.length];
        const fight = buildFight({ playerName: lane.player, levelKey, seconds, outcome, seed: lane.seed + lane.cursor * 7919 });
        lane.cursor++;
        lane.run = { fight, id: `demo-${lane.player.toLowerCase()}-${lane.cursor}-${t}`, startedAt: t, next: 0, live: {} };
      }
      const run = lane.run;
      if (!run) {
        nextDue = Math.min(nextDue, lane.startAt - t);
        continue;
      }
      while (run.next < run.fight.steps.length && t >= run.startedAt + run.fight.steps[run.next].at * 1000) {
        const step = run.fight.steps[run.next];
        const at = run.startedAt + step.at * 1000;
        const first = run.next === 0;
        run.next++;
        let lastEvent = null;
        for (const e of step.events) {
          const event = { id: `${run.id}-${++seq}`, type: e.type, sessionId: run.id, playerName: run.fight.playerName, message: e.message, payload: e.payload, at: iso(at) };
          lastEvent = { type: e.type, message: e.message, at: iso(at) };
          feed.unshift(event);
          emit("event_created", event);
        }
        if (feed.length > 300) feed.length = 300;
        run.live = { ...step.state, ...(lastEvent && lastEvent.type !== "hp" ? { lastEvent } : {}), updatedAt: iso(at) };
        if (step.final) {
          emit("session_finished", { session: sessionOf(run, step, "finished", { outcome: step.outcome }), liveState: run.live, ...finishedExtras({ lane, run, step }) });
          run.done = true;
        } else emit(first ? "session_started" : "session_updated", { session: sessionOf(run, step), liveState: run.live });
      }
      if (run.done) {
        lane.run = null;
        lane.startAt = t + gapMs;
        nextDue = Math.min(nextDue, gapMs);
      } else if (run.next < run.fight.steps.length) {
        nextDue = Math.min(nextDue, run.startedAt + run.fight.steps[run.next].at * 1000 - t);
      }
    }
    return Number.isFinite(nextDue) ? Math.max(20, nextDue) : null;
  }

  return {
    advance,
    /** Fights being played right now, in the shape of GET /api/live. */
    liveSessions(t = now()) {
      return lanes
        .filter((l) => l.run && !l.run.done && l.run.next > 0)
        .map((l) => {
          const run = l.run;
          const step = run.fight.steps[run.next - 1];
          return { ...sessionOf(run, step), liveState: run.live };
        })
        .sort((a, b) => a.startedAt.localeCompare(b.startedAt));
    },
    recentEvents: (n = 80) => feed.slice(0, n),
  };
}
