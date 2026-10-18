const test = require("node:test");
const assert = require("node:assert/strict");
const lab = require("../lab");
const outcomes = require("../outcomes");

const fadeOk = (fadePct) => ({ status: "ok", fadePct });
const row = (o) => ({
  id: "s", player: "A", level: "Easy", outcome: "victory", score: 100, hits: 10, durationSec: 60, startedAt: "2026-10-01T10:00:00.000Z",
  rpe: null, avgSpeed: 4, swings: 3, speeds: [3, 4, 5], fade: { status: "insufficient" }, ...o,
});

test("the level table uses the shared win-rate rule and reports abandoned fights apart", () => {
  const rows = [
    ...Array.from({ length: 10 }, () => row({ level: "Easy", outcome: "victory" })),
    ...Array.from({ length: 2 }, () => row({ level: "Easy", outcome: "defeat" })),
    ...Array.from({ length: 4 }, () => row({ level: "Easy", outcome: "abandoned", score: 0 })),
    ...Array.from({ length: 3 }, () => row({ level: "Hard", outcome: "victory" })),
    ...Array.from({ length: 5 }, () => row({ level: "Hard", outcome: "defeat" })),
  ];
  const out = lab.buildLab(rows, Date.parse("2026-10-18T00:00:00Z"));
  const easy = out.levels.find((l) => l.level === "Easy");
  const hard = out.levels.find((l) => l.level === "Hard");
  assert.equal(easy.winRate, outcomes.winRate(10, 2));
  assert.equal(easy.winRate, 83.3);
  assert.deepEqual([easy.victories, easy.defeats, easy.abandoned, easy.decided, easy.sessions], [10, 2, 4, 12, 16]);
  assert.equal(hard.winRate, 37.5);
  assert.equal(easy.avgScore, 100); // abandoned zero-score fights are not in the average
});

test("a level with no fights is listed, with no rate rather than 0%", () => {
  const out = lab.buildLab([row({ level: "Easy" })]);
  const medium = out.levels.find((l) => l.level === "Medium");
  assert.equal(medium.sessions, 0);
  assert.equal(medium.winRate, null);
  assert.equal(medium.avgScore, null);
  assert.equal(medium.avgSwingSpeed, null);
  assert.deepEqual(out.levels.map((l) => l.level), ["Easy", "Medium", "Hard"]);
});

test("level swing speed and fade average only the sessions that have them", () => {
  const out = lab.buildLab([
    row({ speeds: [2, 4], fade: fadeOk(-10) }),
    row({ speeds: [6], fade: fadeOk(20) }),
    row({ speeds: [], swings: 0, avgSpeed: null }),
  ]);
  const easy = out.levels[0];
  assert.equal(easy.avgSwingSpeed, 4);
  assert.equal(easy.swings, 3);
  assert.equal(easy.avgFadePct, 5);
  assert.equal(easy.fadeSessions, 2);
});

test("RPE scatter: empty state under 8 pairs, a Pearson r from 8", () => {
  const some = (n) => Array.from({ length: n }, (_, i) => row({ id: `s${i}`, rpe: 8 + i, avgSpeed: 2 + i * 0.4 }));
  assert.equal(lab.buildLab(some(7)).rpe.status, "insufficient");
  assert.equal(lab.buildLab(some(7)).rpe.r, null);
  const ok = lab.buildLab(some(8)).rpe;
  assert.equal(ok.status, "ok");
  assert.equal(ok.r, 1);
  assert.equal(ok.points.length, 8);
  assert.equal(lab.buildLab([row({ rpe: 9, avgSpeed: null })]).rpe.points.length, 0); // rated but no swing speed: not a pair
});

test("the payload carries the research note and the sample sizes", () => {
  const out = lab.buildLab([row({ player: "A", rpe: 10 }), row({ player: "B" })]);
  assert.equal(out.note, "Research prototype. Small sample. Correlation is not causation.");
  assert.deepEqual(out.sample, { sessions: 2, players: 2, sessionsWithSwingData: 2, sessionsRated: 1 });
  assert.equal(out.returnRate.windows.length, 3);
});

test("CSV has exactly the requested columns and no IP or device field", () => {
  assert.deepEqual(lab.CSV_COLS, ["player", "level", "outcome", "score", "hits", "avg_swing", "fade_pct", "duration_sec", "started_at"]);
  const csv = lab.toCsv([row({ player: "Kai", fade: fadeOk(-12.4), avgSpeed: 4.123, durationSec: 83.26 })]);
  const [head, line] = csv.trim().split("\n");
  assert.equal(head, "player,level,outcome,score,hits,avg_swing,fade_pct,duration_sec,started_at");
  assert.equal(line, "Kai,Easy,victory,100,10,4.12,-12.4,83.3,2026-10-01T10:00:00.000Z");
  assert.doesNotMatch(csv.toLowerCase(), /\bip\b|device|user.?agent|address/);
});

test("CSV leaves a figure blank when there is none, and keeps the header on an empty export", () => {
  const line = lab.toCsv([row({ avgSpeed: null, durationSec: 0, fade: { status: "insufficient" } })]).trim().split("\n")[1];
  assert.equal(line, "A,Easy,victory,100,10,,,,2026-10-01T10:00:00.000Z");
  assert.equal(lab.toCsv([]), "player,level,outcome,score,hits,avg_swing,fade_pct,duration_sec,started_at\n");
});

test("CSV quotes commas and quotes, and defuses spreadsheet formulas in typed player names", () => {
  assert.equal(lab.csvCell('Kai, "the" Great'), '"Kai, ""the"" Great"');
  assert.equal(lab.csvCell("=HYPERLINK(\"x\")"), '"\'=HYPERLINK(""x"")"');
  assert.equal(lab.csvCell("@sum"), "'@sum");
  assert.equal(lab.csvCell("-5"), "-5"); // a plain negative number is still a number
  assert.equal(lab.csvCell(null), "");
});
