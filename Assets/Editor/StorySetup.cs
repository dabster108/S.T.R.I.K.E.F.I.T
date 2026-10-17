// StorySetup.cs
// Purpose: One-time, idempotent editor setup for Story Mode: import settings for the guardian portraits (Sprite, no mipmaps,
//          1024 px at most, ASTC 6x6 on Android) and the main menu rebuild (MenuSceneBuilder.Build: Story Mode, Guardians and the
//          two-column home). Menu: Tools > Story > Apply.
// Dependencies: MenuSceneBuilder, Assets/Resources/Guardians/level{1,2,3}.png (content/sync-story.js)
// Attach to: nothing (Editor)

using UnityEditor;
using UnityEngine;

public static class StorySetup
{
    public static readonly string[] Portraits =
    {
        "Assets/Resources/Guardians/level1.png", "Assets/Resources/Guardians/level2.png", "Assets/Resources/Guardians/level3.png",
    };

    [MenuItem("Tools/Story/Apply")]
    public static string Apply()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ImportPortraits());
        sb.AppendLine(MenuSceneBuilder.Build());
        return sb.ToString();
    }

    public static string ImportPortraits()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var path in Portraits)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { sb.AppendLine(path + ": not found (run node content/sync-story.js)"); continue; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.Compressed;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            sb.AppendLine($"{path}: Sprite, no mipmaps, Android ASTC 6x6, {tex.width}x{tex.height}");
        }
        return sb.ToString();
    }
}
