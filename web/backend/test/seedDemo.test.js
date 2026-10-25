const test = require("node:test");
const assert = require("node:assert/strict");
const { DEMO } = require("../scripts/seed-demo");
const { assertSafeTarget, cliOptions, runFight } = require("../scripts/simulate-live");

test("the demo plan has the three players with 4 to 5 fights each, on valid levels, spread over the last ten days", () => {
  assert.deepEqual(DEMO.map((d) => d.player), ["Dikshanta", "Gaurav", "Pratyush"]);
  for (const d of DEMO) {
    assert.ok(d.fights.length >= 4 && d.fights.length <= 5, d.player);
    assert.ok(d.skill >= 0.6 && d.skill <= 1.3);
    for (const [level, outcome, seconds, daysAgo] of d.fights) {
      assert.ok(["Easy", "Medium", "Hard"].includes(level));
      assert.ok(["victory", "defeat"].includes(outcome));
      assert.ok(seconds >= 30 && seconds <= 130);
      assert.ok(daysAgo > 0 && daysAgo < 10);
    }
  }
});

test("only the demo script may use a name without ZZTEST, and a remote host still needs ALLOW_PROD_DB", () => {
  assert.throws(() => assertSafeTarget("http://localhost:4000", "Dikshanta", {}), /ZZTEST/);
  assert.doesNotThrow(() => assertSafeTarget("http://localhost:4000", "Dikshanta", {}, { allowAnyName: true }));
  assert.throws(() => assertSafeTarget("https://x.example", "Dikshanta", {}, { allowAnyName: true }), /ALLOW_PROD_DB/);
  assert.doesNotThrow(() => assertSafeTarget("https://x.example", "Dikshanta", { ALLOW_PROD_DB: "1" }, { allowAnyName: true }));
});

test("the name exemption only comes from code: the command line drops it and runFight refuses a real name without it", async () => {
  const args = cliOptions(["--player", "Dikshanta", "--allow-any-name", "--level", "Hard"]);
  assert.equal(args.allowAnyName, undefined);
  assert.equal(args.player, "Dikshanta");
  assert.equal(args.level, "Hard");
  // the safety check runs before any request, so these never touch the network
  await assert.rejects(runFight({ url: "http://localhost:4000", player: "Dikshanta", quiet: true }), /ZZTEST/);
  await assert.rejects(runFight({ url: "http://localhost:4000", player: "Dikshanta", allowAnyName: "true", quiet: true }), /ZZTEST/);
});
