// DevLog.cs
// Purpose: Debug.Log for chatty diagnostics (per attack, per hit, per breathing-tier change) that must cost nothing in a release
//          build. [Conditional]: in a build without UNITY_EDITOR and DEVELOPMENT_BUILD every call is removed by the compiler,
//          INCLUDING the evaluation of its argument, so an interpolated string like $"tier {tier} exertion {x:0.00}" is never built
//          and no stack trace is captured. In the Editor and in Development builds it is exactly Debug.Log.
//          Use it only for informational lines; warnings and errors stay on Debug.LogWarning / Debug.LogError so a release
//          build still reports real problems.
// Dependencies: none
// Attach to: nothing (static)

using System.Diagnostics;

public static class DevLog
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string message) => UnityEngine.Debug.Log(message);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string message, UnityEngine.Object context) => UnityEngine.Debug.Log(message, context);
}
