// DefensePolicy.cs
// Purpose: The monster's decision order, shared by Levels 2 and 3, as plain functions so EditMode tests can check it:
//            1. COMMITTED  - an attack the monster has started (telegraph, strike, recovery) is never interrupted; recovery is the
//                            player's punish window, so nothing is guarded there either.
//            2. DEFEND     - a predicted swing with enough confidence, enough time to impact (more than the reaction delay plus
//                            the time to raise the guard), not faster than the speed floor, guard meter above zero, the block
//                            cooldown over and block-rate credit available: guard on the predicted side.
//            3. COUNTER    - inside the window right after a block or parry: a fast riposte from the side that was blocked.
//            4. PUNISH     - the player whiffed a swing or backed away: a quick attack or a gap-closer.
//            5. TABLE      - otherwise the weighted attack table (MonsterAI), with spacing and strafing while it waits.
//          Plus the fairness helpers: BlockRateLimiter (hard maximum block rate), GuardMeter (every block drains it, the same
//          side drains more, empty = guard break), SideBias (the side the player used most in the last N seconds).
// Dependencies: none
// Attach to: nothing

using UnityEngine;

namespace StrikeFit.Defense
{
    public enum Decision { None = 0, Committed = 1, Defend = 2, Counter = 3, Punish = 4, Table = 5 }

    /// <summary>Why a predicted swing was not defended (verification, telemetry).</summary>
    public enum DefendRefusal
    {
        None = 0, NoPrediction, Committed, LowConfidence, TooLate, TooFast, GuardEmpty, Cooldown, RateCap, Busy
    }

    public struct DecisionInput
    {
        public bool committed;           // attack wind-up / strike / recovery, or a chain in between
        public bool busyOther;           // stagger, phase shift, entrance, flinch: nothing new starts
        public bool predicted;           // a swing whose projected path meets the body
        public float confidence, minConfidence;
        public float timeToImpact, reactionDelay, raiseTime;
        public float swingSpeed, maxBlockableSpeed;
        public float guardMeter;         // 0..1
        public bool blockCooldownReady;
        public bool rateCreditAvailable;
        public bool counterWindowOpen;   // a block / parry happened less than counterWindow ago and the riposte roll succeeded
        public bool counterReady;        // a side-matched fast attack exists and nothing blocks it
        public bool punishOpportunity;   // whiff or retreat, still fresh
        public bool punishReady;         // a fitting attack exists and the cooldown allows it
        public bool tableReady;          // MonsterAI may pick from the table now (cooldown over, in range)
    }

    public static class DefensePolicy
    {
        /// <summary>The single decision for this frame, in priority order.</summary>
        public static Decision Decide(in DecisionInput i, out DefendRefusal refusal)
        {
            refusal = DefendRefusal.None;
            if (i.committed) { if (i.predicted) refusal = DefendRefusal.Committed; return Decision.Committed; }
            if (i.busyOther) { if (i.predicted) refusal = DefendRefusal.Busy; return Decision.None; }
            if (i.predicted)
            {
                refusal = CanDefend(i);
                if (refusal == DefendRefusal.None) return Decision.Defend;
            }
            if (i.counterWindowOpen && i.counterReady) return Decision.Counter;
            if (i.punishOpportunity && i.punishReady) return Decision.Punish;
            if (i.tableReady) return Decision.Table;
            return Decision.None;
        }

        /// <summary>Rule 2's conditions, in the order they are checked.</summary>
        public static DefendRefusal CanDefend(in DecisionInput i)
        {
            if (!i.predicted) return DefendRefusal.NoPrediction;
            if (i.confidence < i.minConfidence) return DefendRefusal.LowConfidence;
            if (i.maxBlockableSpeed > 0f && i.swingSpeed > i.maxBlockableSpeed) return DefendRefusal.TooFast;
            if (i.timeToImpact + 1e-4f < i.reactionDelay + i.raiseTime) return DefendRefusal.TooLate;
            if (i.guardMeter <= 0f) return DefendRefusal.GuardEmpty;
            if (!i.blockCooldownReady) return DefendRefusal.Cooldown;
            if (!i.rateCreditAvailable) return DefendRefusal.RateCap;
            return DefendRefusal.None;
        }
    }

    /// <summary>Hard maximum block rate: every incoming swing adds `rate` credit, a block costs 1, and the bank holds less than two
    /// blocks (at most 1 + rate), so unused credit never turns into a wall of blocks. Blocks can therefore never exceed
    /// rate x incoming swings at any point of the fight (the first block needs ceil(1 / rate) swings), and a monster that tries every
    /// time blocks exactly at the rate. Reset at each phase so each phase keeps its own rate.</summary>
    public sealed class BlockRateLimiter
    {
        public float Credit { get; private set; }
        public int Swings { get; private set; }
        public int Blocks { get; private set; }
        public float Rate => Swings > 0 ? (float)Blocks / Swings : 0f;

        public void Reset() { Credit = 0f; Swings = 0; Blocks = 0; }

        /// <param name="rate">This phase's block rate, already clamped to the level's cap.</param>
        public void OnIncomingSwing(float rate)
        {
            Swings++;
            float r = Mathf.Clamp01(rate);
            Credit = Mathf.Min(1f + r, Credit + r);
        }

        public bool CanBlock => Credit >= 1f - 1e-4f;

        public bool TryBlock()
        {
            if (!CanBlock) return false;
            Credit -= 1f; Blocks++;
            return true;
        }
    }

    /// <summary>Guard meter 0..1. A block drains `drain` (x sameSideMultiplier when it is the same zone as the previous block
    /// within sameSideWindow s); after regenDelay s without a block it refills at regenPerSecond. Empty = guard break.</summary>
    public sealed class GuardMeter
    {
        public float Value { get; private set; } = 1f;
        public SwingZone LastZone { get; private set; } = SwingZone.None;
        float lastDrainAt = -999f;

        public void Reset() { Value = 1f; LastZone = SwingZone.None; lastDrainAt = -999f; }
        public void Empty() { Value = 0f; }
        public void Refill() { Value = 1f; LastZone = SwingZone.None; }

        /// <returns>True when this block emptied the meter (guard break).</returns>
        public bool Drain(SwingZone zone, float now, float drain, float sameSideMultiplier, float sameSideWindow)
        {
            bool same = zone != SwingZone.None && zone == LastZone && now - lastDrainAt <= sameSideWindow;
            Value = Mathf.Max(0f, Value - drain * (same ? sameSideMultiplier : 1f));
            LastZone = zone; lastDrainAt = now;
            return Value <= 0f;
        }

        public void Tick(float now, float dt, float regenDelay, float regenPerSecond)
        {
            if (Value <= 0f || Value >= 1f || now - lastDrainAt < regenDelay) return;
            Value = Mathf.Min(1f, Value + regenPerSecond * dt);
        }
    }

    /// <summary>The player's recent swing zones (fixed ring of 32): which zone was used most in the last `window` seconds.</summary>
    public sealed class SideBias
    {
        const int N = 32;
        readonly SwingZone[] zone = new SwingZone[N];
        readonly float[] time = new float[N];
        int head, count;

        public void Clear() { head = 0; count = 0; }

        public void Add(SwingZone z, float now)
        {
            zone[head] = z; time[head] = now;
            head = (head + 1) % N;
            if (count < N) count++;
        }

        /// <summary>Most used zone in the window and its share of the swings in it (0 when none).</summary>
        public SwingZone Most(float now, float window, out float share)
        {
            int total = 0; int l = 0, r = 0, h = 0, lo = 0, th = 0;
            for (int i = 0; i < count; i++)
            {
                if (now - time[i] > window) continue;
                total++;
                switch (zone[i]) { case SwingZone.Left: l++; break; case SwingZone.Right: r++; break; case SwingZone.High: h++; break; case SwingZone.Low: lo++; break; case SwingZone.Thrust: th++; break; }
            }
            share = 0f;
            if (total == 0) return SwingZone.None;
            SwingZone best = SwingZone.Left; int b = l;
            if (r > b) { best = SwingZone.Right; b = r; }
            if (h > b) { best = SwingZone.High; b = h; }
            if (lo > b) { best = SwingZone.Low; b = lo; }
            if (th > b) { best = SwingZone.Thrust; b = th; }
            share = (float)b / total;
            return best;
        }
    }
}
