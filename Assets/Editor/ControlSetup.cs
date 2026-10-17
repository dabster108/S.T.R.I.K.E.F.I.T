// ControlSetup.cs
// Purpose: One-time, idempotent editor setup for the controller mapping: creates Resources/ControlSettings.asset and swaps the
//          stock OVRInputModule for StrikeFitInputModule on the EventSystem of the menu and the three levels (same serialized
//          values; a scene that already has the subclass is left alone). Menu: Tools > Controls > Apply.
// Dependencies: StrikeFitInputModule, ControlSettings
// Attach to: nothing (Editor)

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

public static class ControlSetup
{
    const string SettingsPath = "Assets/Resources/ControlSettings.asset";
    static readonly string[] Scenes =
    {
        "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Level1_Easy.unity", "Assets/Scenes/Level2_Medium.unity", "Assets/Scenes/Level3_Hard.unity",
    };

    [MenuItem("Tools/Controls/Apply")]
    public static string Apply()
    {
        var sb = new System.Text.StringBuilder();
        if (AssetDatabase.LoadAssetAtPath<ControlSettings>(SettingsPath) == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ControlSettings>(), SettingsPath);
            AssetDatabase.SaveAssets();
            sb.AppendLine("created " + SettingsPath);
        }
        else sb.AppendLine(SettingsPath + " exists");

        string startScene = EditorSceneManager.GetActiveScene().path;
        foreach (var path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            sb.AppendLine(SwapModule(scene.name));
            EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrEmpty(startScene)) EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);
        return sb.ToString();
    }

    static string SwapModule(string sceneName)
    {
        var stock = Object.FindAnyObjectByType<OVRInputModule>(FindObjectsInactive.Include);
        if (stock == null) return sceneName + ": no OVRInputModule";
        if (stock is StrikeFitInputModule) return sceneName + ": already StrikeFitInputModule";
        var go = stock.gameObject;
        string json = JsonUtility.ToJson(stock);
        var ray = stock.rayTransform;
        Object.DestroyImmediate(stock);
        var module = go.AddComponent<StrikeFitInputModule>();
        JsonUtility.FromJsonOverwrite(json, module);
        module.rayTransform = ray;
        module.m_Cursor = null;
        EditorUtility.SetDirty(module);
        return sceneName + ": swapped (rayTransform=" + (module.rayTransform != null ? module.rayTransform.name : "null") + ", click=" + module.joyPadClickButton + ")";
    }
}
