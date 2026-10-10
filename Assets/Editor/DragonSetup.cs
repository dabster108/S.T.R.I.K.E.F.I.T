// DragonSetup.cs
// Purpose: Puts the DragonHazard director into the Level 3 scene (idempotent). Everything is wired - flame material, an orange
//          ground-zone indicator cloned from the boss's slam zone, existing audio clips - and the master switch is OFF with an empty
//          prefab slot: the dragon model in _SourceDownloads is over the asset budget (see project_progress.md), so no dragon is
//          shipped until a model within the budget is dropped into DragonHazard.dragonPrefab.
// Menu: Tools > Level 3 > Set up Dragon Hazard (disabled until a model is provided)

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DragonSetup
{
    const string ScenePath = "Assets/Scenes/Level3_Hard.unity";
    const string FlameMat = "Assets/Materials/Level3/M_L3_Ember.mat";
    const string FlameMatAlt = "Assets/VFX/Level2/M_L2_FireAdditive.mat";
    const string BossPrefab = "Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab";

    [MenuItem("Tools/Level 3/Set up Dragon Hazard (disabled until a model is provided)")]
    public static void Menu() { Debug.Log(Apply()); }

    public static string Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var go = GameObject.Find("DragonDirector");
        if (go == null) go = new GameObject("DragonDirector");
        var hz = go.GetComponent<DragonHazard>();
        if (hz == null) hz = go.AddComponent<DragonHazard>();

        hz.dragonsEnabled = false;                 // shipped off: no model within the budget yet
        hz.allowStandIn = false; hz.hittable = false;
        hz.dragonsByPhase = new[] { 0, 1, 2 };
        hz.flameMaterial = AssetDatabase.LoadAssetAtPath<Material>(FlameMat);
        if (hz.flameMaterial == null) hz.flameMaterial = AssetDatabase.LoadAssetAtPath<Material>(FlameMatAlt);
        hz.roarClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Level3/L3_Roar_Enrage.wav");
        hz.warningClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Level3/L3_Slam_Warning.wav");
        hz.flameClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Level2/L2_Special_FireRoar.wav");
        hz.wingClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Level2/L2_Axe_Whoosh_Heavy.wav");

        // Ground-zone indicator: a copy of the boss slam zone, in fire colours.
        var zone = go.transform.Find("DragonFireZone");
        if (zone == null)
        {
            var boss = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefab);
            var src = boss != null ? boss.transform.Find("DangerZone") : null;
            if (src != null)
            {
                var copy = (GameObject)Object.Instantiate(src.gameObject);
                copy.name = "DragonFireZone";
                copy.transform.SetParent(go.transform, false);
                zone = copy.transform;
            }
        }
        if (zone != null)
        {
            var ind = zone.GetComponent<DangerZoneIndicator>();
            if (ind != null)
            {
                ind.fillColor = new Color(1f, 0.45f, 0.05f, 0.35f);
                if (ind.ring != null) ind.ring.color = new Color(1f, 0.55f, 0.1f, 1f);
                hz.zoneIndicator = ind;
            }
            // DangerZoneIndicator un-parents itself in Awake, so the scene copy is kept inactive-safe at its own root.
        }
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"DragonDirector in {ScenePath}: enabled {hz.dragonsEnabled}, prefab {(hz.dragonPrefab != null)}, flame material {(hz.flameMaterial != null ? hz.flameMaterial.name : "NONE")}, " +
               $"zone indicator {(hz.zoneIndicator != null)}, roar {(hz.roarClip != null)}, warning {(hz.warningClip != null)}, flame {(hz.flameClip != null)}, wing {(hz.wingClip != null)}";
    }
}
