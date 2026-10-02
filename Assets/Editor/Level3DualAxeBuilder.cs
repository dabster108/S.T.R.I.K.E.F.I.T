// Level3DualAxeBuilder.cs
// Purpose: Re-runnable build of the Level 3 boss: the Mixamo character Ch25 (Humanoid) wielding two separate axes.
//   - URP materials from the Mixamo non-PBR textures (specular workflow) + glowing weak-point material
//   - Quest-budget axe mesh from the weapon pack (Axe_deci), split into blade / head / haft submeshes
// Menu: Tools > Level 3 > Build Dual-Axe Monster

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class Level3DualAxeBuilder
{
    public const string ModelPath = "Assets/Models/Monsters/Level3/Ch25_nonPBR.fbx";
    public const string TexDir = "Assets/Models/Monsters/Level3/Textures/Ch25";
    public const string MatDir = "Assets/Materials/Level3";

    // ======================================================================= materials
    public static Material BodyMat(int set) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_L3_Ch25_{set}.mat");
    public static Material WeakMat() => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_L3_Ch25_WeakPoint.mat");

    public static string BuildMaterials(bool useNormalMaps)
    {
        var log = new System.Text.StringBuilder();
        foreach (int set in new[] { 1001, 1002 })
        {
            var diffuse = Tex($"Ch25_{set}_Diffuse");
            var normal = Tex($"Ch25_{set}_Normal");
            var specGloss = PackSpecGloss(set);
            SetNormalMap($"{TexDir}/Ch25_{set}_Normal.png");
            var m = MakeLit($"{MatDir}/M_L3_Ch25_{set}.mat", diffuse, specGloss, useNormalMaps ? normal : null);
            log.AppendLine($"{m.name}: diffuse {diffuse.width}px, specGloss packed, normal {(useNormalMaps ? "on" : "off")}");
        }
        // Weak point: same skin texture, plus emission that MonsterWeakPoint pulses through a property block.
        var w = MakeLit($"{MatDir}/M_L3_Ch25_WeakPoint.mat", Tex("Ch25_1001_Diffuse"), AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Ch25_1001_SpecGloss.png"), null);
        // RealtimeEmissive (not None/EmissiveIsBlack): URP's material validation keeps _EMISSION only then.
        w.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        w.EnableKeyword("_EMISSION");
        w.SetColor("_EmissionColor", new Color(0.35f, 1.6f, 0.25f));
        EditorUtility.SetDirty(w);
        AssetDatabase.SaveAssets();
        log.AppendLine("M_L3_Ch25_WeakPoint: emissive green");
        return log.ToString();
    }

    static Texture2D Tex(string name)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");
        if (t == null) throw new System.Exception("Missing texture " + name);
        return t;
    }

    static void SetNormalMap(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti.textureType == TextureImporterType.NormalMap) return;
        ti.textureType = TextureImporterType.NormalMap;
        ti.SaveAndReimport();
    }

    /// <summary>URP Lit specular workflow wants specular RGB + smoothness A in one map; Mixamo ships them separately.</summary>
    static Texture2D PackSpecGloss(int set)
    {
        string outPath = $"{TexDir}/Ch25_{set}_SpecGloss.png";
        var spec = ReadablePixels($"{TexDir}/Ch25_{set}_Specular.png", out int w, out int h);
        var gloss = ReadablePixels($"{TexDir}/Ch25_{set}_Glossiness.png", out _, out _);
        var px = new Color32[spec.Length];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(spec[i].r, spec[i].g, spec[i].b, gloss[i].r);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply();
        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(outPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
        ti.sRGBTexture = false; ti.alphaIsTransparency = false; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static Color32[] ReadablePixels(string path, out int w, out int h)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!ti.isReadable) { ti.isReadable = true; ti.SaveAndReimport(); }
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        w = t.width; h = t.height;
        return t.GetPixels32();
    }

    // ======================================================================= axe mesh
    public const string AxePackPath = "Assets/Models/Weapons/Player/Level3/Katana/MaceKatanaAxe_Pack.fbx"; // free-mace-katana-axe-weapon-pack (3Blades_1.fbx)
    public const string AxeMeshPath = "Assets/Models/Weapons/Monster/L3_MonsterAxe_Split.asset";
    // Axe_deci space (measured): haft axis at xz (-1.310, -0.410) along +Y, pommel disc y 1.71..1.91, finial to y 5.46,
    // blade 0.33..1.10 out from the haft along (0.637, -0.771) in xz, 0.13 thick, spanning y 2.1..5.3 on two struts.
    static readonly Vector2 AxeHaftXZ = new Vector2(-1.310f, -0.410f);
    static readonly Vector3 AxeBladeDir = new Vector3(0.637f, 0f, -0.771f);
    const float AxeGripY = 2.12f;      // just above the pommel disc: fist half-width (0.045 m) clears it
    public const float AxeScale = 0.25f; // hand-local metres per mesh unit: haft radius 0.025 in a 0.035 fist; 0.96 long (1.15 m at monster scale 1.2)

    /// <summary>
    /// Decimated axe in grip space: origin = grip centre, +Y = haft toward the head, +Z = blade edge direction,
    /// metres (hand-local). Submeshes: 0 blade steel, 1 iron fittings (pommel, collars, struts, finial), 2 wooden haft.
    /// </summary>
    public static string BuildAxeMesh()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(AxePackPath).transform.Find("Axe_deci").GetComponent<MeshFilter>().sharedMesh;
        var dec = MeshDecimateTool.Decimate(src, 3500, AxeMeshPath, out string report, "L3_MonsterAxe_Split");

        var rot = Quaternion.Inverse(Quaternion.LookRotation(AxeBladeDir.normalized, Vector3.up));
        var grip = new Vector3(AxeHaftXZ.x, AxeGripY, AxeHaftXZ.y);
        var v = dec.vertices; var n = dec.normals; var tri = dec.triangles;
        var raw = (Vector3[])v.Clone();
        for (int i = 0; i < v.Length; i++) { v[i] = rot * (v[i] - grip) * AxeScale; n[i] = rot * n[i]; }

        var subs = new[] { new List<int>(), new List<int>(), new List<int>() };
        for (int k = 0; k < tri.Length; k += 3)
        {
            Vector3 c = (raw[tri[k]] + raw[tri[k + 1]] + raw[tri[k + 2]]) / 3f;
            float d = new Vector2(c.x - AxeHaftXZ.x, c.z - AxeHaftXZ.y).magnitude;
            int s = d >= 0.33f ? 0 : (d < 0.16f && c.y > 1.95f && c.y < 5.0f ? 2 : 1);
            subs[s].Add(tri[k]); subs[s].Add(tri[k + 1]); subs[s].Add(tri[k + 2]);
        }
        var mesh = new Mesh { name = "L3_MonsterAxe_Split" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, dec.uv);
        mesh.subMeshCount = 3;
        for (int s = 0; s < 3; s++) mesh.SetTriangles(subs[s], s);
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        MeshDecimateTool.OverwriteMesh(dec, mesh);
        Object.DestroyImmediate(mesh);
        AssetDatabase.SaveAssets();

        MakeAxeMat("M_L3_AxeBlade", new Color(0.78f, 0.80f, 0.84f), 0.95f, 0.78f);
        MakeAxeMat("M_L3_AxeIron", new Color(0.16f, 0.15f, 0.15f), 0.85f, 0.45f);
        MakeAxeMat("M_L3_AxeHaft", new Color(0.24f, 0.15f, 0.09f), 0f, 0.28f);
        return report + $" | split: blade {subs[0].Count / 3}, iron {subs[1].Count / 3}, haft {subs[2].Count / 3} tris; bounds {dec.bounds.min:F3}..{dec.bounds.max:F3}";
    }

    public static Material[] AxeMats() => new[]
    {
        AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_L3_AxeBlade.mat"),
        AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_L3_AxeIron.mat"),
        AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_L3_AxeHaft.mat"),
    };

    static void MakeAxeMat(string name, Color c, float metallic, float smooth)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_WorkflowMode", 1f); m.DisableKeyword("_SPECULAR_SETUP");
        EditorUtility.SetDirty(m);
    }

    // ======================================================================= grip
    // Measured on Ch25's Mixamo fist (sword-grip fingers), mixamorig:<Side>Hand local space, model scale 1:
    // fingers +Y, palm +Z; the curled fingers wrap a 0.035 m cylinder centred at (0, 0.108, 0.036) whose axis is the
    // knuckle line (pinky -> index = thumb side). The haft runs along that line with the head above the thumb, and the
    // blade edge leads along wrist -> knuckles (+Y), like a hammer grip.
    public static readonly Vector3 GripCentreR = new Vector3(-0.002f, 0.108f, 0.036f);
    public static readonly Vector3 GripCentreL = new Vector3(0.002f, 0.106f, 0.036f);
    public static readonly Vector3 HaftUpR = new Vector3(-0.996f, -0.07f, 0.04f);
    public static readonly Vector3 HaftUpL = new Vector3(0.996f, -0.07f, 0.04f);

    public static GameObject AttachAxe(Transform hand, bool right)
    {
        var old = hand.Find(right ? "MonsterAxe_R" : "MonsterAxe_L");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var axe = new GameObject(right ? "MonsterAxe_R" : "MonsterAxe_L");
        axe.tag = "MonsterWeapon";
        axe.transform.SetParent(hand, false);
        Vector3 up = (right ? HaftUpR : HaftUpL).normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(Vector3.up, up).normalized; // hand +Y, made perpendicular to the haft
        axe.transform.localPosition = right ? GripCentreR : GripCentreL;
        axe.transform.localRotation = Quaternion.LookRotation(fwd, up);
        axe.transform.localScale = Vector3.one;

        var vis = new GameObject("Visual");
        vis.transform.SetParent(axe.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(AxeMeshPath);
        var r = vis.AddComponent<MeshRenderer>();
        r.sharedMaterials = AxeMats();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Clash colliders, each axe independently (tag MonsterWeapon, trigger): head (blade + struts) and haft.
        // Grip space: blade y 0..0.80, 0.08..0.28 out along +Z, 0.03 thick; haft y -0.13..0.84.
        var head = axe.AddComponent<BoxCollider>();
        head.isTrigger = true;
        head.center = new Vector3(0f, 0.40f, 0.15f);
        head.size = new Vector3(0.07f, 0.84f, 0.30f);
        var haftGo = new GameObject("HaftCollider");
        haftGo.tag = "MonsterWeapon";
        haftGo.transform.SetParent(axe.transform, false);
        var haft = haftGo.AddComponent<CapsuleCollider>();
        haft.isTrigger = true; haft.direction = 1;
        haft.center = new Vector3(0f, 0.355f, 0f);
        haft.height = 0.98f; haft.radius = 0.035f;

        // Middle of the cutting edge: impact-frame measurement and weapon FX anchor.
        var edge = new GameObject("BladeEdge");
        edge.transform.SetParent(axe.transform, false);
        edge.transform.localPosition = new Vector3(0f, 0.42f, 0.26f);
        return axe;
    }

    static Material MakeLit(string path, Texture2D baseMap, Texture2D specGloss, Texture2D normal)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        m.SetTexture("_BaseMap", baseMap);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_WorkflowMode", 0f); // specular
        m.EnableKeyword("_SPECULAR_SETUP");
        m.SetTexture("_SpecGlossMap", specGloss);
        m.SetColor("_SpecColor", Color.white);
        m.EnableKeyword("_METALLICSPECGLOSSMAP");
        m.SetFloat("_SmoothnessTextureChannel", 0f); // specular alpha
        m.SetFloat("_Smoothness", 0.6f);
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
        EditorUtility.SetDirty(m);
        return m;
    }
}
