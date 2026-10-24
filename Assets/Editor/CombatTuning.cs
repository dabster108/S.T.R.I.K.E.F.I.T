// CombatTuning.cs
// Purpose: The one place that holds the per-level fight tuning written onto the monster prefabs and level scenes:
//          personal-space clamp, MonsterDefense (awareness / reactive guard / guard meter / poise), the Level 3 boss
//          balance (single-hit cap, phase-shift pauses, enrage tempo), monster HP + spacing overrides, weapon damage,
//          hit-sound variants, CombatFeedback haptics, the breathing (PlayerExertionAudio) thresholds and levels, and the monster
//          weapon clash colliders (MonsterWeaponFitter: fitted to the visible mesh, own layers). Level3DualAxeBuilder calls
//          ConfigureMonster(root, 3) so a boss rebuild keeps these values. Run from the menu or Unity MCP; every number lands in
//          a serialized field.
// Dependencies: MonsterDefense, MonsterPersonalSpace, MonsterAI, MonsterController, BossPhaseController, LevelManager,
//               HitDetection, PlayerCombat, CombatFeedback

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CombatTuning
{
    public const string L1Prefab = "Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab";
    public const string L2Prefab = "Assets/Prefabs/Monsters/Monster_Medium_VampireLust.prefab";
    public const string L3Prefab = "Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab";

    // ───────────── per-level monster values (index 0 = Level 1) ─────────────
    // Awareness / detection
    static readonly float[] TurnSpeed = { 90f, 120f, 150f };            // deg/s while free
    static readonly float[] DetectRange = { 2.5f, 2.2f, 1.9f };         // m from the chest: swing speed x reaction delay + margin
    // Reactive guard (fairness limits from the brief)
    static readonly float[] ReactionDelay = { 0.35f, 0.25f, 0.18f };
    static readonly float[] GuardChance = { 0.15f, 0.35f, 0.50f };        // unchanged by the 2026-10-20 pass: the gain came from DetectSpeed (below), not from a higher chance
    static readonly float[] GuardChanceEnraged = { 0.15f, 0.35f, 0.50f }; // L3 phases add GuardChancePerPhase instead
    static readonly float[] MinGuardGap = { 6f, 3.5f, 2.5f };
    static readonly float[] MaxUptime = { 0.12f, 0.22f, 0.30f };
    static readonly float[] TimedGuardInterval = { 12f, 9f, 8f };
    // The slowest swing the guard notices (m/s). The game counts a swing from PlayerCombat.minSwingVelocity (1.2); the guard used to ignore everything
    // under 2.5, and real swings accelerate from rest, so it saw them only at the last moment (or never: a median Level 2 swing peaks near 2.1).
    // MonsterDefense uses max(detectSpeed, minSwingVelocity), so 1.2 means "every counted swing is seen from the start of the stroke".
    static readonly float[] DetectSpeed = { 2.5f, 1.2f, 1.2f };
    // Guard meter / break
    static readonly int[] GuardBreakHits = { 5, 4, 3 };
    static readonly float[] GuardChancePerPhase = { 0f, 0f, 0.05f };   // L3: +5 points per boss phase
    // How fast the guard learns the player's habit: swings of one class / side inside repeatWindow that count as a pattern, the guard chance each
    // repeat adds, and the most it can add. Level 3 notices a repeated side after two swings and defends it harder.
    static readonly int[] RepeatThreshold = { 3, 3, 2 };
    static readonly float[] AdaptiveStep = { 0.15f, 0.15f, 0.20f };
    static readonly float[] AdaptiveMaxBoost = { 0.15f, 0.15f, 0.22f };
    static readonly float[] FollowContact = { 0.5f, 0.5f, 0.65f };      // how far the guard hand goes from the pose anchor toward where the swing will land
    // Movement polish (L1 keeps the old behaviour): walk-in band so the monster approaches steadily, and a limit on how fast the personal-space
    // clamp may push the monster away (it used to snap up to 0.4 m in one frame).
    static readonly float[] ApproachBand = { 0f, 0.10f, 0.10f };
    static readonly float[] MaxPushSpeed = { 0f, 4f, 4f };
    static readonly float[] CounterChance = { 0f, 0.20f, 0.35f };
    static readonly float[] StaggerSeconds = { 2.5f, 2.0f, 1.6f };
    // Hit clip time to its exit (s at speed 1): L1 1.60 x 0.70, L2 1.60 / 1.2 x 0.70, L3 1.05 x 0.95
    static readonly float[] HitClipToExit = { 1.12f, 0.93f, 1.0f };
    static readonly MonsterDefense.SwingTier[] FlinchTier = { MonsterDefense.SwingTier.Light, MonsterDefense.SwingTier.Medium, MonsterDefense.SwingTier.Full };
    static readonly string[] GuardBreakClip = { "Assets/Audio/SFX/Level2/L2_Slam_Impact.wav", "Assets/Audio/SFX/Level3/L3_Slam_Impact.wav", "Assets/Audio/SFX/Level2/L2_Slam_Impact.wav" };

    // ───────────── per-level scene values ─────────────
    static readonly float[] MonsterMaxHp = { 500f, 680f, 5600f };        // L3 2900 -> 5600 (2026-10-08, TTK model); earlier 500 / 800 / 1500
    static readonly float[] PlayerMaxHp = { 0f, 0f, 1200f };             // 0 = table (500 / 800); L3 1100 -> 1200 (was 800 before that)
    static readonly float[] MonsterDamage = { 0f, 0f, 14f };             // 0 = table (8 / 14); L3 18 -> 14: chains carry the extra damage
    static readonly float[] MonsterCooldown = { 0f, 0f, 2.4f };          // 0 = table (3.2 / 2.2); L3 1.9 -> 2.4 (chains bypass it)
    static readonly float[] MonsterAttackRange = { 1.65f, 1.85f, 1.45f }; // horizontal m to the headset (L1 was 1.75, L2 was 1.55)

    // Level 3 boss
    public const float L3MaxSingleHitFraction = 0.06f;
    public const float L3EnrageTempo = 1.18f;          // +18 % (was 1.15); the 0.45 s telegraph floor (L3MinTelegraph) holds the shortest wind-up
    public const float L3Phase2Tempo = 1.08f;          // +8 % in phase 2 (was 1.0)
    public const float L3Phase2CooldownScale = 0.9f;   // phase 2 cooldown x0.9 (enraged keeps x0.7)
    public static Vector3 L3ChainChance = new Vector3(0.50f, 0.60f, 0.80f);   // per phase; 2-3 linked attacks (was 0.65 / 0.75 / 0.85: it attacked ~60 % of the time and could not guard)
    public const float L3TelegraphJitter = 0.15f, L3MinTelegraph = 0.45f;
    public static Vector3 L3SlamWeightByPhase = new Vector3(1f, 2f, 1f);     // must-dodge more often from phase 2 (enraged already uses its own weight 34)
    public const float L3BurstPerSecond = 1f / 76f;   // fastest possible kill about 76 s + phase pauses
    public const float L3MaceMul = 1.0f, L3FlailMul = 1.0f, L3KatanaMul = 1.32f;
    public const float L3EnrageCooldownScale = 0.7f;
    public const float L3PhaseShiftSeconds = 2.4f;
    public const float L3PhaseShiftDamage = 0.25f;

    static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);

    static Transform Chest(GameObject root)
    {
        var a = root.GetComponentInChildren<Animator>(true);
        if (a != null && a.isHuman)
        {
            var t = a.GetBoneTransform(HumanBodyBones.UpperChest);
            if (t == null) t = a.GetBoneTransform(HumanBodyBones.Chest);
            if (t == null) t = a.GetBoneTransform(HumanBodyBones.Spine);
            if (t != null) return t;
        }
        var ai = root.GetComponent<MonsterAI>();
        return ai != null ? ai.reactiveTarget : null;
    }

    /// <summary>Adds / updates the shared fight components on a monster root. level = 1..3.</summary>
    public static void ConfigureMonster(GameObject root, int level)
    {
        int i = Mathf.Clamp(level, 1, 3) - 1;
        var ai = root.GetComponent<MonsterAI>();

        var ps = root.GetComponent<MonsterPersonalSpace>();
        if (ps == null) ps = root.AddComponent<MonsterPersonalSpace>();
        ps.maxPushSpeed = MaxPushSpeed[i];
        ps.clampEnabled = true; ps.headClearance = 0.6f; ps.handClearance = 0.35f; ps.iterations = 4; ps.weaponHeadClearance = 0f; ps.weaponClearance = 0f;

        var d = root.GetComponent<MonsterDefense>();
        if (d == null) d = root.AddComponent<MonsterDefense>();
        d.turnToFacePlayer = true; d.turnSpeed = TurnSpeed[i];
        d.detectSpeed = DetectSpeed[i]; d.detectRange = DetectRange[i]; d.detectConeAngle = 40f; d.guardTarget = Chest(root);
        d.reactiveGuard = true; d.reactionDelay = ReactionDelay[i]; d.guardChance = GuardChance[i]; d.guardChanceEnraged = GuardChanceEnraged[i];
        d.minGuardGap = MinGuardGap[i]; d.guardDuration = new Vector2(0.8f, 1.2f); d.maxGuardUptime = MaxUptime[i];
        d.timedGuard = true; d.timedGuardInterval = TimedGuardInterval[i]; d.timedGuardJitter = 0.3f;
        d.highGuardTrigger = "Defend"; d.sideGuardTrigger = "Defend"; // one guard clip per monster: both directions share it
        d.guardFlashColor = new Color(0.78f, 0.9f, 1f); d.guardFlashSeconds = 0.12f;
        d.guardBreakEnabled = true; d.guardBreakHits = GuardBreakHits[i]; d.staggerSeconds = StaggerSeconds[i];
        d.staggerAnimSpeed = Mathf.Clamp(HitClipToExit[i] / StaggerSeconds[i], 0.1f, 1f);
        d.staggerDamageMultiplier = 1.25f; d.guardLockoutAfterBreak = 4f;
        d.guardBreakClip = Clip(GuardBreakClip[i]); d.guardBreakVolume = 1f;
        d.guardBreakFlashColor = Color.white; d.guardBreakFlashSeconds = 0.2f; d.showGuardMeter = true;
        d.flinchMinTier = FlinchTier[i]; d.flinchCooldown = 0.6f; d.flinchWindow = 0.6f;
        // Zone guard: HIGH / MID / LOW swings, guards covering zones by contact height (legs < 0.45, torso 0.45-0.8, head > 0.8).
        d.zoneGuard = true; d.legsTop = 0.45f; d.headBottom = 0.8f; d.torsoSplit = 0.62f; d.highSwingFrom = 0.62f; d.openingBonus = 0.1f;
        d.midGuardTrigger = "Defend"; d.lowGuardTrigger = "Defend"; // one guard clip per monster: all three guards play it
        d.blockPitch = new Vector3(1.18f, 1f, 0.82f);
        d.guardChancePerPhase = GuardChancePerPhase[i];
        if (GuardChancePerPhase[i] > 0f) d.guardChanceEnraged = GuardChance[i]; // the per-phase step replaces the old enraged bump
        // Counter after a guard that absorbed 2+ hits: L1 never, L2 20 %, L3 35 %; never under 0.45 s of telegraph.
        d.counterEnabled = CounterChance[i] > 0f; d.counterChance = CounterChance[i]; d.counterMinAbsorbed = 2; d.counterWithin = 0.5f; d.counterMinTelegraph = 0.45f;
        // Dodge: no sidestep / backstep clips exist in the project (see project_progress.md for the Mixamo list).
        d.dodgeEnabled = false; d.sidestepTrigger = ""; d.backstepTrigger = ""; d.dodgeCooldown = 8f; d.dodgeChance = 0.12f;

        if (ai != null)
        {
            // Every guard gets a cool raise flash + the block-raise sound (Level 1 had neither).
            ai.defendTintEnabled = true;
            ai.defendTintColor = new Color(0.45f, 0.65f, 1f);
            if (ai.defendBraceClip == null) ai.defendBraceClip = Clip("Assets/Audio/SFX/Level2/L2_Defend_Brace.wav");
            if (ai.defendBlockClip == null) ai.defendBlockClip = Clip("Assets/Audio/SFX/Level2/L2_Defend_Block.wav");
        }

        var ctrl = root.GetComponent<MonsterController>();
        if (ctrl != null)
        {
            ctrl.maxSingleHitFraction = level == 3 ? L3MaxSingleHitFraction : 0f;
            ctrl.maxDamagePerSecondFraction = level == 3 ? L3BurstPerSecond : 0f;
            ctrl.burstWindow = 4f; ctrl.burstExcessMultiplier = 0.2f;
        }
        if (ai != null)
        {
            bool l3 = level == 3;
            ai.approachHysteresis = ApproachBand[i];
            ai.chainChanceByPhase = l3 ? L3ChainChance : Vector3.zero;
            ai.chainMaxAttacks = 3; ai.chainGap = new Vector2(0.35f, 0.6f);
            ai.telegraphJitter = l3 ? L3TelegraphJitter : 0f;
            ai.minTelegraph = l3 ? L3MinTelegraph : 0f;
            ai.unblockableWeightByPhase = l3 ? L3SlamWeightByPhase : Vector3.one;
        }

        // Clash colliders fitted to the visible weapon (+4 cm), weapon / body colliders on their own layers.
        MonsterWeaponFitter.Fit(root);

        ConfigureAdaptiveGuard(root, level);

        var phases = root.GetComponent<BossPhaseController>();
        if (phases != null)
        {
            phases.phaseShiftPauses = true; phases.phaseShiftSeconds = L3PhaseShiftSeconds; phases.phaseShiftDamageMultiplier = L3PhaseShiftDamage;
            phases.resistClip = Clip("Assets/Audio/SFX/Level3/L3_AxeCatch.wav"); phases.resistVolume = 0.5f;
            phases.enrageTempo = L3EnrageTempo; phases.enrageCooldownScale = L3EnrageCooldownScale;
            phases.phase2Tempo = L3Phase2Tempo; phases.phase2CooldownScale = L3Phase2CooldownScale;
            phases.enrageRoarSeconds = L3PhaseShiftSeconds;
        }
    }

    // ───────────── adaptive guard (swing classes, poses, memory, hand IK) ─────────────
    // Opening cue: no dedicated sound exists. L1 / L2 reuse the Level 3 weak-point chime (not used in those levels); Level 3,
    // where that chime is the weak point's own sound, uses the sword-unsheathing ring (not used in Level 3).
    static readonly string[] OpeningClip = { "Assets/Audio/SFX/Level3/L3_WeakPoint_Hit.wav", "Assets/Audio/SFX/Level3/L3_WeakPoint_Hit.wav",
                                             "Assets/Audio/SFX/Monster/dragon-studio-sword-unsheathing-393851.mp3" };
    /// <summary>Side / low guards are shown by MonsterGuardIK (no side or low guard clip exists; both guard clips are left / right
    /// symmetric, so the Animator Mirror flag gives the same pose). Set false to ship the clip-only guard.</summary>
    public static bool GuardIK = true;
    // Per level, from the Play-mode look check (weapon samples inside the body, captures from the head and a 3/4 view):
    // L1: the guard clip already runs the long sword through the chest and the IK side guard made it worse -> clip only.
    // L2: side and low guards no deeper in the body than the clip's own guard -> IK on for both.
    // L3: low guard drove the raised axes through chest and head -> off; side guards on (each hand its own side).
    static readonly bool[] SideGuardIK = { false, true, true };
    static readonly bool[] LowGuardIK = { false, true, false };
    static readonly Vector3[] SideAnchor = { new Vector3(0.4f, 0.7f, 0.45f), new Vector3(0.4f, 0.7f, 0.45f), new Vector3(0.45f, 0.75f, 0.6f) };

    /// <summary>Swing classes, guard poses, adaptive memory, fairness extras, opening cue and the hand-IK guard layer.</summary>
    public static void ConfigureAdaptiveGuard(GameObject root, int level)
    {
        int i = Mathf.Clamp(level, 1, 3) - 1;
        var d = root.GetComponent<MonsterDefense>();
        if (d == null) return;
        d.reactionDelay = ReactionDelay[i]; d.guardChance = GuardChance[i]; d.guardChanceEnraged = GuardChanceEnraged[i];
        d.guardChancePerPhase = GuardChancePerPhase[i];
        d.minGuardGap = MinGuardGap[i]; d.guardDuration = new Vector2(0.8f, 1.2f); d.maxGuardUptime = MaxUptime[i];
        d.swingClasses = true; d.thrustAcrossRatio = 0.5f; d.overheadVertical = 0.8f; d.diagonalVertical = 0.35f; d.uppercutVertical = 0.6f;
        d.sideOverlap = 0.1f;
        d.sideGuards = GuardIK && SideGuardIK[i]; d.lowGuard = GuardIK && LowGuardIK[i];
        d.sideLeftGuardTrigger = d.sideRightGuardTrigger = d.lowPoseGuardTrigger = "Defend"; // one guard clip; the pose comes from the IK
        d.posePitch = new Vector4(1.18f, 1.06f, 0.95f, 0.82f); d.blockPitchJitter = 0.04f;
        d.adaptiveGuard = true; d.memoryLength = 6; d.repeatThreshold = RepeatThreshold[i]; d.repeatWindow = 8f;
        d.adaptiveStep = AdaptiveStep[i]; d.adaptiveMaxBoost = AdaptiveMaxBoost[i]; d.decayPerVariedSwing = 0.05f; d.decayPerSecond = 0.02f; d.maxGuardChance = 0.8f;
        d.maxBlocksInARow = 3; d.forcedOpeningWindow = 1.0f;
        d.openingClip = Clip(OpeningClip[i]); d.openingVolume = 0.7f; d.openingPitch = 1.15f;
        d.openingFlashColor = Color.white; d.openingFlashSeconds = 0.08f;

        // Reach: the predicted contact is clamped to where the monster's weapon can get (shoulder -> hand -> weapon middle).
        var an = root.GetComponentInChildren<Animator>(true);
        var ik = an != null ? an.GetComponent<MonsterGuardIK>() : null;
        if (an != null && an.isHuman)
        {
            if (ik == null) ik = an.gameObject.AddComponent<MonsterGuardIK>();
            var rh = an.GetBoneTransform(HumanBodyBones.RightHand); var lh = an.GetBoneTransform(HumanBodyBones.LeftHand);
            ik.rightWeapon = WeaponUnder(rh); ik.leftWeapon = level == 3 ? WeaponUnder(lh) : null;
            ik.twoWeapons = level == 3;
            ik.ikIntercept = GuardIK && (SideGuardIK[i] || LowGuardIK[i]);
            ik.ikOnHighPose = false;   // the guard clip is the high guard; IK put the blade into the head
            ik.maxHandSpeed = 2.5f; ik.reachFraction = 0.9f; ik.minForward = 0.3f; ik.minHeight = 0.35f;
            ik.maxCross = level == 3 ? 0.15f : 0.35f;   // one weapon crosses the body for the far side; Level 3 has a hand per side
            ik.keepClipHandRotation = true; ik.followContact = FollowContact[i];
            ik.highAnchor = new Vector3(0f, 0.95f, 0.35f); ik.sideAnchor = SideAnchor[i]; ik.lowAnchor = new Vector3(0f, 0.4f, 0.4f);
            ik.blendIn = 8f; ik.blendOut = 5f; ik.positionWeight = 1f;
            var sh = an.GetBoneTransform(HumanBodyBones.RightUpperArm); var el = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            float arm = sh != null && el != null && rh != null ? Vector3.Distance(sh.position, el.position) + Vector3.Distance(el.position, rh.position) : 0.6f;
            float weaponHalf = ik.rightWeapon != null ? WeaponMidDistance(rh, ik.rightWeapon) : 0.3f;
            var chest = d.guardTarget;
            float chestToShoulder = chest != null && sh != null ? Vector3.Distance(chest.position, sh.position) : 0.2f;
            d.guardReach = Mathf.Round((chestToShoulder + arm * 0.9f + weaponHalf) * 100f) / 100f;
            // IK Pass on the base layer, only where the guard IK is used (Level 2 already had it for TwoHandedWeaponIK).
            // A layer flag, not a state; it is never switched off here, because another IK script may need it.
            var ctrl = an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (ctrl != null && ik.ikIntercept && !ctrl.layers[0].iKPass)
            {
                var layers = ctrl.layers; layers[0].iKPass = true; ctrl.layers = layers;
                EditorUtility.SetDirty(ctrl);
            }
        }
        DefenseSetup.Configure(root, level);   // Levels 2-3: MonsterTactics + the distinct guard poses (Level 1: none)
    }

    static Transform WeaponUnder(Transform hand)
    {
        if (hand == null) return null;
        foreach (Transform t in hand)
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
                if (c.CompareTag("MonsterWeapon")) return t;
        return null;
    }

    static float WeaponMidDistance(Transform hand, Transform weapon)
    {
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var c in weapon.GetComponentsInChildren<Collider>(true))
        {
            if (!c.CompareTag("MonsterWeapon")) continue;
            Vector3 centre = c is BoxCollider b ? b.center : Vector3.zero;
            sum += c.transform.TransformPoint(centre); n++;
        }
        return n > 0 ? Vector3.Distance(hand.position, sum / n) : 0.3f;
    }

    [MenuItem("Tools/Combat/Apply Adaptive Guard To Monster Prefabs")]
    public static void ApplyAdaptiveGuardMenu() { Debug.Log(ApplyAdaptiveGuard()); }

    /// <summary>Writes only the adaptive-guard values (ConfigureAdaptiveGuard) onto the three prefabs.</summary>
    public static string ApplyAdaptiveGuard()
    {
        var log = new System.Text.StringBuilder();
        string[] paths = { L1Prefab, L2Prefab, L3Prefab };
        for (int l = 0; l < 3; l++)
        {
            var root = PrefabUtility.LoadPrefabContents(paths[l]);
            try
            {
                ConfigureAdaptiveGuard(root, l + 1);
                PrefabUtility.SaveAsPrefabAsset(root, paths[l]);
                var d = root.GetComponent<MonsterDefense>();
                var ik = root.GetComponentInChildren<MonsterGuardIK>(true);
                log.AppendLine($"L{l + 1}: chance {d.guardChance:0.00} (+{d.guardChancePerPhase:0.00}/phase), delay {d.reactionDelay:0.00} s, gap {d.minGuardGap} s, uptime {d.maxGuardUptime:0.00}, " +
                               $"reach {d.guardReach:0.00} m, side/low guards {d.sideGuards}/{d.lowGuard}, opening clip {(d.openingClip != null ? d.openingClip.name : "none")}, " +
                               $"IK {(ik != null ? $"{ik.ikIntercept} right={(ik.rightWeapon ? ik.rightWeapon.name : "none")} left={(ik.leftWeapon ? ik.leftWeapon.name : "none")}" : "missing")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    [MenuItem("Tools/Combat/Apply Tuning To Monster Prefabs")]
    public static void ApplyPrefabsMenu() { Debug.Log(ApplyPrefabs()); }

    public static string ApplyPrefabs()
    {
        var log = new System.Text.StringBuilder();
        string[] paths = { L1Prefab, L2Prefab, L3Prefab };
        for (int l = 0; l < 3; l++)
        {
            var root = PrefabUtility.LoadPrefabContents(paths[l]);
            try
            {
                ConfigureMonster(root, l + 1);
                PrefabUtility.SaveAsPrefabAsset(root, paths[l]);
                var d = root.GetComponent<MonsterDefense>();
                log.AppendLine($"{paths[l]}: MonsterDefense + MonsterPersonalSpace written (guard target {(d.guardTarget != null ? d.guardTarget.name : "none")}, stagger anim speed {d.staggerAnimSpeed:0.00}).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return log.ToString();
    }

    // ───────────── scenes ─────────────
    static AudioClip[] Clips(params string[] names)
    {
        var list = new System.Collections.Generic.List<AudioClip>();
        foreach (var n in names)
        {
            var c = Clip(n);
            if (c != null) list.Add(c);
        }
        return list.ToArray();
    }

    const string Impact = "Assets/Audio/SFX/Hits/bannythecoolio-impactful-damage-425132.mp3";
    const string Smash = "Assets/Audio/SFX/Hits/dragon-studio-hammer-smash-effect-382731.mp3";
    const string Slice = "Assets/Audio/SFX/Monster/dragon-studio-sword-slice-393847.mp3";
    const string Mace = "Assets/Audio/SFX/Level3/L3_Mace_Impact.wav";
    const string Flail = "Assets/Audio/SFX/Level3/L3_Flail_Impact.wav";
    const string Katana = "Assets/Audio/SFX/Level3/L3_Katana_Hit.wav";

    /// <summary>Applies the scene-side tuning to the open level scene (does not save).</summary>
    public static string ApplyOpenScene()
    {
        var lm = Object.FindAnyObjectByType<LevelManager>(FindObjectsInactive.Include);
        if (lm == null) return "no LevelManager";
        string scene = lm.gameObject.scene.name;
        int l = scene.Contains("Level3") ? 2 : scene.Contains("Level2") ? 1 : 0;
        var log = new System.Text.StringBuilder($"{scene}: ");
        Undo.RecordObject(lm, "Combat tuning");
        lm.monsterMaxHealthOverride = MonsterMaxHp[l];
        lm.monsterAttackRangeOverride = MonsterAttackRange[l];
        lm.playerMaxHealthOverride = PlayerMaxHp[l];
        lm.monsterDamageOverride = MonsterDamage[l];
        lm.monsterAttackCooldownOverride = MonsterCooldown[l];
        EditorUtility.SetDirty(lm);
        log.Append($"monster HP {MonsterMaxHp[l]}, attackRange {MonsterAttackRange[l]}, damage {(MonsterDamage[l] > 0f ? MonsterDamage[l].ToString() : "table")}, cooldown {(MonsterCooldown[l] > 0f ? MonsterCooldown[l].ToString() : "table")}, player HP {(PlayerMaxHp[l] > 0f ? PlayerMaxHp[l].ToString() : "table")}; ");

        foreach (var hd in Object.FindObjectsByType<HitDetection>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(hd, "Combat tuning");
            hd.varyHitSounds = true; hd.pitchJitter = 0.06f;
            string n = hd.transform.parent != null ? hd.transform.parent.name + "/" + hd.name : hd.name;
            if (n.Contains("Hammer"))
            {
                hd.baseDamage = HammerBaseDamage; // was 12; the front-hit bonus now fires (see project_progress.md)
                hd.hitVariants = Clips(Impact, Mace, Flail); hd.heavyHitVariants = Clips(Smash, Mace, Impact);
            }
            else if (n.Contains("Sword"))
            {
                hd.hitVariants = Clips(Slice, Katana, Impact); hd.heavyHitVariants = Clips(Impact, Slice, Katana);
            }
            else if (n.Contains("Mace"))
            {
                hd.hitVariants = Clips(Mace, Impact, Flail); hd.heavyHitVariants = Clips(Smash, Mace, Impact);
            }
            else if (n.Contains("Flail"))
            {
                hd.hitVariants = Clips(Flail, Mace, Impact); hd.heavyHitVariants = Clips(Impact, Flail, Smash);
            }
            else if (n.Contains("Katana"))
            {
                hd.hitVariants = Clips(Katana, Slice, Impact); hd.heavyHitVariants = Clips(Slice, Katana, Impact);
            }
            EditorUtility.SetDirty(hd);
            log.Append($"{n}: {hd.hitVariants.Length}+{hd.heavyHitVariants.Length} variants; ");
        }

        var pc = Object.FindAnyObjectByType<PlayerCombat>(FindObjectsInactive.Include);
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0)
        {
            Undo.RecordObject(pc, "Combat tuning");
            foreach (var set in pc.weaponSets)
            {
                if (set.type == WeaponType.Mace) set.damageMultiplier = L3MaceMul;     // 1.35 -> 1.05 -> 1.0
                if (set.type == WeaponType.Flail) set.damageMultiplier = L3FlailMul;   // 1.1 -> 1.0
                if (set.type == WeaponType.Katana) set.damageMultiplier = L3KatanaMul; // 0.9 -> 1.12 -> 1.32
                log.Append($"{set.type} x{set.damageMultiplier}; ");
            }
            EditorUtility.SetDirty(pc);
        }

        var bm = Object.FindAnyObjectByType<BattleManager>(FindObjectsInactive.Include);
        if (bm != null)
        {
            var fb = bm.GetComponent<CombatFeedback>();
            if (fb == null) fb = Undo.AddComponent<CombatFeedback>(bm.gameObject);
            Undo.RecordObject(fb, "Combat tuning");
            // A clash is clearly stronger than any body hit: 1.0 for 0.15 s + 0.45 tail on the striking hand, 0.3 echo on the other;
            // the strongest body hit is 0.75 for 0.09 s.
            fb.fullPulse = new Vector2(0.75f, 0.09f);
            fb.clashPulse = new Vector2(1f, 0.15f);
            fb.clashTail = new Vector2(0.45f, 0.10f);
            fb.clashOtherHand = new Vector2(0.3f, 0.10f);
            EditorUtility.SetDirty(fb);
            log.Append("CombatFeedback on BattleSystems (clash 1.0/0.15 s + tail, full hit 0.75); ");
        }

        var ex = Object.FindAnyObjectByType<PlayerExertionAudio>(FindObjectsInactive.Include);
        if (ex != null)
        {
            Undo.RecordObject(ex, "Combat tuning");
            var fast = AssetDatabase.LoadAssetAtPath<AudioClip>(FastBreathLeveled);
            if (fast != null) ex.fastClip = fast;
            ex.fastGain = 1f;                       // levelled clip: no script DSP gain
            ex.startVolume = BreathStartVolume; ex.maxVolume = BreathHeavyCap; ex.fastMaxVolume = BreathFastCap;
            ex.exertionWindow = BreathWindow;
            ex.swingsForFullEffort = BreathSwingsForFull[l];
            ex.sourcePriority = 64; ex.logTierChanges = true;
            EditorUtility.SetDirty(ex);
            log.Append($"breathing: fast clip {(fast != null ? fast.name : "MISSING")}, caps {BreathHeavyCap}/{BreathFastCap}, start {BreathStartVolume}, window {BreathWindow} s, {BreathSwingsForFull[l]} weighted swings for full effort; ");
        }
        EditorSceneManager.MarkSceneDirty(lm.gameObject.scene);
        return log.ToString();
    }

    public static float HammerBaseDamage = 9.5f;

    // ───────────── breathing (PlayerExertionAudio), from the headset telemetry of 2026-10-08 ─────────────
    // Real weighted swing attempts per second (effort rise in the 'effort' telemetry): L2 1.44 / 1.65 (two sessions), L3 1.22;
    // L1 has no effort telemetry yet (ASSUMPTION 1.2). Window 30 s: medium (0.28) after 0.28 N / R = about 11 s and fast (0.70)
    // after 0.70 N / R = about 27 s of steady swinging, with N = 38.6 R.
    public const string FastBreathLeveled = "Assets/Audio/SFX/Player/breathingfast_leveled.wav";
    static readonly float[] BreathSwingsForFull = { 46f, 60f, 47f };   // was 19 / 18 / 20
    public static float BreathWindow = 30f;
    // Levels (whole-clip RMS at the cap): heavy -21.7 dB clip x 0.34 = -31 dBFS; fast levelled -23.7 dB x 0.42 = -31 dBFS.
    // Music sits at -45 dBFS, hits / attacks -6 to -24 dBFS.
    public static float BreathStartVolume = 0.22f, BreathHeavyCap = 0.34f, BreathFastCap = 0.42f;
}
