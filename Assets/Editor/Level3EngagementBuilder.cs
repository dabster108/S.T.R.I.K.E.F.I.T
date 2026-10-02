// Level3EngagementBuilder.cs
// Purpose: Idempotent Level 3 scene additions that make the boss fight escalate beyond the monster itself:
//   - ArenaMood (ArenaMoodController): moonlight / ambient / fog / lamps follow the fight (storm -> enraged -> cleared)
//   - Low-HP tension on OVRCameraRig (PlayerLowHealthFeedback): head-locked red vignette + heartbeat + breathing floor
//   - BattleManager.victoryPanelDelay: the Victory panel waits for the boss's slow-motion finale (BossFinale)
//   - Weapon pickups: idle pulsing / turning ground-ring glow (WeaponPickup.markerRing)
// Run on the open Level3_Hard scene (Tools > Level 3 > Apply Engagement FX); also called by Level3SceneBuilder.Build.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Level3EngagementBuilder
{
    const string VfxDir = "Assets/VFX/Level3";
    const string VignetteTexPath = VfxDir + "/T_L3_LowHpVignette.png";
    const string VignetteMatPath = "Assets/Materials/Level3/M_L3_LowHpVignette.mat";
    public const float VictoryPanelDelay = 3.2f; // BossFinale: 1.3 s slow + 0.6 s ramp, then ~1.3 s on the fallen boss

    [MenuItem("Tools/Level 3/Apply Engagement FX")]
    public static void ApplyMenu() { Debug.Log(ApplyToOpenScene(true)); }

    public static string ApplyToOpenScene(bool save)
    {
        var log = new System.Text.StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.path.EndsWith("Level3_Hard.unity")) return "Open Level3_Hard first (active scene is " + scene.path + ").";

        // --- Arena mood ---
        var bs = GameObject.Find("BattleSystems");
        var moodT = bs.transform.Find("ArenaMood");
        if (moodT == null) { moodT = new GameObject("ArenaMood").transform; moodT.SetParent(bs.transform, false); }
        var mood = moodT.GetComponent<ArenaMoodController>();
        if (mood == null) mood = moodT.gameObject.AddComponent<ArenaMoodController>();
        mood.moon = GameObject.Find("Moonlight").GetComponent<Light>();
        var lamps = new System.Collections.Generic.List<Light>();
        var lampRoot = GameObject.Find("Environment_OldSeaport/Lamps");
        if (lampRoot != null) foreach (var l in lampRoot.GetComponentsInChildren<Light>(true)) lamps.Add(l);
        mood.lamps = lamps.ToArray();
        EditorUtility.SetDirty(mood);
        log.AppendLine($"ArenaMood: moon '{mood.moon.name}', {mood.lamps.Length} lamp lights.");

        // --- Weapon pickups: idle glow on each pair's ground ring ---
        foreach (var pick in Object.FindObjectsByType<WeaponPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var ringT = pick.transform.Find("MarkerRing");
            if (ringT == null) continue;
            pick.markerRing = ringT.GetComponent<Renderer>();
            EditorUtility.SetDirty(pick);
            log.AppendLine($"Pickup {pick.weaponType}: idle ring glow wired.");
        }

        // --- Victory hold ---
        var bm = bs.GetComponent<BattleManager>();
        bm.victoryPanelDelay = VictoryPanelDelay;
        EditorUtility.SetDirty(bm);
        log.AppendLine($"BattleManager.victoryPanelDelay = {VictoryPanelDelay} s (real time).");

        // --- Low-HP tension: head-locked vignette quad + heartbeat ---
        var rig = GameObject.Find("OVRCameraRig");
        var eye = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        var vigT = eye.Find("LowHpVignette");
        if (vigT == null)
        {
            vigT = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            vigT.name = "LowHpVignette";
            Object.DestroyImmediate(vigT.GetComponent<Collider>());
            vigT.SetParent(eye, false);
        }
        // 0.3 m ahead, 1.3 m wide: covers a Quest eye's ~100 deg FOV with margin; the texture's clear centre spans
        // the middle of the view and its edge ramp sits in the periphery.
        vigT.localPosition = new Vector3(0f, 0f, 0.3f); vigT.localRotation = Quaternion.identity; vigT.localScale = new Vector3(1.3f, 1.3f, 1f);
        var vr = vigT.GetComponent<MeshRenderer>();
        vr.sharedMaterial = VignetteMaterial();
        vr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; vr.receiveShadows = false;
        vr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; vr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        vr.enabled = false;

        var fb = rig.GetComponent<PlayerLowHealthFeedback>();
        if (fb == null) fb = rig.AddComponent<PlayerLowHealthFeedback>();
        fb.health = rig.GetComponent<PlayerHealth>();
        fb.exertion = rig.GetComponent<PlayerExertionAudio>();
        fb.vignette = vr;
        var hbT = rig.transform.Find("PlayerHeartbeatAudio");
        if (hbT == null) { hbT = new GameObject("PlayerHeartbeatAudio").transform; hbT.SetParent(rig.transform, false); }
        var hb = hbT.GetComponent<AudioSource>();
        if (hb == null) hb = hbT.gameObject.AddComponent<AudioSource>();
        hb.playOnAwake = false; hb.loop = false; hb.spatialBlend = 0f; hb.volume = 1f;
        fb.heartSource = hb;
        fb.heartbeatClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Level3/L3_Heartbeat.wav");
        EditorUtility.SetDirty(fb);
        log.AppendLine("PlayerLowHealthFeedback on OVRCameraRig: vignette under CenterEyeAnchor, heartbeat source, breathing floor.");

        EditorSceneManager.MarkSceneDirty(scene);
        if (save) EditorSceneManager.SaveScene(scene);
        return log.ToString();
    }

    /// <summary>Radial vignette: clear centre, alpha ramps to 1 at the edge of the visible area (quad UV r 0.12 -> 0.30).</summary>
    static Material VignetteMaterial()
    {
        if (!AssetDatabase.IsValidFolder(VfxDir)) AssetDatabase.CreateFolder("Assets/VFX", "Level3");
        const int N = 256;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N - 0.5f, v = (y + 0.5f) / N - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.30f, r));
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        System.IO.File.WriteAllBytes(VignetteTexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(VignetteTexPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(VignetteTexPath);
        ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = N;
        ti.SaveAndReimport();

        var m = AssetDatabase.LoadAssetAtPath<Material>(VignetteMatPath);
        if (m == null)
        {
            // URP Particles/Unlit, transparent alpha blend (the rune circle's shader with alpha instead of additive).
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Level3/M_L3_RuneCircle.mat")) { name = "M_L3_LowHpVignette" };
            AssetDatabase.CreateAsset(m, VignetteMatPath);
        }
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); // transparent, alpha
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(VignetteTexPath));
        m.SetColor("_BaseColor", new Color(0.45f, 0f, 0f, 0f));
        m.renderQueue = 4000; // overlay: after the world's transparents
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return m;
    }
}
