const test = require("node:test");
const assert = require("node:assert/strict");
const core = require("../studyCore");
const lab = require("../lab");

test("SUS: all best answers score 100, all worst 0, neutral 50, and odd/even items score in opposite directions", () => {
  assert.equal(core.susScore([5, 1, 5, 1, 5, 1, 5, 1, 5, 1]), 100);
  assert.equal(core.susScore([1, 5, 1, 5, 1, 5, 1, 5, 1, 5]), 0);
  assert.equal(core.susScore(Array(10).fill(3)), 50);
  // worked example: odd items 4,4,4,4,4 -> 3 each = 15; even items 2,2,2,2,2 -> 3 each = 15; (15 + 15) * 2.5 = 75
  assert.equal(core.susScore([4, 2, 4, 2, 4, 2, 4, 2, 4, 2]), 75);
  // ((odd - 1) + (5 - even)) * 2.5 with mixed answers: odd = 5,3,4,2,5 -> 4+2+3+1+4 = 14; even = 1,2,3,4,2 -> 4+3+2+1+3 = 13; 27 * 2.5 = 67.5
  assert.equal(core.susScore([5, 1, 3, 2, 4, 3, 2, 4, 5, 2]), 67.5);
});

test("SUS rejects anything but ten whole answers from 1 to 5", () => {
  assert.equal(core.susScore([1, 2, 3]), null);
  assert.equal(core.susScore([1, 2, 3, 4, 5, 1, 2, 3, 4, 6]), null);
  assert.equal(core.susScore([1, 2, 3, 4, 5, 1, 2, 3, 4, 2.5]), null);
  assert.equal(core.susScore(null), null);
});

test("SUS bands: 68 is the benchmark, the edges belong to the higher band", () => {
  assert.equal(core.susBand(50.9).label, "Poor");
  assert.equal(core.susBand(51).label, "Below average");
  assert.equal(core.susBand(67.9).label, "Below average");
  assert.equal(core.susBand(68).label, "Good");
  assert.equal(core.susBand(80.3).label, "Excellent");
  assert.equal(core.susBand(90).label, "Best imaginable");
  assert.equal(core.susBand(100).label, "Best imaginable");
  assert.equal(core.susBand(null), null);
});

test("codes: P01..P20, lowest free one is issued, test codes are never issued", () => {
  assert.equal(core.ALL_CODES.length, 20);
  assert.equal(core.ALL_CODES[0], "P01");
  assert.equal(core.ALL_CODES[19], "P20");
  assert.equal(core.nextFreeCode([]), "P01");
  assert.equal(core.nextFreeCode(["P01", "P02", "P04"]), "P03");
  assert.equal(core.nextFreeCode(core.ALL_CODES), null);
  assert.ok(core.isCode("P07") && core.isCode("ZZTEST-P07"));
  assert.ok(!core.isCode("P7") && !core.isCode("p07") && !core.isCode("P077") && !core.isCode("ZZTEST-ann") && !core.isCode(null));
  assert.ok(core.isTestCode("ZZTEST-P01") && !core.isTestCode("P01"));
});

test("pre-play form: only the listed options pass", () => {
  const ok = { ageBand: "25-34", vrExperience: "never", weeklyExercise: "1-3h" };
  assert.deepEqual(core.validatePre(ok), { ok: true, value: ok });
  assert.equal(core.validatePre({ ...ok, ageBand: "30" }).ok, false);
  assert.equal(core.validatePre({ ...ok, vrExperience: undefined }).ok, false);
  assert.equal(core.validatePre(null).ok, false);
});

test("post-play form: five enjoyment items, effort 1-5, play again, ten SUS items; SUS is computed here, not trusted from the client", () => {
  const body = { enjoyment: [5, 4, 4, 3, 5], effortVsUsual: 4, playAgain: "yes", sus: [4, 2, 4, 2, 4, 2, 4, 2, 4, 2], susScore: 999 };
  const out = core.validatePost(body);
  assert.equal(out.ok, true);
  assert.equal(out.value.susScore, 75);
  assert.equal(core.validatePost({ ...body, enjoyment: [5, 4] }).ok, false);
  assert.equal(core.validatePost({ ...body, playAgain: "later" }).ok, false);
  assert.equal(core.validatePost({ ...body, effortVsUsual: 6 }).ok, false);
  assert.equal(core.validatePost({ ...body, sus: [1, 2, 3, 4, 5, 1, 2, 3, 4] }).ok, false);
  assert.equal(core.validatePost({ ...body, enjoyment: ["5", "4", "4", "3", "5"] }).ok, true, "numeric strings from a form are accepted");
});

const resp = (code, o = {}) => ({ code, enjoyment: [4, 4, 4, 4, 4], effortVsUsual: 3, playAgain: "yes", sus: [4, 2, 4, 2, 4, 2, 4, 2, 4, 2], susScore: 75, ...o });
const person = (code) => ({ code, ageBand: "25-34", vrExperience: "never", weeklyExercise: "1-3h", consentedAt: "2026-10-01T00:00:00.000Z" });
const fight = (player, o = {}) => ({ id: `s-${player}-${o.n || 1}`, player, level: "Easy", outcome: "victory", score: 100, hits: 10, durationSec: 60, startedAt: "2026-10-02T10:00:00.000Z", rpe: 13, avgSpeed: 3.5, swings: 10, fade: { status: "ok", fadePct: 12 }, ...o });

test("buildStudy: SUS mean and band, enjoyment mean, play-again share, per-participant fade, RPE points only for participants", () => {
  const people = ["P01", "P02", "P03"].map(person);
  const data = {
    participants: people,
    responses: [resp("P01", { susScore: 80, playAgain: "yes" }), resp("P02", { susScore: 60, playAgain: "maybe", enjoyment: [2, 2, 2, 2, 2] })],
  };
  const sessions = [fight("P01"), fight("P01", { n: 2, fade: { status: "ok", fadePct: 20 }, rpe: 15, avgSpeed: 4.5 }), fight("P02"), fight("someone-else"), fight("p03", { rpe: null })];
  const s = core.buildStudy(data, sessions);
  assert.deepEqual(s.sample, { participants: 3, withFights: 3, completed: 2 });
  assert.equal(s.sus.n, 2);
  assert.equal(s.sus.mean, 70);
  assert.equal(s.sus.band.label, "Good");
  assert.equal(s.sus.smallSample, true);
  assert.equal(s.enjoyment.mean, 3); // P01 mean 4, P02 mean 2
  assert.deepEqual(s.enjoyment.items, [3, 3, 3, 3, 3]);
  assert.deepEqual([s.playAgain.yes, s.playAgain.maybe, s.playAgain.no, s.playAgain.yesPct], [1, 1, 0, 50]);
  assert.equal(s.rpe.points.length, 3); // P03's fight has no RPE, "someone-else" is not a participant
  assert.ok(s.rpe.points.every((p) => /^P0[12]$/.test(p.code)));
  assert.equal(s.rpe.status, "insufficient"); // fewer than 8 pairs: no r
  const p1 = s.perParticipant.find((p) => p.code === "P01");
  assert.equal(p1.fadePct, 16); // mean of 12 and 20
  assert.equal(p1.sessions, 2);
  assert.equal(p1.sus, 80);
  const p3 = s.perParticipant.find((p) => p.code === "P03");
  assert.equal(p3.sus, null);
  assert.equal(p3.sessions, 1);
});

test("buildStudy with nobody has no means, not zeros", () => {
  const s = core.buildStudy({ participants: [], responses: [] }, []);
  assert.equal(s.sus.mean, null);
  assert.equal(s.sus.band, null);
  assert.equal(s.enjoyment.mean, null);
  assert.equal(s.playAgain.yesPct, null);
});

test("the Lab carries the study section only once someone has consented", () => {
  assert.equal(lab.buildLab([], Date.now(), null).study, null);
  assert.equal(lab.buildLab([], Date.now(), { participants: [], responses: [] }).study, null);
  const withOne = lab.buildLab([fight("P01")], Date.now(), { participants: [person("P01")], responses: [] });
  assert.equal(withOne.study.sample.participants, 1);
});

test("the study CSV joins fights to answers by participant code, repeats answers on every fight and carries no IP or device column", () => {
  const data = { participants: [person("P01"), person("P02")], responses: [resp("P01")] };
  const csv = core.studyCsvRows(data, [fight("P01"), fight("P01", { n: 2, level: "Hard" })], lab.csvCell).trim().split("\n");
  const head = csv[0].split(",");
  assert.deepEqual(head, core.STUDY_CSV_COLS);
  assert.equal(csv.length, 4); // header + 2 fights of P01 + 1 row for P02 who has no fight yet
  assert.ok(!head.some((c) => /(^|_)(ip|device|agent|address|name)(_|$)/.test(c)), "no identifying columns");
  const row1 = csv[1].split(",");
  assert.equal(row1[head.indexOf("participant")], "P01");
  assert.equal(row1[head.indexOf("sus_score")], "75");
  assert.equal(row1[head.indexOf("rpe")], "13");
  assert.equal(row1[head.indexOf("enjoyment_mean")], "4");
  assert.equal(csv[2].split(",")[head.indexOf("level")], "Hard");
  const p2 = csv[3].split(",");
  assert.equal(p2[head.indexOf("participant")], "P02");
  assert.equal(p2[head.indexOf("level")], "");
  assert.equal(p2[head.indexOf("sus_score")], "");
});
