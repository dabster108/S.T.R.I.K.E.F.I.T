// WeaponProxySetup.cs
// Purpose: Creates M_WeaponGhost (faint unlit transparent URP material for the ghost of the real controller pose) and assigns it
//          to PlayerCombat.weaponGhostMaterial in the three level scenes, so the shader is referenced by the scene and ships in
//          builds. Idempotent. The WeaponSurfaceProxy components themselves are added at equip time by PlayerCombat.
// Dependencies: PlayerCombat, WeaponSurfaceProxy
// Attach to: nothing (menu: Tools/Combat/Set Up Weapon Proxy, or over Unity MCP)

using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WeaponProxySetup
{
    const string GhostPath = "Assets/Materials/FX/M_WeaponGhost.mat";
    static readonly string[] Scenes = { "Assets/Scenes/Level1_Easy.unity", "Assets/Scenes/Level2_Medium.unity", "Assets/Scenes/Level3_Hard.unity" };

    [MenuItem("Tools/Combat/Set Up Weapon Proxy")]
    public static void Menu() { Debug.Log(Apply()); }

    public static string Apply()
    {
        var log = new StringBuilder();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(GhostPath);
        if (mat == null)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GhostPath));
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_WeaponGhost" };
            AssetDatabase.CreateAsset(mat, GhostPath);
        }
        mat.shader = Shader.Find("Universal Render Pipeline/Unlit");
        mat.SetColor("_BaseColor", new Color(0.55f, 0.8f, 1f, 0.18f));
        mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        log.AppendLine($"material {GhostPath}: {mat.shader.name}, alpha {mat.GetColor("_BaseColor").a:0.00}");
        foreach (var path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var pc = Object.FindAnyObjectByType<PlayerCombat>();
            if (pc == null) { log.AppendLine(path + ": no PlayerCombat"); continue; }
            pc.weaponSurfaceProxy = true; pc.weaponGhost = true; pc.weaponGhostMaterial = mat;
            EditorUtility.SetDirty(pc);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"{path}: PlayerCombat.weaponGhostMaterial set");
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }
}
