// FeelSetup.cs
// Purpose: Idempotent scene wiring for the "feel and fitness" pass (breathing tiers, blood, damage vignette, HUD, results).
//          Each step opens the three level scenes, adds/updates components through SerializedObject, saves, and returns a
//          report. Safe to run again: it only sets values and adds a component when it is missing.
// Run from code: FeelSetup.SetupExertion(), FeelSetup.SetupFeedback() ... (called through the Unity MCP during the pass).

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FeelSetup
{
    public static readonly string[] Scenes =
    {
        "Assets/Scenes/Level1_Easy.unity",
        "Assets/Scenes/Level2_Medium.unity",
        "Assets/Scenes/Level3_Hard.unity",
    };
    public const string FastClipPath = "Assets/Audio/SFX/Player/breathingfast.mp3";

    // Runs fn on each level scene (opened single, saved afterwards if fn returns true) and joins the reports.
    public static string ForEachScene(System.Func<string, string, bool> fn)
    {
        var sb = new StringBuilder();
        string original = EditorSceneManager.GetActiveScene().path;
        foreach (var path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            string name = scene.name;
            string report = null;
            bool changed = false;
            try { changed = fn(name, path) ; }
            catch (System.Exception e) { report = "EXCEPTION " + e.Message; }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            sb.AppendLine(name + (changed ? " saved" : " unchanged") + (report != null ? " " + report : ""));
        }
        if (!string.IsNullOrEmpty(original)) EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        return sb.ToString();
    }

    public static string SetupExertion()
    {
        var fast = AssetDatabase.LoadAssetAtPath<AudioClip>(FastClipPath);
        if (fast == null) return "missing " + FastClipPath;
        return ForEachScene((name, path) =>
        {
            var ex = Object.FindAnyObjectByType<PlayerExertionAudio>(FindObjectsInactive.Include);
            if (ex == null) { Debug.LogWarning("[FeelSetup] " + name + ": no PlayerExertionAudio"); return false; }
            var so = new SerializedObject(ex);
            so.FindProperty("fastClip").objectReferenceValue = fast;
            so.FindProperty("fastBreathing").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        });
    }

    // ───────────────────────── blood, damage vignette, HUD, results ─────────────────────────

    const string FxDir = "Assets/Materials/FX";
    const string VignetteSource = "Assets/Materials/Level3/M_L3_LowHpVignette.mat"; // URP Particles/Unlit, transparent, radial texture

    static Material EnsureMaterial(string name, System.Action<Material> configure)
    {
        if (!AssetDatabase.IsValidFolder(FxDir)) AssetDatabase.CreateFolder("Assets/Materials", "FX");
        string path = FxDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            if (!AssetDatabase.CopyAsset(VignetteSource, path)) throw new System.Exception("could not copy " + VignetteSource);
            mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        configure(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Shared FX materials (assets, so the particle shader is referenced and ships in the build).</summary>
    public static string EnsureFxMaterials()
    {
        var round = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        EnsureMaterial("M_HitBlood", m => { m.SetTexture("_BaseMap", round); m.SetColor("_BaseColor", Color.white); });
        EnsureMaterial("M_HitSparks", m => { m.SetTexture("_BaseMap", round); m.SetColor("_BaseColor", Color.white); });
        // The copy of Level 3's radial vignette used for the player's damage edge on every level.
        EnsureMaterial("M_DamageVignette", m => { });
        AssetDatabase.SaveAssets();
        return "materials ok: " + string.Join(", ", AssetDatabase.FindAssets("t:Material", new[] { FxDir }).Select(g => System.IO.Path.GetFileName(AssetDatabase.GUIDToAssetPath(g))).ToArray());
    }

    struct Medal { public float goldSec, silverSec, goldDmg, silverDmg; }
    static Medal MedalFor(string scene)
    {
        // Development values from the dashboard's few victories (Easy n=8 median 38 s, Medium n=4 median 38 s, Hard n=2 median 59 s;
        // players lose 25 % / 45 % / 38 % of their HP on average). Retune after real playtests.
        if (scene.StartsWith("Level2")) return new Medal { goldSec = 35f, silverSec = 55f, goldDmg = 0.25f, silverDmg = 0.55f };
        if (scene.StartsWith("Level3")) return new Medal { goldSec = 55f, silverSec = 80f, goldDmg = 0.25f, silverDmg = 0.50f };
        return new Medal { goldSec = 35f, silverSec = 50f, goldDmg = 0.15f, silverDmg = 0.40f };
    }

    public static string SetupFeel()
    {
        EnsureFxMaterials();
        var blood = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/M_HitBlood.mat");
        var sparks = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/M_HitSparks.mat");
        var vig = AssetDatabase.LoadAssetAtPath<Material>(FxDir + "/M_DamageVignette.mat");
        return ForEachScene((name, path) =>
        {
            bool l3 = name.StartsWith("Level3");
            var bs = Object.FindAnyObjectByType<BattleManager>(FindObjectsInactive.Include);
            var rig = Object.FindAnyObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            var ui = Object.FindAnyObjectByType<CombatUIController>(FindObjectsInactive.Include);
            if (bs == null || rig == null || ui == null) { Debug.LogWarning("[FeelSetup] " + name + ": missing BattleManager / OVRCameraRig / CombatUIController"); return false; }

            // Blood: one component on BattleSystems.
            var fx = bs.GetComponent<HitBloodFx>() ?? bs.gameObject.AddComponent<HitBloodFx>();
            var so = new SerializedObject(fx);
            so.FindProperty("mode").enumValueIndex = (int)HitBloodFx.Mode.Blood;
            so.FindProperty("bloodMaterial").objectReferenceValue = blood;
            so.FindProperty("sparksMaterial").objectReferenceValue = sparks;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Damage vignette: head-locked quad under the centre eye + the component on the rig.
            Transform eye = rig.centerEyeAnchor;
            var quadT = eye.Find("DamageVignette");
            if (quadT == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "DamageVignette";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(eye, false);
                quad.transform.localPosition = new Vector3(0f, 0f, 0.3f);
                quad.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                quadT = quad.transform;
            }
            var qr = quadT.GetComponent<MeshRenderer>();
            qr.sharedMaterial = vig;
            qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            qr.receiveShadows = false;
            qr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            qr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            qr.enabled = false;
            var dv = rig.GetComponent<PlayerDamageVignette>() ?? rig.gameObject.AddComponent<PlayerDamageVignette>();
            var dso = new SerializedObject(dv);
            dso.FindProperty("health").objectReferenceValue = rig.GetComponent<PlayerHealth>();
            dso.FindProperty("exertion").objectReferenceValue = rig.GetComponent<PlayerExertionAudio>();
            dso.FindProperty("vignette").objectReferenceValue = qr;
            dso.FindProperty("edgeFlash").boolValue = true;
            // Level 3 already has its own low-HP vignette + heartbeat (PlayerLowHealthFeedback); a second pulse would double up.
            dso.FindProperty("lowHpPulse").boolValue = !l3;
            dso.ApplyModifiedPropertiesWithoutUndo();

            // HUD + results + numbers on the existing CombatUIController.
            var m = MedalFor(name);
            var uso = new SerializedObject(ui);
            uso.FindProperty("effortHud").boolValue = true;
            uso.FindProperty("effortHudPosition").vector2Value = new Vector2(220f, -10f);
            uso.FindProperty("resultsExtras").boolValue = true;
            uso.FindProperty("fullPowerPopup").boolValue = true;
            uso.FindProperty("damageNumberPoolSize").intValue = 12;
            uso.FindProperty("goldMaxSeconds").floatValue = m.goldSec;
            uso.FindProperty("silverMaxSeconds").floatValue = m.silverSec;
            uso.FindProperty("goldMaxDamageTakenFraction").floatValue = m.goldDmg;
            uso.FindProperty("silverMaxDamageTakenFraction").floatValue = m.silverDmg;
            uso.ApplyModifiedPropertiesWithoutUndo();
            return true;
        });
    }
}
