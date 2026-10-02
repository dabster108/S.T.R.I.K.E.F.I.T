// Level3Builder.cs
// Purpose: Re-runnable build of Level 3 (Old Seaport, final boss) content:
//   - VFX textures/materials (summoning rune circle, unblockable danger zone, embers)
//   - player weapon pairs (Mace, War Flail, Katana) as prefabs with HitDetection + per-weapon audio/stats
//   - the boss (Mixamo Ch25 with two axes) is built by Level3DualAxeBuilder
// Scene assembly lives in Level3SceneBuilder.cs.
// Menu: Tools > Level 3 > Build Prefabs

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class Level3Builder
{
    public const string VfxDir = "Assets/VFX/Level3";
    public const string MatDir = "Assets/Materials/Level3";
    public const string SfxDir = "Assets/Audio/SFX/Level3";
    public const string WeaponPrefabDir = "Assets/Prefabs/Weapons/Level3";
    public const string MonsterPrefabPath = Level3DualAxeBuilder.PrefabPath;

    // ---------- Weapon stats (one place; reported in project_progress.md) ----------
    public struct WeaponStats
    {
        public float baseDamage, hitCooldown, minSwing, maxSwing, maxVelMul, damageMul, momentum, momentumSeconds, swingTrigger, swingInterval;
    }
    public static readonly WeaponStats Mace = new WeaponStats { baseDamage = 15f, hitCooldown = 0.55f, minSwing = 2.0f, maxSwing = 7f, maxVelMul = 2.6f, damageMul = 1.35f, swingTrigger = 2.4f, swingInterval = 0.45f };
    public static readonly WeaponStats Flail = new WeaponStats { baseDamage = 11f, hitCooldown = 0.40f, minSwing = 1.6f, maxSwing = 6.5f, maxVelMul = 2.4f, damageMul = 1.1f, momentum = 0.45f, momentumSeconds = 0.6f, swingTrigger = 2.2f, swingInterval = 0.35f };
    public static readonly WeaponStats Katana = new WeaponStats { baseDamage = 7.5f, hitCooldown = 0.18f, minSwing = 1.3f, maxSwing = 6f, maxVelMul = 2.2f, damageMul = 0.9f, swingTrigger = 2.0f, swingInterval = 0.18f };

    [MenuItem("Tools/Level 3/Build Prefabs")]
    public static void BuildMenu() { Debug.Log(BuildAll()); }

    public static string BuildAll()
    {
        EnsureFolder("Assets/VFX", "Level3");
        EnsureFolder("Assets/Prefabs", "Weapons");
        EnsureFolder("Assets/Prefabs/Weapons", "Level3");
        var log = new System.Text.StringBuilder();
        BuildVfxAssets(log);
        BuildWeaponMeshes(log);
        BuildWeaponPrefabs(log);
        log.Append(Level3DualAxeBuilder.BuildAll());
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ======================================================================= VFX
    public static Mesh GroundQuad()
    {
        string p = VfxDir + "/L3_GroundQuad.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (m != null) return m;
        m = new Mesh { name = "L3_GroundQuad" };
        m.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    static void BuildVfxAssets(System.Text.StringBuilder log)
    {
        GroundQuad();
        var rune = MakeTexture(VfxDir + "/T_L3_RuneCircle.png", 512, RunePixel);
        var danger = MakeTexture(VfxDir + "/T_L3_DangerRing.png", 512, DangerPixel);
        var soft = MakeTexture(VfxDir + "/T_L3_SoftDot.png", 64, (x, y) => { float r = new Vector2(x - 0.5f, y - 0.5f).magnitude * 2f; float a = Mathf.Clamp01(1f - r); a *= a; return new Color(1, 1, 1, a); });
        ParticleMat(MatDir + "/M_L3_RuneCircle.mat", rune, true);
        ParticleMat(MatDir + "/M_L3_DangerRing.mat", danger, true);
        ParticleMat(MatDir + "/M_L3_DangerFill.mat", soft, false);
        ParticleMat(MatDir + "/M_L3_Ember.mat", soft, true);
        ParticleMat(MatDir + "/M_L3_Dust.mat", soft, false);
        ParticleMat(MatDir + "/M_L3_Rain.mat", soft, false);
        log.AppendLine("VFX textures + materials built.");
    }

    // Rune circle: double outer ring, inner ring, 24 rune ticks, 7-point star, faint fill.
    static Color RunePixel(float u, float v)
    {
        Vector2 p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
        float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
        float c = 0f;
        c += Band(r, 0.95f, 0.025f) + Band(r, 0.88f, 0.012f) + Band(r, 0.62f, 0.018f) + Band(r, 0.30f, 0.012f);
        float tick = Mathf.Abs(Mathf.Repeat(a / (Mathf.PI * 2f) * 24f, 1f) - 0.5f);
        if (r > 0.88f && r < 0.95f && tick < 0.12f) c += 0.9f;
        float glyph = Mathf.Abs(Mathf.Repeat(a / (Mathf.PI * 2f) * 12f + 0.25f, 1f) - 0.5f);
        if (r > 0.66f && r < 0.84f && glyph < 0.06f + 0.05f * Mathf.Sin(r * 60f)) c += 0.7f;
        // 7-point star: lines between every 3rd vertex on the inner ring
        for (int i = 0; i < 7; i++)
        {
            float a0 = i * Mathf.PI * 2f / 7f, a1 = (i + 3) * Mathf.PI * 2f / 7f;
            Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.62f, p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.62f;
            c += Band(DistToSegment(p, p0, p1), 0f, 0.010f) * 0.8f;
        }
        if (r < 0.95f) c += 0.08f * (1f - r);
        if (r > 1f) c = 0f;
        c = Mathf.Clamp01(c);
        return new Color(1, 1, 1, c);
    }

    static Color DangerPixel(float u, float v)
    {
        Vector2 p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
        float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
        float c = Band(r, 0.95f, 0.04f) + Band(r, 0.82f, 0.012f);
        float seg = Mathf.Repeat(a / (Mathf.PI * 2f) * 16f, 1f);
        if (r > 0.84f && r < 0.93f && seg < 0.45f) c += 0.75f;
        if (r < 0.95f) c += 0.18f * Mathf.SmoothStep(0.2f, 0.95f, r);
        if (r > 1f) c = 0f;
        return new Color(1, 1, 1, Mathf.Clamp01(c));
    }

    static float Band(float x, float center, float half) { float d = Mathf.Abs(x - center); return Mathf.Clamp01(1f - d / half); }
    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b) { Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return (p - (a + ab * t)).magnitude; }

    static Texture2D MakeTexture(string path, int size, System.Func<float, float, Color> f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material ParticleMat(string path, Texture tex, bool additive)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_Cull", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ======================================================================= Weapon meshes
    public const string KatanaSplitPath = "Assets/Models/Weapons/Player/Level3/Katana/Katana_Split.asset";
    public const string MaceStaticPath = "Assets/Models/Weapons/Player/Level3/Mace/Mace_Static.asset";
    // Katana mesh space (long axis +Y): handle y < 2.48, guard 2.48..2.83, blade above. Grip centre y 2.13.
    const float KatanaGuardStart = 2.48f, KatanaGuardEnd = 2.83f;
    static readonly Vector3 KatanaGrip = new Vector3(1.09f, 2.13f, -0.66f);
    const float KatanaScale = 0.24f;
    // Mace mesh space (long axis +Z): handle z -0.28..0.05, grip centre z -0.14; head z 0.1..0.77.
    const float MaceScale = 0.72f, MaceGripZ = -0.14f;

    static void BuildWeaponMeshes(System.Text.StringBuilder log)
    {
        var kat = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Weapons/Player/Level3/Katana/Katana_Quest.asset");
        var v = kat.vertices; var tri = kat.triangles;
        var subs = new[] { new List<int>(), new List<int>(), new List<int>() };
        for (int i = 0; i < tri.Length; i += 3)
        {
            float y = (v[tri[i]].y + v[tri[i + 1]].y + v[tri[i + 2]].y) / 3f;
            int s = y < KatanaGuardStart ? 1 : (y < KatanaGuardEnd ? 2 : 0);
            subs[s].Add(tri[i]); subs[s].Add(tri[i + 1]); subs[s].Add(tri[i + 2]);
        }
        var split = Object.Instantiate(kat); split.name = "Katana_Split";
        split.subMeshCount = 3;
        for (int s = 0; s < 3; s++) split.SetTriangles(subs[s], s);
        SaveMesh(split, KatanaSplitPath);
        log.AppendLine($"Katana split: blade {subs[0].Count / 3}, grip {subs[1].Count / 3}, guard {subs[2].Count / 3} tris.");

        var maceSrc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/Player/Level3/Mace/Mace_DoomDarkAges.fbx").GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        var mace = new Mesh { name = "Mace_Static" };
        mace.SetVertices(maceSrc.vertices); mace.SetNormals(maceSrc.normals); mace.SetTangents(maceSrc.tangents); mace.SetUVs(0, maceSrc.uv);
        mace.SetTriangles(maceSrc.triangles, 0); mace.RecalculateBounds();
        int maceTris = maceSrc.triangles.Length / 3;
        SaveMesh(mace, MaceStaticPath);
        log.AppendLine($"Mace static mesh: {maceTris} tris.");
    }

    static void SaveMesh(Mesh m, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { MeshDecimateTool.OverwriteMesh(existing, m); Object.DestroyImmediate(m); }
        else AssetDatabase.CreateAsset(m, path);
    }

    // ======================================================================= Weapon prefabs
    static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    static AudioClip Sfx(string name) => Clip($"{SfxDir}/{name}.wav");
    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

    public static string WeaponPrefabPath(WeaponType t, Hand h) => $"{WeaponPrefabDir}/Player{t}_{h}.prefab";
    public static string StagedPrefabPath(WeaponType t) => $"{WeaponPrefabDir}/Staged_{t}.prefab";

    static void BuildWeaponPrefabs(System.Text.StringBuilder log)
    {
        foreach (var h in new[] { Hand.Right, Hand.Left })
        {
            SavePrefab(BuildMace(h, true), WeaponPrefabPath(WeaponType.Mace, h));
            SavePrefab(BuildFlail(h, true), WeaponPrefabPath(WeaponType.Flail, h));
            SavePrefab(BuildKatana(h, true), WeaponPrefabPath(WeaponType.Katana, h));
        }
        // Staged props: visuals only (no physics / combat components), for leaning in the arena.
        SavePrefab(BuildMace(Hand.Right, false), StagedPrefabPath(WeaponType.Mace));
        SavePrefab(BuildFlail(Hand.Right, false), StagedPrefabPath(WeaponType.Flail));
        SavePrefab(BuildKatana(Hand.Right, false), StagedPrefabPath(WeaponType.Katana));
        log.AppendLine("Weapon prefabs: Mace / Flail / Katana x Right, Left + staged props.");
    }

    static void SavePrefab(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static GameObject WeaponRoot(string name, Hand hand, bool combat)
    {
        var root = new GameObject(name);
        // Same grip alignment as the L1 hammers / L2 swords: weapon axis 40 degrees up from controller-forward.
        root.transform.localRotation = Quaternion.Euler(320f, 0f, 0f);
        if (combat)
        {
            root.tag = "Hammer";
            var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var src = root.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0.25f; src.volume = 0.8f;
        }
        return root;
    }

    static void AddHit(GameObject go, Hand hand, WeaponStats s, AudioClip hit, AudioClip heavy)
    {
        var hd = go.AddComponent<HitDetection>();
        hd.hand = hand;
        hd.baseDamage = s.baseDamage; hd.hitCooldown = s.hitCooldown; hd.minSwingSpeed = s.minSwing;
        hd.maxSwingSpeed = s.maxSwing; hd.maxForceMultiplier = s.maxVelMul;
        hd.momentumMaxBonus = s.momentum; hd.momentumBuildSeconds = s.momentumSeconds > 0f ? s.momentumSeconds : 0.6f;
        hd.hitSound = hit; hd.heavyHitSound = heavy; hd.heavySwingSpeedThreshold = 4.8f;
        hd.clashSound = Clip("Assets/Audio/SFX/ribhavagrawal-swords-collide-230574.mp3");
        hd.audioSource = go.GetComponent<AudioSource>();
    }

    static void AddSwing(GameObject root, Hand hand, WeaponStats s, AudioClip clip)
    {
        var sw = root.AddComponent<WeaponSwingAudio>();
        sw.hand = hand; sw.audioSource = root.GetComponent<AudioSource>(); sw.swingClips = new[] { clip };
        sw.triggerSpeed = s.swingTrigger; sw.minInterval = s.swingInterval; sw.volume = 0.6f;
    }

    static GameObject BuildMace(Hand hand, bool combat)
    {
        var root = WeaponRoot(combat ? (hand == Hand.Right ? "PlayerMace" : "PlayerMace_Left") : "Staged_Mace", hand, combat);
        var vis = new GameObject("Visual"); vis.transform.SetParent(root.transform, false);
        vis.transform.localPosition = new Vector3(0f, 0f, -MaceGripZ * MaceScale);
        vis.transform.localRotation = Quaternion.Euler(0f, 0f, hand == Hand.Right ? 90f : 270f);
        vis.transform.localScale = Vector3.one * MaceScale;
        vis.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MaceStaticPath);
        vis.AddComponent<MeshRenderer>().sharedMaterial = Mat("M_L3_Mace");
        if (combat)
        {
            // Striking part only: the flanged head (mesh z 0.1..0.77), 0.30 m across.
            var box = root.AddComponent<BoxCollider>(); box.isTrigger = true;
            box.center = new Vector3(0f, 0f, (0.435f - MaceGripZ) * MaceScale);
            box.size = new Vector3(0.30f, 0.30f, 0.50f);
            AddHit(root, hand, Mace, Sfx("L3_Mace_Impact"), Clip("Assets/Audio/SFX/Hits/dragon-studio-hammer-smash-effect-382731.mp3"));
            AddSwing(root, hand, Mace, Sfx("L3_Mace_Swing"));
        }
        return root;
    }

    static GameObject BuildKatana(Hand hand, bool combat)
    {
        var root = WeaponRoot(combat ? (hand == Hand.Right ? "PlayerKatana" : "PlayerKatana_Left") : "Staged_Katana", hand, combat);
        var vis = new GameObject("Visual"); vis.transform.SetParent(root.transform, false);
        var rot = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, hand == Hand.Right ? 0f : 180f, 0f); // mesh +Y -> weapon +Z
        vis.transform.localRotation = rot;
        vis.transform.localScale = Vector3.one * KatanaScale;
        vis.transform.localPosition = -(rot * (KatanaGrip * KatanaScale));
        vis.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(KatanaSplitPath);
        vis.AddComponent<MeshRenderer>().sharedMaterials = new[] { Mat("M_L3_KatanaBlade"), Mat("M_L3_KatanaGrip"), Mat("M_L3_KatanaGuard") };
        if (combat)
        {
            // Blade only: mesh y 2.83..5.94 -> 0.17..0.91 m from the grip.
            var box = root.AddComponent<BoxCollider>(); box.isTrigger = true;
            float z0 = (KatanaGuardEnd - KatanaGrip.y) * KatanaScale, z1 = (5.94f - KatanaGrip.y) * KatanaScale;
            box.center = new Vector3(0f, 0f, (z0 + z1) * 0.5f);
            box.size = new Vector3(0.08f, 0.08f, z1 - z0);
            AddHit(root, hand, Katana, Sfx("L3_Katana_Hit"), Clip("Assets/Audio/SFX/Monster/dragon-studio-sword-slice-393847.mp3"));
            AddSwing(root, hand, Katana, Sfx("L3_Katana_Swing"));
        }
        return root;
    }

    // Flail model space (instantiated FBX): handle y -0.33..1.73 (grip centre ~0.25), chain 1.75..3.33, ball centre 3.73.
    const float FlailScale = 0.24f;
    static readonly Vector3 FlailGrip = new Vector3(1.163f, 0.25f, 0.133f);

    static GameObject BuildFlail(Hand hand, bool combat)
    {
        var root = WeaponRoot(combat ? (hand == Hand.Right ? "PlayerFlail" : "PlayerFlail_Left") : "Staged_Flail", hand, combat);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Weapons/Player/Level3/Flail/WarFlail.fbx");
        var src = (GameObject)Object.Instantiate(fbx);
        var mat = Mat("M_L3_Flail");
        var rot = Quaternion.Euler(90f, 0f, 0f); // model +Y -> weapon +Z
        Vector3 ToRoot(Vector3 modelPos) => rot * ((modelPos - FlailGrip) * FlailScale);

        // Handle (rigid on the hand)
        var handleSrc = src.transform.Find("Cylinder.003");
        var handle = new GameObject("Handle"); handle.transform.SetParent(root.transform, false);
        handle.transform.localPosition = ToRoot(handleSrc.position);
        handle.transform.localRotation = rot * handleSrc.rotation;
        handle.transform.localScale = handleSrc.lossyScale * FlailScale;
        handle.AddComponent<MeshFilter>().sharedMesh = handleSrc.GetComponent<MeshFilter>().sharedMesh;
        handle.AddComponent<MeshRenderer>().sharedMaterial = mat;

        var tip = new GameObject("ChainAnchor"); tip.transform.SetParent(root.transform, false);
        tip.transform.localPosition = ToRoot(new Vector3(FlailGrip.x, 1.74f, FlailGrip.z));

        // Chain links + ball, simulated by FlailChainSim (rest pose = straight out along the handle).
        var links = new List<Transform>();
        var linkSrcs = new List<Transform>();
        foreach (Transform c in src.transform) if (c.name.StartsWith("Torus")) linkSrcs.Add(c);
        linkSrcs.Sort((a, b) => a.position.y.CompareTo(b.position.y));
        var chain = new GameObject("Chain"); chain.transform.SetParent(root.transform, false);
        foreach (var ls in linkSrcs)
        {
            var l = new GameObject(ls.name); l.transform.SetParent(chain.transform, false);
            l.transform.localPosition = ToRoot(ls.position);
            l.transform.localRotation = rot * ls.rotation;
            l.transform.localScale = ls.lossyScale * FlailScale;
            l.AddComponent<MeshFilter>().sharedMesh = ls.GetComponent<MeshFilter>().sharedMesh;
            l.AddComponent<MeshRenderer>().sharedMaterial = mat;
            links.Add(l.transform);
        }
        var ballSrc = src.transform.Find("Icosphere.021");
        var ball = new GameObject("Ball"); ball.transform.SetParent(root.transform, false);
        ball.transform.localPosition = ToRoot(ballSrc.position);
        var ballVis = new GameObject("BallVisual"); ballVis.transform.SetParent(ball.transform, false);
        ballVis.transform.localRotation = rot * ballSrc.rotation;
        ballVis.transform.localScale = ballSrc.lossyScale * FlailScale;
        ballVis.AddComponent<MeshFilter>().sharedMesh = ballSrc.GetComponent<MeshFilter>().sharedMesh;
        ballVis.AddComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(src);

        var sim = root.AddComponent<FlailChainSim>();
        sim.anchor = tip.transform; sim.ball = ball.transform; sim.links = links.ToArray();
        sim.chainLength = (ball.transform.localPosition - tip.transform.localPosition).magnitude;

        if (combat)
        {
            // Hit window = the ball, wider than the visible ball (0.09 m) for the flail's unpredictable arc.
            var rb = ball.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var bsrc = ball.AddComponent<AudioSource>(); bsrc.playOnAwake = false; bsrc.spatialBlend = 0.25f; bsrc.volume = 0.8f;
            var sph = ball.AddComponent<SphereCollider>(); sph.isTrigger = true; sph.radius = 0.15f;
            ball.tag = "Hammer";
            AddHit(ball, hand, Flail, Sfx("L3_Flail_Impact"), Clip("Assets/Audio/SFX/Hits/bannythecoolio-impactful-damage-425132.mp3"));
            AddSwing(root, hand, Flail, Sfx("L3_Flail_Swing"));
        }
        else sim.enabled = false; // staged prop keeps its authored pose (chain laid by the scene builder)
        return root;
    }

    // ======================================================================= helpers
    static Transform Child(Transform parent, string name, Vector3 localPos)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = localPos; return t;
    }

    static CapsuleCollider Capsule(Transform parent, string name, Vector3 localPos, float r, float h, int dir)
    {
        var c = Child(parent, name, localPos).gameObject.AddComponent<CapsuleCollider>();
        c.radius = r; c.height = h; c.direction = dir; return c;
    }

    internal static ParticleSystem Particles(Transform parent, string name, Vector3 localPos, Material mat, Color color, float rate, float life, float size, float speed, float gravityUp, ParticleSystemShapeType shape, Vector3 boxSize)
    {
        var t = Child(parent, name, localPos);
        var ps = t.gameObject.AddComponent<ParticleSystem>();
        var main = ps.main; main.playOnAwake = false; main.loop = true; main.startLifetime = life; main.startSize = size;
        main.startSpeed = speed; main.startColor = color; main.gravityModifier = -gravityUp * 0.1f; main.maxParticles = 150;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = shape; sh.scale = boxSize;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = t.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
