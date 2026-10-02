// Level3SceneBuilder.cs
// Purpose: Assembles Assets/Scenes/Level3_Hard.unity.
//   1. Systems: the proven Level 2 roots (BattleSystems, OVRCameraRig, EventSystem, CombatCanvas, BackgroundMusic,
//      MonsterSpawnPoint, PlayerSpawn, Moonlight, BroadcastSystems) are duplicated together so every internal
//      reference (CombatUIController's 21 UI refs, Start onClick, OVRInputModule ray, PlayerExertionAudio...) stays
//      wired, then retuned for Level 3 (Hard params, twin-axe boss prefab, weapon pairs instead of swords).
//   2. Environment: an enclosed Old Seaport dock arena built from the pack's modular pieces.
//   3. Weapon staging: Mace / War Flail / Katana pairs embedded in the scenery, each with a WeaponPickup.
// Menu: Tools > Level 3 > Build Scene

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Level3SceneBuilder
{
    const string L2 = "Assets/Scenes/Level2_Medium.unity";
    const string L3 = "Assets/Scenes/Level3_Hard.unity";
    const string Pack = "Assets/Old Sea Port/prefabs/";
    static readonly string[] CopiedRoots = { "Moonlight", "BattleSystems", "MonsterSpawnPoint", "PlayerSpawn", "OVRCameraRig", "EventSystem", "CombatCanvas", "BackgroundMusic", "BroadcastSystems" };

    [MenuItem("Tools/Level 3/Build Scene")]
    public static void BuildMenu() { Debug.Log(Build()); }

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        var l3 = EditorSceneManager.OpenScene(L3, OpenSceneMode.Single);
        foreach (var r in l3.GetRootGameObjects()) Object.DestroyImmediate(r);

        CopySystemsFromLevel2(l3, log);
        ConfigureSystems(log);
        BuildEnvironment(log);
        BuildWeaponStaging(log);
        ConfigureRendering(log);

        EditorSceneManager.MarkSceneDirty(l3);
        EditorSceneManager.SaveScene(l3);
        UpdateBuildSettings(log);
        return log.ToString();
    }

    // ------------------------------------------------------------------ systems
    static void CopySystemsFromLevel2(Scene l3, System.Text.StringBuilder log)
    {
        var l2 = EditorSceneManager.OpenScene(L2, OpenSceneMode.Additive);
        var sel = new List<Object>();
        foreach (var r in l2.GetRootGameObjects()) if (System.Array.IndexOf(CopiedRoots, r.name) >= 0) sel.Add(r);
        Selection.objects = sel.ToArray();
        Unsupported.DuplicateGameObjectsUsingPasteboard(); // keeps references between the duplicated objects
        var dups = Selection.gameObjects;
        foreach (var d in dups)
        {
            d.name = d.name.Replace(" (1)", "");
            SceneManager.MoveGameObjectToScene(d, l3);
        }
        EditorSceneManager.CloseScene(l2, true);
        SceneManager.SetActiveScene(l3);
        log.AppendLine($"Copied {dups.Length} system roots from Level 2.");
    }

    static GameObject Find(string path) => GameObject.Find(path);

    static void ConfigureSystems(System.Text.StringBuilder log)
    {
        var rig = Find("OVRCameraRig");
        var right = rig.transform.Find("TrackingSpace/RightHandAnchor");
        var left = rig.transform.Find("TrackingSpace/LeftHandAnchor");
        foreach (var n in new[] { "PlayerSword", "PlayerShield", "PlayerSword_Left" })
        {
            var t = right.Find(n);
            if (t == null) t = left.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        var pc = rig.GetComponent<PlayerCombat>();
        pc.hammerObject = pc.offhandHammerObject = pc.swordObject = pc.offhandSwordObject = pc.shieldObject = null;
        pc.weaponTransform = pc.offhandWeaponTransform = pc.shieldTransform = null;
        pc.primaryWeapon = WeaponType.None; pc.offhandWeapon = WeaponType.None;
        pc.showWeaponsBeforeBattle = true;
        pc.weaponSets = new List<WeaponSet>();
        foreach (var (type, st) in new[] { (WeaponType.Mace, Level3Builder.Mace), (WeaponType.Flail, Level3Builder.Flail), (WeaponType.Katana, Level3Builder.Katana) })
        {
            var r = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Level3Builder.WeaponPrefabPath(type, Hand.Right)), right);
            var l = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Level3Builder.WeaponPrefabPath(type, Hand.Left)), left);
            r.SetActive(false); l.SetActive(false);
            pc.weaponSets.Add(new WeaponSet { type = type, rightObject = r, leftObject = l, minSwingVelocity = st.minSwing, maxSwingVelocity = st.maxSwing, maxVelocityDamageMultiplier = st.maxVelMul, damageMultiplier = st.damageMul });
        }
        EditorUtility.SetDirty(pc);

        var bs = Find("BattleSystems");
        var lm = bs.GetComponent<LevelManager>();
        lm.sceneDifficulty = DifficultyLevel.Hard; lm.autoDetectDifficultyFromSceneName = true;
        lm.monsterPrefabForThisScene = AssetDatabase.LoadAssetAtPath<GameObject>(Level3Builder.MonsterPrefabPath);
        lm.overrideDefaultWeapons = true; lm.playerPrimaryWeapon = WeaponType.None; lm.playerOffhandWeapon = WeaponType.None; lm.monsterWeapon = WeaponType.DualAxe;
        lm.autoStartBattleOnLoad = false;
        var bm = bs.GetComponent<BattleManager>();
        bm.timerExpireMeansSurvivalWin = false; bm.timerExpireMeansDefeat = false; bm.sceneMonster = null;
        var spawn = Find("MonsterSpawnPoint").transform; spawn.position = new Vector3(0f, 0f, 3.2f); spawn.rotation = Quaternion.Euler(0f, 180f, 0f);
        bm.monsterSpawnPoint = spawn;

        if (bs.GetComponent<WeaponSelectionStage>() == null) bs.AddComponent<WeaponSelectionStage>();
        var outcome = bs.GetComponent<BattleOutcomeAudio>();
        if (outcome == null) outcome = bs.AddComponent<BattleOutcomeAudio>();
        var oSrc = bs.GetComponent<AudioSource>();
        if (oSrc == null) oSrc = bs.AddComponent<AudioSource>();
        oSrc.playOnAwake = false; oSrc.spatialBlend = 0f;
        outcome.audioSource = oSrc;
        outcome.victoryClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Level3Builder.SfxDir + "/L3_Victory_Sting.wav");
        outcome.defeatClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Level3Builder.SfxDir + "/L3_Defeat_Sting.wav");

        // HUD placeholders (L2 showed its real 800/800 before Start).
        var ui = bs.GetComponent<CombatUIController>();
        if (ui.playerHealthText != null) ui.playerHealthText.text = "HP 800/800";
        if (ui.monsterHealthText != null) ui.monsterHealthText.text = "Enemy 1500/1500";

        var music = Find("BackgroundMusic").GetComponent<AudioSource>();
        log.AppendLine($"Systems configured. Music '{(music.clip ? music.clip.name : "none")}' at {music.volume} (Level 2 balance kept).");
    }

    // ------------------------------------------------------------------ environment
    static GameObject envRoot;

    static GameObject Place(string prefab, Vector3 pos, float yRot, Transform parent = null, Vector3? scale = null)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + prefab + ".prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent != null ? parent : envRoot.transform);
        // Keep the prefab's own root rotation (Z-up exports carry -90 X) and turn it around world Y.
        go.transform.rotation = Quaternion.Euler(0f, yRot, 0f) * src.transform.rotation;
        go.transform.position = pos;
        if (scale.HasValue) go.transform.localScale = Vector3.Scale(src.transform.localScale, scale.Value);
        go.isStatic = true;
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        return go;
    }

    static Transform Group(string name)
    {
        var t = new GameObject(name).transform; t.SetParent(envRoot.transform, false); return t;
    }

    static void BuildEnvironment(System.Text.StringBuilder log)
    {
        envRoot = new GameObject("Environment_OldSeaport");

        // Floor: dock_1 deck (planks at y 2.90-3.07 in the prefab) lowered so the deck top is y = 0, centred on the arena.
        var floor = Group("Floor");
        Place("dock_1", new Vector3(4.485f, -3.07f, -4.295f), 0f, floor);   // arena deck x,z in [-5, 5]
        Place("dock_1", new Vector3(4.485f, -3.07f, 5.705f), 0f, floor);    // quay behind the monster (buildings stand on it)

        // Sea around the docks.
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Sea"; water.transform.SetParent(envRoot.transform, false);
        water.transform.position = new Vector3(0f, -1.4f, 0f); water.transform.localScale = new Vector3(12f, 1f, 12f);
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Old Sea Port/Water/Old Sea Port_Water.mat");
        water.isStatic = true;

        // North: harbour wall behind the boss (the pack's building_segment pieces are quay masonry blocks, not
        // facades), the watchtower and a quay terrace with a window wall beyond it as the skyline.
        var north = Group("North_HarbourWall");
        Place("brick_wall_1", new Vector3(-0.6f, 0f, 5.6f), 0f, north);
        Place("brick_wall_1", new Vector3(4.4f, 0f, 5.6f), 0f, north);
        foreach (float x in new[] { -5.6f, -0.6f, 5.6f }) Place("brick_column_1", new Vector3(x, 0f, 5.6f), 0f, north);
        Place("tower_short", new Vector3(-2.4f, 0f, 10.6f), 20f, north);
        Place("building_segment_6", new Vector3(3.2f, 0f, 8.2f), 180f, north);
        Place("building_segment_5", new Vector3(5.6f, 0f, 8.2f), 180f, north);

        // West: harbour wall with columns.
        var west = Group("West_HarbourWall");
        Place("brick_wall_1", new Vector3(-5.6f, 0f, -4.5f), 90f, west);
        Place("brick_wall_1", new Vector3(-5.6f, 0f, 0.5f), 90f, west);
        Place("brick_wall_1", new Vector3(-5.6f, 0f, 5.5f), 90f, west);
        foreach (float z in new[] { -5.0f, 0f, 5.0f }) Place("brick_column_1", new Vector3(-5.6f, 0f, z), 0f, west);

        // East: tall fence with cargo stacked against it, a moored boat beyond.
        var east = Group("East_FenceAndCargo");
        foreach (float z in new[] { -5.0f, -2.0f, 1.0f, 4.0f }) Place("fence_segment_1", new Vector3(5.6f, 0f, z), 0f, east);
        foreach (float z in new[] { -5.0f, 1.0f, 7.0f }) Place("post_1", new Vector3(5.6f, 0f, z), 0f, east);
        Place("boat_2", new Vector3(8.6f, -1.2f, -1.0f), 0f, east);

        // South (behind the player): low fence + mooring posts, the open sea and a rowing boat beyond.
        var south = Group("South_Mooring");
        foreach (float x in new[] { -4.9f, -3.3f, -1.7f, 0.1f, 1.7f, 3.3f }) Place("fence_segment_2", new Vector3(x, 0f, -5.4f), 90f, south);
        foreach (float x in new[] { -5.0f, -1.7f, 1.7f, 5.0f }) Place("mooring_rope_holder_3", new Vector3(x, 0f, -4.9f), 0f, south);
        Place("boat_1", new Vector3(-2.0f, -1.25f, -8.0f), 75f, south);

        // Clutter along the edges (keeps the 6 x 6 m middle clear for the fight).
        var props = Group("Props");
        Place("crate_2", new Vector3(4.5f, 0f, 3.9f), 10f, props);
        Place("crate_1", new Vector3(4.7f, 0f, 2.7f), 40f, props);
        Place("crate_1", new Vector3(4.6f, 0.72f, 2.9f), 15f, props);
        Place("barrel", new Vector3(4.6f, 0f, -3.6f), 0f, props);
        Place("barrel", new Vector3(4.1f, 0f, -4.3f), 0f, props);
        Place("barrel", new Vector3(-4.6f, 0f, 4.3f), 0f, props);
        Place("crate_2", new Vector3(-4.9f, 0f, -3.9f), 80f, props);
        Place("workbench", new Vector3(-4.7f, 0f, -1.6f), 90f, props);
        Place("Log_1", new Vector3(-4.2f, 0.35f, 1.4f), 0f, props, null).transform.rotation = Quaternion.Euler(0f, 5f, 90f);
        Place("ladder_1", new Vector3(-5.0f, 0f, 2.6f), 90f, props);

        // Lamps: four warm pools of light on the walls/fences (no shadows; Quest budget).
        var lamps = Group("Lamps");
        var lampSpots = new[] { (new Vector3(-5.05f, 2.3f, -2.5f), 90f), (new Vector3(-5.05f, 2.3f, 2.5f), 90f), (new Vector3(5.5f, 2.3f, -3.5f), -90f), (new Vector3(5.5f, 2.3f, 2.6f), -90f) };
        int li = 0;
        foreach (var (p, rot) in lampSpots)
        {
            var lamp = Place("lamp_1", p, rot, lamps);
            var lt = new GameObject("LampLight_" + (++li)).AddComponent<Light>();
            lt.transform.SetParent(lamps, false);
            lt.transform.position = p + Quaternion.Euler(0f, rot, 0f) * new Vector3(0.45f, -0.25f, 0f);
            lt.type = LightType.Point; lt.color = new Color(1f, 0.62f, 0.32f); lt.intensity = 2.4f; lt.range = 7f; lt.shadows = LightShadows.None;
        }

        // Storm: the pack's rain, retuned for Quest (URP material, half the particles).
        var rain = Place("Rain", new Vector3(0f, 9f, 0f), 0f, envRoot.transform);
        rain.isStatic = false;
        foreach (var ps in rain.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main; main.maxParticles = Mathf.Min(main.maxParticles, 600);
            var em = ps.emission; if (ps.name == "Rain") em.rateOverTime = 600f;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Level3Builder.MatDir + "/M_L3_Rain.mat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var rainMat = AssetDatabase.LoadAssetAtPath<Material>(Level3Builder.MatDir + "/M_L3_Rain.mat");
        rainMat.SetColor("_BaseColor", new Color(0.7f, 0.78f, 0.85f, 0.35f));

        int count = envRoot.GetComponentsInChildren<Transform>().Length;
        log.AppendLine($"Environment: {count} transforms under Environment_OldSeaport.");
    }

    // ------------------------------------------------------------------ weapon staging
    static void BuildWeaponStaging(System.Text.StringBuilder log)
    {
        var root = new GameObject("WeaponStaging").transform;
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        var pickupClip = AssetDatabase.LoadAssetAtPath<AudioClip>(Level3Builder.SfxDir + "/L3_Weapon_Pickup.wav");

        // Mace: two maces dropped head-down against a cargo crate, left of the player.
        var crateL = Place("crate_2", new Vector3(-1.95f, 0f, 0.05f), 95f, root);
        Place("crate_1", new Vector3(-1.55f, 0f, 0.95f), 25f, root);
        StagePair(root, WeaponType.Mace, "MACE", "Heaviest hits\nslow, needs a big swing", new Vector3(-1.35f, 0f, 0.35f), font, pickupClip,
            new[] { (new Vector3(-1.38f, 0.72f, 0.18f), new Vector3(-1.05f, 0.12f, 0.30f)), (new Vector3(-1.40f, 0.70f, 0.52f), new Vector3(-1.05f, 0.12f, 0.68f)) });

        // War Flail: draped over a barrel and a broken crate on the right, balls in the planks.
        Place("barrel", new Vector3(1.85f, 0f, 0.35f), 0f, root);
        Place("crate_1", new Vector3(1.75f, 0f, -0.55f), -20f, root);
        StagePair(root, WeaponType.Flail, "WAR FLAIL", "Momentum swings\nwide, wild arc", new Vector3(1.35f, 0f, 0.15f), font, pickupClip,
            new[] { (new Vector3(1.42f, 0.78f, 0.36f), new Vector3(1.0f, 0.05f, 0.62f)), (new Vector3(1.42f, 0.74f, -0.10f), new Vector3(1.0f, 0.05f, -0.30f)) });

        // Katana: two blades driven into the dock planks behind the player.
        StagePair(root, WeaponType.Katana, "KATANA", "Fastest recovery\nmany quick cuts", new Vector3(0f, 0f, -1.45f), font, pickupClip,
            new[] { (new Vector3(-0.22f, 0.80f, -1.40f), new Vector3(-0.30f, -0.2f, -1.55f)), (new Vector3(0.22f, 0.80f, -1.48f), new Vector3(0.32f, -0.2f, -1.60f)) });

        // Prompt under the HUD.
        var prompt = new GameObject("WeaponPrompt").AddComponent<TMPro.TextMeshPro>();
        prompt.transform.SetParent(root, false);
        prompt.transform.position = new Vector3(0f, 1.62f, 1.32f);
        prompt.font = font; prompt.fontSize = 0.42f; prompt.alignment = TMPro.TextAlignmentOptions.Center;
        prompt.color = new Color(1f, 0.86f, 0.55f);
        prompt.rectTransform.sizeDelta = new Vector2(1.6f, 0.4f);
        prompt.text = "GRAB A WEAPON PAIR";
        Find("BattleSystems").GetComponent<WeaponSelectionStage>().prompt = prompt;
        log.AppendLine("Weapon staging: Mace (left, crate), War Flail (right, barrel), Katana (behind, in the planks).");
    }

    static void StagePair(Transform root, WeaponType type, string title, string desc, Vector3 center, TMPro.TMP_FontAsset font, AudioClip clip, (Vector3 grip, Vector3 toward)[] poses)
    {
        var pair = new GameObject("Pickup_" + type).transform; pair.SetParent(root, false); pair.position = center;
        var props = new GameObject("StagedProps").transform; props.SetParent(pair, false);
        var grabs = new List<Transform>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Level3Builder.StagedPrefabPath(type));
        int i = 0;
        foreach (var (grip, toward) in poses)
        {
            var w = (GameObject)PrefabUtility.InstantiatePrefab(prefab, props);
            w.name = $"Staged_{type}_{++i}";
            w.transform.position = grip;
            w.transform.rotation = Quaternion.LookRotation(toward - grip, Vector3.up);
            var g = new GameObject("GrabPoint_" + i).transform; g.SetParent(pair, false); g.position = grip; grabs.Add(g);
        }

        var quad = Level3Builder.GroundQuad();
        Material ring = AssetDatabase.LoadAssetAtPath<Material>(Level3Builder.MatDir + "/M_L3_RuneCircle.mat");
        var dim = MakeRing(pair, "MarkerRing", center + Vector3.up * 0.02f, 0.55f, quad, ring, new Color(1f, 0.75f, 0.3f, 0.35f));
        var bright = MakeRing(pair, "ReachHighlight", center + Vector3.up * 0.025f, 0.62f, quad, ring, new Color(1f, 0.85f, 0.4f, 1f));

        var label = new GameObject("Label").AddComponent<TMPro.TextMeshPro>();
        label.transform.SetParent(pair, false);
        label.transform.position = center + new Vector3(0f, 1.25f, 0f);
        label.transform.rotation = Quaternion.LookRotation(new Vector3(label.transform.position.x, 0f, label.transform.position.z).normalized, Vector3.up);
        label.font = font; label.fontSize = 0.55f; label.alignment = TMPro.TextAlignmentOptions.Center; label.color = new Color(1f, 0.9f, 0.7f);
        label.rectTransform.sizeDelta = new Vector2(1.4f, 0.5f);
        label.text = $"<b>{title}</b>\n<size=55%>{desc}</size>";

        var src = pair.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0.8f;
        var pick = pair.gameObject.AddComponent<WeaponPickup>();
        pick.weaponType = type; pick.displayName = title; pick.description = desc;
        pick.grabPoints = grabs.ToArray(); pick.grabRadius = 0.35f;
        pick.stagedProps = props.gameObject; pick.reachHighlight = bright; pick.label = null;
        pick.audioSource = src; pick.pickupClip = clip;
        bright.SetActive(false);
    }

    static GameObject MakeRing(Transform parent, string name, Vector3 pos, float radius, Mesh quad, Material mat, Color c)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.localScale = Vector3.one * radius;
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var inst = new Material(mat) { name = mat.name + "_" + name }; inst.SetColor("_BaseColor", c);
        string path = $"{Level3Builder.MatDir}/M_L3_{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null) { AssetDatabase.CreateAsset(inst, path); existing = inst; } else { existing.CopyPropertiesFromMaterial(inst); Object.DestroyImmediate(inst); }
        r.sharedMaterial = existing;
        return go;
    }

    // ------------------------------------------------------------------ rendering
    static void ConfigureRendering(System.Text.StringBuilder log)
    {
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Old Sea Port/Materials/SkyEerie.mat");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.16f, 0.20f, 0.26f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.13f, 0.15f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.045f; RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.17f);
        var moon = Find("Moonlight").GetComponent<Light>();
        moon.color = new Color(0.62f, 0.72f, 0.9f); moon.intensity = 0.45f;
        moon.transform.rotation = Quaternion.Euler(48f, 160f, 0f);
        log.AppendLine("Rendering: SkyEerie skybox, trilight ambient, exp2 fog 0.045, moonlight 0.45.");
    }

    static void UpdateBuildSettings(System.Text.StringBuilder log)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == L3);
        int l2 = scenes.FindIndex(s => s.path == L2);
        scenes.Insert(l2 + 1, new EditorBuildSettingsScene(L3, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        var order = new System.Text.StringBuilder();
        for (int i = 0; i < scenes.Count; i++) order.Append($"{i}:{System.IO.Path.GetFileNameWithoutExtension(scenes[i].path)}{(scenes[i].enabled ? "" : "(off)")} ");
        log.AppendLine("Build Settings: " + order);
    }
}
