// DefensePolicyTests.cs
// Purpose: EditMode tests for the Level 2 / 3 decision order (DefensePolicy), the hard block-rate cap (BlockRateLimiter, DefenseProfile
//          rates), the guard meter and the side bias.
// Dependencies: StrikeFit.DefenseCore, NUnit
// Attach to: nothing (Test Runner, EditMode)

using NUnit.Framework;
using UnityEngine;

namespace StrikeFit.Defense.Tests
{
    public class DefensePolicyTests
    {
        /// <summary>Everything allowed: a confident, early, slow swing, a full meter, cooldown and credit ready, every other rule ready.</summary>
        static DecisionInput AllReady() => new DecisionInput
        {
            predicted = true, confidence = 0.9f, minConfidence = 0.45f,
            timeToImpact = 0.33f, reactionDelay = 0.2f, raiseTime = 0.05f,
            swingSpeed = 3f, maxBlockableSpeed = 6f,
            guardMeter = 1f, blockCooldownReady = true, rateCreditAvailable = true,
            counterWindowOpen = true, counterReady = true,
            punishOpportunity = true, punishReady = true,
            tableReady = true
        };

        [Test]
        public void Rule1_CommittedAttackIsNeverInterrupted()
        {
            var i = AllReady(); i.committed = true;
            Assert.AreEqual(Decision.Committed, DefensePolicy.Decide(i, out var why));
            Assert.AreEqual(DefendRefusal.Committed, why, "a predicted swing during an attack is refused as committed");
        }

        [Test]
        public void Rule2_DefendBeatsCounterPunishAndTable()
        {
            Assert.AreEqual(Decision.Defend, DefensePolicy.Decide(AllReady(), out var why));
            Assert.AreEqual(DefendRefusal.None, why);
        }

        [Test]
        public void Rule3_CounterWhenTheSwingCannotBeDefended()
        {
            var i = AllReady(); i.confidence = 0.1f;
            Assert.AreEqual(Decision.Counter, DefensePolicy.Decide(i, out var why));
            Assert.AreEqual(DefendRefusal.LowConfidence, why);
        }

        [Test]
        public void Rule3_CounterBeatsPunishAndTable()
        {
            var i = AllReady(); i.predicted = false;
            Assert.AreEqual(Decision.Counter, DefensePolicy.Decide(i, out _));
        }

        [Test]
        public void Rule4_PunishBeatsTable()
        {
            var i = AllReady(); i.predicted = false; i.counterWindowOpen = false;
            Assert.AreEqual(Decision.Punish, DefensePolicy.Decide(i, out _));
            i.counterWindowOpen = true; i.counterReady = false;   // window open but no fitting riposte
            Assert.AreEqual(Decision.Punish, DefensePolicy.Decide(i, out _));
        }

        [Test]
        public void Rule5_TableOtherwise_ThenNothing()
        {
            var i = AllReady(); i.predicted = false; i.counterWindowOpen = false; i.punishOpportunity = false;
            Assert.AreEqual(Decision.Table, DefensePolicy.Decide(i, out _));
            i.tableReady = false;
            Assert.AreEqual(Decision.None, DefensePolicy.Decide(i, out _));
        }

        [Test]
        public void BusyMonsterStartsNothing()
        {
            var i = AllReady(); i.busyOther = true;
            Assert.AreEqual(Decision.None, DefensePolicy.Decide(i, out var why));
            Assert.AreEqual(DefendRefusal.Busy, why);
        }

        [Test]
        public void DefendConditions_InOrder()
        {
            var i = AllReady(); i.swingSpeed = 6.5f;
            Assert.AreEqual(DefendRefusal.TooFast, DefensePolicy.CanDefend(i), "faster than the speed floor beats the prediction");
            i = AllReady(); i.timeToImpact = 0.2f;   // < 0.2 reaction + 0.05 raise
            Assert.AreEqual(DefendRefusal.TooLate, DefensePolicy.CanDefend(i));
            i = AllReady(); i.guardMeter = 0f;
            Assert.AreEqual(DefendRefusal.GuardEmpty, DefensePolicy.CanDefend(i));
            i = AllReady(); i.blockCooldownReady = false;
            Assert.AreEqual(DefendRefusal.Cooldown, DefensePolicy.CanDefend(i));
            i = AllReady(); i.rateCreditAvailable = false;
            Assert.AreEqual(DefendRefusal.RateCap, DefensePolicy.CanDefend(i));
            i = AllReady(); i.timeToImpact = 0.25f;  // exactly reaction + raise: still in time
            Assert.AreEqual(DefendRefusal.None, DefensePolicy.CanDefend(i));
        }

        // ───────────────────────── block-rate cap ─────────────────────────
        [TestCase(0.40f)]
        [TestCase(0.55f)]
        [TestCase(0.33f)]
        public void BlockRateNeverExceedsTheRate_AtAnyPoint(float rate)
        {
            var lim = new BlockRateLimiter();
            for (int n = 1; n <= 1000; n++)
            {
                lim.OnIncomingSwing(rate);
                lim.TryBlock();   // the monster tries to block every single swing
                Assert.LessOrEqual(lim.Blocks, Mathf.FloorToInt(rate * n + 1e-3f), "after " + n + " swings");
            }
            Assert.AreEqual(rate, lim.Rate, 0.01f, "a monster that always tries reaches the rate, not more");
        }

        [Test]
        public void FirstBlockNeedsEnoughSwings()
        {
            var lim = new BlockRateLimiter();
            lim.OnIncomingSwing(0.4f); Assert.IsFalse(lim.TryBlock());
            lim.OnIncomingSwing(0.4f); Assert.IsFalse(lim.TryBlock());
            lim.OnIncomingSwing(0.4f); Assert.IsTrue(lim.TryBlock(), "ceil(1 / 0.4) = 3rd swing");
        }

        [Test]
        public void BankHoldsAtMostOneBlock()
        {
            var lim = new BlockRateLimiter();
            for (int n = 0; n < 50; n++) lim.OnIncomingSwing(0.5f);   // 50 swings never blocked
            Assert.IsTrue(lim.TryBlock());
            Assert.IsFalse(lim.TryBlock(), "unused credit does not pile up into a wall of blocks");
        }

        [Test]
        public void ProfileRatesAreClampedToTheCap_EnragedIncluded()
        {
            var p = ScriptableObject.CreateInstance<DefenseProfile>();
            p.blockRateByPhase = new Vector3(0.45f, 0.50f, 0.80f);   // an enraged target above the cap
            p.blockRateCap = 0.55f;
            Assert.AreEqual(0.45f, p.RateFor(0), 1e-5f);
            Assert.AreEqual(0.50f, p.RateFor(1), 1e-5f);
            Assert.AreEqual(0.55f, p.RateFor(2), 1e-5f, "higher in the enraged phase only up to the cap");
            Object.DestroyImmediate(p);
        }

        [Test]
        public void PhaseResetStartsTheBankAgain()
        {
            var lim = new BlockRateLimiter();
            lim.OnIncomingSwing(0.55f); lim.OnIncomingSwing(0.55f);
            lim.Reset();
            Assert.AreEqual(0, lim.Swings);
            Assert.IsFalse(lim.CanBlock);
        }

        // ───────────────────────── guard meter + bias ─────────────────────────
        [Test]
        public void SameSideSpamBreaksTheGuardSoonerThanMixedSides()
        {
            var same = new GuardMeter(); int blocksSame = 0; float t = 0f;
            while (true) { blocksSame++; t += 1f; if (same.Drain(SwingZone.Left, t, 0.28f, 1.6f, 4f)) break; }
            var mixed = new GuardMeter(); int blocksMixed = 0; t = 0f;
            var zones = new[] { SwingZone.Left, SwingZone.Right, SwingZone.High, SwingZone.Low };
            while (true) { t += 1f; if (mixed.Drain(zones[blocksMixed++ % 4], t, 0.28f, 1.6f, 4f)) break; }
            Assert.AreEqual(3, blocksSame, "0.28 + 0.448 + 0.448");
            Assert.AreEqual(4, blocksMixed, "0.28 x 4 = 1.12: varied swings need one more block");
            var spaced = new GuardMeter();
            Assert.IsFalse(spaced.Drain(SwingZone.Left, 0f, 0.28f, 1.6f, 4f));
            Assert.IsFalse(spaced.Drain(SwingZone.Left, 10f, 0.28f, 1.6f, 4f), "same side but outside the window: normal drain");
            Assert.AreEqual(1f - 0.56f, spaced.Value, 1e-4f);
        }

        [Test]
        public void MeterRegeneratesOnlyAfterTheDelay()
        {
            var m = new GuardMeter();
            m.Drain(SwingZone.Right, 0f, 0.5f, 1f, 4f);
            m.Tick(1f, 1f, 2.5f, 0.2f); Assert.AreEqual(0.5f, m.Value, 1e-4f);
            m.Tick(3f, 1f, 2.5f, 0.2f); Assert.AreEqual(0.7f, m.Value, 1e-4f);
        }

        [Test]
        public void BiasPicksTheMostUsedZoneInTheWindow()
        {
            var b = new SideBias();
            b.Add(SwingZone.Right, 0f); b.Add(SwingZone.Right, 0.5f); b.Add(SwingZone.Right, 1f);   // old: outside the window at t = 12
            b.Add(SwingZone.Left, 5f); b.Add(SwingZone.Left, 6f); b.Add(SwingZone.High, 7f);
            var z = b.Most(12f, 10f, out float share);
            Assert.AreEqual(SwingZone.Left, z);
            Assert.AreEqual(2f / 3f, share, 1e-4f);
        }
    }
}
