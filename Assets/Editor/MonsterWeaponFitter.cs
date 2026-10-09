// MonsterWeaponFitter.cs
// Purpose: Fits each monster weapon's clash colliders to its visible (opaque) mesh and puts the monster's colliders on their
//          own layers: weapon colliders on "MonsterWeapon", body hit volumes on "MonsterBody" (only GameObjects without a
//          renderer change layer, so nothing renders differently). The weapon is cut into slabs along its long axis and each
//          slab gets one box around the mesh inside it plus a margin, so blade / head, haft and pommel are all covered and
//          nothing much else is. Transparent glow planes (render queue 2450+) are effects, not weapon, and are left out.
//          The weapon colliders stay triggers on the animated hand bone, always enabled while the weapon is shown.
//          CombatTuning.ConfigureMonster calls Fit, so Level3DualAxeBuilder rebuilds keep it.
// Dependencies: none at runtime (Editor only)

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MonsterWeaponFitter
{
    public const string WeaponLayerName = "MonsterWeapon";
    public const string BodyLayerName = "MonsterBody";
    public const string BoxPrefix = "ClashBox_";
    /// <summary>World margin around the visible mesh (m).</summary>
    public static float Margin = 0.04f;
    /// <summary>Target slab length along the weapon (m); 3 to 6 slabs per weapon.</summary>
    public static float SlabLength = 0.3f;

    /// <summary>Adds the two layers if missing and keeps them apart in the collision matrix (weapon-weapon, weapon-body,
    /// body-body). Every other pair is left as it was.</summary>
    public static string EnsureLayers()
    {
        var log = new StringBuilder();
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        foreach (var name in new[] { WeaponLayerName, BodyLayerName })
        {
            if (LayerMask.NameToLayer(name) >= 0) continue;
            for (int i = 8; i < 32; i++)
            {
                var sp = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(sp.stringValue)) continue;
                sp.stringValue = name;
                log.Append($"layer {i} = {name}; ");
                break;
            }
        }
        tm.ApplyModifiedPropertiesWithoutUndo();
        int w = LayerMask.NameToLayer(WeaponLayerName), b = LayerMask.NameToLayer(BodyLayerName);
        if (w < 0 || b < 0) return log + "layers could not be added";
        Physics.IgnoreLayerCollision(w, w, true);
        Physics.IgnoreLayerCollision(w, b, true);
        Physics.IgnoreLayerCollision(b, b, true);
        log.Append($"matrix: {WeaponLayerName}({w}) x {WeaponLayerName} off, x {BodyLayerName}({b}) off, {BodyLayerName} x {BodyLayerName} off");
        return log.ToString();
    }

    /// <summary>Weapon roots: MonsterWeapon-tagged objects whose parent is not part of the same weapon.</summary>
    public static List<Transform> WeaponRoots(GameObject monster)
    {
        var roots = new List<Transform>();
        foreach (var t in monster.GetComponentsInChildren<Transform>(true))
            if (t.CompareTag("MonsterWeapon") && (t.parent == null || !t.parent.CompareTag("MonsterWeapon")) && !roots.Contains(t))
                roots.Add(t);
        return roots;
    }

    /// <summary>Refits every weapon of the monster and sets the collider layers. Idempotent.</summary>
    public static string Fit(GameObject monster)
    {
        var log = new StringBuilder();
        int wl = LayerMask.NameToLayer(WeaponLayerName), bl = LayerMask.NameToLayer(BodyLayerName);
        if (wl < 0 || bl < 0) log.AppendLine(EnsureLayers());
        wl = LayerMask.NameToLayer(WeaponLayerName); bl = LayerMask.NameToLayer(BodyLayerName);
        foreach (var root in WeaponRoots(monster)) log.AppendLine(FitWeapon(root, wl));
        int body = 0;
        foreach (var c in monster.GetComponentsInChildren<Collider>(true))
        {
            if (c.CompareTag("MonsterWeapon") || c.GetComponent<Renderer>() != null) continue;
            if (c.gameObject.layer != bl) { c.gameObject.layer = bl; body++; }
        }
        log.Append($"{monster.name}: {body} body collider objects moved to {BodyLayerName}");
        return log.ToString();
    }

    static string FitWeapon(Transform root, int weaponLayer)
    {
        // Opaque mesh triangles in the root's local space, edges densified so every slab a triangle crosses gets points.
        var pts = new List<Vector3>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null || mf.sharedMesh == null || !IsOpaque(r)) continue;
            var m = mf.sharedMesh;
            var toLocal = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var v = m.vertices; var tri = m.triangles;
            var lv = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) { lv[i] = toLocal.MultiplyPoint3x4(v[i]); pts.Add(lv[i]); }
            for (int i = 0; i + 2 < tri.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    Vector3 a = lv[tri[i + e]], b = lv[tri[i + (e + 1) % 3]];
                    int n = Mathf.Min(16, Mathf.FloorToInt(Vector3.Distance(a, b) * Scale(root) / 0.04f));
                    for (int k = 1; k < n; k++) pts.Add(Vector3.Lerp(a, b, k / (float)n));
                }
        }
        if (pts.Count == 0) return $"{root.name}: no opaque mesh, left as is";

        Vector3 mn = Vector3.one * 1e9f, mx = -mn;
        foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        Vector3 size = mx - mn;
        int ax = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
        float scale = Scale(root);
        float lengthWorld = size[ax] * scale;
        int slabs = Mathf.Clamp(Mathf.CeilToInt(lengthWorld / SlabLength), 3, 6);
        float slabLocal = size[ax] / slabs, marginLocal = Margin / scale;

        // Old colliders out (the root's own and any MonsterWeapon child collider, e.g. "HaftCollider").
        var removed = new StringBuilder();
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
        {
            if (!c.CompareTag("MonsterWeapon")) continue;
            removed.Append(c.GetType().Name).Append(' ');
            var go = c.gameObject;
            Object.DestroyImmediate(c, true);
            if (go != root.gameObject && go.name != null && !go.name.StartsWith(BoxPrefix) &&
                go.transform.childCount == 0 && go.GetComponents<Component>().Length == 1)
                Object.DestroyImmediate(go, true); // an empty "HaftCollider" holder
        }
        for (int i = root.childCount - 1; i >= 0; i--)
            if (root.GetChild(i).name.StartsWith(BoxPrefix)) Object.DestroyImmediate(root.GetChild(i).gameObject, true);

        var sb = new StringBuilder($"{root.name}: {pts.Count} mesh points, length {lengthWorld:0.00} m, {slabs} boxes (margin {Margin * 100f:0} cm); removed {removed}; ");
        for (int s = 0; s < slabs; s++)
        {
            float lo = mn[ax] + slabLocal * s, hi = lo + slabLocal;
            Vector3 smn = Vector3.one * 1e9f, smx = -smn; int n = 0;
            foreach (var p in pts)
            {
                if (p[ax] < lo - 1e-5f || p[ax] > hi + 1e-5f) continue;
                smn = Vector3.Min(smn, p); smx = Vector3.Max(smx, p); n++;
            }
            if (n == 0) continue;
            smn[ax] = lo; smx[ax] = hi;                                  // slabs abut: no gap between boxes
            smn -= Vector3.one * marginLocal; smx += Vector3.one * marginLocal;
            var go = new GameObject(BoxPrefix + s);
            go.tag = "MonsterWeapon";
            go.layer = weaponLayer;
            go.transform.SetParent(root, false);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = (smn + smx) * 0.5f;
            box.size = smx - smn;
            Vector3 w = box.size * scale;
            sb.Append($"[{s}: {w.x * 100f:0}x{w.y * 100f:0}x{w.z * 100f:0} cm] ");
        }
        root.gameObject.layer = root.GetComponent<Renderer>() == null ? weaponLayer : root.gameObject.layer;
        return sb.ToString();
    }

    static float Scale(Transform t) { var s = t.lossyScale; return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))); }

    static bool IsOpaque(Renderer r)
    {
        foreach (var m in r.sharedMaterials) if (m != null && m.renderQueue < 2450) return true;
        return false;
    }
}
