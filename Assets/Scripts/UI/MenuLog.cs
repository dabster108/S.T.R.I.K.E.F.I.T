// MenuLog.cs
// Purpose: One log line per main-menu event, all with the fixed tag [StrikeFitMenu], so a headset session can be followed with
//          `adb logcat -s Unity:I | grep StrikeFitMenu`. Events only (clicks, page changes, results), never per frame.
// Dependencies: none
// Attach to: nothing (static)

using UnityEngine;

public static class MenuLog
{
    public const string Tag = "[StrikeFitMenu]";

    /// <summary>Master switch (tests can silence it). On by default in every build.</summary>
    public static bool Enabled = true;

    public static void Event(string message)
    {
        if (Enabled) Debug.Log(Tag + " " + message);
    }
}
