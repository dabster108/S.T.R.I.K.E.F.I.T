// ExpertAIConfig.cs
// Purpose: Every tunable of Level 3's Expert mode (the trained motion forecaster), next to the Level 3 defence profile
//          (Assets/Settings/Defense): the model files, sampling and inference rates, the per-frame layer budget and the fallback
//          limit, the dominant-hand and grip mapping, the Unity <-> dataset axis mapping (chosen by ExpertMappingProbe, UNVERIFIED on
//          real arms), which outputs the game may read (head only up to maxHorizonSeconds unless useHandPrediction), the uncertainty gate, the attack-side / delay / aim / lead rules, the defence early warning and the fairness
//          governor.
// Dependencies: Unity.InferenceEngine (ModelAsset), AxisMapping
// Attach to: nothing (ScriptableObject, referenced by ExpertMotionPredictor / MonsterExpertBrain)

using Unity.InferenceEngine;
using UnityEngine;

namespace StrikeFit.ExpertAI
{
    public enum HandSide { Right = 0, Left = 1 }

    [CreateAssetMenu(menuName = "StrikeFit/Expert AI Config", fileName = "ExpertAIConfig")]
    public class ExpertAIConfig : ScriptableObject
    {
        [Header("Model (Assets/Models/ExpertAI)")]
        public ModelAsset model;
        public TextAsset norm;

        [Header("Timing")]
        [Tooltip("Samples per second into the 20-frame window (the model was trained at 20 Hz).")]
        public float sampleRateHz = 20f;
        [Tooltip("Inferences per second (never every frame).")]
        public float inferenceHz = 10f;
        [Tooltip("Model layers run per frame: the 378-layer unrolled GRU is spread over several frames.")]
        public int layersPerFrame = 128;
        [Tooltip("Work budget per inference (ms, all its frames together). Logged against; the hard limit is below.")]
        public float budgetMs = 1.5f;
        [Tooltip("Moving average (ms) above which Expert switches itself off for the rest of the fight.")]
        public float maxAverageMs = 3f;

        [Header("Inputs")]
        [Tooltip("The dataset's \"controlling hand\" is the player's dominant hand.")]
        public HandSide dominantHand = HandSide.Right;
        [Tooltip("ASSUMPTION: the dataset's Grip flag (always 1 in the recordings) is the grip button held above this value; the game's " +
                 "weapons are always held, so it reads 1 almost always, which matches the training data.")]
        [Range(0f, 1f)] public float gripThreshold = 0.5f;
        [Tooltip("Unity -> dataset axes. Chosen by ExpertMappingProbe (synthetic motion test). UNVERIFIED on real arm movement.")]
        public AxisMapping mapping = new AxisMapping(false, 1f, 1f);
        [Tooltip("Set by the mapping probe: the mapping that passed the synthetic test, and how.")]
        [TextArea] public string mappingEvidence = "";

        [Header("Which outputs the game may read")]
        [Tooltip("Furthest horizon (s) the monster logic reads (0.25 / 0.5 / 0.75 / 1.0 exist). Measured in ExpertHarness (Captures/ExpertAI/" +
                 "L3_expert_60s.txt): the HEAD prediction beat \"stays still\" up to 0.75 s and not at 1.0 s.")]
        [Range(0.25f, 1f)] public float maxHorizonSeconds = 0.75f;
        [Tooltip("Read the predicted HAND positions (the guard's early warning). Off by default: in the same measurement the hand " +
                 "predictions were worse than assuming the hand stays still, so with this off the guard works exactly as in Normal.")]
        public bool useHandPrediction = false;

        [Header("Uncertainty gate")]
        [Tooltip("Below this mean speed (m/s) of head and hands over the window the player is nearly still: the prediction is not used.")]
        public float minMotionEnergy = 0.15f;

        [Header("Attack choice and timing")]
        [Tooltip("Horizon (index: 0 = 0.25 s, 1 = 0.5 s, 2 = 0.75 s, 3 = 1.0 s) used for where the player is going. Never beyond maxHorizonSeconds.")]
        [Range(0, 3)] public int aimHorizon = 1;
        [Tooltip("Predicted sideways head movement (m) from which the player counts as dodging to a side.")]
        public float dodgeSideways = 0.18f;
        [Tooltip("Hit arc (deg, half-angle) from which a table row counts as a sweep that covers a dodge.")]
        public float wideArcDegrees = 70f;
        [Tooltip("Predicted approach (m toward the monster over the aim horizon) that counts as a lunge / closing in.")]
        public float closingMetres = 0.2f;
        [Tooltip("Longest a due strike is held back for a player who is closing in (s).")]
        public float maxStrikeDelay = 0.25f;
        [Tooltip("Random extra hold (s, 0..this) so the timing is less predictable; always inside maxStrikeDelay.")]
        public float delayJitter = 0.08f;

        [Header("Aim (melee wind-up, Rush lane, thrown axes)")]
        [Tooltip("Largest yaw correction toward the predicted position at the start of a wind-up (deg).")]
        public float maxAimDegrees = 25f;
        [Tooltip("Largest lead (m) a Rush or a thrown axe is aimed ahead of the player.")]
        public float maxLeadMetres = 0.6f;

        [Header("Defence early warning (only with useHandPrediction)")]
        [Tooltip("Predicted hand movement toward the monster at 0.25 s (m) from which the swing predictor's confidence is raised.")]
        public float warnMinDisplacement = 0.12f;
        [Tooltip("Largest confidence added (0..1). The reaction delay, speed floor, cooldown, guard meter and block-rate cap still apply.")]
        [Range(0f, 0.5f)] public float warnMaxBoost = 0.2f;

        [Header("Fairness governor")]
        [Tooltip("Damaging hits per minute on the player in NORMAL mode (measured with ExpertHarness, Normal run).")]
        public float normalHitsPerMinute = 10f;
        [Tooltip("Expert may land at most this multiple of Normal's hits per minute (1.2 = +20 %).")]
        public float expertHitsAllowance = 1.2f;

        public float HitsPerMinuteCap => normalHitsPerMinute * expertHitsAllowance;
    }
}
