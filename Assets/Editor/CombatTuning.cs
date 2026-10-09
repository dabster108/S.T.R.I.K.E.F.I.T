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
    static readonly float[] GuardChance = { 0.15f, 0.30f, 0.45f };
    static readonly float[] GuardChanceEnraged = { 0.15f, 0.30f, 0.50f }; // L3: +5 points while enraged (it is free less often)
    static readonly float[] MinGuardGap = { 6f, 4.5f, 3.5f };
    static readonly float[] MaxUptime = { 0.12f, 0.18f, 0.25f };
    static readonly float[] TimedGuardInterval = { 12f, 9f, 8f };
    // Guard meter / break
    static readonly int[] GuardBreakHits = { 5, 4, 3 };
    static readonly float[] GuardChancePerPhase = { 0f, 0f, 0.05f };   // L3: 45 % / 50 % / 55 % by boss phase
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
    public static Vector3 L3ChainChance = new Vector3(0.65f, 0.75f, 0.85f);   // per phase; 2-3 linked attacks
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
            ai.chainChanceByPhase = l3 ? L3ChainChance : Vector3.zero;
            ai.chainMaxAttacks = 3; ai.chainGap = new Vector2(0.35f, 0.6f);
            ai.telegraphJitter = l3 ? L3TelegraphJitter : 0f;
            ai.minTelegraph = l3 ? L3MinTelegraph : 0f;
            ai.unblockableWeightByPhase = l3 ? L3SlamWeightByPhase : Vector3.one;
        }

        // Clash colliders fitted to the visible weapon (+4 cm), weapon / body colliders on their own layers.
        MonsterWeaponFitter.Fit(root);

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
