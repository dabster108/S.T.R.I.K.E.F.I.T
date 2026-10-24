// EditorTelemetryGuard.cs
// Purpose: Editor test runs only. While armed (SessionState, survives Play-mode entries and domain reloads until disarmed), every
//          SessionReporter in every scene that loads is pointed at a dead local address before its Start runs, so no test
//          session reaches the live server. The scene's own value is untouched (a Play-mode change is not saved).
// Arm / disarm: EditorTelemetryGuard.Arm() / Disarm() (Editor), or the StrikeFit/Test menu (OptimisationHarness).
// Attach to: nothing (static). Compiled out of builds.
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EditorTelemetryGuard
{
    public const string Key = "StrikeFit.TelemetryGuard";
    public const string DeadUrl = "http://127.0.0.1:9";
    public static int Redirected { get; private set; }

    public static bool Armed => UnityEditor.SessionState.GetBool(Key, false);
    public static void Arm() => UnityEditor.SessionState.SetBool(Key, true);
    public static void Disarm() => UnityEditor.SessionState.EraseBool(Key);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        Redirected = 0;
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    // sceneLoaded runs after the scene's Awake / OnEnable and before any Start (the reporter's first request is in Start)
    static void OnLoaded(Scene s, LoadSceneMode mode)
    {
        if (!Armed) return;
        foreach (var r in Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include))
        {
            if (r.baseUrl == DeadUrl) continue;
            r.baseUrl = DeadUrl;
            Redirected++;
            Debug.Log($"[TelemetryGuard] {s.name}: SessionReporter -> {DeadUrl}");
        }
    }
}
#endif
