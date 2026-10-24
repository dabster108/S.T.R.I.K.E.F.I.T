// ExpertGovernor.cs
// Purpose: The fairness limits of Expert mode and the rule of where it may run, as plain logic EditMode tests can check:
//            - MonsterAIModeRules.UseModel: only Level 3, only in Expert, never on Levels 1 and 2.
//            - ExpertGovernor: clamps the aim correction (degrees) and the lead (metres), counts damaging hits on the player over the
//              last 60 s and stops every prediction-driven choice while they exceed the Expert cap (Normal's rate + 20 % by default),
//              requires a punishable recovery after every prediction-aimed attack (no chained follow-up), and refuses to use a prediction
//              the model is unsure about (window not full, player nearly still).
//            - ExpertOutputs: which of the 36 outputs the game may read: the head only, at horizons up to maxHorizonSeconds (0.75 s by
//              default), plus the hands only when useHandPrediction is on (measured: the hand predictions were worse than "stays still").
// Dependencies: none
// Attach to: nothing

using UnityEngine;

namespace StrikeFit.ExpertAI
{
    public enum MonsterAIMode { Normal = 0, Expert = 1 }

    public static class MonsterAIModeRules
    {
        /// <summary>Level 3 (1-based) in Expert mode is the only place the model runs.</summary>
        public static bool UseModel(int level, MonsterAIMode mode) => level == 3 && mode == MonsterAIMode.Expert;
    }

    /// <summary>Which forecaster outputs the monster logic may read. Everything else reads as "no prediction" (zero offset).</summary>
    public static class ExpertOutputs
    {
        /// <summary>The furthest horizon index (0 = 0.25 s .. 3 = 1.0 s) at or below <paramref name="maxSeconds"/>; at least 0.</summary>
        public static int MaxHorizonIndex(float maxSeconds)
        {
            int last = 0;
            for (int i = 0; i < Forecast.Horizons; i++) if (Forecast.HorizonSeconds[i] <= maxSeconds + 1e-4f) last = i;
            return last;
        }

        public static int ClampHorizon(int horizon, float maxSeconds) => Mathf.Clamp(horizon, 0, MaxHorizonIndex(maxSeconds));

        /// <summary>May the game use point <paramref name="p"/> at <paramref name="horizon"/>? The head up to the limit; the hands only with
        /// useHandPrediction.</summary>
        public static bool MayRead(Forecast.Point p, int horizon, bool useHandPrediction, float maxSeconds) =>
            horizon >= 0 && horizon <= MaxHorizonIndex(maxSeconds) && (p == Forecast.Point.Head || useHandPrediction);
    }

    public sealed class ExpertGovernor
    {
        readonly float maxAimDeg, maxLead, hitsPerMinuteCap, minEnergy, maxHorizonSeconds;
        public bool UseHands { get; }
        readonly float[] hitTimes = new float[64];
        int hitHead, hitCount;
        public int AimedAttacks { get; private set; }
        public int BlockedByHitCap { get; private set; }

        public ExpertGovernor(float maxAimDegrees, float maxLeadMetres, float hitsPerMinuteCap, float minMotionEnergy,
                              float maxHorizonSeconds = 0.75f, bool useHandPrediction = false)
        {
            maxAimDeg = Mathf.Max(0f, maxAimDegrees); maxLead = Mathf.Max(0f, maxLeadMetres);
            this.hitsPerMinuteCap = Mathf.Max(0f, hitsPerMinuteCap); minEnergy = Mathf.Max(0f, minMotionEnergy);
            this.maxHorizonSeconds = maxHorizonSeconds; UseHands = useHandPrediction;
        }

        /// <summary>A horizon the game may read: never beyond maxHorizonSeconds.</summary>
        public int ClampHorizon(int horizon) => ExpertOutputs.ClampHorizon(horizon, maxHorizonSeconds);

        public float ClampAim(float desiredDegrees) => Mathf.Clamp(desiredDegrees, -maxAimDeg, maxAimDeg);

        public Vector3 ClampLead(Vector3 lead)
        {
            lead.y = 0f;
            return lead.sqrMagnitude > maxLead * maxLead ? lead.normalized * maxLead : lead;
        }

        public void RegisterHitOnPlayer(float now)
        {
            hitTimes[hitHead] = now; hitHead = (hitHead + 1) % hitTimes.Length;
            if (hitCount < hitTimes.Length) hitCount++;
        }

        public int HitsInLastMinute(float now)
        {
            int n = 0;
            for (int i = 0; i < hitCount; i++) if (now - hitTimes[i] <= 60f) n++;
            return n;
        }

        /// <summary>May a prediction change a decision now? Not while the model is unsure, nor while the player has already taken
        /// the Expert cap of hits in the last minute.</summary>
        public bool MayUsePrediction(float now, bool windowFull, float motionEnergy, bool healthy)
        {
            if (!healthy || !windowFull || motionEnergy < minEnergy) return false;
            if (HitsInLastMinute(now) >= hitsPerMinuteCap) { BlockedByHitCap++; return false; }
            return true;
        }

        public void NoteAimedAttack() => AimedAttacks++;

        /// <summary>An attack aimed with the prediction always leaves its recovery open: no chained follow-up after it.</summary>
        public static bool AllowChainAfter(bool attackWasPredictionAimed) => !attackWasPredictionAimed;
    }
}
