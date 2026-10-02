// Level3DualAxeBuilder.cs
// Purpose: Re-runnable build of the Level 3 boss: the Mixamo character Ch25 (Humanoid) wielding two separate axes.
//   - URP Simple Lit materials from the Mixamo non-PBR textures (diffuse / specular+gloss / normal) + a generated
//     emission mask (eyes, mouth, skin cracks over the shoulders and upper back) + glowing weak-point material
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

    // Resting body glow (MonsterGlow drives the live value through a property block): deep red, the slam danger ring's hue.
    public static readonly Color BodyGlow = new Color(0.85f, 0.07f, 0.03f);

    /// <summary>
    /// Ch25 ships non-PBR maps (Mixamo "nonPBR": Diffuse, Specular, Glossiness, Normal: a Blinn-Phong set).
    /// URP Simple Lit is that shading model. URP Lit was wrong here: its specular workflow read the flat 0.22 specular
    /// map (linear import) as F0 0.22 (skin is ~0.04) and added PBR sky reflections, which outshone the very dark maroon
    /// albedo and turned the whole body grey-blue in the night arena.
    /// </summary>
    public static string BuildMaterials(bool useNormalMaps)
    {
        var log = new System.Text.StringBuilder();
        log.Append(BuildEmissionMasks());
        foreach (int set in new[] { 1001, 1002 })
        {
            var diffuse = Tex($"Ch25_{set}_Diffuse");
            var normal = Tex($"Ch25_{set}_Normal");
            var specGloss = PackSpecGloss(set);
            SetNormalMap($"{TexDir}/Ch25_{set}_Normal.png");
            // A hand-painted mask (same UV layout as the diffuse, white = glow) wins over the generated one.
            var painted = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Ch25_{set}_Emission_Painted.png");
            var mask = painted != null ? painted : Tex($"Ch25_{set}_Emission");
            var m = MakeSimpleLit($"{MatDir}/M_L3_Ch25_{set}.mat", diffuse, specGloss, useNormalMaps ? normal : null, mask, BodyGlow);
            log.AppendLine($"{m.name}: Simple Lit, diffuse {diffuse.width}px, spec/gloss packed, normal {(useNormalMaps ? "on" : "off")}, emission mask {mask.name}");
        }
        // Weak point: same skin texture, plus emission that MonsterWeakPoint pulses through a property block (no mask:
        // the whole forearm glows green).
        var w = MakeSimpleLit($"{MatDir}/M_L3_Ch25_WeakPoint.mat", Tex("Ch25_1001_Diffuse"), AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/Ch25_1001_SpecGloss.png"),
            useNormalMaps ? Tex("Ch25_1001_Normal") : null, null, new Color(0.35f, 1.6f, 0.25f));
        AssetDatabase.SaveAssets();
        log.AppendLine("M_L3_Ch25_WeakPoint: Simple Lit, emissive green");
        return log.ToString();
    }

    // ======================================================================= emission mask
    const int EmissionSize = 2048;

    /// <summary>
    /// Writes Ch25_{set}_Emission.png (grayscale, UV space of each texture set). Two areas, chosen on the mesh in bind
    /// pose (metres, model faces +Z) and rasterised into UV space:
    ///   1. the face: both eyeballs (full) and the mouth interior (the dark throat glows, the lighter teeth barely);
    ///   2. the skin's own crack lines (dark lines in the diffuse, found by a local high-pass) over the shoulders,
    ///      collarbones and upper back, fading out toward the arms and belly.
    /// Arms below the shoulder (the green weak point is the right forearm) and the lower body stay dark.
    /// </summary>
    public static string BuildEmissionMasks()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        var verts = mesh.vertices; var normals = mesh.normals; var uvs = mesh.uv;
        var crackW = new float[verts.Length]; var faceW = new float[verts.Length]; var mawW = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i];
            crackW[i] = Mathf.Max(Mathf.Max(Ball(p, new Vector3(0.20f, 1.45f, -0.01f), 0.20f), Ball(p, new Vector3(-0.20f, 1.45f, -0.01f), 0.20f)),
                        Mathf.Max(0.85f * Ball(p, new Vector3(0f, 1.37f, 0.11f), 0.15f), Ball(p, new Vector3(0f, 1.38f, -0.12f), 0.22f)));
            bool eye = normals[i].z > 0.25f && (Vector3.Distance(p, new Vector3(0.032f, 1.67f, 0.125f)) < 0.03f || Vector3.Distance(p, new Vector3(-0.032f, 1.67f, 0.125f)) < 0.03f);
            faceW[i] = eye ? 1f : 0f;
            mawW[i] = p.x > -0.065f && p.x < 0.065f && p.y > 1.57f && p.y < 1.65f && p.z > 0.035f && p.z < 0.175f ? 1f : 0f;
        }

        var log = new System.Text.StringBuilder();
        int[] sets = { 1001, 1002 };
        for (int s = 0; s < 2; s++)
        {
            int N = EmissionSize;
            var region = new float[N * N]; var face = new float[N * N]; var maw = new float[N * N]; var cover = new bool[N * N];
            var tri = mesh.GetTriangles(s);
            for (int k = 0; k < tri.Length; k += 3)
                RasterTri(N, uvs[tri[k]], uvs[tri[k + 1]], uvs[tri[k + 2]],
                    new Vector3(crackW[tri[k]], crackW[tri[k + 1]], crackW[tri[k + 2]]),
                    new Vector3(faceW[tri[k]], faceW[tri[k + 1]], faceW[tri[k + 2]]),
                    new Vector3(mawW[tri[k]], mawW[tri[k + 1]], mawW[tri[k + 2]]), region, face, maw, cover);

            // Diffuse luminance at N (read from the PNG on disk: the importer copy is not readable and stays that way).
            var src = new Texture2D(2, 2, TextureFormat.RGB24, false);
            src.LoadImage(System.IO.File.ReadAllBytes($"{TexDir}/Ch25_{sets[s]}_Diffuse.png"));
            var px = src.GetPixels32(); int sw = src.width, f = sw / N;
            Object.DestroyImmediate(src);
            var lum = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float sum = 0f;
                    for (int dy = 0; dy < f; dy++)
                        for (int dx = 0; dx < f; dx++) { var c = px[(y * f + dy) * sw + x * f + dx]; sum += (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f; }
                    lum[y * N + x] = sum / (f * f);
                }
            // Crack / vein lines: band-pass (fine blur minus wide blur) = lines darker than the skin around them.
            // Only where the whole neighbourhood is inside the UV island (else the grey padding reads as a crack).
            var blur = BoxBlur(lum, N, CrackRadius);
            var fine = BoxBlur(lum, N, 1);
            var coverF = new float[N * N]; for (int i = 0; i < N * N; i++) coverF[i] = cover[i] ? 1f : 0f;
            var inside = BoxBlur(coverF, N, CrackRadius + 1);
            float socket = s == 0 ? 0.45f : 1f; // set 1001 holds the eyelid skin, 1002 the eyeballs themselves
            var outPx = new float[N * N];
            int lit = 0;
            for (int i = 0; i < N * N; i++)
            {
                float crack = inside[i] > 0.999f ? Mathf.Clamp01((blur[i] - fine[i]) * CrackGain - CrackBias) : 0f;
                float dark = Mathf.Clamp01(1f - lum[i] / 0.12f);
                float v = Mathf.Max(Mathf.Max(crack * region[i], socket * face[i]), 0.6f * maw[i] * dark);
                outPx[i] = v;
                if (v > 0.05f) lit++;
            }
            outPx = BoxBlur(outPx, N, 1);
            Dilate(outPx, cover, N, 3);
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var cols = new Color32[N * N];
            for (int i = 0; i < cols.Length; i++) { byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(outPx[i]) * 255f); cols[i] = new Color32(b, b, b, 255); }
            tex.SetPixels32(cols); tex.Apply();
            string path = $"{TexDir}/Ch25_{sets[s]}_Emission.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None; ti.maxTextureSize = N; ti.mipmapEnabled = true;
            ti.SaveAndReimport();
            log.AppendLine($"Ch25_{sets[s]}_Emission: {N}px, {100f * lit / (N * N):F1}% of texels glow");
        }
        return log.ToString();
    }

    public static float CrackGain = 24f, CrackBias = 0.15f; public static int CrackRadius = 6;

    static float Ball(Vector3 p, Vector3 c, float r) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f * r, r, Vector3.Distance(p, c)));

    /// <summary>Rasterise one UV triangle into three buffers; w0/w1/w2 hold each buffer's weight at vertices (a, b, c).</summary>
    static void RasterTri(int N, Vector2 a, Vector2 b, Vector2 c, Vector3 w0, Vector3 w1, Vector3 w2, float[] o0, float[] o1, float[] o2, bool[] cover)
    {
        // Texture rows: PNG row 0 is the bottom (v = 0), as Texture2D.GetPixels32 / SetPixels32 store them.
        Vector2 pa = a * N, pb = b * N, pc = c * N;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))) - 1), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))) + 1);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))) - 1), y1 = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))) + 1);
        float area = (pb.x - pa.x) * (pc.y - pa.y) - (pc.x - pa.x) * (pb.y - pa.y);
        if (Mathf.Abs(area) < 1e-8f) return;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float l1 = ((pc.x - pb.x) * (p.y - pb.y) - (pc.y - pb.y) * (p.x - pb.x)) / area;
                float l2 = ((pa.x - pc.x) * (p.y - pc.y) - (pa.y - pc.y) * (p.x - pc.x)) / area;
                float l3 = 1f - l1 - l2;
                const float e = -0.02f; // a hair outside the edge: no gaps between neighbouring triangles
                if (l1 < e || l2 < e || l3 < e) continue;
                int i = y * N + x;
                cover[i] = true;
                o0[i] = Mathf.Max(o0[i], l1 * w0.x + l2 * w0.y + l3 * w0.z);
                o1[i] = Mathf.Max(o1[i], l1 * w1.x + l2 * w1.y + l3 * w1.z);
                o2[i] = Mathf.Max(o2[i], l1 * w2.x + l2 * w2.y + l3 * w2.z);
            }
    }

    static float[] BoxBlur(float[] src, int N, int r)
    {
        var tmp = new float[N * N]; var dst = new float[N * N]; float inv = 1f / (2 * r + 1);
        for (int y = 0; y < N; y++)
        {
            float acc = 0f; int row = y * N;
            for (int x = -r; x <= r; x++) acc += src[row + Mathf.Clamp(x, 0, N - 1)];
            for (int x = 0; x < N; x++) { tmp[row + x] = acc * inv; acc += src[row + Mathf.Min(N - 1, x + r + 1)] - src[row + Mathf.Max(0, x - r)]; }
        }
        for (int x = 0; x < N; x++)
        {
            float acc = 0f;
            for (int y = -r; y <= r; y++) acc += tmp[Mathf.Clamp(y, 0, N - 1) * N + x];
            for (int y = 0; y < N; y++) { dst[y * N + x] = acc * inv; acc += tmp[Mathf.Min(N - 1, y + r + 1) * N + x] - tmp[Mathf.Max(0, y - r) * N + x]; }
        }
        return dst;
    }

    /// <summary>Grow the mask into texels outside every UV island (seam padding so mipmaps do not darken island edges).</summary>
    static void Dilate(float[] a, bool[] cover, int N, int passes)
    {
        for (int p = 0; p < passes; p++)
        {
            var src = (float[])a.Clone();
            for (int y = 1; y < N - 1; y++)
                for (int x = 1; x < N - 1; x++)
                {
                    int i = y * N + x;
                    if (cover[i] || src[i] > 0f) continue;
                    a[i] = Mathf.Max(Mathf.Max(src[i - 1], src[i + 1]), Mathf.Max(src[i - N], src[i + N]));
                }
        }
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

    static Material MakeSimpleLit(string path, Texture2D baseMap, Texture2D specGloss, Texture2D normal, Texture2D emissionMap, Color emission)
    {
        var sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else { m.shader = sh; m.shaderKeywords = new string[0]; }
        m.SetTexture("_BaseMap", baseMap);
        m.SetColor("_BaseColor", Color.white);
        // Blinn-Phong specular from the map: RGB = Mixamo's specular level, A = its glossiness (x _SpecColor.a).
        m.SetTexture("_SpecGlossMap", specGloss);
        m.SetColor("_SpecColor", new Color(1f, 1f, 1f, 1f));
        m.SetFloat("_Smoothness", 1f);
        m.SetFloat("_SmoothnessSource", 0f); // specular alpha
        m.SetFloat("_SpecularHighlights", 1f);
        m.EnableKeyword("_SPECGLOSSMAP");
        m.EnableKeyword("_SPECULAR_COLOR");
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
        m.SetTexture("_EmissionMap", emissionMap);
        m.SetColor("_EmissionColor", emission);
        m.EnableKeyword("_EMISSION");
        // RealtimeEmissive (not None/EmissiveIsBlack): URP's material validation keeps _EMISSION only then.
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }
}
