using System;
using NUnit.Framework;
using UnityEngine;

namespace StrikeFit.Engagement.Tests
{
    public class RankLadderTests
    {
        [TestCase(0, "Rookie")]
        [TestCase(299, "Rookie")]
        [TestCase(300, "Brawler")]
        [TestCase(1199, "Brawler")]
        [TestCase(1200, "Warrior")]
        [TestCase(3499, "Warrior")]
        [TestCase(3500, "Guardian")]
        [TestCase(1000000, "Guardian")]
        [TestCase(-5, "Rookie")]
        public void Thresholds_MatchTheWebsite(int points, string rank) => Assert.AreEqual(rank, RankLadder.RankFor(points).name);

        [Test]
        public void SessionPoints_SwingsFullPowerVictoryHard()
        {
            Assert.AreEqual(0, RankLadder.SessionPoints(0, 0, false, false));
            Assert.AreEqual(30 + 2 * 10, RankLadder.SessionPoints(30, 10, false, false));          // a defeat: swings only
            Assert.AreEqual(30 + 20 + 40, RankLadder.SessionPoints(30, 10, true, false));          // a victory
            Assert.AreEqual(30 + 20 + 40 + 60, RankLadder.SessionPoints(30, 10, true, true));      // a Hard victory
            Assert.AreEqual(0, RankLadder.SessionPoints(-3, -1, false, true));                     // nonsense never subtracts
        }

        [Test]
        public void Progress_AndPointsToNext()
        {
            Assert.AreEqual(0f, RankLadder.Progress(0), 1e-6f);
            Assert.AreEqual(0.5f, RankLadder.Progress(150), 1e-6f);
            Assert.AreEqual(0f, RankLadder.Progress(300), 1e-6f);   // exactly on a threshold = the start of the new rung
            Assert.AreEqual(1f, RankLadder.Progress(5000), 1e-6f);  // top rank
            Assert.AreEqual(300, RankLadder.PointsToNext(0));
            Assert.AreEqual(1, RankLadder.PointsToNext(1199));
            Assert.AreEqual(-1, RankLadder.PointsToNext(3500));
            Assert.AreEqual("Warrior", RankLadder.NextRung(400).name);
            Assert.AreEqual("Guardian", RankLadder.NextRung(4000).name);
        }

        [Test]
        public void RankUp_FiresOnceAndNotForOldRanks()
        {
            Assert.IsTrue(RankLadder.IsRankUp(290, 310, 0));    // crossed Brawler
            Assert.IsFalse(RankLadder.IsRankUp(310, 330, 1));   // still Brawler
            Assert.IsFalse(RankLadder.IsRankUp(290, 310, 1));   // Brawler was already celebrated (a replayed fight)
            Assert.IsFalse(RankLadder.IsRankUp(1300, 1400, 0)); // Warrior reached elsewhere earlier: not this fight's rank up
            Assert.IsTrue(RankLadder.IsRankUp(1190, 3600, 1));  // a jump over a rung still fires (to Guardian)
        }
    }

    public class BestScoreTests
    {
        [Test]
        public void FirstFight_IsNeverANewBest()
        {
            Assert.AreEqual(BestScore.Kind.First, BestScore.Compare(true, 0, 820));
            Assert.AreEqual(820, BestScore.After(BestScore.Kind.First, 0, 820));
        }

        [Test]
        public void Compare_BelowTiedNew()
        {
            Assert.AreEqual(BestScore.Kind.Below, BestScore.Compare(true, 1340, 1220));
            Assert.AreEqual(BestScore.Kind.Tied, BestScore.Compare(true, 1340, 1340));
            Assert.AreEqual(BestScore.Kind.NewBest, BestScore.Compare(true, 1340, 1341));
            Assert.AreEqual(1341, BestScore.After(BestScore.Kind.NewBest, 1340, 1341));
            Assert.AreEqual(1340, BestScore.After(BestScore.Kind.Below, 1340, 1220));
        }

        [Test]
        public void OnlyDecidedFightsWithAScoreCount()
        {
            Assert.IsTrue(BestScore.Counts("victory", 10));
            Assert.IsTrue(BestScore.Counts("defeat", 10));
            Assert.IsFalse(BestScore.Counts("abandoned", 900));
            Assert.IsFalse(BestScore.Counts("victory", 0));
            Assert.AreEqual(BestScore.Kind.NotCounted, BestScore.Compare(false, 500, 900));
        }

        [Test]
        public void NextGoal_SaysHowFarToTheBest()
        {
            StringAssert.Contains("121 more points", BestScore.NextGoal(BestScore.Kind.Below, "Level 2", 1340, 1220, 50, "Warrior"));
            StringAssert.Contains("Level 2", BestScore.NextGoal(BestScore.Kind.Below, "Level 2", 1340, 1220, 50, "Warrior"));
            StringAssert.Contains("250 effort points to Warrior", BestScore.NextGoal(BestScore.Kind.NewBest, "Level 2", 1340, 1400, 250, "Warrior"));
            StringAssert.Contains("beat it again", BestScore.NextGoal(BestScore.Kind.NewBest, "Level 3", 2000, 2100, -1, "Guardian"));
            StringAssert.Contains("first score", BestScore.NextGoal(BestScore.Kind.First, "Level 1", 0, 600, 100, "Brawler"));
        }
    }

    public class DailyGoalTests
    {
        [Test]
        public void ForDay_IsDeterministic_OnePerTier()
        {
            int day = DailyGoalRules.DayNumber(new DateTime(2026, 10, 24));
            var a = DailyGoalRules.ForDay(day);
            var b = DailyGoalRules.ForDay(day);
            Assert.AreEqual(3, a.Length);
            for (int i = 0; i < 3; i++) Assert.AreEqual(a[i].key, b[i].key);
            Assert.AreEqual("easy", a[0].tier); Assert.AreEqual("medium", a[1].tier); Assert.AreEqual("stretch", a[2].tier);
            foreach (var g in a) { StringAssert.StartsWith("local:", g.key); StringAssert.EndsWith(":" + day, g.key); Assert.Greater(g.bonusXp, 0); Assert.Greater(g.target, 0f); }
        }

        [Test]
        public void ForDay_Rotates_AndUsesEveryLocalGoal()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            int changes = 0; string prev = null;
            for (int d = 9000; d < 9120; d++)
            {
                var set = DailyGoalRules.ForDay(d);
                string sig = set[0].title + set[1].title + set[2].title;
                if (prev != null && sig != prev) changes++;
                prev = sig;
                foreach (var g in set) seen.Add(g.title);
            }
            Assert.Greater(changes, 60, "the set should change on most days");
            Assert.AreEqual(DailyGoalRules.Easy.Length + DailyGoalRules.Medium.Length + DailyGoalRules.Stretch.Length, seen.Count);
        }

        [Test]
        public void Combine_EachMetric()
        {
            var live = new FightTally { fullSwings = 4, bestCombo = 6, ended = false, level = "Medium" };
            var won = new FightTally { fullSwings = 4, bestCombo = 6, ended = true, won = true, level = "Medium", damagePercent = 30f, activeMinutes = 2, kcal = 3.5f };
            var lost = won; lost.won = false;
            GoalDef G(GoalMetric m, float t, string level = null, float maxDmg = 100f) => new GoalDef { metric = m, target = t, level = level, maxDamagePercent = maxDmg };
            Assert.AreEqual(7f, DailyGoalRules.Combine(G(GoalMetric.FullSwings, 5), 3f, live));
            Assert.AreEqual(6f, DailyGoalRules.Combine(G(GoalMetric.BestCombo, 5), 2f, live));   // the best, not a sum
            Assert.AreEqual(8f, DailyGoalRules.Combine(G(GoalMetric.BestCombo, 5), 8f, live));
            Assert.AreEqual(0f, DailyGoalRules.Combine(G(GoalMetric.Wins, 1), 0f, live));       // not over yet
            Assert.AreEqual(1f, DailyGoalRules.Combine(G(GoalMetric.Wins, 1), 0f, won));
            Assert.AreEqual(0f, DailyGoalRules.Combine(G(GoalMetric.Wins, 1), 0f, lost));
            Assert.AreEqual(2f, DailyGoalRules.Combine(G(GoalMetric.Fights, 2), 1f, lost));
            Assert.AreEqual(1f, DailyGoalRules.Combine(G(GoalMetric.ClearLevel, 1, "Medium"), 0f, won));
            Assert.AreEqual(0f, DailyGoalRules.Combine(G(GoalMetric.ClearLevel, 1, "Hard"), 0f, won));
            Assert.AreEqual(1f, DailyGoalRules.Combine(G(GoalMetric.ClearDamage, 1, null, 50f), 0f, won));
            var hurt = won; hurt.damagePercent = 70f;
            Assert.AreEqual(0f, DailyGoalRules.Combine(G(GoalMetric.ClearDamage, 1, null, 50f), 0f, hurt));
            Assert.AreEqual(5f, DailyGoalRules.Combine(G(GoalMetric.ActiveMinutes, 6), 3f, won));
            Assert.AreEqual(4f, DailyGoalRules.Combine(G(GoalMetric.Kcal, 10), 0.5f, won), 1e-5f);
        }

        [Test]
        public void Met_AndFraction()
        {
            var g = new GoalDef { metric = GoalMetric.FullSwings, target = 5 };
            Assert.IsFalse(DailyGoalRules.Met(g, 4.99f));
            Assert.IsTrue(DailyGoalRules.Met(g, 5f));
            Assert.AreEqual(0.4f, DailyGoalRules.Fraction(g, 2f), 1e-6f);
            Assert.AreEqual(1f, DailyGoalRules.Fraction(g, 50f), 1e-6f);
        }

        [Test]
        public void ServerMetricNames_Map()
        {
            Assert.IsTrue(DailyGoalRules.TryMetric("fullSwings", out var m)); Assert.AreEqual(GoalMetric.FullSwings, m);
            Assert.IsTrue(DailyGoalRules.TryMetric("sessions", out m)); Assert.AreEqual(GoalMetric.Fights, m);
            Assert.IsTrue(DailyGoalRules.TryMetric("clearDamage", out m)); Assert.AreEqual(GoalMetric.ClearDamage, m);
            Assert.IsFalse(DailyGoalRules.TryMetric("somethingNew", out _));
        }
    }

    public class PerfectClashTests
    {
        const float W = 0.15f, Min = 1.2f;

        [Test]
        public void Window_JustBeforeTheHitFrame()
        {
            Assert.IsTrue(PerfectClashRule.IsPerfect(9.90f, 10f, W, true, false, 3f, Min));    // 0.10 s early
            Assert.IsTrue(PerfectClashRule.IsPerfect(10f, 10f, W, true, false, 3f, Min));      // on the hit frame
            Assert.IsTrue(PerfectClashRule.IsPerfect(9.85f, 10f, W, true, false, 3f, Min));    // the edge
            Assert.IsFalse(PerfectClashRule.IsPerfect(9.80f, 10f, W, true, false, 3f, Min));   // too early
            Assert.IsFalse(PerfectClashRule.IsPerfect(10.01f, 10f, W, true, false, 3f, Min));  // after it connected
        }

        [Test]
        public void Fairness_Gates()
        {
            Assert.IsFalse(PerfectClashRule.IsPerfect(9.95f, 10f, W, false, false, 3f, Min));  // not in the strike
            Assert.IsFalse(PerfectClashRule.IsPerfect(9.95f, 10f, W, true, true, 3f, Min));    // unblockable
            Assert.IsFalse(PerfectClashRule.IsPerfect(9.95f, 10f, W, true, false, 0.5f, Min)); // a held weapon, not a swing
            Assert.IsFalse(PerfectClashRule.IsPerfect(9.95f, -1f, W, true, false, 3f, Min));   // no hit frame known
            Assert.IsFalse(PerfectClashRule.IsPerfect(9.95f, 10f, 0f, true, false, 3f, Min));  // window switched off
            Assert.IsTrue(PerfectClashRule.IsPerfect(9.70f, 10f, 0.3f, true, false, 3f, Min)); // the window is the config value
        }

        [Test]
        public void OncePerAttack()
        {
            Assert.IsTrue(PerfectClashRule.FirstForAttack(5, 4));
            Assert.IsFalse(PerfectClashRule.FirstForAttack(5, 5));
        }
    }

    public class GlowAndProgressTests
    {
        [Test]
        public void Palettes_UnlockByRank()
        {
            Assert.IsTrue(GlowPalettes.Unlocked(0, 0));
            Assert.IsFalse(GlowPalettes.Unlocked(GlowPalettes.IndexOf("frost"), 1));
            Assert.IsTrue(GlowPalettes.Unlocked(GlowPalettes.IndexOf("frost"), 2));
            Assert.AreEqual(0, GlowPalettes.Effective("royal", 2));      // chosen but locked: Steel
            Assert.AreEqual(GlowPalettes.IndexOf("ember"), GlowPalettes.Effective("ember", 3));
            Assert.AreEqual(0, GlowPalettes.Effective("unknown", 3));
            Assert.AreEqual(GlowPalettes.All.Length - 1, GlowPalettes.Step(0, -1));
            Assert.AreEqual(0, GlowPalettes.Step(GlowPalettes.All.Length - 1, 1));
        }

        [Test]
        public void ProgressData_RoundTripsAndRollsTheDay()
        {
            var d = new ProgressData { effortPoints = 412, celebratedRank = 1, glowPalette = "ember" };
            d.RollDay(9000);
            d.SetBest("Medium", 1340); d.SetBest("Medium", 1400); d.SetBest("Easy", 600);
            d.fightsToday = 2;
            d.Goal("local:full5:9000", true).value = 3f;
            var back = JsonUtility.FromJson<ProgressData>(JsonUtility.ToJson(d));
            Assert.AreEqual(412, back.effortPoints);
            Assert.AreEqual(1400, back.BestOn("Medium"));
            Assert.AreEqual(600, back.BestOn("Easy"));
            Assert.AreEqual(0, back.BestOn("Hard"));
            Assert.AreEqual(3f, back.Goal("local:full5:9000", false).value);
            back.RollDay(9000);
            Assert.AreEqual(2, back.fightsToday, "same day: kept");
            back.RollDay(9001);
            Assert.AreEqual(0, back.fightsToday);
            Assert.IsNull(back.Goal("local:full5:9000", false));
            Assert.AreEqual(1400, back.BestOn("Medium"), "bests survive a new day");
            Assert.AreEqual("ember", back.glowPalette);
        }
    }
}
