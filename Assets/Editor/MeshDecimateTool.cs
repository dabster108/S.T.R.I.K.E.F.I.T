// MeshDecimateTool.cs
// Purpose: Bake a Quest-friendly copy of a heavy mesh. Uses Unity's own Mesh LOD generator (UV-seam aware),
//          picks the generated level closest to a triangle budget and saves it as a standalone .asset with only
//          the vertices that level uses. The source model is not modified.
// Used for: Level 3 Armored Executioner (1.9 M tris) and the Level 3 weapon meshes.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MeshDecimateTool
{
    /// <summary>
    /// Rewrites an existing mesh asset in place (keeps its GUID and every reference to it). Goes through the Mesh API so
    /// the GPU buffers are re-uploaded: EditorUtility.CopySerialized left stale index data that drew the wrong triangles
    /// with the second submesh's material.
    /// </summary>
    public static void OverwriteMesh(Mesh target, Mesh source)
    {
        target.Clear();
        target.name = source.name;
        target.indexFormat = source.indexFormat;
        target.SetVertices(source.vertices);
        if (source.normals.Length > 0) target.SetNormals(source.normals);
        if (source.tangents.Length > 0) target.SetTangents(source.tangents);
        if (source.uv.Length > 0) target.SetUVs(0, source.uv);
        if (source.colors.Length > 0) target.SetColors(source.colors);
        target.subMeshCount = source.subMeshCount;
        for (int s = 0; s < source.subMeshCount; s++) target.SetTriangles(source.GetTriangles(s), s, false);
        target.RecalculateBounds();
        EditorUtility.SetDirty(target);
    }

    /// <summary>Returns the saved mesh. Report lists the triangle count of every generated level.</summary>
    public static Mesh Decimate(Mesh source, int targetTriangles, string assetPath, out string report, string meshName = null)
    {
        var work = Object.Instantiate(source);
        work.name = source.name + "_lodwork";
        MeshLodUtility.GenerateMeshLods(work, 16);

        var sb = new System.Text.StringBuilder();
        int subCount = work.subMeshCount;
        int levels = Mathf.Max(1, work.lodCount);

        // Pick the level whose total triangle count is closest to the budget without exceeding it (else the smallest).
        int best = levels - 1;
        for (int l = 0; l < levels; l++)
        {
            int tris = 0;
            for (int s = 0; s < subCount; s++) tris += (int)work.GetIndexCount(s, l) / 3;
            sb.Append($"L{l}={tris} ");
            if (tris <= targetTriangles) { best = l; break; }
        }

        var srcVerts = work.vertices;
        var srcNormals = work.normals;
        var srcTangents = work.tangents;
        var srcUv = work.uv;
        var srcColors = work.colors;
        bool hasN = srcNormals != null && srcNormals.Length == srcVerts.Length;
        bool hasT = srcTangents != null && srcTangents.Length == srcVerts.Length;
        bool hasUv = srcUv != null && srcUv.Length == srcVerts.Length;
        bool hasC = srcColors != null && srcColors.Length == srcVerts.Length;

        var remap = new Dictionary<int, int>();
        var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tangents = new List<Vector4>();
        var uvs = new List<Vector2>(); var colors = new List<Color>();
        var subIndices = new List<int[]>();
        for (int s = 0; s < subCount; s++)
        {
            var lodIndices = work.GetIndices(s, best, true);
            int count = lodIndices.Length;
            var outIdx = new int[count];
            for (int i = 0; i < count; i++)
            {
                int src = lodIndices[i];
                if (!remap.TryGetValue(src, out int dst))
                {
                    dst = verts.Count;
                    remap[src] = dst;
                    verts.Add(srcVerts[src]);
                    if (hasN) normals.Add(srcNormals[src]);
                    if (hasT) tangents.Add(srcTangents[src]);
                    if (hasUv) uvs.Add(srcUv[src]);
                    if (hasC) colors.Add(srcColors[src]);
                }
                outIdx[i] = dst;
            }
            subIndices.Add(outIdx);
        }

        var mesh = new Mesh { name = meshName ?? source.name + "_Quest" };
        mesh.indexFormat = verts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        if (hasN) mesh.SetNormals(normals);
        if (hasUv) mesh.SetUVs(0, uvs);
        if (hasC) mesh.SetColors(colors);
        mesh.subMeshCount = subCount;
        for (int s = 0; s < subCount; s++) mesh.SetTriangles(subIndices[s], s, false);
        if (!hasN) mesh.RecalculateNormals();
        if (hasT) mesh.SetTangents(tangents); else if (hasUv) mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        Object.DestroyImmediate(work);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null) { OverwriteMesh(existing, mesh); Object.DestroyImmediate(mesh); mesh = existing; }
        else AssetDatabase.CreateAsset(mesh, assetPath);
        AssetDatabase.SaveAssets();

        int finalTris = 0; for (int s = 0; s < subCount; s++) finalTris += subIndices[s].Length / 3;
        report = $"{source.name}: {source.triangles.Length / 3} tris -> level {best} = {finalTris} tris, {verts.Count} verts. Levels: {sb}";
        return mesh;
    }
}
