// HumanoidClipBaker.cs
// Purpose: Build new Humanoid clips out of existing Mixamo mocap, in muscle space (so they retarget onto any Humanoid
//          avatar exactly like the source clips). Operations:
//            Mirror      - the same left/right swap Unity's AnimatorState.mirror does (bilateral muscles swapped,
//                          centre Left-Right / Twist / Turn / Tilt muscles and root yaw/roll negated)
//            Warp        - re-time a clip: segments, speed changes, slow wind-ups, holds
//            Override    - take some muscles (e.g. the left arm + fingers) from another track
//            Concat      - chain two tracks with a crossfade
//            Offset      - add authored muscle offsets over time (poses with no mocap source: rise, roar, guard)
//          Used by Level3DualAxeBuilder to make dual-wield attacks from one-handed sword/axe mocap.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class HumanoidTrack
{
    public readonly Dictionary<string, AnimationCurve> curves = new Dictionary<string, AnimationCurve>();
    public float length;
    public const float Fps = 60f;

    // ------------------------------------------------------------------ load / bake
    public static HumanoidTrack FromClip(string assetPath, string clipName = null)
    {
        AnimationClip clip = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            var c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview")) continue;
            if (clipName == null || c.name == clipName) { clip = c; break; }
        }
        if (clip == null) throw new Exception($"No clip '{clipName}' in {assetPath}");
        if (!clip.humanMotion) throw new Exception($"{assetPath} is not a Humanoid clip");
        var t = new HumanoidTrack { length = clip.length };
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
            if (b.type == typeof(Animator) && b.path == "") t.curves[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
        return t;
    }

    public float Sample(string prop, float time)
    {
        AnimationCurve c;
        return curves.TryGetValue(prop, out c) ? c.Evaluate(Mathf.Clamp(time, 0f, length)) : 0f;
    }

    public AnimationClip Bake(string path, bool loop)
    {
        // path == null: in-memory clip (previews / measurements), nothing written to the project.
        var clip = path != null ? AssetDatabase.LoadAssetAtPath<AnimationClip>(path) : null;
        if (clip == null)
        {
            clip = new AnimationClip { name = path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : "preview" };
            if (path != null) AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = Fps;
        foreach (var kv in curves)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), kv.Value);
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop;
        s.loopBlend = false;
        // The monster runs with applyRootMotion off. Body yaw (spins, twists) and height (jumps, crouches) are baked
        // into the pose so they stay visible, like the project's GreatSwordSpin_RotationBaked / MutantJump_HeightBaked.
        // Horizontal travel is not: the monster attacks in place, so hit checks from its root stay valid.
        s.loopBlendOrientation = true; s.keepOriginalOrientation = true;
        s.loopBlendPositionY = true; s.keepOriginalPositionY = true;
        s.loopBlendPositionXZ = false; s.keepOriginalPositionXZ = false;
        s.heightFromFeet = false;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        if (path != null) EditorUtility.SetDirty(clip);
        return clip;
    }

    // ------------------------------------------------------------------ core resampler
    /// <summary>New track of newLength whose value at t is f(prop, t). Root/IK quaternions are re-normalised.</summary>
    public static HumanoidTrack Generate(IEnumerable<string> props, float newLength, Func<string, float, float> f)
    {
        var t = new HumanoidTrack { length = newLength };
        int frames = Mathf.Max(2, Mathf.CeilToInt(newLength * Fps) + 1);
        var keys = new Dictionary<string, Keyframe[]>();
        foreach (var p in props) keys[p] = new Keyframe[frames];
        for (int i = 0; i < frames; i++)
        {
            float time = Mathf.Min(newLength, i / Fps);
            foreach (var p in props) keys[p][i] = new Keyframe(time, f(p, time));
        }
        // Normalise every quaternion group (RootQ, LeftFootQ, ...) frame by frame.
        foreach (var p in props)
        {
            if (!p.EndsWith("Q.x")) continue;
            string g = p.Substring(0, p.Length - 1);
            if (!keys.ContainsKey(g + "y") || !keys.ContainsKey(g + "z") || !keys.ContainsKey(g + "w")) continue;
            for (int i = 0; i < frames; i++)
            {
                var q = new Vector4(keys[g + "x"][i].value, keys[g + "y"][i].value, keys[g + "z"][i].value, keys[g + "w"][i].value);
                float m = q.magnitude; if (m < 1e-6f) { q = new Vector4(0, 0, 0, 1); m = 1f; }
                q /= m;
                keys[g + "x"][i].value = q.x; keys[g + "y"][i].value = q.y; keys[g + "z"][i].value = q.z; keys[g + "w"][i].value = q.w;
            }
        }
        foreach (var kv in keys)
        {
            var curve = new AnimationCurve(kv.Value);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            t.curves[kv.Key] = curve;
        }
        return t;
    }

    // ------------------------------------------------------------------ operations
    static string SwapSide(string p)
    {
        if (p.StartsWith("Left")) return "Right" + p.Substring(4);
        if (p.StartsWith("Right")) return "Left" + p.Substring(5);
        return p;
    }

    static bool NegateOnMirror(string p)
    {
        // Centre-line muscles that encode a side (lean, twist, turn, tilt); bilateral ones swap instead.
        if (!p.StartsWith("Left") && !p.StartsWith("Right") &&
            (p.EndsWith("Left-Right") || p.Contains("Twist Left-Right") || p.Contains("Turn Left-Right") || p.Contains("Tilt Left-Right"))) return true;
        // Reflection across the body's mid plane (x -> -x): positions negate x, quaternions negate y and z.
        if (p.EndsWith("T.x")) return true;
        if (p.EndsWith("Q.y") || p.EndsWith("Q.z")) return true;
        return false;
    }

    public HumanoidTrack Mirror()
    {
        var src = this;
        return Generate(curves.Keys, length, (p, t) =>
        {
            float v = src.Sample(SwapSide(p), t);
            return NegateOnMirror(p) ? -v : v;
        });
    }

    /// <summary>Re-time: value at new time t = this at map(t).</summary>
    public HumanoidTrack Warp(float newLength, Func<float, float> map)
    {
        var src = this;
        return Generate(curves.Keys, newLength, (p, t) => src.Sample(p, map(t)));
    }

    /// <summary>Piecewise-linear re-time. pairs = (newTime, sourceTime) ascending in newTime.</summary>
    public HumanoidTrack Retime(params Vector2[] pairs)
    {
        float newLen = pairs[pairs.Length - 1].x;
        return Warp(newLen, t =>
        {
            for (int i = 1; i < pairs.Length; i++)
                if (t <= pairs[i].x) return Mathf.Lerp(pairs[i - 1].y, pairs[i].y, Mathf.InverseLerp(pairs[i - 1].x, pairs[i].x, t));
            return pairs[pairs.Length - 1].y;
        });
    }

    /// <summary>Take the properties selected by 'which' from 'other' (sampled at t + otherOffset), the rest from this.</summary>
    public HumanoidTrack Override(HumanoidTrack other, Func<string, bool> which, float otherOffset = 0f, float weight = 1f)
    {
        var src = this;
        var keys = new HashSet<string>(curves.Keys); keys.UnionWith(other.curves.Keys);
        return Generate(keys, length, (p, t) =>
        {
            if (!which(p)) return src.Sample(p, t);
            float o = other.Sample(p, Mathf.Clamp(t + otherOffset, 0f, other.length));
            return weight >= 1f ? o : Mathf.Lerp(src.Sample(p, t), o, weight);
        });
    }

    /// <summary>this, then b, overlapping by 'blend' seconds (linear crossfade in muscle space).</summary>
    public HumanoidTrack Concat(HumanoidTrack b, float blend)
    {
        var a = this;
        float start = a.length - blend;
        var keys = new HashSet<string>(curves.Keys); keys.UnionWith(b.curves.Keys);
        return Generate(keys, start + b.length, (p, t) =>
        {
            if (t <= start) return a.Sample(p, t);
            float tb = t - start;
            if (tb >= blend) return b.Sample(p, tb);
            float w = Mathf.SmoothStep(0f, 1f, tb / Mathf.Max(1e-4f, blend));
            return Mathf.Lerp(a.Sample(p, t), b.Sample(p, tb), w);
        });
    }

    /// <summary>Add offset(prop, t) to every listed property (muscles are clamped to the human range -1..1 by Unity).</summary>
    public HumanoidTrack Offset(Func<string, float, float> offset)
    {
        var src = this;
        return Generate(curves.Keys, length, (p, t) => src.Sample(p, t) + offset(p, t));
    }

    /// <summary>Hold one source time for 'seconds' (a still pose clip, e.g. Underground).</summary>
    public HumanoidTrack Hold(float sourceTime, float seconds)
    {
        var src = this;
        return Generate(curves.Keys, seconds, (p, t) => src.Sample(p, sourceTime));
    }

    /// <summary>
    /// Dual-wield composite: right arm (+ fingers) from 'right', left arm (+ fingers) from 'left' delayed by leftDelay,
    /// everything else (spine, head, legs, root) blended from right to left by bodyToLeft(t) (0 = right's body,
    /// 1 = left's body, 0.5 = symmetric average when left is the mirror of right).
    /// </summary>
    public static HumanoidTrack DualWield(HumanoidTrack right, HumanoidTrack left, float leftDelay, Func<float, float> bodyToLeft, float length = -1f)
    {
        var keys = new HashSet<string>(right.curves.Keys); keys.UnionWith(left.curves.Keys);
        if (length <= 0f) length = Mathf.Max(right.length, left.length + leftDelay);
        return Generate(keys, length, (p, t) =>
        {
            if (RightArm(p)) return right.Sample(p, t);
            float tl = Mathf.Clamp(t - leftDelay, 0f, left.length);
            if (LeftArm(p)) return left.Sample(p, tl);
            return Mathf.Lerp(right.Sample(p, t), left.Sample(p, tl), Mathf.Clamp01(bodyToLeft(t)));
        });
    }

    /// <summary>Replace every finger muscle of both hands with constant values sampled from 'fist' at fistTime (right hand), mirrored for the left.</summary>
    public HumanoidTrack WithFists(HumanoidTrack fist, float fistTime = 0f)
    {
        var src = this;
        var keys = new HashSet<string>(curves.Keys); keys.UnionWith(fist.curves.Keys);
        return Generate(keys, length, (p, t) =>
        {
            if (RightFingers(p)) return fist.Sample(p, fistTime);
            if (LeftFingers(p)) return fist.Sample("Right" + p.Substring(4), fistTime);
            return src.Sample(p, t);
        });
    }

    // ------------------------------------------------------------------ property groups
    public static bool LeftArm(string p) =>
        p.StartsWith("Left Shoulder") || p.StartsWith("Left Arm") || p.StartsWith("Left Forearm") || p.StartsWith("Left Hand") || p.StartsWith("LeftHand.") || p.StartsWith("LeftHandT") || p.StartsWith("LeftHandQ");
    public static bool RightArm(string p) =>
        p.StartsWith("Right Shoulder") || p.StartsWith("Right Arm") || p.StartsWith("Right Forearm") || p.StartsWith("Right Hand") || p.StartsWith("RightHand.") || p.StartsWith("RightHandT") || p.StartsWith("RightHandQ");
    public static bool LeftFingers(string p) => p.StartsWith("LeftHand.");
    public static bool RightFingers(string p) => p.StartsWith("RightHand.");
    /// <summary>Spine / chest / neck / head side muscles and root yaw/roll: what averaging with the mirror zeroes.</summary>
    public static bool Lateral(string p) =>
        (!p.StartsWith("Left") && !p.StartsWith("Right") && (p.EndsWith("Left-Right"))) || p == "RootT.x" || p == "RootQ.y" || p == "RootQ.z";
}
