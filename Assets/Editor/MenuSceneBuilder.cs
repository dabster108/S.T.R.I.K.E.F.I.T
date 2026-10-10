// MenuSceneBuilder.cs
// Purpose: Builds Assets/Scenes/MainMenu.unity from the Level 1 scene's own rig (OVRCameraRig with its laser pointer,
//          EventSystem + OVRInputModule, OVRRaycaster canvas), strips every battle-only part, and adds the menu: STRIKEFIT title,
//          Level 1 / 2 / 3 cards (one line each + a "Loading..." label), Exit Game with a confirm panel. Also adds the
//          OutcomeMenuButtons component to the three level scenes and sets Build Settings (MainMenu 0, Level 1-3, all enabled,
//          Results listed but disabled). Idempotent: Build() rewrites MainMenu.unity from scratch.
//          Environment: a dark floor and one thin ring, unlit materials, no lights, no shadows, no post-processing.
// Dependencies: MenuUi, MainMenuController, ExitConfirmPanel, OutcomeMenuButtons, SessionReporter
// Attach to: nothing (menu: Tools/Menu/Build Main Menu And Wire Scenes, or over Unity MCP)

using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class MenuSceneBuilder
{
    const string MenuPath = "Assets/Scenes/MainMenu.unity";
    const string SourcePath = "Assets/Scenes/Level1_Easy.unity";
    static readonly string[] LevelPaths = { "Assets/Scenes/Level1_Easy.unity", "Assets/Scenes/Level2_Medium.unity", "Assets/Scenes/Level3_Hard.unity" };

    [MenuItem("Tools/Menu/Build Main Menu And Wire Scenes")]
    public static void BuildMenu() { Debug.Log(BuildAll()); }

    public static string BuildAll()
    {
        var log = new StringBuilder();
        log.AppendLine(Build());
        log.AppendLine(WireLevels());
        log.AppendLine(SetBuildSettings());
        return log.ToString();
    }

    // ───────────────────────── the menu scene ─────────────────────────
    public static string Build()
    {
        var src = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(src, MenuPath, true);   // copy: the level scene is not touched
        var scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);

        // Strip everything that belongs to a fight.
        foreach (var root in scene.GetRootGameObjects())
        {
            string n = root.name;
            if (n == "Directional Light" || n == "Environment_LiteDungeon" || n == "BattleSystems" || n == "MonsterSpawnPoint" || n == "PlayerSpawn" || n == "BackgroundMusic")
                Object.DestroyImmediate(root);
        }
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        foreach (var t in new System.Type[] { typeof(PlayerDamageVignette), typeof(PlayerExertionAudio), typeof(PlayerCombat), typeof(PlayerHealth) })
        {
            var c = rig.GetComponent(t);
            if (c != null) Object.DestroyImmediate(c);
        }
        var rigAudio = rig.GetComponent<AudioSource>();
        if (rigAudio != null) Object.DestroyImmediate(rigAudio);
        foreach (var name in new[] { "PlayerHammer", "PlayerHammer_Left", "PlayerShield", "DamageVignette" })
            foreach (var tr in rig.GetComponentsInChildren<Transform>(true))
                if (tr != null && tr.name == name) { Object.DestroyImmediate(tr.gameObject); break; }
        var breathing = GameObject.Find("PlayerBreathingAudio");
        if (breathing != null) Object.DestroyImmediate(breathing);
        rig.transform.position = Vector3.zero; rig.transform.rotation = Quaternion.identity;

        // Dark, flat, no skybox, solid-colour cameras (no post-processing is enabled anywhere).
        var bg = new Color(0.015f, 0.015f, 0.03f);
        foreach (var cam in rig.GetComponentsInChildren<Camera>(true)) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg; }
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.06f, 0.06f, 0.1f);
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;

        BuildEnvironment();
        BuildCanvas();
        BuildSystems();
        BuildBreathingTest(rig);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"MainMenu.unity built from {SourcePath}: rig kept (laser, EventSystem, OVRRaycaster), battle parts stripped, lights {Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Length}, AudioListeners {Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length}.";
    }

    static Material UnlitMaterial(string path, Color color)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void BuildEnvironment()
    {
        var root = new GameObject("MenuEnvironment");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor"; floor.transform.SetParent(root.transform, false);
        floor.transform.localScale = new Vector3(3f, 1f, 3f);
        Object.DestroyImmediate(floor.GetComponent<Collider>());   // only the canvas stops the laser beam
        var fr = floor.GetComponent<MeshRenderer>();
        fr.sharedMaterial = UnlitMaterial("Assets/Materials/Menu/M_MenuFloor.mat", new Color(0.035f, 0.035f, 0.055f));
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fr.receiveShadows = false;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Ring"; ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        ring.transform.localScale = new Vector3(5.2f, 0.005f, 5.2f);
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        var rr = ring.GetComponent<MeshRenderer>();
        rr.sharedMaterial = UnlitMaterial("Assets/Materials/Menu/M_MenuRing.mat", new Color(0.18f, 0.12f, 0.3f));
        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;
        var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        inner.name = "RingInner"; inner.transform.SetParent(root.transform, false);
        inner.transform.localPosition = new Vector3(0f, 0.012f, 0f);
        inner.transform.localScale = new Vector3(5.0f, 0.005f, 5.0f);
        Object.DestroyImmediate(inner.GetComponent<Collider>());
        var ir = inner.GetComponent<MeshRenderer>();
        ir.sharedMaterial = floor.GetComponent<MeshRenderer>().sharedMaterial;
        ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ir.receiveShadows = false;
    }

    static void BuildCanvas()
    {
        var canvasGo = GameObject.Find("CombatCanvas");
        canvasGo.name = "MenuCanvas";
        var rt = (RectTransform)canvasGo.transform;
        for (int i = rt.childCount - 1; i >= 0; i--) Object.DestroyImmediate(rt.GetChild(i).gameObject);
        rt.sizeDelta = new Vector2(1000f, 720f);
        rt.position = new Vector3(0f, 1.55f, 2.5f);
        rt.rotation = Quaternion.identity;
        rt.localScale = Vector3.one * 0.0025f;   // 2.5 m wide at 2.5 m
        var box = canvasGo.GetComponent<BoxCollider>();
        if (box == null) box = canvasGo.AddComponent<BoxCollider>();
        box.center = Vector3.zero; box.size = new Vector3(1000f, 720f, 4f);   // the laser beam stops on the menu
        var oldGroup = canvasGo.GetComponent<CanvasGroup>();
        if (oldGroup != null) Object.DestroyImmediate(oldGroup);

        var backdrop = MenuUi.Panel(rt, "Backdrop", Vector2.zero, new Vector2(1000f, 720f), new Color(0.03f, 0.03f, 0.06f, 0.9f));
        backdrop.raycastTarget = false;
        // Everything clickable lives under Content, which has its own CanvasGroup: the confirm panel is a sibling of it, so
        // locking Content while the confirm is open does not lock the confirm's own buttons.
        var content = MenuUi.Rect(rt, "Content", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var group = content.gameObject.AddComponent<CanvasGroup>();
        MenuUi.Label(content, "Title", "STRIKEFIT", 120, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 292f), new Vector2(900f, 140f), FontStyle.Bold);
        MenuUi.Label(content, "Subtitle", "Choose your fight", 40, new Color(0.7f, 0.75f, 0.9f), TextAnchor.MiddleCenter, new Vector2(0f, 212f), new Vector2(900f, 56f));

        string[] titles = { "LEVEL 1\nEASY", "LEVEL 2\nMEDIUM", "LEVEL 3\nHARD" };
        string[] lines =
        {
            "Dual hammers vs a sword brute in a torch-lit dungeon.",
            "Dual swords vs a night vampire with a two-handed axe.",
            "Grab a weapon on the docks and face the Twin-Axe Brute.",
        };
        Color[] colors = { new Color(0.1f, 0.42f, 0.3f, 0.96f), new Color(0.36f, 0.2f, 0.58f, 0.96f), new Color(0.58f, 0.16f, 0.16f, 0.96f) };
        string[] scenes = { SceneFlow.Level1, SceneFlow.Level2, SceneFlow.Level3 };
        var cards = new MainMenuController.LevelCard[3];
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 320f;
            var b = MenuUi.MakeButton(content, "Level" + (i + 1) + "Button", "", 40, colors[i], new Vector2(x, -2f), new Vector2(300f, 360f));
            var label = b.GetComponentInChildren<Text>();
            Object.DestroyImmediate(label.gameObject);
            MenuUi.Label(b.transform, "Title", titles[i], 46, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 112f), new Vector2(276f, 120f), FontStyle.Bold);
            MenuUi.Label(b.transform, "Description", lines[i], 31, new Color(0.92f, 0.94f, 1f), TextAnchor.UpperCenter, new Vector2(0f, -42f), new Vector2(270f, 190f));
            var status = MenuUi.Label(b.transform, "Status", "", 34, new Color(1f, 0.9f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0f, -156f), new Vector2(276f, 46f), FontStyle.Bold);
            cards[i] = new MainMenuController.LevelCard { scene = scenes[i], button = b, status = status };
        }
        var exit = MenuUi.MakeButton(content, "ExitGameButton", "Exit Game", 40, new Color(0.45f, 0.45f, 0.5f, 0.96f), new Vector2(0f, -240f), new Vector2(320f, 84f));
        MenuUi.Label(content, "Hint", "Point with the right controller and pull the trigger", 26, new Color(0.6f, 0.64f, 0.75f), TextAnchor.MiddleCenter, new Vector2(0f, -318f), new Vector2(900f, 36f));
        var confirm = ExitConfirmPanel.Build(rt, new[] { group }, new Vector2(560f, 260f), 48);

        var systems = new GameObject("MenuSystems");
        var mc = systems.AddComponent<MainMenuController>();
        mc.levels = cards; mc.exitButton = exit; mc.confirm = confirm; mc.content = group;
    }

    /// <summary>Breathing hardware test (Development builds, hidden until both grips are held 3 s): the same PlayerExertionAudio the
    /// levels use, on the menu rig, plus a small laser panel with a "Test breathing" button and a readout.</summary>
    static void BuildBreathingTest(OVRCameraRig rig)
    {
        var ex = rig.gameObject.AddComponent<PlayerExertionAudio>();
        BreathingSetup.ApplyTo(ex);
        ex.selfTestInDevBuild = false;
        var canvas = GameObject.Find("MenuCanvas").transform;
        var panel = MenuUi.Panel(canvas, "BreathingTestPanel", new Vector2(345f, 285f), new Vector2(290f, 150f), new Color(0.05f, 0.1f, 0.08f, 0.97f));
        MenuUi.Label(panel.transform, "Title", "DEBUG: breathing", 22, new Color(0.75f, 1f, 0.8f), TextAnchor.MiddleCenter, new Vector2(0f, 56f), new Vector2(270f, 30f), FontStyle.Bold);
        var button = MenuUi.MakeButton(panel.transform, "TestBreathingButton", "Test breathing", 26, new Color(0.12f, 0.4f, 0.3f, 0.98f), new Vector2(0f, 14f), new Vector2(250f, 52f));
        var readout = MenuUi.Label(panel.transform, "Readout", "", 18, new Color(0.85f, 0.95f, 0.88f), TextAnchor.UpperCenter, new Vector2(0f, -42f), new Vector2(270f, 66f));
        var systems = GameObject.Find("MenuSystems");
        var bt = systems.AddComponent<BreathingTestPanel>();
        bt.panel = panel.gameObject; bt.testButton = button; bt.readout = readout; bt.secondsPerTier = 5f;
    }

    static void BuildSystems()
    {
        var systems = GameObject.Find("MenuSystems");
        // The same reporter the levels carry: the wake ping starts as soon as the app opens (Render cold start), and Exit Game
        // flushes it. Production URL, 60 s wake timeout, 5 s request timeouts are the component's own defaults.
        var rep = systems.AddComponent<SessionReporter>();
        rep.baseUrl = "https://s-t-r-i-k-e-f-i-t.onrender.com";
        rep.wakeBackendOnLoad = true; rep.wakeTimeoutSeconds = 60; rep.requestTimeoutSeconds = 5; rep.verboseLogging = false;
    }

    // ───────────────────────── the level scenes ─────────────────────────
    public static string WireLevels()
    {
        var log = new StringBuilder();
        foreach (var path in LevelPaths)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var canvas = GameObject.Find("CombatCanvas");
            if (canvas == null) { log.AppendLine(path + ": no CombatCanvas"); continue; }
            var c = canvas.GetComponent<OutcomeMenuButtons>();
            if (c == null) c = canvas.AddComponent<OutcomeMenuButtons>();
            c.buttonsEnabled = true; c.panelLift = 0.08f; c.prompt = "Play again?";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"{path}: OutcomeMenuButtons on CombatCanvas");
        }
        return log.ToString();
    }

    // ───────────────────────── Build Settings ─────────────────────────
    public static string SetBuildSettings()
    {
        var scenes = new[]
        {
            new EditorBuildSettingsScene(MenuPath, true),
            new EditorBuildSettingsScene("Assets/Scenes/Level1_Easy.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Level2_Medium.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Level3_Hard.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Results.unity", false),
        };
        EditorBuildSettings.scenes = scenes;
        var sb = new StringBuilder("Build Settings:");
        for (int i = 0; i < scenes.Length; i++) sb.Append($" [{i}] {System.IO.Path.GetFileNameWithoutExtension(scenes[i].path)} {(scenes[i].enabled ? "on" : "off")};");
        return sb.ToString();
    }
}
