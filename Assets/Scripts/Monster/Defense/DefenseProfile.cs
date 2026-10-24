// DefenseProfile.cs
// Purpose: Every tunable of the Level 2 / Level 3 defence and decision order, one asset per level (Assets/Settings/Defense):
//          swing prediction, reaction delay, the speed floor, block cooldown, block-rate targets and cap, guard meter, counter
//          window and riposte map, punish rules, side-bias window, spacing and strafing. A monster without a profile keeps the
//          old behaviour (Level 1).
// Dependencies: SwingPredictor (PredictorSettings)
// Attach to: nothing (ScriptableObject, referenced by MonsterTactics)

using UnityEngine;

namespace StrikeFit.Defense
{
    [CreateAssetMenu(menuName = "StrikeFit/Defense Profile", fileName = "DefenseProfile")]
    public class DefenseProfile : ScriptableObject
    {
        [Header("Prediction")]
        public PredictorSettings predictor = PredictorSettings.Default;
        [Tooltip("Radius (m) of the body capsule the projected path has to enter.")]
        public float bodyRadius = 0.42f;
        [Tooltip("A prediction needs at least this confidence (0..1) to be defended.")]
        [Range(0f, 1f)] public float minConfidence = 0.45f;

        [Header("Reaction (random per swing, shorter in later phases)")]
        [Tooltip("Seconds from the prediction to the guard starting to rise (random in this range).")]
        public Vector2 reactionDelay = new Vector2(0.12f, 0.26f);
        [Tooltip("Reaction delay multiplier per phase (1 / 2 / 3 = enraged on Level 3; HP bands 66 % / 33 % on Level 2).")]
        public Vector3 reactionScaleByPhase = new Vector3(1f, 0.92f, 0.85f);
        [Tooltip("Seconds the guard pose needs to reach the weapon; added to the reaction delay for the time-to-impact test.")]
        public float raiseTime = 0.05f;
        [Tooltip("Speed floor (m/s): a swing faster than this always beats the prediction. 0 = off.")]
        public float maxBlockableSpeed = 6.5f;

        [Header("Fairness")]
        [Tooltip("At most one predicted block per this many seconds.")]
        public float blockCooldown = 1.4f;
        [Tooltip("Block-rate target per phase (blocks / incoming swings). Always clamped to blockRateCap.")]
        public Vector3 blockRateByPhase = new Vector3(0.32f, 0.36f, 0.40f);
        [Tooltip("Hard maximum block rate: never exceeded in any phase, enraged included.")]
        [Range(0f, 1f)] public float blockRateCap = 0.40f;
        [Tooltip("How long a predicted guard stays up after the predicted impact (s).")]
        public float holdAfterImpact = 0.35f;
        [Tooltip("Shortest predicted guard (s).")]
        public float minGuardSeconds = 0.45f;

        [Header("Guard meter (every block drains it; empty = guard break)")]
        [Range(0f, 1f)] public float drainPerBlock = 0.28f;
        [Tooltip("Drain multiplier for a block on the same side as the previous one (spamming one direction breaks the guard sooner).")]
        public float sameSideDrainMultiplier = 2f;
        public float sameSideWindow = 10f;
        public float regenDelay = 3f;
        public float regenPerSecond = 0.06f;
        [Tooltip("Guard-break stagger (s): no guard, no attack, extra damage taken (the player's free combo).")]
        public float guardBreakStagger = 2.2f;
        [Tooltip("No guard for this long after the stagger ends; then the meter refills.")]
        public float guardLockoutAfterBreak = 2f;

        [Header("Counter (riposte after a block or parry)")]
        public float counterWindow = 0.4f;
        [Tooltip("Chance to riposte inside the window after a block or parry.")]
        [Range(0f, 1f)] public float counterChance = 0.55f;
        [Tooltip("Riposte per blocked side (attack-table row names): LEFT, RIGHT, HIGH, LOW / THRUST.")]
        public string counterLeft = "", counterRight = "", counterHigh = "", counterLow = "";

        [Header("Punish (whiff or retreat)")]
        [Tooltip("A swing that ended without a hit after passing within this distance (m) of the body is a whiff.")]
        public float whiffRange = 1.3f;
        [Tooltip("The player's head moving away faster than this (m/s) for retreatSeconds is a retreat.")]
        public float retreatSpeed = 0.45f;
        public float retreatSeconds = 0.2f;
        [Tooltip("A whiff / retreat can be punished for this long (s).")]
        public float punishWindow = 1.0f;
        [Tooltip("A punish may start this many seconds before the attack cooldown is over; the next attack waits that long longer, so " +
                 "the attack frequency does not change.")]
        public float punishMaxEarly = 0.8f;
        [Tooltip("Quick attack for a whiff: the fastest table row whose wind-up is at most this (s).")]
        public float quickAttackMaxWindUp = 0.65f;
        [Tooltip("Gap-closers for a retreat (row names). Rush rows of the table are used first when the distance fits their band.")]
        public string[] gapClosers = new string[0];

        [Header("Side bias")]
        [Tooltip("The zone the player used most in the last biasWindow seconds is read more readily.")]
        public float biasWindow = 10f;
        [Tooltip("Confidence added to a prediction on the most-used zone, times that zone's share.")]
        public float biasConfidence = 0.25f;
        [Tooltip("Reaction delay cut on the most-used zone, times that zone's share (0.3 = up to 30 % faster).")]
        [Range(0f, 0.6f)] public float biasReactionCut = 0.25f;

        [Header("Spacing and strafing (while the attack cooldown runs)")]
        public bool strafe = true;
        [Tooltip("Strafe target: up to this far (m) either side of the centre line.")]
        public float strafeWidth = 0.3f;
        [Tooltip("Seconds between strafe direction changes (random in this range).")]
        public Vector2 strafeInterval = new Vector2(1.4f, 2.6f);
        [Tooltip("Closer than this (m, horizontal) the monster steps back while it waits.")]
        public float spacingMin = 1.25f;
        public float spacingSpeed = 0.3f;

        [Header("Phases (monsters without a BossPhaseController)")]
        [Tooltip("HP fractions where phase 2 and phase 3 start.")]
        public Vector2 hpPhaseThresholds = new Vector2(0.66f, 0.33f);

        public float RateFor(int phase) => Mathf.Min(blockRateCap, phase <= 0 ? blockRateByPhase.x : phase == 1 ? blockRateByPhase.y : blockRateByPhase.z);
        public float ReactionScale(int phase) => phase <= 0 ? reactionScaleByPhase.x : phase == 1 ? reactionScaleByPhase.y : reactionScaleByPhase.z;

        public string CounterFor(SwingZone z)
        {
            switch (z)
            {
                case SwingZone.Left: return counterLeft;
                case SwingZone.Right: return counterRight;
                case SwingZone.High: return counterHigh;
                default: return counterLow;
            }
        }
    }
}
