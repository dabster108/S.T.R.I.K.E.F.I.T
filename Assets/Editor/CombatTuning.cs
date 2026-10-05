// CombatTuning.cs
// Purpose: The one place that holds the per-level fight tuning written onto the monster prefabs and level scenes:
//          personal-space clamp, MonsterDefense (awareness / reactive guard / guard meter / poise), the Level 3 boss
//          balance (single-hit cap, phase-shift pauses, enrage tempo), monster HP + spacing overrides, weapon damage,
//          hit-sound variants and CombatFeedback. Level3DualAxeBuilder calls ConfigureMonster(root, 3) so a boss rebuild
//          keeps these values. Run from the menu or Unity MCP; every number lands in a serialized field.
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
    static readonly float[] GuardChance = { 0.15f, 0.30f, 0.45f };
    static readonly float[] GuardChanceEnraged = { 0.15f, 0.30f, 0.50f }; // L3: +5 points while enraged (it is free less often)
    static readonly float[] MinGuardGap = { 6f, 4.5f, 3.5f };
    static readonly float[] MaxUptime = { 0.12f, 0.18f, 0.25f };
    static readonly float[] TimedGuardInterval = { 12f, 9f, 8f };
    // Guard meter / break
    static readonly int[] GuardBreakHits = { 5, 4, 3 };
    static readonly float[] StaggerSeconds = { 2.5f, 2.0f, 1.6f };
    // Hit clip time to its exit (s at speed 1): L1 1.60 x 0.70, L2 1.60 / 1.2 x 0.70, L3 1.05 x 0.95
    static readonly float[] HitClipToExit = { 1.12f, 0.93f, 1.0f };
    static readonly MonsterDefense.SwingTier[] FlinchTier = { MonsterDefense.SwingTier.Light, MonsterDefense.SwingTier.Medium, MonsterDefense.SwingTier.Full };
    static readonly string[] GuardBreakClip = { "Assets/Audio/SFX/Level2/L2_Slam_Impact.wav", "Assets/Audio/SFX/Level3/L3_Slam_Impact.wav", "Assets/Audio/SFX/Level2/L2_Slam_Impact.wav" };

    // ───────────── per-level scene values ─────────────
    static readonly float[] MonsterMaxHp = { 500f, 680f, 2900f };        // was 500 / 800 / 1500
    static readonly float[] PlayerMaxHp = { 0f, 0f, 1100f };             // 0 = table (500 / 800); L3 was 800
    static readonly float[] MonsterAttackRange = { 1.65f, 1.85f, 1.45f }; // horizontal m to the headset (L1 was 1.75, L2 was 1.55)

    // Level 3 boss
    public const float L3MaxSingleHitFraction = 0.06f;
    public const float L3EnrageTempo = 1.15f;          // shortest wind-up 0.525 s / 1.15 = 0.457 s >= 0.45 s
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
        ps.clampEnabled = true; ps.headClearance = 0.6f; ps.handClearance = 0.35f; ps.iterations = 4; ps.weaponHeadClearance = 0f; ps.weaponClearance = 0f;

        var d = root.GetComponent<MonsterDefense>();
        if (d == null) d = root.AddComponent<MonsterDefense>();
        d.turnToFacePlayer = true; d.turnSpeed = TurnSpeed[i];
        d.detectSpeed = 2.5f; d.detectRange = DetectRange[i]; d.detectConeAngle = 40f; d.guardTarget = Chest(root);
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
        if (ctrl != null) ctrl.maxSingleHitFraction = level == 3 ? L3MaxSingleHitFraction : 0f;

        var phases = root.GetComponent<BossPhaseController>();
        if (phases != null)
        {
            phases.phaseShiftPauses = true; phases.phaseShiftSeconds = L3PhaseShiftSeconds; phases.phaseShiftDamageMultiplier = L3PhaseShiftDamage;
            phases.resistClip = Clip("Assets/Audio/SFX/Level3/L3_AxeCatch.wav"); phases.resistVolume = 0.5f;
            phases.enrageTempo = L3EnrageTempo; phases.enrageCooldownScale = L3EnrageCooldownScale;
            phases.enrageRoarSeconds = L3PhaseShiftSeconds;
        }
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
        EditorUtility.SetDirty(lm);
        log.Append($"monster HP {MonsterMaxHp[l]}, attackRange {MonsterAttackRange[l]}; ");

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
                if (set.type == WeaponType.Mace) set.damageMultiplier = 1.05f;   // was 1.35
                if (set.type == WeaponType.Flail) set.damageMultiplier = 1.0f;   // was 1.1
                if (set.type == WeaponType.Katana) set.damageMultiplier = 1.12f; // was 0.9
                log.Append($"{set.type} x{set.damageMultiplier}; ");
            }
            EditorUtility.SetDirty(pc);
        }

        var bm = Object.FindAnyObjectByType<BattleManager>(FindObjectsInactive.Include);
        if (bm != null)
        {
            var fb = bm.GetComponent<CombatFeedback>();
            if (fb == null) fb = Undo.AddComponent<CombatFeedback>(bm.gameObject);
            log.Append("CombatFeedback on BattleSystems; ");
        }
        EditorSceneManager.MarkSceneDirty(lm.gameObject.scene);
        return log.ToString();
    }

    public static float HammerBaseDamage = 9.5f;
}
