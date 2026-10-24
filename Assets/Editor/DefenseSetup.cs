// DefenseSetup.cs
// Purpose: Writes the Level 2 / Level 3 defence setup: one DefenseProfile asset per level (Assets/Settings/Defense) with every tunable
//          of the swing predictor, the decision order and the fairness rules, a MonsterTactics on the Level 2 and Level 3 monster
//          prefabs pointing at it, and the distinct guard poses on their MonsterGuardIK (the weapon turned per pose, the high guard
//          moved too). Level 1 gets no MonsterTactics (its behaviour does not change). CombatTuning.ConfigureAdaptiveGuard calls
//          Configure, so re-running the combat tuning keeps this.
//          Menu: Tools/Combat/Apply Defense Setup (keeps existing profile values) and Tools/Combat/Reset Defense Profiles.
// Dependencies: CombatTuning (prefab paths), MonsterTactics, MonsterGuardIK, MonsterDefense, StrikeFit.Defense.DefenseProfile
// Attach to: nothing (static, Editor only)

using StrikeFit.Defense;
using UnityEditor;
using UnityEngine;

public static class DefenseSetup
{
    public const string ProfileDir = "Assets/Settings/Defense";
    public const string L2Profile = ProfileDir + "/L2_CrimsonWarden_Defense.asset";
    public const string L3Profile = ProfileDir + "/L3_HarborTyrant_Defense.asset";

    [MenuItem("Tools/Combat/Apply Defense Setup")]
    public static void ApplyMenu() { Debug.Log(Apply(false)); }

    [MenuItem("Tools/Combat/Reset Defense Profiles")]
    public static void ResetMenu() { Debug.Log(Apply(true)); }

    /// <summary>Profiles (created, or reset to the defaults below when <paramref name="resetProfiles"/>) + the three prefabs.</summary>
    public static string Apply(bool resetProfiles)
    {
        var log = new System.Text.StringBuilder();
        string[] paths = { CombatTuning.L1Prefab, CombatTuning.L2Prefab, CombatTuning.L3Prefab };
        for (int l = 0; l < 3; l++)
        {
            if (l > 0) Profile(l + 1, resetProfiles);
            var root = PrefabUtility.LoadPrefabContents(paths[l]);
            try
            {
                Configure(root, l + 1);
                PrefabUtility.SaveAsPrefabAsset(root, paths[l]);
                var t = root.GetComponent<MonsterTactics>();
                var ik = root.GetComponentInChildren<MonsterGuardIK>(true);
                log.AppendLine($"L{l + 1}: tactics {(t != null ? t.profile.name : "none")}, guard IK {(ik != null ? $"intercept {ik.ikIntercept}, high {ik.ikOnHighPose}, orient {ik.orientWeapon}" : "none")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    /// <summary>Level 2 / 3: MonsterTactics + distinct guard poses. Level 1: none of it.</summary>
    public static void Configure(GameObject root, int level)
    {
        var d = root.GetComponent<MonsterDefense>();
        var t = root.GetComponent<MonsterTactics>();
        var an = root.GetComponentInChildren<Animator>(true);
        var ik = an != null ? an.GetComponent<MonsterGuardIK>() : null;
        if (level <= 1 || d == null)
        {
            if (t != null) Object.DestroyImmediate(t, true);
            if (ik != null) ik.orientWeapon = false;
            return;
        }
        if (t == null) t = root.AddComponent<MonsterTactics>();
        t.profile = Profile(level, false);
        t.logDecisions = true;
        // Every pose can be shown now: the side and low / thrust guards by the hand IK, the high guard too.
        d.sideGuards = true; d.lowGuard = true; d.swingClasses = true; d.zoneGuard = true;
        d.detectSpeed = t.profile.predictor.detectSpeed;
        if (ik != null)
        {
            ik.ikIntercept = true;
            ik.ikOnHighPose = true;
            // Level 2's rig follows a turned hand goal; Level 3's does not (its wrist limits / arm twist turned the axes far past the
            // pose, measured in Play mode), so Level 3 keeps the clip's hand rotation and shows the poses by where the axes go:
            // one axe out to the swing's side, both axes up for HIGH, both down for LOW.
            ik.orientWeapon = level == 2;
            ik.twinHighLow = level == 3;
            ik.highAnchor = new Vector3(0f, 1.0f, 0.45f);   // level over the head and in front of it, so the player sees the bar
            // Level 3 keeps the clip's hand rotation (axes pointing up from the fist), so its low guard aims the fists lower.
            ik.lowAnchor = level == 3 ? new Vector3(0f, 0.22f, 0.45f) : new Vector3(0f, 0.42f, 0.45f);
            ik.minHeight = level == 3 ? 0.2f : 0.35f;
            ik.sideAnchor = level == 3 ? new Vector3(0.45f, 0.72f, 0.55f) : new Vector3(0.45f, 0.68f, 0.45f);
            ik.highAxis = new Vector3(1f, 0.08f, 0.1f);
            ik.sideAxis = new Vector3(0.12f, 1f, 0.3f);
            ik.lowAxis = new Vector3(0.75f, -0.55f, 0.35f);
            ik.crossSideAxis = new Vector3(-0.7f, 0.7f, 0.25f);   // Level 2's one axe guarding the player's right leans across the body
            ik.twinSpread = 0.16f; ik.maxHandTurn = 900f;
            // A predicted guard has ~0.1-0.25 s between raise and impact: the hand must reach the pose in that time (2.5 m/s left the
            // high guard at chest height when the overhead blow arrived).
            ik.maxHandSpeed = 5f; ik.blendIn = 12f;
        }
    }

    /// <summary>The level's profile asset, created with the defaults when missing (or when reset).</summary>
    public static DefenseProfile Profile(int level, bool reset)
    {
        string path = level == 3 ? L3Profile : L2Profile;
        var p = AssetDatabase.LoadAssetAtPath<DefenseProfile>(path);
        bool fresh = p == null;
        if (fresh)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            if (!AssetDatabase.IsValidFolder(ProfileDir)) AssetDatabase.CreateFolder("Assets/Settings", "Defense");
            p = ScriptableObject.CreateInstance<DefenseProfile>();
            AssetDatabase.CreateAsset(p, path);
        }
        if (fresh || reset) { WriteDefaults(p, level); EditorUtility.SetDirty(p); }
        return p;
    }

    /// <summary>The tuned defaults. Level 2: 40 % block-rate cap; Level 3: 55 %, faster reactions in later phases.</summary>
    public static void WriteDefaults(DefenseProfile p, int level)
    {
        bool l3 = level == 3;
        p.predictor = PredictorSettings.Default;   // 1.2 m/s detection, 6-sample fit, 0.15-0.35 s look-ahead
        p.bodyRadius = 0.42f;
        p.minConfidence = l3 ? 0.4f : 0.45f;
        p.reactionDelay = new Vector2(0.12f, 0.26f);
        p.reactionScaleByPhase = l3 ? new Vector3(1f, 0.88f, 0.78f) : new Vector3(1f, 0.92f, 0.85f);
        p.raiseTime = 0.05f;
        p.maxBlockableSpeed = l3 ? 7f : 6f;
        p.blockCooldown = l3 ? 1.0f : 1.4f;
        p.blockRateByPhase = l3 ? new Vector3(0.45f, 0.50f, 0.55f) : new Vector3(0.32f, 0.36f, 0.40f);
        p.blockRateCap = l3 ? 0.55f : 0.40f;
        p.holdAfterImpact = 0.35f; p.minGuardSeconds = 0.45f;
        p.drainPerBlock = l3 ? 0.26f : 0.28f;
        // The rate cap spreads guarded blocks out (several seconds apart), so the same-side memory spans the bias window and the
        // meter refills slowly: spamming one side breaks the guard on the 3rd block, varied swings do not.
        p.sameSideDrainMultiplier = l3 ? 2.2f : 2.0f; p.sameSideWindow = 10f;
        p.regenDelay = 3f; p.regenPerSecond = 0.06f;
        p.guardBreakStagger = l3 ? 1.6f : 2.0f;          // the levels' existing stagger lengths
        p.guardLockoutAfterBreak = 2f;
        p.counterWindow = 0.4f;
        p.counterChance = l3 ? 0.7f : 0.55f;
        if (l3) { p.counterLeft = "Alternating Chops"; p.counterRight = "Backhand Double Chop"; p.counterHigh = "Twin Diagonal Hack"; p.counterLow = "Rending Double Strike"; }
        else { p.counterLeft = "Horizontal Sweep"; p.counterRight = "Backhand Cut"; p.counterHigh = "Overhead Chop"; p.counterLow = "Diagonal Slash"; }
        p.whiffRange = 1.3f; p.retreatSpeed = 0.45f; p.retreatSeconds = 0.2f; p.punishWindow = 1.0f; p.punishMaxEarly = 0.8f;
        p.quickAttackMaxWindUp = l3 ? 0.6f : 0.65f;
        p.gapClosers = l3 ? new string[0] : new[] { "Lunging Cleave", "Leaping Cleave" };   // Level 3 uses its Rush rows (Lunge Strike, Brute Rush)
        p.biasWindow = 10f; p.biasConfidence = 0.25f; p.biasReactionCut = 0.25f;
        p.strafe = true; p.strafeWidth = l3 ? 0.35f : 0.3f; p.strafeInterval = new Vector2(1.4f, 2.6f);
        p.spacingMin = 1.2f; p.spacingSpeed = 0.3f;
        p.hpPhaseThresholds = new Vector2(0.66f, 0.33f);
    }
}
