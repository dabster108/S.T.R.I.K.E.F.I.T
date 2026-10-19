import test from "node:test";
import assert from "node:assert/strict";
import { COMMENTARY, LINES, SITUATIONS } from "../config/commentary.js";
import { createQueue, createState, enqueue, fill, nextLine, nextReleaseDelay, pickVariant, release, shortMonsterName, step, swingLimits } from "./commentary.js";

const HARD = { levelKey: "Hard", playerName: "Ava", monsterName: "The Harbor Tyrant" };
const EASY = { levelKey: "Easy", playerName: "Ava", monsterName: "The Gatekeeper" };
const ev = (type, payload = {}, at = 0) => ({ type, payload, at });
const texts = (r) => r.lines.map((l) => l.text);
const hp = (monster, player = 1) => ({ monsterHp: monster * 1000, monsterMaxHp: 1000, playerHp: player * 100, playerMaxHp: 100 });

test("every situation has a template pool of at least three variants, and a valid priority class", () => {
  for (const key of Object.keys(SITUATIONS)) {
    assert.ok(Array.isArray(LINES[key]) && LINES[key].length >= 3, `${key} needs 3+ variants`);
    assert.ok(SITUATIONS[key].priority in COMMENTARY.priority, `${key} priority class`);
  }
  for (const key of Object.keys(LINES)) assert.ok(key in SITUATIONS, `${key} has no situation entry`);
});

test("the priority order is result > phase > monster low > player low > full power > wind-up > combo > clash > rest", () => {
  const p = COMMENTARY.priority;
  const order = [p.result, p.phase, p.monsterLow, p.playerLow, p.fullPower, p.windUp, p.combo, p.clash, p.rest];
  for (let i = 1; i < order.length; i++) assert.ok(order[i - 1] > order[i]);
});

test("pickVariant never returns the variant used last", () => {
  let seed = 7;
  let last = 0;
  for (let i = 0; i < 200; i++) {
    const r = pickVariant(3, last, seed);
    assert.notEqual(r.index, last);
    assert.ok(r.index >= 0 && r.index < 3);
    last = r.index;
    seed = r.seed;
  }
  assert.equal(pickVariant(1, 0, 1).index, 0); // a one-variant pool cannot avoid repeating
});

test("the same situation never repeats its text twice in a row", () => {
  let state = createState(EASY);
  let last = null;
  for (let i = 0; i < 40; i++) {
    const r = step(ev("clash", {}, i * 5000), state);
    state = r.state;
    assert.equal(r.lines.length, 1);
    assert.notEqual(r.lines[0].text, last);
    last = r.lines[0].text;
  }
});

test("the same events always give the same lines", () => {
  const run = () => {
    let s = createState(HARD);
    const out = [];
    for (const e of [ev("monster_attack", { attack: "Earthsplitter Slam" }, 0), ev("clash", {}, 1), ev("hit", { swingSpeed: 6, tier: "full", ...hp(0.9) }, 2)]) {
      const r = step(e, s);
      s = r.state;
      out.push(...texts(r));
    }
    return out;
  };
  assert.deepEqual(run(), run());
});

test("a full-power swing says the clamped speed, using the level's own full-power threshold", () => {
  assert.match(texts(step(ev("hit", { swingSpeed: 7.44, tier: "full" }), createState(EASY)))[0], /7\.4 m\/s/);
  assert.equal(fill(LINES.fullPower[0], { speed: "7.4" }), "Full-power swing, 7.4 m/s!"); // the sentence from the brief is variant 0
  // raw controller speeds above the game's 8 m/s cap are shown as 8.0
  assert.match(texts(step(ev("hit", { swingSpeed: 11.6, tier: "full" }), createState(EASY)))[0], /8\.0 m\/s/);
  // with no tier reported the level's threshold decides: Level 2 counts 4.6 m/s as full power, Level 1 needs 5
  assert.equal(swingLimits("Medium").fullPower, 4.6);
  assert.equal(swingLimits("Easy").fullPower, 5);
  assert.equal(step(ev("hit", { swingSpeed: 4.7 }), createState({ ...EASY, levelKey: "Medium" })).lines.length, 1);
  assert.equal(step(ev("hit", { swingSpeed: 4.7 }), createState(EASY)).lines.length, 0);
  // a medium hit says nothing
  assert.equal(step(ev("hit", { swingSpeed: 3.2, tier: "medium" }), createState(EASY)).lines.length, 0);
});

test("a hit on a guarding monster says how much the guard absorbed", () => {
  const line = texts(step(ev("hit", { swingSpeed: 6, tier: "full", defended: true }), createState(EASY)))[0];
  assert.match(line, new RegExp(`${COMMENTARY.guardAbsorbPct}%`));
  assert.doesNotMatch(line, /Full-power/);
});

test("a wind-up names the real attack and the monster", () => {
  for (const seed of [1, 2, 3, 4, 5, 6]) {
    const line = texts(step(ev("monster_attack", { attack: "Earthsplitter Slam", category: "Special" }), createState({ ...HARD, seed })))[0];
    assert.match(line, /The Harbor Tyrant/);
    assert.match(line, /Earthsplitter Slam/);
  }
  assert.equal(fill(LINES.windUp[0], { monster: "The Harbor Tyrant", attack: "Earthsplitter Slam" }), "The Harbor Tyrant winds up Earthsplitter Slam!");
});

test("guard up, guard down, clash, hurt and dodge each have a line", () => {
  const s = createState(EASY);
  for (const [type, payload] of [["monster_defend"], ["monster_guard", { blockedHits: 2 }], ["clash"], ["player_hurt", { amount: 12.4, attack: "Slash" }], ["dodge", { attack: "Slash" }]]) {
    assert.equal(step(ev(type, payload), s).lines.length, 1, type);
  }
  assert.equal(step(ev("player_hurt", { amount: 0 }), s).lines.length, 0); // a blocked attack is not "hurt"
});

test("combo milestones 5, 10 and 20 are announced once each and can be earned again after the chain breaks", () => {
  let s = createState(EASY);
  const said = [];
  for (const combo of [1, 2, 3, 4, 5, 6, 9, 10, 11, 19, 20, 21, 3, 4, 5]) {
    const r = step(ev("hit", { swingSpeed: 3, tier: "medium", combo }), s);
    s = r.state;
    said.push(...r.lines.map((l) => l.situation));
  }
  assert.deepEqual(said, ["combo5", "combo10", "combo20", "combo5"]);
  assert.match(texts(step(ev("hit", { combo: 10 }), createState(EASY)))[0], /10/);
});

test("Level 3 phase changes fire at the 66% and 33% HP breaks, and only on Level 3", () => {
  let s = createState({ ...HARD, initial: hp(0.8) });
  let r = step(ev("hp", hp(0.7)), s); // still phase 1
  assert.equal(r.lines.length, 0);
  s = r.state;
  r = step(ev("hit", { swingSpeed: 3, ...hp(0.64) }), s);
  assert.match(texts(r)[0], /The Tyrant enters phase 2|Phase 2/);
  assert.equal(r.lines[0].situation, "phase2");
  s = r.state;
  r = step(ev("hp", hp(0.5)), s);
  assert.equal(r.lines.length, 0); // no second announcement
  r = step(ev("hp", hp(0.31)), r.state);
  assert.equal(r.lines[0].situation, "phase3");
  assert.match(r.lines[0].text, /enraged|Enraged/);
  // jumping from phase 1 to phase 3 in one step announces phase 3 only
  assert.deepEqual(step(ev("hp", hp(0.2)), createState({ ...HARD, initial: hp(0.9) })).lines.map((l) => l.situation), ["phase3"]);
  // no phases on Level 1
  assert.equal(step(ev("hp", hp(0.5)), createState({ ...EASY, initial: hp(0.9) })).lines.length, 0);
  // opening the page mid-fight, already in phase 2, announces nothing
  assert.equal(step(ev("hp", hp(0.5)), createState({ ...HARD, initial: hp(0.5) })).lines.length, 0);
  assert.equal(shortMonsterName("The Harbor Tyrant"), "The Tyrant");
  assert.equal(shortMonsterName("The Gatekeeper"), "The Gatekeeper");
});

test("monster HP under 10% says one more hit, once; player HP under 25% warns, and can warn again after recovering", () => {
  let s = createState({ ...EASY, initial: hp(0.5) });
  let r = step(ev("hit", { swingSpeed: 3, ...hp(0.09) }), s);
  assert.deepEqual(r.lines.map((l) => l.situation), ["monsterLow"]);
  s = r.state;
  assert.equal(step(ev("hit", { swingSpeed: 3, ...hp(0.05) }), s).lines.length, 0);
  assert.equal(step(ev("hp", hp(0)), s).lines.length, 0); // dead is not "one more hit"

  s = createState({ ...EASY, initial: hp(0.5, 1) });
  r = step(ev("player_hurt", { amount: 5, ...hp(0.5, 0.2) }), s);
  assert.ok(r.lines.some((l) => l.situation === "playerLow"));
  s = r.state;
  assert.equal(step(ev("player_hurt", { amount: 5, ...hp(0.5, 0.15) }), s).lines.some((l) => l.situation === "playerLow"), false);
  s = step(ev("hp", hp(0.5, 0.6)), s).state; // recovered above the re-arm line
  assert.ok(step(ev("player_hurt", { amount: 5, ...hp(0.5, 0.2) }), s).lines.some((l) => l.situation === "playerLow"));
  assert.match(LINES.monsterLow[0], /One more hit!/);
});

test("when one event triggers several lines the most important comes first", () => {
  const s = createState({ ...HARD, initial: hp(0.7) });
  const r = step(ev("hit", { swingSpeed: 7, tier: "full", combo: 5, ...hp(0.6) }), s);
  assert.deepEqual(r.lines.map((l) => l.situation), ["phase2", "fullPower", "combo5"]);
  assert.equal(nextLine(ev("hit", { swingSpeed: 7, tier: "full", combo: 5, ...hp(0.6) }), s).priority, COMMENTARY.priority.phase);
});

test("ghost pace lines are at most once per 20 s and need a real gap", () => {
  let s = createState(EASY);
  let r = step(ev("pace", { delta: 140 }, 1000), s);
  assert.equal(r.lines[0].situation, "paceAhead");
  assert.match(r.lines[0].text, /140/);
  s = r.state;
  assert.equal(step(ev("pace", { delta: -90 }, 12000), s).lines.length, 0); // 11 s later: too soon
  r = step(ev("pace", { delta: -90 }, 21500), s);
  assert.equal(r.lines[0].situation, "paceBehind");
  assert.equal(step(ev("pace", { delta: 3 }, 99999), createState(EASY)).lines.length, 0); // a 3-damage gap is not news
});

test("the idle nudge comes after 6 s without a swing, once, and re-arms after the next swing", () => {
  let s = createState({ ...EASY });
  s = step(ev("session_start", {}, 0), s).state;
  assert.equal(step(ev("tick", {}, 5900), s).lines.length, 0);
  let r = step(ev("tick", {}, 6000), s);
  assert.equal(r.lines[0].situation, "idle");
  assert.match(r.lines[0].text, /1\.2/);
  s = r.state;
  assert.equal(step(ev("tick", {}, 9000), s).lines.length, 0); // once
  s = step(ev("hit", { swingSpeed: 3, tier: "medium" }, 9500), s).state;
  assert.equal(step(ev("tick", {}, 15000), s).lines.length, 0);
  assert.equal(step(ev("tick", {}, 15500), s).lines[0].situation, "idle");
  // the minimum speed is the level's own: Level 3 counts nothing under 1.6 m/s
  const hard = step(ev("tick", {}, 6000), step(ev("session_start", {}, 0), createState(HARD)).state);
  assert.match(hard.lines[0].text, /1\.6/);
  // opening the page mid-fight starts the idle clock at the first tick
  const mid = step(ev("tick", {}, 100000), createState(EASY)).state;
  assert.equal(step(ev("tick", {}, 105000), mid).lines.length, 0);
  assert.equal(step(ev("tick", {}, 106000), mid).lines.length, 1);
});

test("results: victory, defeat and abandoned each have a line, and nothing follows a result", () => {
  for (const [outcome, situation] of [["victory", "victory"], ["defeat", "defeat"], ["abandoned", "abandoned"], ["unknown", "abandoned"]]) {
    const r = step(ev("result", { outcome }), createState(HARD));
    assert.equal(r.lines[0].situation, situation);
    assert.equal(r.lines[0].priority, COMMENTARY.priority.result);
    assert.equal(step(ev("clash"), r.state).lines.length, 0);
  }
  assert.match(texts(step(ev("result", { outcome: "victory" }), createState(HARD)))[0], /Ava|Tyrant/);
});

// ---------------------------------------------------------------------------------------------------------------- pacing

const line = (situation, priority, text = situation) => ({ situation, priority, text, tone: "muted", icon: "info", at: 0 });

test("at most one line per 1.2 s", () => {
  let q = enqueue(createQueue(), line("clash", 2), 0);
  q = enqueue(q, line("hurt", 1), 0);
  let r = release(q, 0);
  assert.equal(r.line.situation, "clash");
  q = r.queue;
  assert.equal(nextReleaseDelay(q, 100), 1100);
  r = release(q, 1199);
  assert.equal(r.line, null);
  r = release(r.queue, 1200);
  assert.equal(r.line.situation, "hurt");
  assert.equal(nextReleaseDelay(r.queue, 1300), null); // nothing waiting
});

test("the queue holds three and drops the lowest priority first", () => {
  let q = createQueue();
  q = enqueue(q, line("hurt", 1), 0);
  q = enqueue(q, line("clash", 2), 0);
  q = enqueue(q, line("combo5", 3), 0);
  q = enqueue(q, line("fullPower", 5), 0); // full: the priority-1 line goes
  assert.deepEqual(q.items.map((l) => l.situation).sort(), ["clash", "combo5", "fullPower"]);
  q = enqueue(q, line("dodge", 1), 0); // the new line is the lowest of all: it is the one dropped
  assert.equal(q.items.length, 3);
  assert.ok(!q.items.some((l) => l.situation === "dodge"));
  const out = [];
  let t = 0;
  for (let i = 0; i < 3; i++) {
    const r = release(q, t);
    out.push(r.line.situation);
    q = r.queue;
    t += 1200;
  }
  assert.deepEqual(out, ["fullPower", "combo5", "clash"]); // highest priority first
});

test("equal priorities drop the oldest first and show the earliest first", () => {
  let q = createQueue();
  for (const [i, s] of ["a", "b", "c", "d"].entries()) q = enqueue(q, line(s, 1), i);
  assert.deepEqual(q.items.map((l) => l.situation), ["b", "c", "d"]);
  assert.equal(release(q, 10).line.situation, "b");
});

test("the same situation is never repeated within 4 s, shown or still waiting", () => {
  let q = enqueue(createQueue(), line("clash", 2), 0);
  assert.equal(enqueue(q, line("clash", 2), 100).items.length, 1); // already waiting
  q = release(q, 0).queue;
  assert.equal(enqueue(q, line("clash", 2), 3999).items.length, 0); // shown 3.999 s ago
  assert.equal(enqueue(q, line("clash", 2), 4000).items.length, 1);
  assert.equal(enqueue(q, line("hurt", 1), 100).items.length, 1); // other situations are unaffected
});

test("a line that waited too long is dropped instead of shown", () => {
  let q = enqueue(createQueue(), line("clash", 2), 0);
  q = { ...q, lastEmitAt: 0 };
  assert.equal(release(q, COMMENTARY.staleMs + 1).line, null);
  assert.equal(release(q, COMMENTARY.staleMs + 1).queue.items.length, 0);
});

test("a result clears everything that was still waiting", () => {
  let q = createQueue();
  q = enqueue(q, line("windUp", 4), 0);
  q = enqueue(q, line("combo5", 3), 0);
  q = enqueue(q, line("victory", COMMENTARY.priority.result), 10);
  assert.deepEqual(q.items.map((l) => l.situation), ["victory"]);
  const r = release(q, 20);
  assert.equal(r.line.situation, "victory");
  assert.equal(r.queue.items.length, 0);
});
