// MonsterHitVolumeBuilder.cs
// Editor-only utility: builds bone-following body hit volumes (HitVol_* capsules) on a Mixamo monster prefab so
// a weapon registers anywhere on the visible body, not only on the static root capsule.
// Each capsule is fitted to the skinned-mesh vertices weighted to that body part and parented to its bone.
// Weapon colliders (tag MonsterWeapon) are never touched.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterHitVolumeBuilder
{
    const string Prefix = "HitVol_";

    struct Part
    {
        public string name;      // HitVol_<name>
        public string parent;    // bone the capsule follows
        public string end;       // limb parts: the capsule always spans parent -> end, even where few vertices are weighted
        public string[] roots;   // vertices weighted to these bones (and their children, until another part's root)
        public Part(string name, string parent, string end, params string[] roots) { this.name = name; this.parent = parent; this.end = end; this.roots = roots; }
    }

    static readonly Part[] Parts =
    {
        new Part("Hips", "Hips", null, "Hips"),
        new Part("Chest", "Spine1", null, "Spine"),
        new Part("Head", "Head", null, "Neck"),
        new Part("L_UpperArm", "LeftArm", "LeftForeArm", "LeftShoulder"),
        new Part("L_LowerArm", "LeftForeArm", "LeftHand", "LeftForeArm"),
        new Part("R_UpperArm", "RightArm", "RightForeArm", "RightShoulder"),
        new Part("R_LowerArm", "RightForeArm", "RightHand", "RightForeArm"),
        new Part("L_UpperLeg", "LeftUpLeg", "LeftLeg", "LeftUpLeg"),
        new Part("L_LowerLeg", "LeftLeg", "LeftFoot", "LeftLeg"),
        new Part("L_Foot", "LeftFoot", "LeftToeBase", "LeftFoot"),
        new Part("R_UpperLeg", "RightUpLeg", "RightLeg", "RightUpLeg"),
        new Part("R_LowerLeg", "RightLeg", "RightFoot", "RightLeg"),
        new Part("R_Foot", "RightFoot", "RightToeBase", "RightFoot"),
    };

    [MenuItem("Tools/Monster/Rebuild Body Hit Volumes (selected prefab)")]
    static void RebuildSelected()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
        {
            Debug.LogWarning("[MonsterHitVolumeBuilder] Select a monster prefab asset first.");
            return;
        }
        Debug.Log(Rebuild(path, 0.85f));
    }

    /// <summary>
    /// Replaces every HitVol_* child of the prefab with freshly fitted capsules.
    /// radiusPercentile: share of a part's vertices that must lie inside the capsule radius (0.85 = generous).
    /// </summary>
    public static string Rebuild(string prefabPath, float radiusPercentile)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var report = Build(root, radiusPercentile);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return report;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string Build(GameObject root, float radiusPercentile)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var old in root.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name.StartsWith(Prefix)).ToArray())
            Object.DestroyImmediate(old.gameObject);

        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (smrs.Length == 0) return "No SkinnedMeshRenderer under " + root.name;

        // World-space vertices in the current (bind) pose + dominant bone per vertex.
        var verts = new List<Vector3>();
        var vertBone = new List<Transform>();
        foreach (var smr in smrs)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var bv = baked.vertices;
            var weights = mesh.boneWeights;
            var bones = smr.bones;
            // BakeMesh(useScale: true) already applies the renderer's scale; only position + rotation remain.
            var l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            for (int i = 0; i < bv.Length && i < weights.Length; i++)
            {
                verts.Add(l2w.MultiplyPoint3x4(bv[i]));
                vertBone.Add(bones[weights[i].boneIndex0]);
            }
            Object.DestroyImmediate(baked);
        }

        var all = root.GetComponentsInChildren<Transform>(true);
        System.Func<string, Transform> bone = n => all.FirstOrDefault(t => t.name == "mixamorig:" + n || t.name == n);

        // Bone -> part index: a part owns its root bones and every descendant not claimed by another part.
        var owner = new Dictionary<Transform, int>();
        var rootToPart = new Dictionary<Transform, int>();
        for (int p = 0; p < Parts.Length; p++)
            foreach (var r in Parts[p].roots)
            {
                var t = bone(r);
                if (t != null) rootToPart[t] = p;
            }
        foreach (var t in all)
        {
            for (var cur = t; cur != null && cur != root.transform.parent; cur = cur.parent)
            {
                int p;
                if (rootToPart.TryGetValue(cur, out p)) { owner[t] = p; break; }
            }
        }

        int layer = root.layer;
        for (int p = 0; p < Parts.Length; p++)
        {
            var parent = bone(Parts[p].parent);
            if (parent == null) { sb.AppendLine("  skip " + Parts[p].name + " (bone missing)"); continue; }

            var pts = new List<Vector3>();
            for (int i = 0; i < verts.Count; i++)
            {
                int o;
                if (vertBone[i] != null && owner.TryGetValue(vertBone[i], out o) && o == p) pts.Add(verts[i]);
            }
            if (pts.Count < 8) { sb.AppendLine("  skip " + Parts[p].name + " (" + pts.Count + " verts)"); continue; }

            // Principal axis of the part (power iteration on the covariance).
            Vector3 mean = Vector3.zero;
            foreach (var v in pts) mean += v;
            mean /= pts.Count;
            Vector3 axis = Vector3.up;
            for (int it = 0; it < 24; it++)
            {
                Vector3 next = Vector3.zero;
                foreach (var v in pts)
                {
                    Vector3 d = v - mean;
                    next += d * Vector3.Dot(d, axis);
                }
                if (next.sqrMagnitude < 1e-12f) break;
                axis = next.normalized;
            }

            float min = float.MaxValue, max = float.MinValue;
            var radial = new List<float>(pts.Count);
            foreach (var v in pts)
            {
                float a = Vector3.Dot(v - mean, axis);
                min = Mathf.Min(min, a);
                max = Mathf.Max(max, a);
                radial.Add(((v - mean) - axis * a).magnitude);
            }
            var endBone = Parts[p].end != null ? bone(Parts[p].end) : null;
            if (endBone != null)
            {
                foreach (var joint in new[] { parent.position, endBone.position })
                {
                    float a = Vector3.Dot(joint - mean, axis);
                    min = Mathf.Min(min, a);
                    max = Mathf.Max(max, a);
                }
            }
            radial.Sort();
            float radius = radial[Mathf.Clamp(Mathf.RoundToInt(radiusPercentile * (radial.Count - 1)), 0, radial.Count - 1)];
            float length = max - min;
            Vector3 center = mean + axis * ((min + max) * 0.5f);

            var go = new GameObject(Prefix + Parts[p].name);
            go.layer = layer;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);
            Vector3 ls = go.transform.lossyScale;
            float s = Mathf.Max(0.0001f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);

            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.isTrigger = false;
            cap.radius = radius / s;
            cap.height = Mathf.Max(length, radius * 2f) / s;

            sb.AppendLine(string.Format("  {0} on {1}: r {2:F3} h {3:F3} ({4} verts)", go.name, parent.name, radius, Mathf.Max(length, radius * 2f), pts.Count));
        }
        return "Hit volumes for " + root.name + ":\n" + sb;
    }
}
#endif
