// ExecutionerMeshSplitter.cs
// Purpose: The Level 3 Armored Executioner is an unrigged single-shell mesh (Tripo export, no bones). To animate it
//          without a skeleton it is split into two rigid parts that the Generic Animator can move:
//            Lower  = legs + skirt (stays planted)
//            Upper  = torso, head, both arms and the two-handed polearm-flail (pivots at the waist), with the
//                     left forearm (the arm driving the weapon head) as its own submesh -> glowing weak point.
//          Vertices are baked into a standing, +Z-facing, metre-scaled space: feet at y = 0, Upper relative to its
//          waist pivot. Replace with a Mixamo-rigged version when available (see project_progress.md, Level 3).
// Menu: Tools > Monster > Level 3 > Rebuild Executioner Parts

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ExecutionerMeshSplitter
{
    public const string SourceMesh = "Assets/Models/Monsters/Level3/ArmoredExecutioner_Quest.asset";
    public const string LowerPath = "Assets/Models/Monsters/Level3/Executioner_Lower.asset";
    public const string UpperPath = "Assets/Models/Monsters/Level3/Executioner_Upper.asset";

    // Import space -> standing space: the FBX node is Z-up (-90 X) and faces +X; 0.98 m tall -> 2.35 m.
    public static readonly Quaternion SourceRotation = Quaternion.Euler(-90f, 90f, 0f);
    public const float Scale = 2.4f;

    // Standing-space regions (metres, feet at y = 0, monster faces +Z, its right hand at +X).
    public const float WaistY = 1.0f;
    public const float ArmsMinY = 0.72f;
    public const float LegColumnMinX = -0.42f, LegColumnMaxX = 0.60f;
    public static readonly Vector3 WaistPivot = new Vector3(0.08f, 1.02f, -0.12f);
    // Weak point = left forearm (elbow -> wrist), the arm that drives the weapon head. Capsule test on triangle centroids.
    public static readonly Vector3 WeakElbow = new Vector3(-0.60f, 1.42f, -0.10f);
    public static readonly Vector3 WeakWrist = new Vector3(-0.58f, 1.32f, 0.30f);
    public const float WeakRadius = 0.15f;
    public const float WeakMaxZ = 0.24f;

    [MenuItem("Tools/Monster/Level 3/Rebuild Executioner Parts")]
    public static void RebuildMenu() { Debug.Log(Rebuild()); }

    public static string Rebuild()
    {
        var src = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMesh);
        if (src == null) return "Source mesh missing: " + SourceMesh;

        var M = Matrix4x4.TRS(Vector3.zero, SourceRotation, Vector3.one * Scale);
        var v = src.vertices; var n = src.normals; var t = src.tangents; var uv = src.uv; var tri = src.triangles;
        var w = new Vector3[v.Length]; var wn = new Vector3[v.Length]; var wt = new Vector4[v.Length];
        float minY = float.MaxValue;
        for (int i = 0; i < v.Length; i++) { w[i] = M.MultiplyPoint3x4(v[i]); if (w[i].y < minY) minY = w[i].y; }
        for (int i = 0; i < v.Length; i++)
        {
            w[i].y -= minY;
            wn[i] = (SourceRotation * n[i]).normalized;
            Vector3 td = SourceRotation * new Vector3(t[i].x, t[i].y, t[i].z);
            wt[i] = new Vector4(td.x, td.y, td.z, t[i].w);
        }

        var lower = new List<int>(); var upper = new List<int>(); var weak = new List<int>();
        for (int k = 0; k < tri.Length; k += 3)
        {
            Vector3 c = (w[tri[k]] + w[tri[k + 1]] + w[tri[k + 2]]) / 3f;
            bool isUpper = c.y >= WaistY || (c.y >= ArmsMinY && (c.x < LegColumnMinX || c.x > LegColumnMaxX));
            var list = !isUpper ? lower : (InWeakPoint(c) ? weak : upper);
            list.Add(tri[k]); list.Add(tri[k + 1]); list.Add(tri[k + 2]);
        }

        var lowerMesh = Build("Executioner_Lower", w, wn, wt, uv, Vector3.zero, lower);
        var upperMesh = Build("Executioner_Upper", w, wn, wt, uv, WaistPivot, upper, weak);
        Save(lowerMesh, LowerPath);
        Save(upperMesh, UpperPath);
        AssetDatabase.SaveAssets();
        return $"Executioner split: lower {lower.Count / 3} tris, upper {upper.Count / 3} tris, weak point {weak.Count / 3} tris (feet offset {minY:F3}).";
    }

    public static bool InWeakPoint(Vector3 c)
    {
        if (c.z > WeakMaxZ) return false; // the fist and the polearm shaft in front of it stay unmarked
        Vector3 ab = WeakWrist - WeakElbow;
        float t = Mathf.Clamp01(Vector3.Dot(c - WeakElbow, ab) / ab.sqrMagnitude);
        return (c - (WeakElbow + ab * t)).sqrMagnitude <= WeakRadius * WeakRadius;
    }

    static Mesh Build(string name, Vector3[] w, Vector3[] wn, Vector4[] wt, Vector2[] uv, Vector3 pivot, params List<int>[] subs)
    {
        var remap = new Dictionary<int, int>();
        var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pt = new List<Vector4>(); var puv = new List<Vector2>();
        var outSubs = new List<int[]>();
        foreach (var s in subs)
        {
            var idx = new int[s.Count];
            for (int i = 0; i < s.Count; i++)
            {
                if (!remap.TryGetValue(s[i], out int d))
                {
                    d = pv.Count; remap[s[i]] = d;
                    pv.Add(w[s[i]] - pivot); pn.Add(wn[s[i]]); pt.Add(wt[s[i]]); puv.Add(uv[s[i]]);
                }
                idx[i] = d;
            }
            outSubs.Add(idx);
        }
        var m = new Mesh { name = name };
        m.indexFormat = pv.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(pv); m.SetNormals(pn); m.SetTangents(pt); m.SetUVs(0, puv);
        m.subMeshCount = outSubs.Count;
        for (int i = 0; i < outSubs.Count; i++) m.SetTriangles(outSubs[i], i, false);
        m.RecalculateBounds();
        return m;
    }

    static void Save(Mesh m, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { MeshDecimateTool.OverwriteMesh(existing, m); Object.DestroyImmediate(m); }
        else AssetDatabase.CreateAsset(m, path);
    }
}
