// Level2EntranceBuilder.cs
// Purpose: Sets up the Level 2 "drop from the dark" entrance (MonsterEntrance, style DropFromAbove). Idempotent:
//          - Monster_L2_Axe.controller: triggers EntranceDrop / EntranceRoar, states EntranceDrop (Mutant Jump, height baked)
//            and EntranceRoar (L3_Roar, humanoid), reached only through real transitions: Idle -> EntranceDrop (trigger, starts
//            at the jump's apex through the transition offset) -> EntranceRoar (trigger) -> Idle (exit time), plus
//            EntranceDrop / EntranceRoar -> Idle on the Idle trigger. No Any State transition is added.
//          - M_L2_EntranceMist.mat: alpha-blended URP particle material (the shader already ships), soft-glow texture.
//          - Monster_Medium_VampireLust.prefab: MonsterEntrance (DropFromAbove) wired to the body's emission, the axe fire, the
//            right hand, the monster's sfx AudioSource and existing Level 2 sounds (no new audio).
// Run: Tools/Combat/Apply Level 2 Entrance, or Level2EntranceBuilder.Apply() over Unity MCP.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class Level2EntranceBuilder
{
    const string ControllerPath = "Assets/Animations/Monster/Level2/Monster_L2_Axe.controller";
    const string PrefabPath = "Assets/Prefabs/Monsters/Monster_Medium_VampireLust.prefab";
    const string JumpFbx = "Assets/Animations/Monster/Mutant Jump Attack.fbx";
    const string RoarClip = "Assets/Animations/Monster/Level3/L3_Roar.anim";
    const string FireMat = "Assets/VFX/Level2/M_L2_FireAdditive.mat";
    const string MistMat = "Assets/VFX/Level2/M_L2_EntranceMist.mat";
    const string SoftGlow = "Assets/VFX/Level2/T_L2_SoftGlow.png";
    // Existing Level 2 sounds (no dedicated entrance audio exists): growl, landing impact, roar.
    const string Growl = "Assets/Audio/SFX/Level2/L2_Heavy_Growl.wav";
    const string Land = "Assets/Audio/SFX/Level2/L2_Slam_Impact.wav";
    const string Roar = "Assets/Audio/SFX/Level2/L2_Special_FireRoar.wav";
    // Measured on the vampire (preview scene): apex 1.0 s (hips 2.0 m above standing), lands 1.6 s, risen 2.9 s, of 3.70 s.
    public const float ApexTime = 1.0f, LandTime = 1.6f, RiseEndTime = 2.9f;
    public static float GrowlVolume = 0.5f, LandVolume = 0.6f, RoarVolume = 0.6f;

    [MenuItem("Tools/Combat/Apply Level 2 Entrance")]
    public static void ApplyMenu() { Debug.Log(Apply()); }

    public static string Apply()
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine(Controller());
        log.AppendLine(Material());
        log.AppendLine(Prefab());
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static AnimationClip Jump()
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(JumpFbx))
            if (o is AnimationClip c && c.name == "MutantJump_HeightBaked") return c;
        return null;
    }

    static string Controller()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var jump = Jump();
        var roar = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoarClip);
        if (ctrl == null || jump == null || roar == null) return "controller / clips missing";
        foreach (var p in new[] { "EntranceDrop", "EntranceRoar" })
        {
            bool has = false;
            foreach (var q in ctrl.parameters) if (q.name == p) has = true;
            if (!has) ctrl.AddParameter(p, AnimatorControllerParameterType.Trigger);
        }
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState idle = null, drop = null, roarSt = null;
        foreach (var cs in sm.states)
        {
            if (cs.state.name == "Idle") idle = cs.state;
            if (cs.state.name == "EntranceDrop") drop = cs.state;
            if (cs.state.name == "EntranceRoar") roarSt = cs.state;
        }
        if (idle == null) return "no Idle state";
        if (drop == null) drop = sm.AddState("EntranceDrop", new Vector3(-320f, -120f, 0f));
        if (roarSt == null) roarSt = sm.AddState("EntranceRoar", new Vector3(-320f, -40f, 0f));
        drop.motion = jump; drop.speed = 1f; drop.writeDefaultValues = idle.writeDefaultValues;
        roarSt.motion = roar; roarSt.speed = 1f; roarSt.writeDefaultValues = idle.writeDefaultValues;

        // Rebuild only the transitions this builder owns.
        foreach (var t in idle.transitions) if (t.destinationState == drop) idle.RemoveTransition(t);
        foreach (var t in drop.transitions) drop.RemoveTransition(t);
        foreach (var t in roarSt.transitions) roarSt.RemoveTransition(t);

        var toDrop = idle.AddTransition(drop);
        toDrop.hasExitTime = false; toDrop.duration = 0f; toDrop.offset = ApexTime / jump.length;
        toDrop.AddCondition(AnimatorConditionMode.If, 0f, "EntranceDrop");

        var toRoar = drop.AddTransition(roarSt);
        toRoar.hasExitTime = false; toRoar.duration = 0.15f;
        toRoar.AddCondition(AnimatorConditionMode.If, 0f, "EntranceRoar");
        var dropIdle = drop.AddTransition(idle);
        dropIdle.hasExitTime = false; dropIdle.duration = 0.15f;
        dropIdle.AddCondition(AnimatorConditionMode.If, 0f, "Idle");

        var roarIdle = roarSt.AddTransition(idle);
        roarIdle.hasExitTime = true; roarIdle.exitTime = 0.95f; roarIdle.duration = 0.2f;
        var roarIdleT = roarSt.AddTransition(idle);
        roarIdleT.hasExitTime = false; roarIdleT.duration = 0.15f;
        roarIdleT.AddCondition(AnimatorConditionMode.If, 0f, "Idle");
        EditorUtility.SetDirty(ctrl);
        return $"controller: EntranceDrop ({jump.name}, starts at {toDrop.offset:0.000} = {ApexTime} s), EntranceRoar ({roar.name}); transitions Idle->EntranceDrop->EntranceRoar->Idle (+ Idle trigger exits)";
    }

    static string Material()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MistMat);
        var src = AssetDatabase.LoadAssetAtPath<Material>(FireMat);
        if (src == null) return "fire material missing";
        if (mat == null) { mat = new Material(src) { name = "M_L2_EntranceMist" }; AssetDatabase.CreateAsset(mat, MistMat); }
        // Same URP Particles/Unlit shader as the fire, alpha blended instead of additive (mist and dust must darken, not glow).
        mat.shader = src.shader;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftGlow);
        if (tex != null) { mat.SetTexture("_BaseMap", tex); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex); }
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 0f);     // alpha
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON"); mat.DisableKeyword("_ALPHAMODULATE_ON"); mat.DisableKeyword("_ADDITIVE_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return $"material: {MistMat} (shader {mat.shader.name}, alpha blend)";
    }

    static string Prefab()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var e = root.GetComponent<MonsterEntrance>();
            if (e == null) e = root.AddComponent<MonsterEntrance>();
            var an = root.GetComponent<Animator>();
            var ai = root.GetComponent<MonsterAI>();
            e.style = MonsterEntrance.EntranceStyle.DropFromAbove;
            e.riseOffset = null; e.summoningCircle = null;
            e.dropTrigger = "EntranceDrop"; e.dropStateClip = "MutantJump_HeightBaked";
            e.dropApexTime = ApexTime; e.dropLandTime = LandTime; e.dropRiseEndTime = RiseEndTime; e.dropExtraHeight = 1.0f;
            e.entranceRoarTrigger = "EntranceRoar"; e.entranceRoarStateClip = "L3_Roar";
            e.landAtDistance = 1.85f;   // Level 2's stand-off (LevelManager.monsterAttackRangeOverride)
            e.minLandDistance = 1.5f;
            e.torchDimSeconds = 1.2f; e.torchDimTo = 0.3f; e.torchFlickerHz = 1.2f; e.torchFlickerDepth = 0.12f;
            e.eyesSeconds = 1.6f; e.glowUpSeconds = 2.0f; e.torchRestoreSeconds = 1.0f; e.retrySeconds = 1.5f; e.alwaysFull = false;
            SkinnedMeshRenderer body = null;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == "Vampire") body = r;
            e.emissionRenderer = body;
            e.eyeColor = new Color(0.6f, 0.25f, 1f); e.eyeIntensity = 1.6f; e.glowIntensity = 2.6f; e.eyeFlareIntensity = 5f; e.eyeFlareSeconds = 0.35f;
            ParticleSystem fire = null;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) if (ps.name == "AxeFireVFX") fire = ps;
            e.weaponFire = fire;
            e.weaponHand = an != null ? an.GetBoneTransform(HumanBodyBones.RightHand) : null;
            e.igniteSeconds = 0.8f;
            e.fxMaterial = AssetDatabase.LoadAssetAtPath<Material>(MistMat);
            e.mistColor = new Color(0.45f, 0.2f, 0.7f, 0.35f); e.mistParticles = 24;
            e.dustColor = new Color(0.42f, 0.38f, 0.45f, 0.55f); e.dustParticles = 16;
            e.ringColor = new Color(0.7f, 0.35f, 1f, 0.8f); e.ringParticles = 24;
            e.audioSource = ai != null && ai.sfxAudioSource != null ? ai.sfxAudioSource : root.GetComponent<AudioSource>();
            e.growlClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Growl); e.growlVolume = GrowlVolume;
            e.landClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Land); e.landVolume = LandVolume;
            e.entranceRoarClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Roar); e.entranceRoarVolume = RoarVolume;
            e.landHaptic = new Vector2(0.5f, 0.12f); e.landRumbleTail = new Vector2(0.22f, 0.4f); e.rumbleFrequency = 0.25f;
            e.tremor = MonsterEntrance.TremorStrength.Light; e.tremorLightMeters = 0.010f; e.tremorStrongMeters = 0.025f; e.tremorHz = 7f; e.tremorSeconds = 0.5f; e.tremorKeepsHudSteady = true;
            e.fallingDust = 12; e.torchDipSeconds = 0.5f; e.torchDipDepth = 0.45f; e.dimFlames = true;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            return $"prefab: MonsterEntrance DropFromAbove (body {(body != null)}, fire {(fire != null)}, hand {(e.weaponHand != null)}, audio {(e.audioSource != null)}, " +
                   $"growl {(e.growlClip != null)}, land {(e.landClip != null)}, roar {(e.entranceRoarClip != null)})";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
