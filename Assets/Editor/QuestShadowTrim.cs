// QuestShadowTrim.cs
// Purpose: Quest shadow budget for Level 3 (docs/performance-audit.md). Every shadow caster is drawn a second time into the
//          main-light shadow map, so small props stop casting:
//            - scene mesh renderers whose largest bounds side is at most SmallPropMaxSize (lamps, lamp glass, mooring holders,
//              small crates), and flat planes wider than 50 m (the sea: nothing lies under it to receive a shadow);
//            - the staged weapon prefabs (all of their parts are under 1 m);
//            - the chain links of the held flails (17 per flail); the flail handle and ball keep their shadows.
//          Kept: the monster and everything on the player (spawned at runtime, never touched here), and every larger prop or
//          piece of architecture (walls, columns, fences, boats, posts, the dock, crates and barrels of 1.4 m).
//          Receiving shadows is unchanged. Called by Level3SceneBuilder.Build and Level3Builder.BuildFlail so rebuilds keep it.
// Attach to: nothing (static, Editor only)

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class QuestShadowTrim
{
    public const float SmallPropMaxSize = 1.05f;
    public static readonly string[] StagedPrefabs =
    {
        "Assets/Prefabs/Weapons/Level3/Staged_Flail.prefab",
        "Assets/Prefabs/Weapons/Level3/Staged_Mace.prefab",
        "Assets/Prefabs/Weapons/Level3/Staged_Katana.prefab",
    };
    public static readonly string[] FlailPrefabs =
    {
        "Assets/Prefabs/Weapons/Level3/PlayerFlail_Right.prefab",
        "Assets/Prefabs/Weapons/Level3/PlayerFlail_Left.prefab",
    };

    static bool Small(Renderer r)
    {
        var s = r.bounds.size;
        float max = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        bool hugePlane = s.y < 0.1f && Mathf.Min(s.x, s.z) > 50f;
        return max <= SmallPropMaxSize || hugePlane;
    }

    // Through SerializedObject so a change on a prefab instance is recorded as an override and saved with the scene.
    static bool Off(Renderer r)
    {
        if (r.shadowCastingMode == ShadowCastingMode.Off) return false;
        var so = new SerializedObject(r);
        so.FindProperty("m_CastShadows").intValue = (int)ShadowCastingMode.Off;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    /// <summary>Small props in the open scene. Objects under the player rig or a monster, and staged-weapon instances (handled in
    /// their prefabs), are left alone.</summary>
    public static string ApplyToOpenScene()
    {
        int n = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r.GetComponentInParent<OVRCameraRig>(true) != null || r.GetComponentInParent<MonsterController>(true) != null) continue;
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
            if (root != null && System.Array.IndexOf(StagedPrefabs, AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root))) >= 0) continue;
            if (!r.gameObject.activeInHierarchy && r.bounds.size == Vector3.zero) continue; // inactive: bounds unknown, leave it
            if (Small(r) && Off(r)) n++;
        }
        if (n > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        return $"QuestShadowTrim: {n} small scene props stop casting shadows.\n";
    }

    /// <summary>The chain links of a flail root (children of "Chain").</summary>
    public static int ChainLinksOff(GameObject flailRoot)
    {
        int n = 0;
        var chain = flailRoot.transform.Find("Chain");
        if (chain == null) return 0;
        foreach (var r in chain.GetComponentsInChildren<MeshRenderer>(true)) if (Off(r)) n++;
        return n;
    }

    /// <summary>Every mesh renderer under a root (the staged weapons).</summary>
    public static int AllOff(GameObject root)
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) if (Off(r)) n++;
        return n;
    }

    public static string ApplyToPrefabs()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in StagedPrefabs) sb.AppendLine(EditPrefab(p, AllOff));
        foreach (var p in FlailPrefabs) sb.AppendLine(EditPrefab(p, ChainLinksOff));
        return sb.ToString();
    }

    static string EditPrefab(string path, System.Func<GameObject, int> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) return "missing " + path;
        try
        {
            int n = edit(root);
            if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            return $"{path}: {n} renderers stop casting shadows";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("StrikeFit/Quest/Trim Level 3 shadow casters (open scene + weapon prefabs)")]
    static void Menu() => Debug.Log(ApplyToPrefabs() + ApplyToOpenScene());
}
