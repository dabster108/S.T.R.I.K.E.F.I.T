// SceneFlow.cs
// Purpose: The one place that changes scene: main menu, level select, play again. Static, no scene object needed, so every
//          level scene still runs on its own in the Editor. One load at a time (double clicks and an A press in the same frame
//          are ignored), time scale back to 1, the retry flag set only for Play Again, and the name preference is left alone
//          (PlayerNameStore keeps it in PlayerPrefs). Scenes load asynchronously so a "Loading..." label can draw.
// Dependencies: GameStateManager (retry flag), SceneManager, Build Settings (MainMenu + three levels)
// Attach to: nothing (static)

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneFlow
{
    public const string MenuScene = "MainMenu";
    public const string Level1 = "Level1_Easy", Level2 = "Level2_Medium", Level3 = "Level3_Hard";

    /// <summary>A scene load is under way; every further request is ignored until it finishes.</summary>
    public static bool Busy { get; private set; }
    public static string Target { get; private set; }
    /// <summary>Raised once, just before a load starts (scene name).</summary>
    public static event Action<string> LoadStarting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Busy = false; Target = null; LoadStarting = null; }

    public static bool InBuild(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            if (System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)) == sceneName) return true;
        return false;
    }

    /// <summary>Loads a scene by name. Returns false when a load is already running or the scene is not in Build Settings.</summary>
    public static bool Load(string sceneName, bool retry = false)
    {
        if (Busy || string.IsNullOrEmpty(sceneName)) return false;
        if (!InBuild(sceneName))
        {
            Debug.LogError($"[SceneFlow] Scene '{sceneName}' is not in Build Settings.");
            return false;
        }
        Busy = true;
        Target = sceneName;
        Time.timeScale = 1f;                       // a slow-motion finale must not follow us into the next scene
        if (retry) GameStateManager.FlagRetry(sceneName); else GameStateManager.ClearRetry();
        CombatDebugInfo.ResetCounts();              // debug counters belong to one scene
        LoadStarting?.Invoke(sceneName);
        var op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null) { Busy = false; return false; }
        op.completed += _ => { Busy = false; Target = null; };
        return true;
    }

    /// <summary>Reload the scene that is open (fresh fight; the Level 2 entrance plays its short version).</summary>
    public static bool PlayAgain() => Load(SceneManager.GetActiveScene().name, true);

    /// <summary>Back to the main menu.</summary>
    public static bool LevelSelect() => Load(MenuScene);

    /// <summary>Closes the app (Quest: back to Home). In the Editor it stops Play mode instead.</summary>
    public static void QuitNow()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
