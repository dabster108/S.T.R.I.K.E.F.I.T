// MenuDungeonBuilder.cs
// Purpose: The main menu's living-dungeon background, built into MainMenu.unity (MenuSceneBuilder.Build calls BuildInto, so a
//          menu rebuild keeps the dungeon). Steps:
//          - ConvertMaterials: Decrepit Dungeon LITE ships Built-in "Standard" / legacy particle materials, which render pink
//            under URP. Each is switched in place to URP Lit (albedo, colour, normal map, occlusion, smoothness, metallic; the
//            fade-mode decals as URP transparent) or URP Particles/Unlit (the additive flame), keywords reset by URP's own
//            material setup, GPU instancing on. Idempotent.
//          - BuildInto: a hall copied from the pack's own demo layout (Demo_Scene: the lower cell-block hall), moved so the
//            player's fixed spot is the menu rig (rig and camera never move). The demo's lights (59), particle systems and
//            colliders are not copied; chains and barrels are skipped (triangle budget). The two torches either side of the
//            menu, 5 m ahead, get the only realtime lights (Mixed point, no shadows, flickered by MenuTorchFlicker); every other
//            torch gets a Baked point light, and all torch flames come from one pooled particle system (MenuAmbience). Static
//            (batched + lightmapped), 120 light probes, linear fog. Also: dust, embers, floor mist, a swinging hanging cage, a
//            drifting cell door, a shadow gliding along the wall, the menu canvas 2 m ahead with a warm frame, the menu music
//            (MenuMusic, through StrikeFitMixer's Music group).
//          - BakeLighting (async) then CompressForAndroid (ASTC 6x6 overrides on the lightmaps and textures).
//          - RenderView / Budget: look-dev renders and a cost count without Play mode.
// Dependencies: MenuSceneBuilder (scene layout), MenuAmbience, MenuTorchFlicker, MenuMusic, URP
// Attach to: nothing (Tools/Menu/Build Menu Dungeon, or over Unity MCP)

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MenuDungeonBuilder
{
    public const string PackRoot = "Assets/Decrepit Dungeon LITE";

    // ───────────────────────── materials ─────────────────────────

    [MenuItem("Tools/Menu/Convert Decrepit Dungeon Materials To URP")]
    public static void ConvertMenu() { Debug.Log(ConvertMaterials()); }

    public static string ConvertMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var log = new StringBuilder();
        int converted = 0, skipped = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PackRoot + "/Materials" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            string from = m.shader.name;
            // Built-in Standard's rendering mode (0 opaque, 1 cutout, 2 fade, 3 transparent), read from the file: it stays in the
            // saved properties after the shader switch, so a second run still knows the decals are see-through.
            int mode = BuiltinMode(path);
            if (from == lit.name) { ApplySurface(m, mode); Rekey(m); skipped++; continue; }
            if (from == particles.name) { Rekey(m); skipped++; continue; }
            if (from.StartsWith("Legacy Shaders/Particles")) ToParticle(m, particles);
            else { ToLit(m, lit); ApplySurface(m, mode); }
            Rekey(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            converted++;
            log.AppendLine("  " + m.name + ": " + from + " -> " + m.shader.name);
        }
        AssetDatabase.SaveAssets();
        return "Decrepit Dungeon materials: " + converted + " converted, " + skipped + " already URP\n" + log;
    }

    static void ToLit(Material m, Shader lit)
    {
        Texture albedo = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
        Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
        Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        Texture normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
        float bumpScale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
        Texture occlusion = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
        float occStrength = m.HasProperty("_OcclusionStrength") ? m.GetFloat("_OcclusionStrength") : 1f;
        float smooth = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0f;
        float metal = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
        Texture metalMap = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;

        m.shader = lit;
        m.SetTexture("_BaseMap", albedo);
        m.SetTextureScale("_BaseMap", scale);
        m.SetTextureOffset("_BaseMap", offset);
        m.SetColor("_BaseColor", color);
        m.SetTexture("_BumpMap", normal);
        m.SetFloat("_BumpScale", bumpScale);
        if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
        // The pack points its occlusion slot at the albedo (a darkening trick in Built-in); kept, it reads the same in URP.
        m.SetTexture("_OcclusionMap", occlusion);
        m.SetFloat("_OcclusionStrength", occStrength);
        if (occlusion != null) m.EnableKeyword("_OCCLUSIONMAP"); else m.DisableKeyword("_OCCLUSIONMAP");
        m.SetFloat("_Smoothness", Mathf.Min(smooth, 0.4f));   // dull stone: no white glints under the torches
        m.SetFloat("_Metallic", metal);
        m.SetTexture("_MetallicGlossMap", metalMap);
        if (metalMap != null) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
        m.SetFloat("_EnvironmentReflections", 0f);              // no reflection probe in the menu: the sky cube would show
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.SetFloat("_WorkflowMode", 1f);
        m.SetFloat("_Surface", 0f);
        m.renderQueue = -1;
    }

    /// <summary>Clears the Built-in keywords (_ALPHAPREMULTIPLY_ON, _DETAIL_MULX2, _EMISSION ... stay on the material after a
    /// shader switch and select variants that do not exist: the decals drew pink) and lets URP set its own, as its material
    /// inspector does when a shader is assigned.</summary>
    static void Rekey(Material m)
    {
        m.shaderKeywords = null;
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;   // nothing here glows (the old flag kept _EMISSION on)
        if (m.shader.name == "Universal Render Pipeline/Lit")
        {
            m.SetColor("_EmissionColor", Color.black);
            // Standard's detail and height slots carried over; on a Quest they are two extra texture reads for little gain.
            m.SetTexture("_DetailAlbedoMap", null); m.SetTexture("_DetailNormalMap", null); m.SetTexture("_ParallaxMap", null);
            BaseShaderGUI.SetMaterialKeywords(m, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords);
        }
        else BaseShaderGUI.SetMaterialKeywords(m, null, UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(m);
    }

    static int BuiltinMode(string path)
    {
        foreach (var line in System.IO.File.ReadAllLines(path))
        {
            string t = line.Trim();
            if (t.StartsWith("- _Mode:") && int.TryParse(t.Substring(8).Trim(), out int v)) return v;
        }
        return 0;
    }

    /// <summary>Cutout -> alpha clip; Fade / Transparent (the grunge, hay and wax decals) -> URP transparent alpha blend.</summary>
    static void ApplySurface(Material m, int mode)
    {
        if (mode == 1)
        {
            m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            return;
        }
        if (mode < 2) return;
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static void ToParticle(Material m, Shader particles)
    {
        Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        Color tint = m.HasProperty("_TintColor") ? m.GetColor("_TintColor") : Color.white;
        m.shader = particles;
        m.SetTexture("_BaseMap", tex);
        // Legacy additive doubled the tint; same brightness here.
        m.SetColor("_BaseColor", new Color(Mathf.Clamp01(tint.r * 2f), Mathf.Clamp01(tint.g * 2f), Mathf.Clamp01(tint.b * 2f), 1f));
        SetAdditive(m);
    }

    /// <summary>URP Particles/Unlit as a transparent additive material (no depth write).</summary>
    public static void SetAdditive(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);   // additive
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // ───────────────────────── the hall ─────────────────────────

    public const string MenuPath = "Assets/Scenes/MainMenu.unity";
    public const string DemoPath = PackRoot + "/EXAMPLE/Demo_Scene.unity";
    public const string RootName = "MenuDungeon";
    const string MatDir = "Assets/Materials/Menu";
    const string LightingPath = "Assets/Settings/Lighting/MainMenuLighting.lighting";

    // The player's fixed spot in the demo's lower hall (floor top measured by raycast), and the turn that makes the hall run
    // away from the menu along +z. The demo's realtime lights (59) never come across.
    static readonly Vector3 DemoSpot = new Vector3(-14.4f, -11.574f, 54f);
    static readonly Quaternion Turn = Quaternion.Euler(0f, 180f, 0f);
    static readonly Bounds DemoRegion = new Bounds(new Vector3(-14.5f, -8.75f, 37f), new Vector3(17f, 7.5f, 50f));
    static readonly string[] Skip = { "Chain_", "Barrel" };   // chains are 12k triangles each, barrels 3.4k: not for a Quest menu

    // Lights: the two torches either side, 5 m ahead, are the only realtime lights; every other torch is baked.
    public const int RealtimeTorches = 2;
    static readonly Color TorchColor = new Color(1f, 0.58f, 0.28f);

    const StaticEditorFlags StaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                          StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

    // Menu canvas: about 2 m ahead (was 2.5 m), scaled down by the same ratio so it covers the same angle (text keeps its size in view).
    public static readonly Vector3 CanvasPosition = new Vector3(0f, 1.42f, 2.0f);
    public const float CanvasScale = 0.0016f;

    [MenuItem("Tools/Menu/Build Menu Dungeon")]
    public static void BuildMenu() { Debug.Log(Apply()); }

    /// <summary>Materials, lightmap UVs, then the hall into MainMenu.unity (opened, built, saved). Bake separately (BakeLighting).</summary>
    public static string Apply()
    {
        var log = new StringBuilder();
        log.AppendLine(ConvertMaterials().Split('\n')[0]);
        log.AppendLine(PrepareModels());
        log.AppendLine(EnsureMixer());
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MenuPath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        log.AppendLine(BuildInto(scene));
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return log.ToString();
    }

    /// <summary>The pack's meshes have no second UV set: generate lightmap UVs on import (needed for the bake).</summary>
    public static string PrepareModels()
    {
        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PackRoot + "/Meshes" }))
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
            if (imp == null || imp.generateSecondaryUV) continue;
            imp.generateSecondaryUV = true;
            imp.SaveAndReimport();
            changed++;
        }
        return "lightmap UVs: " + changed + " models reimported";
    }

    public static string BuildInto(UnityEngine.SceneManagement.Scene scene)
    {
        var log = new StringBuilder();
        foreach (var r in scene.GetRootGameObjects())
            if (r.name == RootName || r.name == "MenuEnvironment") Object.DestroyImmediate(r);   // the old flat floor and ring go too
        var root = new GameObject(RootName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);

        // 1. Copy the hall.
        var demo = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DemoPath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        var flameAnchors = new List<Transform>();
        int copied = 0;
        try
        {
            GameObject demoRoot = null;
            foreach (var r in demo.GetRootGameObjects()) if (r.name == "Demo Scene") demoRoot = r;
            foreach (Transform cat in demoRoot.transform)
            {
                var group = new GameObject(cat.name).transform;
                group.SetParent(root.transform, false);
                foreach (Transform o in cat)
                {
                    if (System.Array.Exists(Skip, k => o.name.StartsWith(k))) continue;
                    bool inside = false;
                    foreach (var rr in o.GetComponentsInChildren<Renderer>(true)) if (DemoRegion.Intersects(rr.bounds)) { inside = true; break; }
                    if (!inside) continue;
                    var copy = Object.Instantiate(o.gameObject);
                    copy.name = o.name;
                    copy.transform.SetPositionAndRotation(Turn * (o.position - DemoSpot), Turn * o.rotation);
                    copy.transform.localScale = o.lossyScale;
                    copy.transform.SetParent(group, true);
                    copied++;
                }
                if (group.childCount == 0) Object.DestroyImmediate(group.gameObject);
            }
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(demo, true); }
        log.AppendLine("hall: " + copied + " objects copied from the demo's lower hall");

        // 2. Strip lights, particles and colliders (nothing here is touched; the laser stops only on the menu canvas).
        int lights = 0, systems = 0, colliders = 0;
        foreach (var torch in root.GetComponentsInChildren<Transform>(true))
        {
            if (!torch.name.StartsWith("Torch") || torch.parent == null || torch.parent.name != "Props") continue;
            var fire = torch.GetComponentInChildren<ParticleSystem>(true);
            var lamp = torch.GetComponentInChildren<Light>(true);
            var anchor = new GameObject("FlameAnchor").transform;
            anchor.SetParent(torch, false);
            anchor.position = fire != null ? fire.transform.position : lamp != null ? lamp.transform.position : torch.position;
            flameAnchors.Add(anchor);
        }
        systems = root.GetComponentsInChildren<ParticleSystem>(true).Length;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps.gameObject);
        foreach (var l in root.GetComponentsInChildren<Light>(true)) { Object.DestroyImmediate(l); lights++; }
        foreach (var c in root.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(c); colliders++; }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticFlags);
            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;   // for the bake only: no light casts realtime shadows
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        }
        log.AppendLine("removed from the copy: " + lights + " lights, " + systems + " particle systems, " + colliders + " colliders");

        // 3. Torch lights.
        var spot = new Vector3(0f, 1.6f, 0f);
        flameAnchors.Sort((a, b) => Score(a.position).CompareTo(Score(b.position)));
        var torchRoot = new GameObject("TorchLights").transform;
        torchRoot.SetParent(root.transform, false);
        for (int i = 0; i < flameAnchors.Count; i++)
        {
            bool realtime = i < RealtimeTorches;
            var go = new GameObject((realtime ? "Realtime" : "Baked") + "TorchLight_" + i);
            go.transform.SetParent(torchRoot, false);
            go.transform.position = flameAnchors[i].position + Vector3.up * 0.15f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = TorchColor;
            l.range = realtime ? 9f : 7f;
            l.intensity = realtime ? 2.2f : 1.6f;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = realtime ? LightmapBakeType.Mixed : LightmapBakeType.Baked;
            l.bounceIntensity = 1f;
            if (realtime) go.AddComponent<MenuTorchFlicker>();
        }
        log.AppendLine("torches: " + flameAnchors.Count + " flame anchors, " + Mathf.Min(RealtimeTorches, flameAnchors.Count) + " realtime (Mixed, no shadows) + " + Mathf.Max(0, flameAnchors.Count - RealtimeTorches) + " baked point lights");

        // 4. Moving props (not static: lit by the probes).
        var ambience = new GameObject("Ambience").AddComponent<MenuAmbience>();
        ambience.transform.SetParent(root.transform, false);
        ambience.flameAnchors = flameAnchors.ToArray();
        ambience.flamesPerSecond = 10f;   // 14 torches x 10/s x 0.55 s = 77 alive, under the 80 cap
        BuildParticles(ambience);
        log.AppendLine(BuildMovers(root, ambience));

        // 5. Light probes over the walkable floor and up to the torches.
        var probes = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
        probes.transform.SetParent(root.transform, false);
        var pts = new List<Vector3>();
        foreach (float x in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
            foreach (float y in new[] { 0.4f, 2.2f, 4.2f })
                foreach (float z in new[] { -1f, 2f, 5f, 9f, 13f, 17f, 21f, 26f, 31f, 36f })
                    pts.Add(new Vector3(x, y, z));
        probes.probePositions = pts.ToArray();
        log.AppendLine("light probes: " + pts.Count);

        // 6. Fog, ambient, camera background (cheap: linear fog, no post-processing).
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.035f, 0.025f, 0.02f);
        RenderSettings.fogStartDistance = 5f;
        RenderSettings.fogEndDistance = 48f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.045f, 0.04f, 0.04f);
        RenderSettings.skybox = null;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (cam.gameObject.scene == scene) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor; }

        // 7. The menu canvas about 2 m ahead, and a warm frame on every page panel so it reads against the dark hall.
        log.AppendLine(PlaceCanvas(scene));

        // 8. Music (MenuSystems/MenuMusic).
        log.AppendLine(BuildMusic(scene));

        // 9. Lighting settings for the bake.
        Lightmapping.lightingSettings = LightingAsset();
        return log.ToString();
    }

    // Nearest torches to a point 5 m ahead of the player (the pair either side of the menu), then by distance.
    static float Score(Vector3 p) => (new Vector3(p.x * 0.2f, 0f, p.z) - new Vector3(0f, 0f, 5f)).sqrMagnitude;

    static Material ParticleMaterial(string name, Texture tex, bool additive)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        if (additive) SetAdditive(m);
        else
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        m.shaderKeywords = null;
        BaseShaderGUI.SetMaterialKeywords(m, null, UnityEditor.Rendering.Universal.ShaderGUI.ParticleGUI.SetMaterialKeywords);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(MatDir + "/Textures/" + file);

    static ParticleSystem NewSystem(Transform parent, string name, Material mat, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = true;
        main.useUnscaledTime = true;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.maxParticleSize = 2f;
        return ps;
    }

    static ParticleSystem.MinMaxGradient Fade(Color a, Color b, float peak)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.2f), new GradientAlphaKey(peak, 0.7f), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(g);
    }

    // Particle budget (maxParticles): flames 80 + embers 26 + dust 50 + mist 12 = 168, under 200.
    public const int FlameMax = 80, EmberMax = 26, DustMax = 50, MistMax = 12;

    /// <summary>Rebuilds only the four particle systems under MenuDungeon/Ambience (no re-bake needed: they are not lit).</summary>
    public static string RebuildParticles()
    {
        var amb = Object.FindAnyObjectByType<MenuAmbience>();
        if (amb == null) return "no MenuAmbience";
        foreach (var ps in amb.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps.gameObject);
        BuildParticles(amb);
        amb.flamesPerSecond = 10f;
        EditorUtility.SetDirty(amb);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(amb.gameObject.scene);
        return "particles rebuilt: " + amb.GetComponentsInChildren<ParticleSystem>(true).Length + " systems";
    }

    static void BuildParticles(MenuAmbience amb)
    {
        var parent = amb.transform;
        // Flames: emitted by MenuAmbience at every torch (one pool).
        var flames = NewSystem(parent, "TorchFlames", ParticleMaterial("M_MenuFlame", Tex("T_MenuFlame.png"), true), FlameMax);
        var m = flames.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.65f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.52f);
        m.startSpeed = 0f;
        m.startColor = Color.white;
        var em = flames.emission; em.enabled = false;
        var shape = flames.shape; shape.enabled = false;
        var col = flames.colorOverLifetime; col.enabled = true;
        col.color = Fade(new Color(1f, 0.97f, 0.85f), new Color(1f, 0.4f, 0.08f), 1f);   // white-hot core: reads against the torch-lit wall
        var size = flames.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
        flames.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.VerticalBillboard;
        amb.flames = flames;

        // Embers: a few sparks drifting up from the torches.
        var embers = NewSystem(parent, "Embers", ParticleMaterial("M_MenuEmber", Tex("T_MenuSoftDot.png"), true), EmberMax);
        m = embers.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
        m.startSpeed = 0f;
        m.gravityModifier = -0.02f;
        em = embers.emission; em.enabled = false;
        shape = embers.shape; shape.enabled = false;
        col = embers.colorOverLifetime; col.enabled = true;
        col.color = Fade(new Color(1f, 0.7f, 0.3f), new Color(0.8f, 0.15f, 0.02f), 1f);
        var noise = embers.noise; noise.enabled = true; noise.strength = 0.15f; noise.frequency = 0.6f; noise.quality = ParticleSystemNoiseQuality.Low;
        amb.embers = embers;

        // Dust motes floating in the torchlight around the menu.
        var dust = NewSystem(parent, "DustMotes", ParticleMaterial("M_MenuDust", Tex("T_MenuSoftDot.png"), true), DustMax);
        dust.transform.position = new Vector3(0f, 1.9f, 4f);
        m = dust.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
        m.prewarm = true;
        em = dust.emission; em.enabled = true; em.rateOverTime = 4.5f;
        shape = dust.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(7f, 3.2f, 8f);
        col = dust.colorOverLifetime; col.enabled = true;
        col.color = Fade(new Color(1f, 0.85f, 0.6f), new Color(1f, 0.75f, 0.5f), 0.45f);
        noise = dust.noise; noise.enabled = true; noise.strength = 0.04f; noise.frequency = 0.25f; noise.quality = ParticleSystemNoiseQuality.Low;

        // Mist: a few large, faint puffs drifting slowly along the floor.
        var mist = NewSystem(parent, "FloorMist", ParticleMaterial("M_MenuMist", Tex("T_MenuMist.png"), false), MistMax);
        mist.transform.position = new Vector3(0f, 0.35f, 15f);
        m = mist.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(16f, 22f);
        m.startSize = new ParticleSystem.MinMaxCurve(3f, 5f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.prewarm = true;
        em = mist.emission; em.enabled = true; em.rateOverTime = 0.6f;
        shape = mist.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(10f, 0.3f, 26f);
        col = mist.colorOverLifetime; col.enabled = true;
        col.color = Fade(new Color(0.55f, 0.5f, 0.48f), new Color(0.4f, 0.37f, 0.36f), 0.16f);
        var rot = mist.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        var mr = mist.GetComponent<ParticleSystemRenderer>();
        mr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;   // lies on the floor: low overdraw for its size
        mr.maxParticleSize = 6f;
    }

    static string BuildMovers(GameObject root, MenuAmbience amb)
    {
        var log = new StringBuilder();
        var movers = new GameObject("Movers").transform;
        movers.SetParent(root.transform, false);

        // A hanging cage on the right, 13 m ahead, swinging slowly from a pivot at the ceiling.
        var cagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + "/Prefabs/Props/Hanging_Cage.prefab");
        if (cagePrefab != null)
        {
            var cage = (GameObject)PrefabUtility.InstantiatePrefab(cagePrefab);
            Bounds b = RendererBounds(cage);
            var pivot = new GameObject("CagePivot").transform;
            pivot.SetParent(movers, false);
            pivot.position = new Vector3(3.2f, 5.75f, 13f);
            cage.transform.position += pivot.position - new Vector3(b.center.x, b.max.y, b.center.z);
            cage.transform.SetParent(pivot, true);
            foreach (var c in cage.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            Dynamic(cage);
            amb.swing = pivot;
            log.Append("cage " + Mathf.RoundToInt(b.size.y * 100f) / 100f + " m tall swings at " + pivot.position + "; ");
        }

        // The left cell door 6 m ahead drifts ajar now and then (hinge at the end nearer the player).
        Transform door = null;
        float best = 99f;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Door_A") && t.parent != null && t.parent.name == "Doors")
            {
                float d = (t.position - new Vector3(-6.2f, 0f, 6f)).sqrMagnitude;
                if (d < best) { best = d; door = t; }
            }
        if (door != null)
        {
            Bounds b = RendererBounds(door.gameObject);
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(movers, false);
            hinge.position = new Vector3(b.center.x, b.min.y, b.min.z);
            door.SetParent(hinge, true);
            Dynamic(door.gameObject);
            amb.door = hinge;
            log.Append("door " + door.name + " hinged at " + hinge.position + "; ");
        }

        // The passing shadow: a hooded figure's shadow gliding along the right wall, 16 m to 9.5 m ahead (beside the menu panel,
        // which covers the middle of the hall; the pillar at z 13 hides it for a moment on the way).
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "PassingShadow";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(movers, false);
        quad.transform.localScale = new Vector3(1.5f, 3f, 1f);
        quad.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // lies on the right wall, facing into the hall
        string path = MatDir + "/M_MenuShadow.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetTexture("_BaseMap", Tex("T_MenuShadow.png"));
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.6f));
        mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
        mat.shaderKeywords = null;
        BaseShaderGUI.SetMaterialKeywords(mat);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        var qr = quad.GetComponent<MeshRenderer>();
        qr.sharedMaterial = mat;
        qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; qr.receiveShadows = false;
        qr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        amb.shadow = qr;
        amb.shadowFrom = new Vector3(5.66f, 2.1f, 16.5f);
        amb.shadowTo = new Vector3(5.66f, 2.1f, 9.5f);
        quad.transform.position = amb.shadowFrom;
        log.Append("shadow glides along the right wall, z 16.5 to 9.5");
        return log.ToString();
    }

    static void Dynamic(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            mr.receiveGI = ReceiveGI.LightProbes;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    public static string PlaceCanvas(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject canvas = null;
        foreach (var r in scene.GetRootGameObjects()) if (r.name == "MenuCanvas") canvas = r;
        if (canvas == null) return "menu canvas not found";
        canvas.transform.position = CanvasPosition;
        canvas.transform.localScale = Vector3.one * CanvasScale;
        int framed = 0;
        foreach (var img in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if (img.name != "Panel" && img.GetComponent<ExitConfirmPanel>() == null) continue;
            var c = img.color; c.a = 1f; img.color = c;   // opaque: the Outline draws offset copies behind it, which tint a see-through panel
            var o = img.GetComponent<UnityEngine.UI.Outline>();
            if (o == null) o = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = new Color(0.85f, 0.55f, 0.25f, 0.7f);
            o.effectDistance = new Vector2(3f, -3f);
            framed++;
        }
        return "menu canvas at " + CanvasPosition + " (" + CanvasPosition.z + " m ahead), scale " + CanvasScale + "; warm frame on " + framed + " panels";
    }

    public const string MixerPath = "Assets/Audio/StrikeFitMixer.mixer";

    /// <summary>Creates StrikeFitMixer.mixer with a "Music" group under Master and its volume exposed as "MusicVolume". Unity has
    /// no public API for this, so it goes through the editor's own AudioMixerController (what Create > Audio Mixer and the
    /// "Expose to script" menu call).</summary>
    public static string EnsureMixer()
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(MixerPath) != null) return "mixer exists: " + MixerPath;
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                 System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var asm = typeof(Editor).Assembly;
        var tc = asm.GetType("UnityEditor.Audio.AudioMixerController");
        var tg = asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
        var tp = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
        var ctrl = tc.GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { MixerPath });
        var master = tc.GetProperty("masterGroup", F).GetValue(ctrl);
        var group = tc.GetMethod("CreateNewGroup", F).Invoke(ctrl, new object[] { "Music", false });
        tc.GetMethod("AddChildToParent", F).Invoke(ctrl, new[] { group, master });
        var guid = tg.GetMethod("GetGUIDForVolume", F).Invoke(group, null);
        var path = System.Activator.CreateInstance(tp, F, null, new[] { group, guid }, null);
        tc.GetMethod("AddExposedParameter", F).Invoke(ctrl, new[] { path });
        var prop = tc.GetProperty("exposedParameters", F);
        var arr = (System.Array)prop.GetValue(ctrl);
        for (int i = 0; i < arr.Length; i++)
        {
            object p = arr.GetValue(i);
            var gf = p.GetType().GetField("guid", F);
            if (gf != null && gf.GetValue(p).Equals(guid)) { p.GetType().GetField("name", F).SetValue(p, "MusicVolume"); arr.SetValue(p, i); }
        }
        prop.SetValue(ctrl, arr);
        EditorUtility.SetDirty((Object)ctrl);
        EditorUtility.SetDirty((Object)group);
        AssetDatabase.SaveAssets();
        return "mixer created: " + MixerPath + " (Master > Music, exposed MusicVolume)";
    }

    static string BuildMusic(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject systems = null;
        foreach (var r in scene.GetRootGameObjects()) if (r.name == "MenuSystems") systems = r;
        if (systems == null) return "MenuSystems not found";
        var t = systems.transform.Find("MenuMusic");
        var go = t != null ? t.gameObject : new GameObject("MenuMusic");
        go.transform.SetParent(systems.transform, false);
        var src = go.GetComponent<AudioSource>();
        if (src == null) src = go.AddComponent<AudioSource>();
        src.playOnAwake = false; src.loop = true; src.spatialBlend = 0f; src.volume = 0f;
        var music = go.GetComponent<MenuMusic>();
        if (music == null) music = go.AddComponent<MenuMusic>();
        music.source = src;
        music.menu = systems.GetComponent<MainMenuController>();
        var mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(MixerPath);
        music.mixer = mixer;
        music.musicGroup = null;
        if (mixer != null)
            foreach (var g in mixer.FindMatchingGroups("Music")) if (g.name == "Music") music.musicGroup = g;
        src.outputAudioMixerGroup = music.musicGroup;
        return "music: " + (music.clip != null ? music.clip.name : "NO CLIP (warning at runtime)") + ", mixer " + (mixer != null) + ", group " + (music.musicGroup != null);
    }

    static LightingSettings LightingAsset()
    {
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
        if (ls == null)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LightingPath));
            ls = new LightingSettings { name = "MainMenuLighting" };
            AssetDatabase.CreateAsset(ls, LightingPath);
        }
        ls.bakedGI = true;
        ls.realtimeGI = false;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;   // the two torches' direct light stays realtime (it flickers)
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.lightmapResolution = 4f;
        ls.lightmapPadding = 2;
        ls.lightmapMaxSize = 1024;
        ls.lightmapCompression = LightmapCompression.NormalQuality;
        ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.ao = true; ls.aoMaxDistance = 1f;
        ls.maxBounces = 2;
        ls.directSampleCount = 32; ls.indirectSampleCount = 128; ls.environmentSampleCount = 64;
        ls.filteringMode = LightingSettings.FilterMode.Auto;
        EditorUtility.SetDirty(ls);
        return ls;
    }

    [MenuItem("Tools/Menu/Bake Menu Dungeon Lighting")]
    public static void BakeMenu() { Debug.Log(BakeLighting()); }

    /// <summary>Starts the bake of the open MainMenu scene (async: poll Lightmapping.isRunning).</summary>
    public static string BakeLighting()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != MenuPath) return "open MainMenu.unity first";
        Lightmapping.lightingSettings = LightingAsset();
        bool ok = Lightmapping.BakeAsync();
        return "bake started: " + ok;
    }

    /// <summary>After a bake: the lightmaps (and every menu texture) get an Android ASTC override (lightmaps default to ETC2).</summary>
    public static string CompressForAndroid()
    {
        var log = new StringBuilder();
        var paths = new List<string>();
        foreach (var d in LightmapSettings.lightmaps) if (d.lightmapColor != null) paths.Add(AssetDatabase.GetAssetPath(d.lightmapColor));
        foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { MatDir + "/Textures", PackRoot + "/Textures" })) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        int set = 0;
        foreach (var p in paths)
        {
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            if (imp == null) continue;
            var s = imp.GetPlatformTextureSettings("Android");
            const TextureImporterFormat want = TextureImporterFormat.ASTC_6x6;
            if (s.overridden && s.format == want && s.maxTextureSize <= 1024) continue;
            s.overridden = true; s.format = want; s.maxTextureSize = Mathf.Min(s.maxTextureSize > 0 ? s.maxTextureSize : 1024, 1024);
            imp.SetPlatformTextureSettings(s);
            imp.SaveAndReimport();
            set++;
        }
        log.Append("Android ASTC 6x6 override set on " + set + " of " + paths.Count + " textures (lightmaps, menu particle textures, Decrepit Dungeon)");
        return log.ToString();
    }

    /// <summary>Counts what the hall costs: renderers, triangles, lights, particle caps.</summary>
    public static string Budget()
    {
        var root = GameObject.Find(RootName);
        if (root == null) return "no " + RootName;
        int renderers = 0, tris = 0, realtime = 0, baked = 0, maxParticles = 0;
        var mats = new HashSet<Material>();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderers++;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (int)(mf.sharedMesh.GetIndexCount(i) / 3);
            foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);
        }
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (l.lightmapBakeType == LightmapBakeType.Baked) baked++; else realtime++;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) maxParticles += ps.main.maxParticles;
        return "hall renderers " + renderers + ", triangles (all) " + tris + ", materials " + mats.Count +
               ", lights realtime/mixed " + realtime + " baked " + baked + ", particle caps " + maxParticles;
    }

    // ───────────────────────── look-dev ─────────────────────────

    /// <summary>Renders the open scene from a point (yaw/pitch in degrees) to a PNG, for layout checks without Play mode.</summary>
    public static string RenderView(Vector3 pos, Vector3 euler, string file, float fov = 90f, int w = 640, int h = 400)
    {
        var go = new GameObject("TmpViewCam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            go.transform.position = pos; go.transform.eulerAngles = euler;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
            return file;
        }
        finally { Object.DestroyImmediate(go); }
    }
}
