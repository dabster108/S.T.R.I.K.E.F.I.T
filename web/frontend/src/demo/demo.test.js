import test from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { CFG, LIVE_KEYS, makeFightBuilder } from "./fightScript.js";
import { createEngine } from "./engine.js";
import { createDemoApi, daysBetween, keyOf, shiftTimes } from "./handler.js";

const require = createRequire(import.meta.url);
const attackTable = require("../content/monsterAttacks.json");
const sim = require("../../../backend/scripts/simulate-live.js");
const buildFight = makeFightBuilder({ attackTable });

test("the scripted fight's numbers are the simulator's", () => {
  assert.deepEqual(CFG, sim.CFG);
  const backendKeys = /const LIVE_KEYS = \[([^\]]+)\]/.exec(require("node:fs").readFileSync(new URL("../../../backend/server.js", import.meta.url), "utf8"))[1].match(/"(\w+)"/g).map((s) => s.slice(1, -1));
  assert.deepEqual(LIVE_KEYS, backendKeys);
});

test("a fight is deterministic for its seed and different for another", () => {
  const a = buildFight({ playerName: "Ava", levelKey: "Easy", seconds: 40, outcome: "victory", seed: 5 });
  const b = buildFight({ playerName: "Ava", levelKey: "Easy", seconds: 40, outcome: "victory", seed: 5 });
  const c = buildFight({ playerName: "Ava", levelKey: "Easy", seconds: 40, outcome: "victory", seed: 6 });
  assert.deepEqual(a, b);
  assert.notDeepEqual(a.steps.map((s) => s.session.score), c.steps.map((s) => s.session.score));
});

test("every level and outcome finishes on script, with sane health, speeds and a closing result", () => {
  for (const [levelKey, outcome, seconds] of [["Easy", "victory", 40], ["Medium", "victory", 55], ["Hard", "defeat", 75], ["Hard", "victory", 85]]) {
    const f = buildFight({ playerName: "Ben", levelKey, seconds, outcome, seed: 3 });
    assert.equal(f.outcome, outcome, `${levelKey} ${outcome}`);
    assert.ok(f.durationSec > seconds * 0.4 && f.durationSec <= seconds * 1.25 + 1, `${levelKey} ${outcome} lasted ${f.durationSec}s of ${seconds}`);
    let prevAt = -1;
    for (const s of f.steps) {
      assert.ok(s.at >= prevAt);
      prevAt = s.at;
      assert.ok(s.state.playerHp === undefined || (s.state.playerHp >= 0 && s.state.playerHp <= CFG[levelKey].hp));
      assert.ok(s.state.monsterHp === undefined || (s.state.monsterHp >= 0 && s.state.monsterHp <= CFG[levelKey].monsterHp));
      for (const e of s.events) if (e.type === "hit") assert.ok(e.payload.swingSpeed >= CFG[levelKey].min && ["tap", "light", "medium", "full"].includes(e.payload.tier) && e.payload.damage > 0);
    }
    const last = f.steps[f.steps.length - 1];
    assert.equal(last.final, true);
    assert.equal(last.events.at(-1).type, "session_complete");
    assert.equal(f.steps.filter((s) => s.final).length, 1);
    assert.ok(f.steps.some((s) => s.events.some((e) => e.type === "monster_attack")));
    assert.ok(f.steps.some((s) => s.events.some((e) => e.type === "hit" && e.payload.tier === "full")), "at least one full-power hit, so the impact animations have something to show");
  }
});

// ----- the engine, on a fake clock
const plan = [{ player: "Ava", offsetMs: 0, seed: 1, fights: [["Easy", "victory", 30]] }, { player: "Ben", offsetMs: 5000, seed: 2, fights: [["Medium", "victory", 40]] }];
function run(until, step = 400, gapMs = 4000) {
  let clock = 1_000_000;
  const engine = createEngine({ buildFight, plan, now: () => clock, gapMs });
  const log = [];
  const emit = (type, payload) => log.push({ type, payload, at: clock });
  const t0 = clock;
  while (clock - t0 < until) {
    engine.advance(emit, clock);
    clock += step;
  }
  return { engine, log, clock };
}

test("the engine starts a fight with session_started, streams events, and ends it with session_finished", () => {
  const { log } = run(60_000);
  const ava = log.filter((m) => (m.payload.session || m.payload).playerName === "Ava");
  assert.equal(ava[0].type, "event_created"); // the session_start event
  assert.equal(ava.find((m) => m.type === "session_started").payload.session.status, "live");
  const fin = ava.find((m) => m.type === "session_finished");
  assert.ok(fin, "Ava's fight finished within a minute");
  assert.equal(fin.payload.session.status, "finished");
  assert.equal(fin.payload.session.outcome, "victory");
  assert.ok(ava.some((m) => m.type === "session_updated" && m.payload.liveState.playerMaxHp === 500));
  assert.ok(ava.filter((m) => m.type === "event_created").every((m) => m.payload.id && m.payload.at && m.payload.sessionId && m.payload.payload));
});

test("both players are live together, and each starts again after a gap", () => {
  const { engine, log } = run(30_000);
  const live = engine.liveSessions();
  assert.deepEqual(live.map((s) => s.playerName).sort(), ["Ava", "Ben"]);
  for (const s of live) {
    assert.equal(s.status, "live");
    assert.equal(s.outcome, "in_progress");
    assert.ok(s.liveState && typeof s.liveState.monsterHp === "number");
  }
  const long = run(140_000);
  const starts = long.log.filter((m) => m.type === "session_started" && m.payload.session.playerName === "Ava");
  assert.ok(starts.length >= 2, "Ava's script loops");
  assert.notEqual(starts[0].payload.session.id, starts[1].payload.session.id);
  assert.ok(log.length > 100);
});

test("advance reports when the next thing is due", () => {
  let clock = 5000;
  const engine = createEngine({ buildFight, plan: [plan[0]], now: () => clock });
  const wait = engine.advance(() => {}, clock);
  assert.ok(wait >= 20 && wait <= 5000);
});

// ----- the HTTP stand-in
test("keyOf drops tz, empty and all, and sorts", () => {
  assert.equal(keyOf("/api/analytics?tz=Asia/Kathmandu&level=all&player="), "/api/analytics");
  assert.equal(keyOf("/api/leaderboard?limit=10&level=Easy"), "/api/leaderboard?level=Easy&limit=10");
});

test("shiftTimes moves ISO times and bare dates by whole days and leaves the rest", () => {
  const text = '{"at":"2026-10-18T14:30:40.502Z","day":"2026-10-31","n":"12","s":"2026-10-30T23:59:59Z"}';
  assert.equal(shiftTimes(text, 2), '{"at":"2026-10-20T14:30:40.502Z","day":"2026-11-02","n":"12","s":"2026-11-01T23:59:59Z"}');
  assert.equal(shiftTimes(text, 0), text);
  assert.equal(daysBetween(0, 3.5 * 864e5), 3);
  assert.equal(daysBetween(10, 5), 0);
});

const snapshot = {
  capturedAt: Date.parse("2026-10-18T12:00:00Z"),
  json: {
    "/api/sessions": { sessions: [{ id: "a", playerName: "Ava", level: "Easy", outcome: "victory", status: "finished", endedAt: "2026-10-18T10:00:00.000Z" }, { id: "b", playerName: "Ben", level: "Hard", outcome: "defeat", status: "finished", endedAt: "2026-10-17T10:00:00.000Z" }] },
    "/api/events?limit=300": { events: [{ id: "e1", type: "hit", playerName: "Ava", sessionId: "a", at: "2026-10-18T10:00:00.000Z" }] },
    "/api/players/Ava": { playerName: "Ava" },
    "/api/analytics": { ok: true, level: "all" },
    "/api/analytics?level=Hard": { ok: true, level: "Hard" },
    "/api/health": { ok: true, service: "x" },
  },
  csv: { "export/sessions.csv": "id,at\na,2026-10-18T10:00:00.000Z\n" },
};
const demo = (extra = {}) => createDemoApi({ snapshot, engine: null, now: () => Date.parse("2026-10-20T12:00:00Z"), ...extra });
const body = (r) => JSON.parse(r.body);

test("GETs come from the snapshot, shifted to look recent, and filters work", () => {
  const api = demo();
  assert.equal(api.days, 2);
  const all = body(api.handle("GET", "/api/sessions"));
  assert.equal(all.count, 2);
  assert.equal(all.sessions[0].endedAt, "2026-10-20T10:00:00.000Z");
  assert.deepEqual(body(api.handle("GET", "/api/sessions?level=Hard")).sessions.map((s) => s.id), ["b"]);
  assert.equal(body(api.handle("GET", "/api/sessions?limit=1")).sessions.length, 1);
  assert.equal(body(api.handle("GET", "/api/analytics?tz=X&level=Hard")).level, "Hard");
  assert.equal(body(api.handle("GET", "/api/analytics?tz=X&level=all")).level, "all");
  assert.equal(api.handle("GET", "/api/players/Zed").status, 404);
  assert.equal(body(api.handle("GET", "/api/events?player=Ava")).events.length, 1);
  assert.equal(body(api.handle("GET", "/api/events?player=Nobody")).events.length, 0);
});

test("the demo is read-only, except the two things a demo needs", () => {
  const api = demo();
  for (const [m, u] of [["DELETE", "/api/sessions/a"], ["PATCH", "/api/players/Ava"], ["PUT", "/api/players/Ava/goal"], ["POST", "/api/anything-else"]]) {
    const r = api.handle(m, u, {});
    assert.equal(r.status, 403, `${m} ${u}`);
    assert.match(body(r).error, /read-only/);
  }
  assert.equal(api.handle("POST", "/api/sessions/a/feedback", { rpe: 13 }).status, 201);
});

test("CSV exports are served with a row count and a file name", () => {
  const r = demo().handle("GET", "/api/export/sessions.csv");
  assert.equal(r.status, 200);
  assert.match(r.type, /text\/csv/);
  assert.equal(r.headers["X-Row-Count"], "1");
  assert.match(r.body, /2026-10-20T10:00/);
  assert.equal(demo().handle("GET", "/api/export/events.csv").status, 404);
});

test("the study flow works in memory: consent, a code, the two forms, and the 'fight' that unlocks the second", () => {
  let clock = Date.parse("2026-10-20T12:00:00Z");
  const api = createDemoApi({ snapshot, engine: null, now: () => clock });
  assert.equal(api.handle("POST", "/api/study/participants", {}).status, 400);
  const enrolled = body(api.handle("POST", "/api/study/participants", { consent: true }));
  assert.equal(enrolled.code, "P11");
  assert.equal(body(api.handle("GET", "/api/study/participants/P11")).preDone, false);
  assert.equal(api.handle("POST", "/api/study/participants/P11/post", {}).status, 409);
  assert.equal(api.handle("POST", "/api/study/participants/P11/pre", {}).status, 201);
  assert.equal(body(api.handle("GET", "/api/study/participants/P11")).fights.finished, 0);
  clock += 8000;
  assert.equal(body(api.handle("GET", "/api/study/participants/P11")).fights.finished, 1);
  assert.equal(api.handle("POST", "/api/study/participants/P11/post", {}).status, 201);
  assert.equal(body(api.handle("GET", "/api/study/participants/P11")).postDone, true);
  assert.equal(api.handle("GET", "/api/study/participants/P99").status, 404);
});

// ---------------------------------------------------------------------------------------------------------------- engagement features

import { demoFinishedExtras } from "./finishedExtras.js";
import { emptyWrappedWeek, shiftToMonday } from "./handler.js";

const clock = Date.UTC(2026, 9, 20, 12, 0, 0);
const wrappedOf = (weekStart, extra = {}) => ({ ok: true, playerName: "Ava", timeZone: "Asia/Kathmandu", weekStart, weekEnd: weekStart, isCurrentWeek: false, weeks: [{ weekStart, weekEnd: weekStart, fights: 3 }], fights: 3, speedCap: 8, rules: {}, ...extra });

test("the batch of ranks for the leaderboard is put together from the one-per-player answers", () => {
  const snapshot = { capturedAt: clock, json: { "/api/players/Ava/rank": { playerName: "Ava", rank: { key: "warrior", name: "Warrior" }, effortPoints: 2252, progress: 0.46, ladder: [] }, "/api/players/Ben/rank": { playerName: "Ben", rank: { key: "brawler", name: "Brawler" }, effortPoints: 1043, progress: 0.83 } } };
  const api = createDemoApi({ snapshot, engine: null, now: () => clock });
  const body = JSON.parse(api.handle("GET", "/api/ranks?players=Ava,Ben,Nobody").body);
  assert.deepEqual(Object.keys(body.ranks), ["Ava", "Ben"]);
  assert.deepEqual(body.ranks.Ava, { rank: { key: "warrior", name: "Warrior" }, effortPoints: 2252, progress: 0.46 });
});

test("a Wrapped week is looked up under its original date, the default is the recorded latest week, an unrecorded week is empty", () => {
  const days = 6; // the snapshot is six days old, so every date inside it moves forward by six days
  const snapshot = {
    capturedAt: clock - days * 864e5,
    json: {
      "/api/players/Ava/wrapped": wrappedOf("2026-10-12", { fights: 4 }),
      "/api/players/Ava/wrapped?weekStart=2026-10-12": wrappedOf("2026-10-12", { fights: 4 }),
      "/api/players/Ava/wrapped?weekStart=2026-10-05": wrappedOf("2026-10-05", { fights: 3 }),
    },
  };
  const api = createDemoApi({ snapshot, engine: null, now: () => clock });
  const get = (p) => JSON.parse(api.handle("GET", p).body);
  assert.equal(get("/api/players/Ava/wrapped").fights, 4);
  assert.equal(get("/api/players/Ava/wrapped").weekStart, "2026-10-18"); // 10-12 + 6 days
  assert.equal(get("/api/players/Ava/wrapped?weekStart=2026-10-18").fights, 4); // asked by its moved date, found by its original one
  assert.equal(get("/api/players/Ava/wrapped?weekStart=2026-10-11").fights, 3);
  const empty = get("/api/players/Ava/wrapped?weekStart=2026-09-27");
  assert.deepEqual([empty.fights, empty.winRate, empty.bestDay, empty.personality.name], [0, null, null, "Warming Up"]);
  assert.equal(api.handle("GET", "/api/players/Zed/wrapped").status, 404);
});

test("an empty week keeps the player's week list and has neighbours", () => {
  const e = emptyWrappedWeek(wrappedOf("2026-10-12", { isCurrentWeek: true }), "2026-10-05");
  assert.deepEqual([e.weekStart, e.weekEnd, e.previousWeek, e.nextWeek, e.weeks.length], ["2026-10-05", "2026-10-11", "2026-09-28", "2026-10-12", 1]);
  assert.equal(shiftToMonday("2026-10-15"), "2026-10-12");
  assert.equal(shiftToMonday("2026-10-18"), "2026-10-12");
  assert.equal(shiftToMonday("2026-10-12"), "2026-10-12");
});

test("scripted fights finish with sample records after the first fight of a loop, and one rank-up", () => {
  const won = { outcome: "victory", session: { score: 1000, damageDealt: 500.4 } };
  assert.deepEqual(demoFinishedExtras({ lane: { player: "Ben", cursor: 1 }, step: won }).newRecords, []); // the first fight has nothing to beat
  const second = demoFinishedExtras({ lane: { player: "Ben", cursor: 2 }, step: won });
  assert.deepEqual(second.newRecords.map((r) => [r.metric, r.value, r.previous]), [["damageDealt", 500.4, 425.3], ["score", 1000, 820]]);
  assert.equal(second.rankUp, null);
  assert.deepEqual(demoFinishedExtras({ lane: { player: "Ben", cursor: 2 }, step: { outcome: "defeat", session: won.session } }).newRecords, []);
  assert.equal(demoFinishedExtras({ lane: { player: "Ava", cursor: 2 }, step: won }).rankUp.to.name, "Warrior");
});

test("the engine puts the extras on the finished message and only there", () => {
  const messages = [];
  let t = 0;
  const engine = createEngine({ buildFight, now: () => t, gapMs: 1000, plan: [{ player: "Ava", offsetMs: 0, seed: 11, fights: [["Easy", "victory", 20], ["Easy", "victory", 20]] }], finishedExtras: () => ({ newRecords: [{ metric: "score", value: 1, previous: 0 }], rankUp: null }) });
  for (let i = 0; i < 120; i++) {
    t += 500;
    engine.advance((type, payload) => messages.push([type, payload]));
  }
  const finished = messages.filter(([type]) => type === "session_finished");
  assert.ok(finished.length >= 1);
  assert.ok(finished.every(([, p]) => Array.isArray(p.newRecords) && "rankUp" in p));
  assert.ok(messages.filter(([type]) => type !== "session_finished" && type !== "event_created").every(([, p]) => !("newRecords" in p)));
});
