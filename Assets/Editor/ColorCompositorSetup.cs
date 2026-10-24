// ColorCompositorSetup.cs
// Purpose: Re-runnable wiring of the colour compositor (StrikeFit > Colour > Wire Compositors). Visual only: no gameplay value is
//          read or written.
//            1. Palettes: Assets/Settings/Color/L1/L2/L3 MonsterColorPalette and the shared PlayerWeaponGlow profile (created once,
//               then left alone so tuning in the Inspector survives a re-run; Force Palettes overwrites them).
//            2. Level 1 accent: Warrok_AccentMask.png, an emission map made from the body texture (red / brown leather armour and
//               trims = 1, fur and skin = 0.3), set on warrok_MAT so the armour carries the accent and a hit still reads on the skin.
//               The Level 1 monster sword gets M_L1_MonsterSwordMetal, a copy of its embedded lambert2 material with emission enabled.
//            3. Emission keyword on the monster and player weapon materials, with an invisible emission colour (0.002) and the
//               realtime-emissive GI flag, which URP's material validation needs to keep the keyword: emission written through a property block only shows with it.
//               Smoothness / metallic are never touched (the less-shiny metal fix stays).
//            4. MonsterColorCompositor on the three monster prefabs and PlayerWeaponGlow on every player weapon (Weapon_Hammer and the
//               six Level 3 prefabs; the Level 1 left hammer and the Level 2 swords are scene objects, wired in their scenes).
// Dependencies: MonsterColorCompositor, PlayerWeaponGlow, StrikeFit.ColorFx
// Attach to: nothing (Editor menu)

using System.Collections.Generic;
using System.IO;
using System.Text;
using StrikeFit.ColorFx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ColorCompositorSetup
{
    const string Dir = "Assets/Settings/Color";
    const string L1Prefab = "Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab";
    const string L2Prefab = "Assets/Prefabs/Monsters/Monster_Medium_VampireLust.prefab";
    const string L3Prefab = "Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab";
    const string WarrokMat = "Assets/Materials/warrok_MAT.mat";
    const string WarrokTex = "Assets/training/bear_diffuse.png";
    const string WarrokMask = "Assets/Materials/Level1/Warrok_AccentMask.png";
    const string SwordMetal = "Assets/Materials/Level1/M_L1_MonsterSwordMetal.mat";
    // Fur and skin keep this much of the body emission, so a hit flash still reads on the whole body; the armour gets all of it.
    const float MaskFloor = 0.3f;
    static readonly Color InvisibleEmission = new Color(0.002f, 0.002f, 0.002f, 1f);

    static readonly string[] PlayerWeaponPrefabs =
    {
        "Assets/Prefabs/Weapons/Weapon_Hammer.prefab",
        "Assets/Prefabs/Weapons/Level3/PlayerMace_Left.prefab", "Assets/Prefabs/Weapons/Level3/PlayerMace_Right.prefab",
        "Assets/Prefabs/Weapons/Level3/PlayerFlail_Left.prefab", "Assets/Prefabs/Weapons/Level3/PlayerFlail_Right.prefab",
        "Assets/Prefabs/Weapons/Level3/PlayerKatana_Left.prefab", "Assets/Prefabs/Weapons/Level3/PlayerKatana_Right.prefab",
    };

    static readonly (string scene, string[] paths)[] SceneWeapons =
    {
        ("Assets/Scenes/Level1_Easy.unity", new[] { "OVRCameraRig/TrackingSpace/LeftHandAnchor/PlayerHammer_Left" }),
        ("Assets/Scenes/Level2_Medium.unity", new[] { "OVRCameraRig/TrackingSpace/RightHandAnchor/PlayerSword", "OVRCameraRig/TrackingSpace/LeftHandAnchor/PlayerSword_Left" }),
    };

    [MenuItem("StrikeFit/Colour/Wire Compositors")]
    public static void MenuRun() => Debug.Log(Run(false));

    [MenuItem("StrikeFit/Colour/Wire Compositors (Force Palettes)")]
    public static void MenuRunForce() => Debug.Log(Run(true));

    public static string Run(bool forcePalettes)
    {
        var log = new StringBuilder("[ColorSetup]\n");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Settings", "Color");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/Level1")) AssetDatabase.CreateFolder("Assets/Materials", "Level1");

        var l1 = Palette("L1_Gatekeeper_Palette", forcePalettes, FillL1, log);
        var l2 = Palette("L2_CrimsonWarden_Palette", forcePalettes, FillL2, log);
        var l3 = Palette("L3_HarborTyrant_Palette", forcePalettes, FillL3, log);
        var wp = WeaponProfile(forcePalettes, log);

        BuildWarrokMask(log, forcePalettes);
        var swordMetal = BuildSwordMetal(log);
        EnableEmission(log);

        WireMonster(L1Prefab, l1, log, swordMetal);
        WireMonster(L2Prefab, l2, log, null);
        WireMonster(L3Prefab, l3, log, null);
        foreach (var p in PlayerWeaponPrefabs) WirePlayerWeaponPrefab(p, wp, log);
        WireSceneWeapons(wp, log);

        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ───────────────────────── palettes ─────────────────────────

    static MonsterColorPalette Palette(string name, bool force, System.Action<MonsterColorPalette> fill, StringBuilder log)
    {
        string path = Dir + "/" + name + ".asset";
        var p = AssetDatabase.LoadAssetAtPath<MonsterColorPalette>(path);
        bool made = p == null;
        if (made) { p = ScriptableObject.CreateInstance<MonsterColorPalette>(); AssetDatabase.CreateAsset(p, path); }
        if (force && !made) { var fresh = ScriptableObject.CreateInstance<MonsterColorPalette>(); EditorUtility.CopySerialized(fresh, p); Object.DestroyImmediate(fresh); p.name = Path.GetFileNameWithoutExtension(path); }
        if (made || force) { fill(p); EditorUtility.SetDirty(p); }
        log.AppendLine((made ? "created " : force ? "refilled " : "kept ") + path);
        return p;
    }

    // Level 1, the Gatekeeper: richer, warmer body, amber accent on the leather armour (emission mask), a gold-amber blade.
    static void FillL1(MonsterColorPalette p)
    {
        p.blend = new BlendSettings { maxEmission = 0.9f, telegraphMinWeight = 0.55f, telegraphMinEmission = 0.3f, telegraphReserve = 0.4f };
        p.bodyTint = new Color(1f, 0.9f, 0.78f);
        p.bodyEmission = new Color(0.24f, 0.1f, 0.01f);           // through the armour mask: amber trims, a faint warmth on the fur
        p.auraScale = 0.3f;
        p.accentTint = p.bodyTint; p.accentEmission = p.bodyEmission;
        p.weaponTint = new Color(1f, 0.93f, 0.82f);
        p.weaponEmission = new Color(0.08f, 0.035f, 0f);
        p.weaponAccentTint = new Color(1f, 0.7f, 0.22f);           // the sword's glow planes: gold-amber instead of plain
        p.weaponAccentEmission = Color.black;
        p.woundColor = new Color(0.62f, 0.12f, 0.08f);
        p.rageRimIntensity = 0f;
    }

    // Level 2, the Crimson Warden: crimson cloth with a violet smoulder, violet edge on the axe (matches the violet fire aura).
    static void FillL2(MonsterColorPalette p)
    {
        p.blend = new BlendSettings { maxEmission = 1.1f, telegraphMinWeight = 0.55f, telegraphMinEmission = 0.3f, telegraphReserve = 0.45f };
        p.bodyTint = new Color(1f, 0.96f, 1f);
        p.bodyEmission = Color.black;
        p.accentTint = new Color(0.95f, 0.62f, 0.7f);
        p.accentEmission = new Color(0.22f, 0.03f, 0.28f);
        p.weaponTint = Color.white;
        p.weaponEmission = Color.black;
        p.weaponAccentTint = new Color(0.9f, 0.85f, 1f);
        p.weaponAccentEmission = new Color(0.3f, 0.05f, 0.42f);
        p.woundColor = new Color(0.45f, 0.06f, 0.18f);
        p.rageRimIntensity = 0f;
        // The vampire's emission map is an authored eye / rune mask, so emission flashes only show there: lean on the base shift.
        p.hitWeight = 0.75f;
        p.guardWeight = 0.85f;
        p.cueWeight = 0.7f;
    }

    // Level 3, the Harbor Tyrant: the glow mask stays MonsterGlow's; ember edges on the axes, an orange rage rim.
    static void FillL3(MonsterColorPalette p)
    {
        p.blend = new BlendSettings { maxEmission = 2f, telegraphMinWeight = 0.55f, telegraphMinEmission = 0.4f, telegraphReserve = 0.6f };
        p.bodyTint = Color.white;
        p.glowTint = new Color(0.97f, 0.95f, 1f);
        p.weaponTint = new Color(0.95f, 0.95f, 1f);
        p.weaponEmission = Color.black;
        p.weaponAccentTint = Color.white;
        p.weaponAccentEmission = new Color(0.16f, 0.05f, 0.01f);
        p.rageRimColor = new Color(1f, 0.38f, 0.06f);
        p.rageRimIntensity = 0.35f;
        p.woundColor = new Color(0.5f, 0.1f, 0.06f);
        // Ch25 is near black and its emission map (cracks, eyes, mouth) dims every emission flash: they need about twice the
        // emission of Levels 1-2 to read (still under this level's 2.0 cap).
        p.tapIntensity = 0.5f; p.lightIntensity = 0.8f; p.mediumIntensity = 1.2f; p.fullIntensity = 1.7f; p.weakIntensity = 1.7f;
        p.guardIntensity = 0.9f;
        p.cueIntensity = 0.7f;
        p.rimIntensity = 0.5f;
        p.woundMaxIntensity = 0.6f;
    }

    static WeaponGlowProfile WeaponProfile(bool force, StringBuilder log)
    {
        string path = Dir + "/PlayerWeaponGlow_Profile.asset";
        var p = AssetDatabase.LoadAssetAtPath<WeaponGlowProfile>(path);
        bool made = p == null;
        if (made) { p = ScriptableObject.CreateInstance<WeaponGlowProfile>(); AssetDatabase.CreateAsset(p, path); }
        if (force && !made) { var fresh = ScriptableObject.CreateInstance<WeaponGlowProfile>(); EditorUtility.CopySerialized(fresh, p); Object.DestroyImmediate(fresh); p.name = Path.GetFileNameWithoutExtension(path); }
        if (made || force) EditorUtility.SetDirty(p);
        log.AppendLine((made ? "created " : force ? "refilled " : "kept ") + path);
        return p;
    }

    // ───────────────────────── Level 1 accent mask and sword material ─────────────────────────

    static void BuildWarrokMask(StringBuilder log, bool rebuild)
    {
        if (rebuild || !File.Exists(WarrokMask))
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(WarrokTex));   // from the file: the import does not need Read/Write
            const int N = 1024;
            int sw = src.width, sh = src.height;
            var px = src.GetPixels32();
            var outPx = new Color32[N * N];
            int armour = 0;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var c = px[(y * sh / N) * sw + (x * sw / N)];
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    // Leather armour, straps and trims: red to orange-brown hues, saturated enough, not near black.
                    bool warm = h < 0.11f || h > 0.94f;
                    float k = warm ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.55f, s)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.25f, v)) : 0f;
                    if (k > 0.5f) armour++;
                    byte m = (byte)Mathf.RoundToInt(255f * Mathf.Lerp(MaskFloor, 1f, k));
                    outPx[y * N + x] = new Color32(m, m, m, 255);
                }
            var mask = new Texture2D(N, N, TextureFormat.RGBA32, false);
            mask.SetPixels32(outPx); mask.Apply();
            File.WriteAllBytes(WarrokMask, mask.EncodeToPNG());
            Object.DestroyImmediate(src); Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(WarrokMask);
            log.AppendLine("wrote " + WarrokMask + " (armour " + (100f * armour / (N * N)).ToString("0.0") + " % of the texture)");
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(WarrokMat);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(WarrokMask);
        if (mat != null && tex != null)
        {
            mat.SetTexture("_EmissionMap", tex);
            MakeEmissive(mat);
            log.AppendLine("warrok_MAT: emission map = accent mask, emission keyword on");
        }
    }

    static Material BuildSwordMetal(StringBuilder log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(SwordMetal);
        if (existing != null) { MakeEmissive(existing); return existing; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(L1Prefab);
        Material lambert = null;
        foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            if (r.sharedMaterial != null && r.sharedMaterial.name == "lambert2") { lambert = r.sharedMaterial; break; }
        if (lambert == null) { log.AppendLine("lambert2 not found on the Level 1 sword"); return null; }
        var copy = new Material(lambert) { name = "M_L1_MonsterSwordMetal" };   // same shader and values: looks identical
        MakeEmissive(copy);
        AssetDatabase.CreateAsset(copy, SwordMetal);
        log.AppendLine("created " + SwordMetal + " (copy of the embedded lambert2, emission on)");
        return copy;
    }

    // ───────────────────────── emission keyword ─────────────────────────

    static void EnableEmission(StringBuilder log)
    {
        var names = new List<string>();
        string[] folders = { "Assets/Materials/Level2", "Assets/Materials/Level3" };
        string[] prefixes = { "M_L2_Axe", "M_L2_ZSword_", "M_L2_Sword", "M_L3_Axe", "M_L3_Flail", "M_L3_Katana" };
        foreach (var g in AssetDatabase.FindAssets("t:Material", folders))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var n = Path.GetFileNameWithoutExtension(path);
            bool want = false;
            foreach (var pre in prefixes) if (n.StartsWith(pre)) want = true;
            if (!want || n.Contains("Trail")) continue;   // trails are particle materials, not weapon slots
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || (m.IsKeywordEnabled("_EMISSION") && m.globalIlluminationFlags != MaterialGlobalIlluminationFlags.None)) continue;
            MakeEmissive(m); names.Add(n);
        }
        var hammer = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Weapons/hammer/source/Material.001.mat");
        if (hammer != null && (!hammer.IsKeywordEnabled("_EMISSION") || hammer.globalIlluminationFlags == MaterialGlobalIlluminationFlags.None)) { MakeEmissive(hammer); names.Add("Material.001 (hammer)"); }
        log.AppendLine("emission keyword enabled on: " + (names.Count > 0 ? string.Join(", ", names) : "(already on)"));
    }

    static void MakeEmissive(Material m)
    {
        if (!m.HasProperty("_EmissionColor")) return;
        var c = m.GetColor("_EmissionColor");
        if (c.maxColorComponent <= 0f) m.SetColor("_EmissionColor", InvisibleEmission);   // black would let URP drop the keyword again
        // URP's Lit validation drops _EMISSION when the GI flags are None (tested: any colour), so mark it realtime-emissive.
        // Realtime GI is off in every scene and these objects are not static, so the flag changes no lighting.
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(m);
    }

    // ───────────────────────── wiring ─────────────────────────

    static void WireMonster(string prefabPath, MonsterColorPalette palette, StringBuilder log, Material swordMetal)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var comp = root.GetComponent<MonsterColorCompositor>();
            if (comp == null) comp = root.AddComponent<MonsterColorCompositor>();
            comp.palette = palette;
            var slots = new List<ColorCompositor.Slot>();
            int swapped = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.sharedMaterials;
                if (mats.Length == 0 || mats[0] == null) continue;
                string shader = mats[0].shader.name;
                if (shader.Contains("Particles")) continue;   // rune circle, danger zone, ripple: VFX with their own writers
                bool weapon = InWeapon(r.transform);
                if (r is SkinnedMeshRenderer)
                {
                    if (prefabPath == L3Prefab)
                    {
                        // Ch25: slots 0-1 carry the glow mask (MonsterGlow), slot 2 the weak point (MonsterWeakPoint).
                        for (int i = 0; i < mats.Length; i++)
                            slots.Add(new ColorCompositor.Slot { renderer = r, materialIndex = i, group = mats[i].name.Contains("WeakPoint") ? ColorGroup.WeakPoint : ColorGroup.Glow, addMaterialEmission = false });
                    }
                    else if (prefabPath == L2Prefab)
                    {
                        for (int i = 0; i < mats.Length; i++)
                            slots.Add(new ColorCompositor.Slot { renderer = r, materialIndex = i, group = mats[i].name.Contains("Cloth") ? ColorGroup.Accent : ColorGroup.Body, addMaterialEmission = true });
                    }
                    else slots.Add(new ColorCompositor.Slot { renderer = r, materialIndex = -1, group = ColorGroup.Body, addMaterialEmission = true });
                    continue;
                }
                if (!weapon) continue;
                if (swordMetal != null && mats.Length == 1 && mats[0].name == "lambert2") { r.sharedMaterial = swordMetal; mats = r.sharedMaterials; swapped++; }
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    bool accent = n.Contains("SwordGlow") || n.Contains("Edge") || n.Contains("Blade");
                    slots.Add(new ColorCompositor.Slot { renderer = r, materialIndex = i, group = accent ? ColorGroup.WeaponAccent : ColorGroup.Weapon, addMaterialEmission = true });
                }
            }
            comp.slots = slots.ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            int body = 0, weaponSlots = 0;
            foreach (var s in slots) if ((s.group & ColorGroup.AllWeapon) != 0) weaponSlots++; else body++;
            log.AppendLine(Path.GetFileName(prefabPath) + ": MonsterColorCompositor, palette " + palette.name + ", " + body + " body slot(s), " + weaponSlots + " weapon slot(s)" + (swapped > 0 ? ", " + swapped + " sword pieces -> M_L1_MonsterSwordMetal" : ""));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static bool InWeapon(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.StartsWith("MonsterSword") || p.name.StartsWith("MonsterAxe")) return true;
        return false;
    }

    static void FillWeaponSlots(PlayerWeaponGlow glow, GameObject root)
    {
        var slots = new List<ColorCompositor.Slot>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (r.name.Contains("Ghost")) continue;   // WeaponSurfaceProxy's ghost: built at runtime with its own material
            slots.Add(new ColorCompositor.Slot { renderer = r, materialIndex = -1, group = ColorGroup.Weapon, addMaterialEmission = true });
        }
        glow.slots = slots.ToArray();
    }

    static void WirePlayerWeaponPrefab(string path, WeaponGlowProfile profile, StringBuilder log)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var glow = root.GetComponent<PlayerWeaponGlow>();
            if (glow == null) glow = root.AddComponent<PlayerWeaponGlow>();
            glow.profile = profile;
            FillWeaponSlots(glow, root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine(Path.GetFileName(path) + ": PlayerWeaponGlow, " + glow.slots.Length + " renderer(s)");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void WireSceneWeapons(WeaponGlowProfile profile, StringBuilder log)
    {
        if (EditorApplication.isPlaying) { log.AppendLine("scene weapons skipped (Play mode)"); return; }
        foreach (var (scenePath, paths) in SceneWeapons)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            int wired = 0;
            foreach (var p in paths)
            {
                var go = GameObject.Find(p);
                if (go == null)
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        var t = root.transform.parent == null ? FindPath(root.transform, p) : null;
                        if (t != null) { go = t.gameObject; break; }
                    }
                }
                if (go == null) { log.AppendLine("  missing " + p + " in " + scenePath); continue; }
                var glow = go.GetComponent<PlayerWeaponGlow>();
                if (glow == null) glow = Undo.AddComponent<PlayerWeaponGlow>(go);
                glow.profile = profile;
                FillWeaponSlots(glow, go);
                EditorUtility.SetDirty(glow);
                wired++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine(Path.GetFileName(scenePath) + ": PlayerWeaponGlow on " + wired + " scene weapon(s)");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    // GameObject.Find skips inactive objects (the weapons are inactive until a battle starts): walk the hierarchy by name instead.
    static Transform FindPath(Transform root, string path)
    {
        var parts = path.Split('/');
        if (root.name != parts[0]) return null;
        var t = root;
        for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
        return t;
    }
}
