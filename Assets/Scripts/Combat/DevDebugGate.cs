// DevDebugGate.cs
// Purpose: The Development-build switch for the debug tools (the combat debug readout and the "Test breathing" laser button).
//          Hold BOTH grips for HoldSeconds (3 s) on the menu or in a level to toggle it. Release builds never poll it
//          (Debug.isDebugBuild), the state is a static bool (lives until the app closes, survives scene loads), default off.
// Dependencies: OVRInput
// Attach to: nothing (static; polled once per frame by BreathingTestPanel on the menu and by CombatUIController in levels)

using UnityEngine;

public static class DevDebugGate
{
    public const float HoldSeconds = 3f;
    public static bool On { get; private set; }

    static float held;
    static bool latched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { On = false; held = 0f; latched = false; }

    /// <summary>Call once per frame. Returns true on the frame the tools were toggled.</summary>
    public static bool Poll()
    {
        if (!Debug.isDebugBuild) return false;
        bool both = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) &&
                    OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        if (!both) { held = 0f; latched = false; return false; }
        held += Time.unscaledDeltaTime;
        if (held < HoldSeconds || latched) return false;
        latched = true;
        On = !On;
        Debug.Log("[StrikeFitDebug] debug tools " + (On ? "on" : "off") + " (both grips held " + HoldSeconds + " s)");
        return true;
    }

    /// <summary>Editor / test harness: set the state directly.</summary>
    public static void Set(bool on) { On = on; }
}
