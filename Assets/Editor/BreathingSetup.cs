// BreathingSetup.cs
// Purpose: Applies the 2026-10-10 breathing fix to the project (idempotent): the two re-levelled clips (limited, 100 Hz high-pass,
//          RMS -19 / -16 dBFS, peak -1 dBFS) get explicit Default + Android import settings (mono, PCM, decompress on load, no
//          Unity normalise), and each level's PlayerExertionAudio gets the clips, source volumes and swing / tier tuning.
//          The menu's copy is built by MenuSceneBuilder with the same values (ApplyTo).
// Menu: Tools > Combat > Apply Breathing Fix

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BreathingSetup
{
    public const string HeavyPath = "Assets/Audio/SFX/Player/breathing_heavy_v3.wav";
    public const string FastPath = "Assets/Audio/SFX/Player/breathing_fast_v3.wav";

    [MenuItem("Tools/Combat/Apply Breathing Fix")]
    public static void Menu() { Debug.Log(Apply()); }

    public static void ImportSettings(string path)
    {
        var imp = (AudioImporter)AssetImporter.GetAtPath(path);
        if (imp == null) return;
        imp.forceToMono = true; imp.loadInBackground = false; imp.ambisonic = false;
        var s = new AudioImporterSampleSettings
        {
            loadType = AudioClipLoadType.DecompressOnLoad,
            compressionFormat = AudioCompressionFormat.PCM,
            quality = 1f,
            sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
            preloadAudioData = true,
        };
        imp.defaultSampleSettings = s;
        imp.SetOverrideSampleSettings("Android", s);
        SerializedObject so = new SerializedObject(imp);
        var norm = so.FindProperty("m_Normalize"); if (norm != null) { norm.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
        imp.SaveAndReimport();
    }

    /// <summary>The tuned values, shared by the level scenes and the menu.</summary>
    public static void ApplyTo(PlayerExertionAudio p)
    {
        p.breathingClip = AssetDatabase.LoadAssetAtPath<AudioClip>(HeavyPath);
        p.fastClip = AssetDatabase.LoadAssetAtPath<AudioClip>(FastPath);
        p.fastBreathing = true; p.fastGain = 1f;
        p.startVolume = 0.55f; p.maxVolume = 0.8f; p.fastMaxVolume = 0.9f;
        p.useLeakyEffort = true; p.effortPerSwing = 0.029f; p.activeDecaySeconds = 200f; p.restGraceSeconds = 2.5f; p.restDecaySeconds = 8f;
        p.swingTriggerSpeed = 1.2f; p.swingRearmSpeed = 0.8f; p.minSwingWeight = 0.75f; p.fullWeightSpeed = 4f;
        p.exertionWindow = 30f; p.swingsForFullEffort = 34.5f;
        p.mediumOn = 0.28f; p.mediumOff = 0.18f; p.highOn = 0.65f; p.highOff = 0.50f;
        p.lowHpOn = 0.25f; p.lowHpOff = 0.30f;
        p.logTierChanges = true;
        EditorUtility.SetDirty(p);
    }

    public static string Apply()
    {
        AssetDatabase.Refresh();
        ImportSettings(HeavyPath); ImportSettings(FastPath);
        var log = new System.Text.StringBuilder();
        foreach (var sc in new[] { "Level1_Easy", "Level2_Medium", "Level3_Hard" })
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + sc + ".unity", OpenSceneMode.Single);
            var p = Object.FindAnyObjectByType<PlayerExertionAudio>();
            if (p == null) { log.AppendLine(sc + ": no PlayerExertionAudio"); continue; }
            ApplyTo(p);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"{sc}: heavy {p.breathingClip != null} fast {p.fastClip != null}, caps {p.maxVolume}/{p.fastMaxVolume}, trigger {p.swingTriggerSpeed}, tiers {p.mediumOn}/{p.highOn}");
        }
        return log.ToString();
    }
}
