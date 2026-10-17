// MenuSceneBuilder.cs
// Purpose: Builds Assets/Scenes/MainMenu.unity from the Level 1 scene's own rig (OVRCameraRig with its laser pointer,
//          EventSystem + OVRInputModule, OVRRaycaster canvas), strips every battle-only part, and adds the menu: STRIKEFIT title,
//          home column and pages (see Layout below), Exit Game with a confirm panel. Also adds the
//          OutcomeMenuButtons component to the three level scenes and sets Build Settings (MainMenu 0, Level 1-3, all enabled,
//          Results listed but disabled). Idempotent: Build() rewrites MainMenu.unity from scratch.
//          Environment: a dark floor and one thin ring, unlit materials, static (batched), no lights, no shadows, no
//          post-processing. Layout (2026-10-17): rig at 1.6 m like the levels (EyeLevel origin), a two-column grid on a dark
//          panel (Story Mode | Start Game, Guardians | Scoreboard, Change Name | Exit Game, "Playing as" above), pages: Story
//          Mode (Continue / New Story / Watch intro), the story slides, Guardians, level select, Scoreboard, name picker, each
//          with Back.
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
        // EyeLevel tracking origin (same as the levels): the eyes start at the rig's own height, so the rig must stand where the
        // eyes are (1.6 m, the levels' value). At y = 0 the eyes were on the floor plane: the menu hung 0.65-2.45 m above them
        // and the floor was edge-on (the "menu cut off / blank environment" bug on the Quest).
        rig.transform.position = new Vector3(0f, 1.6f, 0f); rig.transform.rotation = Quaternion.identity;

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

    // Static: batched with each other, never move; no shadows anywhere.
    const StaticEditorFlags EnvFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

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
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, EnvFlags);
    }

    // ───────────────────────── the menu canvas ─────────────────────────
    // World-space, 2.5 m in front of the eyes, centre 0.18 m below eye level (rig at 1.6 m, EyeLevel origin = eyes at 1.6 m).
    // 1200 x 800 units at 2 mm per unit = 2.4 x 1.6 m: +-25.6 deg across, -21.4 to +13.9 deg up, inside the Quest's view without
    // turning the head. Legacy Text cap height = 0.716 x font size x 2 mm: font 24 = 3.4 cm, 46 = 6.6 cm.
    public const float CanvasScale = 0.002f;
    public static readonly Vector2 CanvasSize = new Vector2(1200f, 800f);
    public static readonly Vector3 CanvasPosition = new Vector3(0f, 1.42f, 2.5f);
    public static readonly Color PagePanel = new Color(0.03f, 0.035f, 0.07f, 0.9f);
    public static readonly Color StartColor = new Color(0.1f, 0.42f, 0.3f, 0.96f);
    public static readonly Color NameColor = new Color(0.2f, 0.35f, 0.6f, 0.96f);
    public static readonly Color ScoreColor = new Color(0.36f, 0.2f, 0.58f, 0.96f);
    public static readonly Color StoryColor = new Color(0.55f, 0.36f, 0.12f, 0.96f);
    public static readonly Color GuardianColor = new Color(0.5f, 0.18f, 0.18f, 0.96f);
    public static readonly Color ExitColor = new Color(0.45f, 0.45f, 0.5f, 0.96f);
    public static readonly Color BackColor = new Color(0.25f, 0.25f, 0.3f, 0.96f);
    public static readonly Color HintColor = new Color(0.62f, 0.66f, 0.78f);
    public static readonly Color[] LevelColors = { new Color(0.1f, 0.42f, 0.3f, 0.96f), new Color(0.36f, 0.2f, 0.58f, 0.96f), new Color(0.58f, 0.16f, 0.16f, 0.96f) };

    static void BuildCanvas()
    {
        var canvasGo = GameObject.Find("CombatCanvas");
        canvasGo.name = "MenuCanvas";
        var rt = (RectTransform)canvasGo.transform;
        for (int i = rt.childCount - 1; i >= 0; i--) Object.DestroyImmediate(rt.GetChild(i).gameObject);
        var outcome = canvasGo.GetComponent<OutcomeMenuButtons>();   // copied with the Level 1 canvas; the menu has no fight panels
        if (outcome != null) Object.DestroyImmediate(outcome);
        var introLine = canvasGo.GetComponent<LevelIntroLine>();      // a level's pre-battle line: not in the menu
        if (introLine != null) Object.DestroyImmediate(introLine);
        rt.sizeDelta = CanvasSize;
        rt.position = CanvasPosition;
        rt.rotation = Quaternion.identity;
        rt.localScale = Vector3.one * CanvasScale;
        var box = canvasGo.GetComponent<BoxCollider>();
        if (box == null) box = canvasGo.AddComponent<BoxCollider>();
        box.center = Vector3.zero; box.size = new Vector3(CanvasSize.x, CanvasSize.y, 4f);   // the laser beam stops on the menu
        var oldGroup = canvasGo.GetComponent<CanvasGroup>();
        if (oldGroup != null) Object.DestroyImmediate(oldGroup);

        // Everything clickable lives under Content, which has its own CanvasGroup: the confirm panel is a sibling of it, so
        // locking Content while the confirm is open does not lock the confirm's own buttons.
        var content = MenuUi.Rect(rt, "Content", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var group = content.gameObject.AddComponent<CanvasGroup>();

        var systems = new GameObject("MenuSystems");
        var mc = systems.AddComponent<MainMenuController>();
        mc.content = group;

        // Reading order (left to right, top to bottom): the story first, free play next to it, Exit last.
        var homeButtons = new System.Collections.Generic.List<HomeButton>();
        homeButtons.Add(new HomeButton("StoryModeButton", "Story Mode", StoryColor));
        homeButtons.Add(new HomeButton("StartGameButton", "Start Game", StartColor));
        homeButtons.Add(new HomeButton("GuardiansButton", "Guardians", GuardianColor));
        homeButtons.Add(new HomeButton("ScoreboardButton", "Scoreboard", ScoreColor));
        homeButtons.Add(new HomeButton("ChangeNameButton", "Change Name", NameColor));
        homeButtons.Add(new HomeButton("ExitGameButton", "Exit Game", ExitColor));
        var built = BuildHome(content, mc, homeButtons);
        mc.startGameButton = built["StartGameButton"];
        mc.changeNameButton = built["ChangeNameButton"];
        mc.exitButton = built["ExitGameButton"];
        mc.storyModeButton = built["StoryModeButton"];
        mc.guardiansButton = built["GuardiansButton"];

        BuildLevelPage(content, mc);
        BuildNamePage(content, mc, systems);
        BuildScoreboardPage(content, mc, built["ScoreboardButton"], systems);
        var intro = BuildStoryPage(content, mc, null, systems);   // the intro slides: opened from Story Mode (Watch intro, New Story)
        BuildStoryModePage(content, mc, built["StoryModeButton"], systems, intro);
        BuildGuardiansPage(content, mc, built["GuardiansButton"], systems);

        var pages = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < content.childCount; i++) pages.Add(content.GetChild(i).gameObject);
        mc.pages = pages.ToArray();
        mc.confirm = ExitConfirmPanel.Build(rt, new[] { group }, new Vector2(600f, 280f), 48);
    }

    /// <summary>A home-column button: object name, label, colour.</summary>
    public struct HomeButton
    {
        public string name, label; public Color color;
        public HomeButton(string n, string l, Color c) { name = n; label = l; color = c; }
    }

    /// <summary>An empty page root covering the canvas (pages are siblings under Content; one is shown at a time).</summary>
    public static RectTransform PageRoot(RectTransform content, string name)
    {
        var page = MenuUi.Rect(content, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return page;
    }

    // A two-column grid on a dark semi-opaque panel: title, "Playing as", three rows of two buttons, a hint. 1000 x 596 units
    // (2.0 x 1.19 m at 2.5 m: about 44 x 27 degrees), so all six entries sit well inside the Quest's view without turning the
    // head (a single column of six would be 1.83 m tall and push Exit Game under the view). Positions are computed (no layout
    // group), so nothing can collapse at runtime.
    static System.Collections.Generic.Dictionary<string, Button> BuildHome(RectTransform content, MainMenuController mc, System.Collections.Generic.List<HomeButton> buttons)
    {
        const float width = 1000f, btnW = 450f, btnH = 92f, gap = 14f, colGap = 30f, titleH = 112f, nameH = 46f, hintH = 34f, pad = 26f;
        int rows = (buttons.Count + 1) / 2;
        float h = pad + titleH + 6f + nameH + 22f + rows * btnH + (rows - 1) * gap + 20f + hintH + pad;
        var page = PageRoot(content, "HomePage");
        var panel = MenuUi.Panel(page, "Panel", Vector2.zero, new Vector2(width, h), PagePanel);
        panel.raycastTarget = false;
        float y = h * 0.5f - pad - titleH * 0.5f;
        MenuUi.Label(page, "Title", "STRIKEFIT", 96, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, y), new Vector2(width - 20f, titleH), FontStyle.Bold);
        y -= titleH * 0.5f + 6f + nameH * 0.5f;
        mc.playingAsLabel = MenuUi.Label(page, "PlayingAs", "Playing as", 34, new Color(0.85f, 0.88f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, y), new Vector2(width - 30f, nameH));
        y -= nameH * 0.5f + 22f + btnH * 0.5f;
        var result = new System.Collections.Generic.Dictionary<string, Button>();
        float colX = (btnW + colGap) * 0.5f;
        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            float x = i % 2 == 0 ? -colX : colX;
            float by = y - (i / 2) * (btnH + gap);
            result[b.name] = MenuUi.MakeButton(page, b.name, b.label, 46, b.color, new Vector2(x, by), new Vector2(btnW, btnH));
        }
        y -= (rows - 1) * (btnH + gap);           // the last row's centre
        y -= btnH * 0.5f + 20f + hintH * 0.5f;
        MenuUi.Label(page, "Hint", "Point with the right controller, pull the trigger. B goes back.", 24, HintColor, TextAnchor.MiddleCenter, new Vector2(0f, y), new Vector2(width - 20f, hintH));
        mc.homePage = page.gameObject;
        return result;
    }

    static readonly string[] CardTitles = { "LEVEL 1\nEASY", "LEVEL 2\nMEDIUM", "LEVEL 3\nHARD" };
    static readonly string[] CardLines =
    {
        "Dual hammers vs a sword brute in a torch-lit dungeon.",
        "Dual swords vs a night vampire with a two-handed axe.",
        "Grab a weapon on the docks and face the Twin-Axe Brute.",
    };

    /// <summary>Optional one-line text per level card (the story's level intro), filled by a later feature; null = none.</summary>
    public static string[] CardIntroLines;

    // Start Game: the three level cards. A card click selects it (yellow frame), Play starts it, Back returns home.
    static void BuildLevelPage(RectTransform content, MainMenuController mc)
    {
        var page = PageRoot(content, "LevelPage");
        var panel = MenuUi.Panel(page, "Panel", Vector2.zero, new Vector2(1120f, 770f), PagePanel);
        panel.raycastTarget = false;
        MenuUi.Label(page, "Title", "Choose your fight", 52, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 330f), new Vector2(1000f, 70f), FontStyle.Bold);
        string[] scenes = { SceneFlow.Level1, SceneFlow.Level2, SceneFlow.Level3 };
        var cards = new MainMenuController.LevelCard[3];
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 350f;
            var b = MenuUi.MakeButton(page, "Level" + (i + 1) + "Button", "", 40, LevelColors[i], new Vector2(x, 40f), new Vector2(320f, 430f));
            Object.DestroyImmediate(b.GetComponentInChildren<Text>().gameObject);
            var frame = MenuUi.Rect(b.transform, "SelectedFrame", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var gold = new Color(1f, 0.85f, 0.3f, 1f);
            const float t = 9f;
            Edge(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -t), Vector2.zero, gold);
            Edge(frame, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, t), gold);
            Edge(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(t, 0f), gold);
            Edge(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-t, 0f), Vector2.zero, gold);
            frame.gameObject.SetActive(false);
            MenuUi.Label(b.transform, "Title", CardTitles[i], 44, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 145f), new Vector2(296f, 110f), FontStyle.Bold);
            MenuUi.Label(b.transform, "Description", CardLines[i], 29, new Color(0.92f, 0.94f, 1f), TextAnchor.UpperCenter, new Vector2(0f, 22f), new Vector2(290f, 124f));
            if (CardIntroLines != null && i < CardIntroLines.Length && !string.IsNullOrEmpty(CardIntroLines[i]))
                MenuUi.Label(b.transform, "Intro", CardIntroLines[i], 25, new Color(1f, 0.92f, 0.7f), TextAnchor.UpperCenter, new Vector2(0f, -98f), new Vector2(290f, 112f), FontStyle.Italic);
            var status = MenuUi.Label(b.transform, "Status", "", 32, new Color(1f, 0.9f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0f, -184f), new Vector2(296f, 44f), FontStyle.Bold);
            cards[i] = new MainMenuController.LevelCard { scene = scenes[i], button = b, status = status, selectedFrame = frame.gameObject };
        }
        mc.levels = cards;
        mc.levelBackButton = MenuUi.MakeButton(page, "LevelBackButton", "Back", 42, BackColor, new Vector2(-370f, -312f), new Vector2(260f, 90f));
        mc.playButton = MenuUi.MakeButton(page, "PlayButton", "Select a level", 46, StartColor, new Vector2(150f, -312f), new Vector2(480f, 96f));
        mc.playLabel = mc.playButton.GetComponentInChildren<Text>();
        mc.playButton.interactable = false;
        mc.levelPage = page.gameObject;
        page.gameObject.SetActive(false);
    }

    // Scoreboard: title, rule note, tabs All / L1 / L2 / L3, a header and 10 pre-built rows, then Back, "Your best" and the
    // status line. Rows are 42 units (8.4 cm) high; row text 28-30 pt (cap 4.0-4.3 cm at 2.5 m).
    static void BuildScoreboardPage(RectTransform content, MainMenuController mc, Button open, GameObject systems)
    {
        var page = PageRoot(content, "ScoreboardPage");
        var panel = MenuUi.Panel(page, "Panel", Vector2.zero, new Vector2(1120f, 770f), PagePanel);
        panel.raycastTarget = false;
        MenuUi.Label(page, "Title", "Scoreboard", 50, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 338f), new Vector2(1000f, 60f), FontStyle.Bold);
        MenuUi.Label(page, "Rule", "Ranked by best score per level", 24, HintColor, TextAnchor.MiddleCenter, new Vector2(0f, 292f), new Vector2(1000f, 32f));
        var sp = systems.AddComponent<ScoreboardPanel>();
        sp.menu = mc; sp.page = page.gameObject; sp.openButton = open;
        string[] tabNames = { "All", "L1", "L2", "L3" };
        sp.tabs = new Button[4];
        for (int i = 0; i < 4; i++)
            sp.tabs[i] = MenuUi.MakeButton(page, "Tab" + tabNames[i], tabNames[i], 36, sp.tabColor, new Vector2(-330f + i * 220f, 232f), new Vector2(200f, 70f));

        var grey = new Color(0.62f, 0.66f, 0.78f);
        MenuUi.Label(page, "HeadRank", "#", 24, grey, TextAnchor.MiddleCenter, new Vector2(-490f, 172f), new Vector2(60f, 30f));
        MenuUi.Label(page, "HeadName", "Player", 24, grey, TextAnchor.MiddleLeft, new Vector2(-180f, 172f), new Vector2(560f, 30f));
        MenuUi.Label(page, "HeadScore", "Score", 24, grey, TextAnchor.MiddleRight, new Vector2(180f, 172f), new Vector2(160f, 30f));
        MenuUi.Label(page, "HeadLevel", "Level", 24, grey, TextAnchor.MiddleCenter, new Vector2(330f, 172f), new Vector2(100f, 30f));
        MenuUi.Label(page, "HeadDate", "Date", 24, grey, TextAnchor.MiddleCenter, new Vector2(470f, 172f), new Vector2(150f, 30f));

        var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        sp.rows = new ScoreboardPanel.RowView[10];
        for (int i = 0; i < 10; i++)
        {
            var row = MenuUi.Centred(page, "Row" + (i + 1), new Vector2(0f, 128f - i * 44f), new Vector2(1080f, 42f));
            var bg = row.gameObject.AddComponent<Image>(); bg.color = Color.clear; bg.raycastTarget = false;
            var medal = MenuUi.Centred(row, "Medal", new Vector2(-490f, 0f), new Vector2(40f, 40f)).gameObject.AddComponent<Image>();
            medal.sprite = knob; medal.raycastTarget = false;
            var v = new ScoreboardPanel.RowView
            {
                root = row.gameObject, background = bg, medal = medal,
                rank = MenuUi.Label(row, "Rank", "", 28, Color.white, TextAnchor.MiddleCenter, new Vector2(-490f, 0f), new Vector2(60f, 40f), FontStyle.Bold),
                name = MenuUi.Label(row, "Name", "", 30, Color.white, TextAnchor.MiddleLeft, new Vector2(-180f, 0f), new Vector2(560f, 40f)),
                score = MenuUi.Label(row, "Score", "", 30, Color.white, TextAnchor.MiddleRight, new Vector2(180f, 0f), new Vector2(160f, 40f), FontStyle.Bold),
                date = MenuUi.Label(row, "Date", "", 26, new Color(0.8f, 0.83f, 0.92f), TextAnchor.MiddleCenter, new Vector2(470f, 0f), new Vector2(150f, 40f)),
            };
            v.name.horizontalOverflow = HorizontalWrapMode.Overflow;   // names are capped at 20 characters by the server
            v.chip = MenuUi.Centred(row, "Chip", new Vector2(330f, 0f), new Vector2(80f, 34f)).gameObject.AddComponent<Image>();
            v.chip.raycastTarget = false;
            v.chipText = MenuUi.Label(v.chip.transform, "ChipText", "", 24, Color.white, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(80f, 34f), FontStyle.Bold);
            row.gameObject.SetActive(false);
            sp.rows[i] = v;
        }
        sp.messageText = MenuUi.Label(page, "Message", "Loading scores...", 36, new Color(0.9f, 0.92f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, -40f), new Vector2(1000f, 60f));
        sp.backButton = MenuUi.MakeButton(page, "ScoreboardBackButton", "Back", 42, BackColor, new Vector2(-420f, -332f), new Vector2(220f, 84f));
        sp.youText = MenuUi.Label(page, "YourBest", "", 30, new Color(1f, 0.88f, 0.45f), TextAnchor.MiddleLeft, new Vector2(110f, -314f), new Vector2(640f, 40f), FontStyle.Bold);
        sp.statusText = MenuUi.Label(page, "Status", "", 24, HintColor, TextAnchor.MiddleLeft, new Vector2(110f, -354f), new Vector2(640f, 32f));
        page.gameObject.SetActive(false);
    }

    static void Edge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color c)
    {
        var e = MenuUi.Rect(parent, name, aMin, aMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        e.offsetMin = offMin; e.offsetMax = offMax;
        var img = e.gameObject.AddComponent<Image>();
        img.color = c; img.raycastTarget = false;
    }

    // Change Name: the level scenes' PlayerNamePicker in menu mode. It fills PickerArea (the same 900 x 500 layout it has on the
    // fight canvas) and clones the hidden template button for every key, exactly as it clones the Start button in a level.
    static void BuildNamePage(RectTransform content, MainMenuController mc, GameObject systems)
    {
        var page = PageRoot(content, "NamePage");
        var area = MenuUi.Centred(page, "PickerArea", new Vector2(0f, 20f), new Vector2(900f, 520f));
        var template = MenuUi.MakeButton(area, "PickerTemplate", "", 30, NameColor, Vector2.zero, new Vector2(200f, 60f));
        template.gameObject.SetActive(false);
        var picker = systems.AddComponent<PlayerNamePicker>();
        picker.menuMode = true;
        picker.templateButton = template;
        picker.confirmRename = true;
        picker.autoSelectSeconds = 0f;
        mc.namePicker = picker;
        mc.namePage = page.gameObject;
        page.gameObject.SetActive(false);
    }

    /// <summary>Breathing hardware test (Development builds, hidden until both grips are held 3 s): the same PlayerExertionAudio the
    /// levels use, on the menu rig, plus a small laser panel with a "Test breathing" button and a readout.</summary>
    static void BuildBreathingTest(OVRCameraRig rig)
    {
        var ex = rig.gameObject.AddComponent<PlayerExertionAudio>();
        BreathingSetup.ApplyTo(ex);
        ex.selfTestInDevBuild = false;
        var canvas = GameObject.Find("MenuCanvas").transform;
        var panel = MenuUi.Panel(canvas, "BreathingTestPanel", new Vector2(452f, 312f), new Vector2(290f, 150f), new Color(0.05f, 0.1f, 0.08f, 0.97f));
        MenuUi.Label(panel.transform, "Title", "DEBUG: breathing", 22, new Color(0.75f, 1f, 0.8f), TextAnchor.MiddleCenter, new Vector2(0f, 56f), new Vector2(270f, 30f), FontStyle.Bold);
        var button = MenuUi.MakeButton(panel.transform, "TestBreathingButton", "Test breathing", 26, new Color(0.12f, 0.4f, 0.3f, 0.98f), new Vector2(0f, 14f), new Vector2(250f, 52f));
        var readout = MenuUi.Label(panel.transform, "Readout", "", 18, new Color(0.85f, 0.95f, 0.88f), TextAnchor.UpperCenter, new Vector2(0f, -42f), new Vector2(270f, 66f));
        var systems = GameObject.Find("MenuSystems");
        var bt = systems.AddComponent<BreathingTestPanel>();
        bt.panel = panel.gameObject; bt.testButton = button; bt.readout = readout; bt.secondsPerTier = 5f;
    }

    // Story: full dark panel, warm title "The Legend", slide text on a fading CanvasGroup (1 s cross-fade, no mesh rebuild),
    // slide counter, Skip button, a warm point light for the torch flicker (off; StoryPanel.torchFlicker enables it while playing).
    // The glow Graphic behind the text pulses with the light at < 1 Hz so nothing flashes faster than 2 Hz (comfort rule).
    static StoryPanel BuildStoryPage(RectTransform content, MainMenuController mc, Button open, GameObject systems)
    {
        var page = PageRoot(content, "StoryPage");
        var panel = MenuUi.Panel(page, "Panel", Vector2.zero, new Vector2(1120f, 770f), new Color(0.008f, 0.01f, 0.018f, 0.98f));
        panel.raycastTarget = false;
        MenuUi.Label(page, "Title", "The Legend", 44, new Color(0.9f, 0.72f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0f, 328f), new Vector2(900f, 56f), FontStyle.Bold);

        // Glow: a faint warm circle behind the slide text; StoryPanel drives its alpha with the torch light.
        var glowRt = MenuUi.Centred(page, "TextGlow", new Vector2(0f, -10f), new Vector2(920f, 460f));
        var glowImg = glowRt.gameObject.AddComponent<Image>();
        glowImg.color = new Color(0.85f, 0.65f, 0.25f, 0.08f); glowImg.raycastTarget = false;

        // SlideGroup: its CanvasGroup fades only the text (not the page or glow).
        var sgRt = MenuUi.Centred(page, "SlideGroup", new Vector2(0f, -10f), new Vector2(980f, 460f));
        var sg = sgRt.gameObject.AddComponent<CanvasGroup>();
        sg.blocksRaycasts = false;
        var slideText = MenuUi.Label(sgRt, "SlideText", "", 44, new Color(0.97f, 0.97f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(940f, 440f));
        slideText.lineSpacing = 1.25f;

        var counter = MenuUi.Label(page, "Counter", "1 / 3", 26, new Color(0.75f, 0.78f, 0.88f), TextAnchor.MiddleRight, new Vector2(492f, 316f), new Vector2(220f, 34f));
        var skipBtn = MenuUi.MakeButton(page, "SkipButton", "Skip", 38, BackColor, new Vector2(398f, -322f), new Vector2(200f, 76f));

        // No realtime light (the menu has none): the glow behind the text pulses slowly instead (StoryPanel, under 1 Hz).
        var sp = systems.AddComponent<StoryPanel>();
        sp.menu = mc; sp.page = page.gameObject; sp.openButton = open;
        sp.skipButton = skipBtn; sp.slideText = slideText; sp.slideGroup = sg;
        sp.counterText = counter; sp.glow = glowImg; sp.torchLight = null;
        sp.torches = null;

        page.gameObject.SetActive(false);
        return sp;
    }

    // Story Mode: title, progress line, Continue (two lines: "Continue / Chapter N: <guardian>", hidden without progress), New
    // Story, Watch intro, Back, and a small "start over?" confirm in front of them.
    static void BuildStoryModePage(RectTransform content, MainMenuController mc, Button open, GameObject systems, StoryPanel intro)
    {
        var page = PageRoot(content, "StoryModePage");
        var panel = MenuUi.Panel(page, "Panel", Vector2.zero, new Vector2(900f, 680f), PagePanel);
        panel.raycastTarget = false;
        MenuUi.Label(page, "Title", "Story Mode", 60, new Color(0.9f, 0.72f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0f, 270f), new Vector2(860f, 76f), FontStyle.Bold);
        var status = MenuUi.Label(page, "Status", "", 30, new Color(0.85f, 0.88f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, 208f), new Vector2(860f, 44f));
        var buttons = MenuUi.Rect(page, "Buttons", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var group = buttons.gameObject.AddComponent<CanvasGroup>();
        var cont = MenuUi.MakeButton(buttons, "ContinueButton", "Continue", 40, StartColor, new Vector2(0f, 100f), new Vector2(600f, 120f));
        var contLabel = cont.GetComponentInChildren<Text>();
        contLabel.fontSize = 34;   // two lines ("Continue" / "Chapter 3: The Harbor Tyrant") must fit the 600 x 120 button
        var fresh = MenuUi.MakeButton(buttons, "NewStoryButton", "New Story", 44, StoryColor, new Vector2(0f, -38f), new Vector2(600f, 92f));
        var watch = MenuUi.MakeButton(buttons, "WatchIntroButton", "Watch intro", 44, NameColor, new Vector2(0f, -144f), new Vector2(600f, 92f));
        var back = MenuUi.MakeButton(buttons, "StoryModeBackButton", "Back", 42, BackColor, new Vector2(0f, -262f), new Vector2(300f, 84f));

        var confirm = MenuUi.Panel(page, "NewStoryConfirm", Vector2.zero, new Vector2(700f, 300f), new Color(0.05f, 0.06f, 0.1f, 0.98f));
        MenuUi.Label(confirm.transform, "Question", "Start a new story? Your chapters cleared go back to 0.", 38, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 60f), new Vector2(640f, 120f), FontStyle.Bold);
        var yes = MenuUi.MakeButton(confirm.transform, "YesButton", "Yes", 40, new Color(0.65f, 0.15f, 0.15f, 0.97f), new Vector2(-150f, -80f), new Vector2(240f, 84f));
        var no = MenuUi.MakeButton(confirm.transform, "NoButton", "No", 40, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(150f, -80f), new Vector2(240f, 84f));
        confirm.gameObject.SetActive(false);

        var sm = systems.AddComponent<StoryModePanel>();
        sm.menu = mc; sm.intro = intro; sm.page = page.gameObject; sm.openButton = open;
        sm.continueButton = cont; sm.continueLabel = contLabel; sm.newStoryButton = fresh; sm.watchIntroButton = watch; sm.backButton = back;
        sm.statusText = status; sm.confirm = confirm.gameObject; sm.confirmYes = yes; sm.confirmNo = no; sm.buttonsGroup = group;
        mc.storyMode = sm;
        page.gameObject.SetActive(false);
    }

    // Guardians: one card at a time, portrait left, text right, arrows and Back under it. Text 30-56 units (4.3-8.0 cm cap height).
    static void BuildGuardiansPage(RectTransform content, MainMenuController mc, Button open, GameObject systems)
    {
        var page = PageRoot(content, "GuardiansPage");
        var panel = MenuUi.Panel(page, "Panel", Vector2.zero, new Vector2(1120f, 770f), PagePanel);
        panel.raycastTarget = false;
        MenuUi.Label(page, "Title", "Guardians", 50, new Color(0.9f, 0.72f, 0.35f), TextAnchor.MiddleCenter, new Vector2(0f, 338f), new Vector2(900f, 60f), FontStyle.Bold);
        var counter = MenuUi.Label(page, "Counter", "1 / 3", 28, HintColor, TextAnchor.MiddleRight, new Vector2(470f, 338f), new Vector2(140f, 40f));
        var portraitRt = MenuUi.Centred(page, "Portrait", new Vector2(-300f, 20f), new Vector2(400f, 500f));
        var portrait = portraitRt.gameObject.AddComponent<Image>();
        portrait.preserveAspect = true; portrait.raycastTarget = false;
        var name = MenuUi.Label(page, "Name", "", 52, Color.white, TextAnchor.MiddleLeft, new Vector2(200f, 250f), new Vector2(600f, 66f), FontStyle.Bold);
        var weapon = MenuUi.Label(page, "Weapon", "", 28, HintColor, TextAnchor.MiddleLeft, new Vector2(200f, 200f), new Vector2(600f, 38f));
        var lore = MenuUi.Label(page, "Lore", "", 32, new Color(0.94f, 0.95f, 1f), TextAnchor.UpperLeft, new Vector2(200f, 90f), new Vector2(600f, 170f));
        var tips = MenuUi.Label(page, "Tips", "", 30, new Color(1f, 0.92f, 0.7f), TextAnchor.UpperLeft, new Vector2(200f, -110f), new Vector2(600f, 200f));
        var prev = MenuUi.MakeButton(page, "PrevButton", "<", 52, NameColor, new Vector2(-380f, -300f), new Vector2(130f, 90f));
        var next = MenuUi.MakeButton(page, "NextButton", ">", 52, NameColor, new Vector2(-220f, -300f), new Vector2(130f, 90f));
        var back = MenuUi.MakeButton(page, "GuardiansBackButton", "Back", 42, BackColor, new Vector2(380f, -300f), new Vector2(240f, 84f));
        var gp = systems.AddComponent<GuardiansPanel>();
        gp.menu = mc; gp.page = page.gameObject; gp.openButton = open; gp.prevButton = prev; gp.nextButton = next; gp.backButton = back;
        gp.portrait = portrait; gp.nameText = name; gp.weaponText = weapon; gp.loreText = lore; gp.tipsText = tips; gp.counterText = counter;
        mc.guardians = gp;
        page.gameObject.SetActive(false);
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
            var intro = canvas.GetComponent<LevelIntroLine>();
            if (intro == null) intro = canvas.AddComponent<LevelIntroLine>();
            intro.showIntroLine = true;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"{path}: OutcomeMenuButtons + LevelIntroLine on CombatCanvas");
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
