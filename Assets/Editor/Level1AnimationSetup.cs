// Level1AnimationSetup.cs
// Purpose: Adds the Level 1 cosmetic animation states to Monster_L1_Sword.controller (in place, idempotent) and MonsterCosmetics to
//          the Level 1 monster prefab. Nothing that exists is changed: every new state is reached from Idle (or left to Idle) with the
//          same kind of transition the existing states use, and Any State still goes only to Death.
//            IdleB / IdleC  two short idle variations baked from Monster_SwordIdle as Humanoid muscle offsets (HumanoidTrack)
//            Taunt          the HipHop model's own dance clip (Hip Hop Dancing.fbx, Humanoid) at 1.5x
//            HitLeft / HitRight  the existing hit reaction, mirrored / not mirrored
//            HitHeavy       Great Sword Impact (Humanoid, unused until now) for a full-power swing
//          Transitions out of the new states copy Idle's: attack / guard / hit triggers win at once, "Idle" trigger or exit time returns.
// Menu: Tools > Combat > Apply Level 1 Animations

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class Level1AnimationSetup
{
    const string ControllerPath = "Assets/Animations/Monster/Level1/Monster_L1_Sword.controller";
    const string PrefabPath = "Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab";
    const string IdleFbx = "Assets/Animations/Monster/Monster_SwordIdle.fbx";
    const string DanceFbx = "Assets/Models/Monsters/Hip Hop Dancing.fbx";
    const string ImpactFbx = "Assets/Animations/Monster/Great Sword Impact.fbx";
    const string OutDir = "Assets/Animations/Monster/Level1";
    const float IdleVariantSpeed = 2.4f;     // 3.7 s variants play in 1.5 s: they fit the 1 to 2 s lulls between Level 1 attacks
    const float HeavyLegBlend = 0.6f;

    [MenuItem("Tools/Combat/Apply Level 1 Animations")]
    public static void Menu() { Debug.Log(Apply()); }

    static AnimationClip Clip(string fbx, string clipName = "mixamo.com")
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is AnimationClip c && c.name == clipName) return c;
        return null;
    }

    static float Env(float t, float T) { float s = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / T)); return s * s; }

    static AnimationClip BakeVariant(string name, System.Func<string, float, float, float> offset)
    {
        var idle = HumanoidTrack.FromClip(IdleFbx);
        float T = idle.length;
        var t = idle.Offset((p, time) => offset(p, time, T));
        return t.Bake($"{OutDir}/{name}.anim", false);
    }

    public static string Apply()
    {
        var log = new System.Text.StringBuilder();
        // 1. Idle variations (muscle offsets over the existing idle; zero at both ends so they join the idle loop cleanly).
        var idleB = BakeVariant("L1_IdleB", (p, t, T) =>
        {
            float e = Env(t, T), look = Mathf.Sin(2f * Mathf.PI * t / T);
            switch (p)
            {
                case "Head Turn Left-Right": return 0.55f * look * e;
                case "Neck Turn Left-Right": return 0.25f * look * e;
                case "Chest Twist Left-Right": return 0.10f * look * e;
                case "Head Nod Down-Up": return -0.06f * e;
            }
            return 0f;
        });
        var idleC = BakeVariant("L1_IdleC", (p, t, T) =>
        {
            float e = Env(t, T), roll = Mathf.Sin(2f * Mathf.PI * t / T * 2f);
            switch (p)
            {
                case "Right Shoulder Down-Up": return 0.28f * roll * e;
                case "Left Shoulder Down-Up": return -0.28f * roll * e;
                case "Chest Front-Back": return 0.10f * e;
                case "Head Nod Down-Up": return 0.12f * Mathf.Sin(2f * Mathf.PI * t / T) * e;
                case "Spine Front-Back": return -0.04f * e;
            }
            return 0f;
        });
        log.AppendLine($"clips: L1_IdleB {idleB.length:0.00} s, L1_IdleC {idleC.length:0.00} s (baked from Monster_SwordIdle)");
        AssetDatabase.SaveAssets();
        var dance = Clip(DanceFbx);
        if (dance == null || Clip(ImpactFbx) == null) return log + "dance / impact clip missing";
        // Great Sword Impact starts in a deep crouch (hips 0.56 against the idle's 0.82): blended 60 % toward the idle's legs and
        // hip height so the heavy stagger is a hard flinch, not a drop to the knees (hips ~0.64 m against 0.85 m).
        var impactTrack = HumanoidTrack.FromClip(ImpactFbx);
        var idleTrack = HumanoidTrack.FromClip(IdleFbx);
        var heavy = impactTrack.Override(idleTrack, p => p == "RootT.y" || p.Contains("Leg") || p.Contains("Foot") || p.Contains("Toes"), 0f, HeavyLegBlend)
                               .Bake($"{OutDir}/L1_HitHeavy.anim", false);
        log.AppendLine($"clip L1_HitHeavy {heavy.length:0.00} s (Great Sword Impact, legs / hip height {HeavyLegBlend * 100:0} % toward idle)");

        // 2. Controller, in place.
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var sm = ctrl.layers[0].stateMachine;
        AnimatorState Find(string n) { foreach (var c in sm.states) if (c.state.name == n) return c.state; return null; }
        var idle = Find("Idle"); var hit = Find("Hit"); var defend = Find("Defend");
        var attackNames = new List<string>();
        foreach (var c in sm.states) if (c.state.name.StartsWith("Atk_")) attackNames.Add(c.state.name);
        foreach (var trig in new[] { "IdleB", "IdleC", "Taunt", "HitLeft", "HitRight", "HitHeavy" })
        {
            bool has = false; foreach (var p in ctrl.parameters) if (p.name == trig) has = true;
            if (!has) ctrl.AddParameter(trig, AnimatorControllerParameterType.Trigger);
        }
        AnimatorState Ensure(string name, Motion motion, Vector3 pos, float speed, bool mirror)
        {
            var s = Find(name);
            if (s == null) s = sm.AddState(name, pos);
            s.motion = motion; s.speed = speed; s.mirrorParameterActive = false; s.mirror = mirror; s.writeDefaultValues = true; s.iKOnFeet = false;
            return s;
        }
        var sB = Ensure("IdleB", idleB, new Vector3(-300, 0, 0), IdleVariantSpeed, false);
        var sC = Ensure("IdleC", idleC, new Vector3(-300, 60, 0), IdleVariantSpeed, false);
        var sT = Ensure("Taunt", dance, new Vector3(-300, 120, 0), 1.5f, false);
        var sL = Ensure("HitLeft", hit.motion, new Vector3(250, -200, 0), hit.speed, true);
        var sR = Ensure("HitRight", hit.motion, new Vector3(250, -260, 0), hit.speed, false);
        var sH = Ensure("HitHeavy", heavy, new Vector3(250, -320, 0), 0.8f, false);

        bool HasTransition(AnimatorState from, AnimatorState to, string trigger)
        {
            foreach (var t in from.transitions)
                if (t.destinationState == to && ((trigger == null && t.conditions.Length == 0 && t.hasExitTime) || (trigger != null && t.conditions.Length == 1 && t.conditions[0].parameter == trigger))) return true;
            return false;
        }
        void Trig(AnimatorState from, AnimatorState to, string trigger, float dur, bool interruptDest = false)
        {
            if (HasTransition(from, to, trigger)) return;
            var t = from.AddTransition(to);
            t.hasExitTime = false; t.duration = dur; t.hasFixedDuration = true; t.offset = 0f;
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            if (interruptDest) t.interruptionSource = TransitionInterruptionSource.Destination;
        }
        void ExitTo(AnimatorState from, AnimatorState to, float exitTime, float dur)
        {
            if (HasTransition(from, to, null)) return;
            var t = from.AddTransition(to);
            t.hasExitTime = true; t.exitTime = exitTime; t.duration = dur; t.hasFixedDuration = true; t.interruptionSource = TransitionInterruptionSource.Destination;
        }
        // Idle -> the new states (a hit goes in at once, like Idle -> Hit).
        // interruptible by the destination's own transitions: an attack that becomes due while a variant is still blending in must not wait for the blend.
        Trig(idle, sB, "IdleB", 0.2f, true); Trig(idle, sC, "IdleC", 0.2f, true); Trig(idle, sT, "Taunt", 0.2f, true);
        foreach (var tr in idle.transitions)
            if (tr.destinationState == sB || tr.destinationState == sC || tr.destinationState == sT) tr.interruptionSource = TransitionInterruptionSource.Destination;
        foreach (var (s, trig) in new[] { (sL, "HitLeft"), (sR, "HitRight"), (sH, "HitHeavy") }) Trig(idle, s, trig, 0.05f, true);
        // Out of every new state: guard, attacks, hits win at once; the Idle trigger or the end of the clip goes back to Idle.
        foreach (var from in new[] { sB, sC, sT })
        {
            Trig(from, hit, "Hit", 0.05f, true); Trig(from, sL, "HitLeft", 0.05f, true); Trig(from, sR, "HitRight", 0.05f, true); Trig(from, sH, "HitHeavy", 0.05f, true);
            Trig(from, defend, "Defend", 0.08f);
            foreach (var a in attackNames) Trig(from, Find(a), a, 0.10f);
            Trig(from, idle, "Idle", 0.15f, true);
            ExitTo(from, idle, 0.97f, 0.2f);
        }
        foreach (var from in new[] { sL, sR, sH })
        {
            Trig(from, idle, "Idle", 0.08f, true);
            ExitTo(from, idle, from == sH ? 0.95f : 0.70f, 0.08f);
            Trig(from, defend, "Defend", 0.08f);
            foreach (var a in attackNames) Trig(from, Find(a), a, 0.10f);
        }
        // Hit -> the side / heavy hits as well (a second hit while the first plays).
        Trig(hit, sL, "HitLeft", 0.05f, true); Trig(hit, sR, "HitRight", 0.05f, true); Trig(hit, sH, "HitHeavy", 0.05f, true);
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        int n = sm.anyStateTransitions.Length; foreach (var c in sm.states) n += c.state.transitions.Length;
        log.AppendLine($"controller {ControllerPath}: {sm.states.Length} states, {n} transitions, Any State transitions {sm.anyStateTransitions.Length} (-> {sm.anyStateTransitions[0].destinationState.name})");

        // 3. Prefab: the cosmetics component with its default toggles on.
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var c = root.GetComponent<MonsterCosmetics>();
            if (c == null) c = root.AddComponent<MonsterCosmetics>();
            // Serialized values on the prefab outlive changes to the script defaults, so the tuned values are written here every time.
            c.idleVariants = c.taunt = c.directionalHits = c.heavyHitReaction = c.sway = true;
            c.idleVariantInterval = new Vector2(6f, 11f);
            c.tauntAfterNoHit = new Vector2(8f, 10f);
            c.tauntCooldown = 14f;
            c.minPlainIdle = 0.4f;
            c.variantMinTimeBeforeAttack = 0.7f;
            c.tauntMinTimeBeforeAttack = 1.2f;
            c.heavyCooldown = 6f; c.heavyTier = "FULL"; c.sideThreshold = 0.25f;
            c.swayDegrees = 1.4f; c.swayPeriod = 4.2f; c.leanDegrees = 5f; c.leanSeconds = 0.28f;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            log.AppendLine($"prefab {PrefabPath}: MonsterCosmetics (idle variants {c.idleVariants}, taunt {c.taunt}, directional hits {c.directionalHits}, heavy hits {c.heavyHitReaction}, sway {c.sway})");
        }
        finally { PrefabUnloadSafe(root); }
        return log.ToString();
    }

    static void PrefabUnloadSafe(GameObject root) { PrefabUtility.UnloadPrefabContents(root); }
}
