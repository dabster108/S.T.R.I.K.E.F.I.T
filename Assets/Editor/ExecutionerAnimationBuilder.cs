// ExecutionerAnimationBuilder.cs
// Purpose: Builds the Level 3 Executioner's Generic animation set and Animator controller. The model has no skeleton,
//          so every clip moves two rigid parts made by ExecutionerMeshSplitter:
//            RiseOffset/Body        - whole figure (step, lunge, hop, fall)
//            RiseOffset/Body/Upper  - torso + both arms + polearm-flail, pivoting at the waist (twist, lean, chop)
//          Hit frames are authored at exactly the MonsterAI wind-up time (seconds at speed 1), so the attack table's
//          windUp values land on the visible impact; Enrage scales MonsterAI.tempoScale and Animator.speed together.
//          Transition rules follow the fixed Level 1/2 controllers: attacks / Defend / Roar enter from both idles AND
//          from Hit, blends into Idle are interruptible by the next state, Any State is used ONLY for Death.
// Menu: Tools > Monster > Level 3 > Rebuild Executioner Animator

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ExecutionerAnimationBuilder
{
    public const string Folder = "Assets/Animations/Monster/Level3";
    public const string ControllerPath = Folder + "/Monster_L3_Executioner.controller";
    const string BodyPath = "RiseOffset/Body";
    const string UpperPath = "RiseOffset/Body/Upper";

    public struct Pose
    {
        public float t; public Vector3 bodyPos, bodyRot, upperRot;
        public Pose(float t, Vector3 bodyPos, Vector3 bodyRot, Vector3 upperRot) { this.t = t; this.bodyPos = bodyPos; this.bodyRot = bodyRot; this.upperRot = upperRot; }
    }

    static Pose P(float t, float bx, float by, float bz, float brx, float bry, float brz, float ux, float uy, float uz)
        => new Pose(t, new Vector3(bx, by, bz), new Vector3(brx, bry, brz), new Vector3(ux, uy, uz));
    // Upper-only shorthand (body at rest).
    static Pose U(float t, float ux, float uy, float uz, float bz = 0f, float by = 0f) => P(t, 0, by, bz, 0, 0, 0, ux, uy, uz);

    public class ClipSpec
    {
        public string name; public bool loop; public float hit; public float[] extraHits = new float[0];
        public List<Pose> poses = new List<Pose>();
    }

    // Attack table rows read these (name -> first hit second, follow-up hits relative to the first).
    public static readonly Dictionary<string, ClipSpec> Specs = new Dictionary<string, ClipSpec>();

    static ClipSpec Spec(string name, bool loop, float hit, params Pose[] poses)
    {
        var s = new ClipSpec { name = name, loop = loop, hit = hit };
        s.poses.AddRange(poses);
        Specs[name] = s;
        return s;
    }

    /// <summary>Fill Specs (clip timings) without touching any asset; used by the attack-table builder.</summary>
    public static void EnsureSpecs() { if (Specs.Count == 0) DefineClips(); }

    static void DefineClips()
    {
        Specs.Clear();
        // Idle: slow heavy breathing, polearm held across the body.
        Spec("Exec_Idle", true, 0f,
            U(0f, 4f, 0f, 0f), U(1.2f, 7f, 3f, -1.5f, 0f, -0.015f), U(2.4f, 4f, 0f, 0f));
        // Enraged idle: faster, deeper, restless.
        Spec("Exec_IdleEnraged", true, 0f,
            U(0f, 9f, -6f, 2f), U(0.3f, 14f, 4f, -3f, 0.03f, -0.03f), U(0.6f, 9f, 8f, 2f), U(0.9f, 15f, -2f, -2f, 0.03f, -0.03f), U(1.2f, 9f, -6f, 2f));
        // Underground: crouched start pose (first frame of Rise), hidden below the floor by MonsterEntrance.
        Spec("Exec_Underground", true, 0f,
            P(0f, 0f, -0.45f, 0f, 0f, 0f, 0f, 55f, -10f, 12f), P(1f, 0f, -0.45f, 0f, 0f, 0f, 0f, 55f, -10f, 12f));
        // Rise / get up: hunched -> lurch -> upright (MonsterEntrance moves RiseOffset up over this clip's length).
        Spec("Exec_Rise", false, 0f,
            P(0f, 0f, -0.45f, 0f, 0f, 0f, 0f, 55f, -10f, 12f),
            P(1.1f, 0f, -0.30f, 0f, 0f, 0f, 0f, 42f, 8f, -6f),
            P(2.0f, 0f, -0.10f, 0.05f, 0f, 0f, 0f, 22f, -6f, 4f),
            P(2.6f, 0f, 0.02f, 0.04f, 0f, 0f, 0f, 2f, 2f, 0f),
            P(3.0f, 0f, 0f, 0f, 0f, 0f, 0f, 4f, 0f, 0f));
        // Roar: chest out, head back, shake, settle.
        Spec("Exec_Roar", false, 0f,
            U(0f, 4f, 0f, 0f), U(0.45f, -20f, 0f, 0f, -0.05f), U(0.7f, -24f, 5f, 3f, -0.05f), U(0.9f, -22f, -5f, -3f, -0.05f),
            U(1.1f, -24f, 5f, 3f, -0.05f), U(1.3f, -22f, -5f, -3f, -0.05f), U(1.5f, -24f, 4f, 2f, -0.05f), U(1.85f, -6f, 0f, 0f), U(2.2f, 4f, 0f, 0f));

        // --- Attacks (hit = first damage frame, seconds at speed 1; MonsterAI windUp uses the same number) ---
        // Reaping Sweep (standard): twist left to wind up, whip the head across the front.
        Spec("Atk_ReapSweep", false, 0.50f,
            U(0f, 4f, 0f, 0f), U(0.34f, 8f, -40f, 6f, -0.08f), U(0.50f, 10f, 58f, -4f, 0.20f), U(0.64f, 8f, 85f, -6f, 0.18f), U(1.05f, 6f, 40f, -2f, 0.05f), U(1.45f, 4f, 0f, 0f));
        // Overhead Chop (standard): lean back with the head raised on the left, drive it down in front.
        Spec("Atk_OverheadChop", false, 0.52f,
            U(0f, 4f, 0f, 0f), U(0.38f, -30f, 20f, -16f, -0.06f), U(0.52f, 30f, 55f, 4f, 0.22f), U(0.66f, 38f, 58f, 5f, 0.22f), U(1.05f, 20f, 30f, 2f, 0.08f), U(1.45f, 4f, 0f, 0f));
        // Polearm Jab (standard): pull back, lunge the head straight forward.
        Spec("Atk_PolearmJab", false, 0.45f,
            P(0f, 0, 0, 0, 0, 0, 0, 4f, 0f, 0f), P(0.30f, 0, 0, -0.15f, 0, 0, 0, -4f, 10f, 0f),
            P(0.45f, 0, 0, 0.45f, 0, 0, 0, 16f, 55f, 0f), P(0.60f, 0, 0, 0.50f, 0, 0, 0, 18f, 58f, 0f),
            P(0.95f, 0, 0, 0.20f, 0, 0, 0, 8f, 2f, 0f), P(1.25f, 0, 0, 0, 0, 0, 0, 4f, 0f, 0f));
        // Twin Reap (combo, 2 hits): sweep right, then backhand sweep left.
        Spec("Atk_TwinReap", false, 0.48f,
            U(0f, 4f, 0f, 0f), U(0.32f, 8f, -40f, 6f, -0.06f), U(0.48f, 10f, 58f, -2f, 0.18f), U(0.64f, 8f, 88f, -6f, 0.15f),
            U(0.86f, 6f, 92f, -4f, 0.10f), U(1.03f, 10f, 56f, 2f, 0.22f), U(1.20f, 8f, -25f, 6f, 0.18f), U(1.6f, 6f, -10f, 2f, 0.05f), U(2.0f, 4f, 0f, 0f));
        Specs["Atk_TwinReap"].extraHits = new[] { 0.55f };
        // Reaping Whirl (heavy): coil, full 360 spin of the whole body with the head out, recover.
        Spec("Atk_ReapingWhirl", false, 0.85f,
            P(0f, 0, 0, 0, 0, 0, 0, 4f, 0f, 0f), P(0.45f, 0, -0.06f, 0, 0, -40f, 0, 10f, -35f, 8f),
            P(0.85f, 0, 0, 0.12f, 0, 60f, 0, 6f, 10f, -10f), P(1.15f, 0, 0, 0.10f, 0, 250f, 0, 6f, 20f, -10f),
            P(1.35f, 0, 0, 0.05f, 0, 360f, 0, 6f, 10f, -4f), P(1.9f, 0, 0, 0, 0, 360f, 0, 4f, 0f, 0f));
        // Executioner's Ground Slam (UNBLOCKABLE): leap with the polearm raised overhead, crash it into the dock.
        Spec("Atk_GroundSlam", false, 1.20f,
            P(0f, 0, 0, 0, 0, 0, 0, 4f, 0f, 0f), P(0.40f, 0, -0.12f, -0.05f, 0, 0, 0, 12f, 0f, 0f),
            P(0.85f, 0, 0.38f, 0.10f, 0, 0, 0, -42f, 20f, -10f), P(1.05f, 0, 0.42f, 0.20f, 0, 0, 0, -44f, 30f, -10f),
            P(1.20f, 0, -0.06f, 0.35f, 0, 0, 0, 58f, 55f, 2f), P(1.75f, 0, -0.06f, 0.35f, 0, 0, 0, 56f, 55f, 2f),
            P(2.35f, 0, 0, 0.05f, 0, 0, 0, 4f, 0f, 0f));

        // Defend: shaft brought across the front and held.
        Spec("Exec_Defend", false, 0f,
            U(0f, 4f, 0f, 0f), U(0.15f, -6f, 32f, -8f, -0.04f), U(0.85f, -6f, 32f, -8f, -0.04f), U(1.0f, 4f, 0f, 0f));
        // Hit reaction: short recoil.
        Spec("Exec_Hit", false, 0f,
            U(0f, 4f, 0f, 0f), U(0.10f, -14f, 6f, 4f, -0.07f), U(0.45f, 4f, 0f, 0f));
        // Death: slump, topple backwards.
        Spec("Exec_Death", false, 0f,
            P(0f, 0, 0, 0, 0, 0, 0, 4f, 0f, 0f), P(0.5f, 0, -0.05f, 0, -4f, 0, 3f, 38f, 10f, 8f),
            P(1.4f, 0, 0.05f, -0.15f, -82f, 0, 6f, 30f, 10f, 8f), P(1.6f, 0, 0.0f, -0.2f, -88f, 0, 6f, 34f, 10f, 8f), P(2.5f, 0, 0.0f, -0.2f, -88f, 0, 6f, 34f, 10f, 8f));
    }

    [MenuItem("Tools/Monster/Level 3/Rebuild Executioner Animator")]
    public static void RebuildMenu() { Debug.Log(Rebuild()); }

    public static string Rebuild()
    {
        DefineClips();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Animations/Monster", "Level3");

        var clips = new Dictionary<string, AnimationClip>();
        foreach (var s in Specs.Values) clips[s.name] = BuildClip(s);

        // Rebuild in place (never delete): the controller keeps its GUID, so the prefab's reference stays valid.
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else
        {
            foreach (var p in ctrl.parameters) ctrl.RemoveParameter(p);
            var old = ctrl.layers[0].stateMachine;
            foreach (var t in old.anyStateTransitions) old.RemoveAnyStateTransition(t);
            foreach (var cs in old.states) old.RemoveState(cs.state);
        }
        string[] triggers = { "Idle", "Hit", "Death", "Defend", "Rise", "Roar", "Atk_ReapSweep", "Atk_OverheadChop", "Atk_PolearmJab", "Atk_TwinReap", "Atk_ReapingWhirl", "Atk_GroundSlam" };
        foreach (var t in triggers) ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Enraged", AnimatorControllerParameterType.Bool);

        var sm = ctrl.layers[0].stateMachine;
        AnimatorState S(string state, string clip, Vector2 pos) { var st = sm.AddState(state, pos); st.motion = clips[clip]; st.writeDefaultValues = true; return st; }

        var underground = S("Underground", "Exec_Underground", new Vector2(-300, 0));
        var rise = S("Rise", "Exec_Rise", new Vector2(-300, 100));
        var roar = S("Roar", "Exec_Roar", new Vector2(-300, 200));
        var idle = S("Idle", "Exec_Idle", new Vector2(0, 0));
        var idleE = S("IdleEnraged", "Exec_IdleEnraged", new Vector2(0, 120));
        var hit = S("Hit", "Exec_Hit", new Vector2(250, -150));
        var defend = S("Defend", "Exec_Defend", new Vector2(250, -60));
        var death = S("Death", "Exec_Death", new Vector2(-300, 320));
        sm.defaultState = underground;

        string[] attacks = { "Atk_ReapSweep", "Atk_OverheadChop", "Atk_PolearmJab", "Atk_TwinReap", "Atk_ReapingWhirl", "Atk_GroundSlam" };
        var attackStates = new List<AnimatorState>();
        for (int i = 0; i < attacks.Length; i++) attackStates.Add(S(attacks[i], attacks[i], new Vector2(500, i * 70)));

        // Entrance
        Trig(underground, rise, "Rise", 0.05f);
        Trig(rise, roar, "Roar", 0.15f);
        ExitTo(rise, idle, 1f, 0.2f);
        ExitTo(roar, idle, 0.98f, 0.25f);

        // Idle <-> enraged idle
        var toE = idle.AddTransition(idleE); toE.hasExitTime = false; toE.duration = 0.2f; toE.AddCondition(AnimatorConditionMode.If, 0, "Enraged");
        var toN = idleE.AddTransition(idle); toN.hasExitTime = false; toN.duration = 0.2f; toN.AddCondition(AnimatorConditionMode.IfNot, 0, "Enraged");

        // Every action starts from either idle, from Hit (a flinch never delays a swing / guard / roar), from the tail
        // of Defend and from Roar (the AI resumes before the enrage roar clip ends).
        var sources = new[] { idle, idleE, hit, defend, roar };
        foreach (var src in sources)
        {
            for (int i = 0; i < attacks.Length; i++) Trig(src, attackStates[i], attacks[i], 0.08f);
            if (src != defend) Trig(src, defend, "Defend", 0.08f);
            if (src != roar) Trig(src, roar, "Roar", 0.12f);
        }
        Trig(idle, hit, "Hit", 0.05f, true);
        Trig(idleE, hit, "Hit", 0.05f, true);

        // Back to Idle: on the Idle trigger (EndAttack / end of defend / flinch) or by exit time; interruptible.
        foreach (var st in attackStates) { Trig(st, idle, "Idle", 0.12f, true); ExitTo(st, idle, 0.98f, 0.12f); }
        Trig(hit, idle, "Idle", 0.1f, true); ExitTo(hit, idle, 0.95f, 0.1f);
        Trig(defend, idle, "Idle", 0.12f, true); ExitTo(defend, idle, 0.98f, 0.12f);
        Trig(roar, idle, "Idle", 0.2f, true);

        // Any State -> Death only.
        var d = sm.AddAnyStateTransition(death); d.hasExitTime = false; d.duration = 0.1f; d.canTransitionToSelf = false; d.AddCondition(AnimatorConditionMode.If, 0, "Death");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return $"Executioner animator: {sm.states.Length} states, {CountTransitions(sm)} transitions, {clips.Count} clips.";
    }

    static void Trig(AnimatorState from, AnimatorState to, string trigger, float blend, bool interruptibleByNext = false)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false; t.duration = blend; t.hasFixedDuration = true; t.offset = 0f;
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        if (interruptibleByNext) t.interruptionSource = TransitionInterruptionSource.Destination;
    }

    static void ExitTo(AnimatorState from, AnimatorState to, float exitTime, float blend)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true; t.exitTime = exitTime; t.duration = blend; t.hasFixedDuration = true;
        t.interruptionSource = TransitionInterruptionSource.Destination;
    }

    static int CountTransitions(AnimatorStateMachine sm)
    {
        int n = sm.anyStateTransitions.Length;
        foreach (var s in sm.states) n += s.state.transitions.Length;
        return n;
    }

    static AnimationClip BuildClip(ClipSpec s)
    {
        string path = $"{Folder}/{s.name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = s.name }; AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = 60f;

        Vector3 upperRest = ExecutionerMeshSplitter.WaistPivot;
        var props = new (string path, string prop, System.Func<Pose, float> get)[]
        {
            (BodyPath, "m_LocalPosition.x", p => p.bodyPos.x),
            (BodyPath, "m_LocalPosition.y", p => p.bodyPos.y),
            (BodyPath, "m_LocalPosition.z", p => p.bodyPos.z),
            (BodyPath, "localEulerAnglesRaw.x", p => p.bodyRot.x),
            (BodyPath, "localEulerAnglesRaw.y", p => p.bodyRot.y),
            (BodyPath, "localEulerAnglesRaw.z", p => p.bodyRot.z),
            (UpperPath, "localEulerAnglesRaw.x", p => p.upperRot.x),
            (UpperPath, "localEulerAnglesRaw.y", p => p.upperRot.y),
            (UpperPath, "localEulerAnglesRaw.z", p => p.upperRot.z),
            // Upper's position is constant (waist pivot) but keyed so Write Defaults never moves it.
            (UpperPath, "m_LocalPosition.x", p => upperRest.x),
            (UpperPath, "m_LocalPosition.y", p => upperRest.y),
            (UpperPath, "m_LocalPosition.z", p => upperRest.z),
        };
        foreach (var pr in props)
        {
            var curve = new AnimationCurve();
            foreach (var pose in s.poses) curve.AddKey(new Keyframe(pose.t, pr.get(pose)));
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(pr.path, typeof(Transform), pr.prop), curve);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = s.loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
