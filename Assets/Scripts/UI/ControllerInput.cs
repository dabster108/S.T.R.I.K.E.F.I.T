// ControllerInput.cs
// Purpose: The one way the UI, the pause menu and the end panels read controller buttons. Every press is a button-DOWN edge,
//          debounced per button and per hand (ControlSettings.debounceSeconds, real time, so it works at timeScale 0), and the
//          answer is cached per frame so two systems asking in the same frame see the same edge. Allocation-free. Tests inject
//          a press with Inject(): it goes through exactly this code, so a simulated B press takes the path a real one takes.
// Dependencies: OVRInput, ControlSettings
// Attach to: nothing (static)

using UnityEngine;

public enum Pad { Trigger, One, Two, Start, Grip }   // One = A (right) / X (left); Two = B (right) / Y (left)

public static class ControllerInput
{
    const int Hands = 2;

    static readonly float[] lastActed = new float[5 * Hands];
    static readonly int[] edgeFrame = new int[5 * Hands];
    static readonly bool[] edgeValue = new bool[5 * Hands];
    static readonly int[] upFrame = new int[5 * Hands];
    static readonly bool[] upValue = new bool[5 * Hands];
    static readonly bool[] injectDown = new bool[5 * Hands];
    static readonly bool[] injectUp = new bool[5 * Hands];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < lastActed.Length; i++)
        {
            lastActed[i] = -10f; edgeFrame[i] = -1; edgeValue[i] = false; upFrame[i] = -1; upValue[i] = false;
            injectDown[i] = false; injectUp[i] = false;
        }
    }

    static int Index(Pad p, OVRInput.Controller hand) => (int)p * Hands + (hand == OVRInput.Controller.LTouch ? 0 : 1);

    static OVRInput.Button Map(Pad p)
    {
        switch (p)
        {
            case Pad.Trigger: return OVRInput.Button.PrimaryIndexTrigger;
            case Pad.One: return OVRInput.Button.One;
            case Pad.Two: return OVRInput.Button.Two;
            case Pad.Start: return OVRInput.Button.Start;
            default: return OVRInput.Button.PrimaryHandTrigger;
        }
    }

    /// <summary>True once per press of this button on this hand (button-down, debounced). Same answer for every caller in a frame.</summary>
    public static bool Down(Pad p, OVRInput.Controller hand)
    {
        int i = Index(p, hand);
        int frame = Time.frameCount;
        if (edgeFrame[i] == frame) return edgeValue[i];
        edgeFrame[i] = frame;
        bool raw = injectDown[i] || OVRInput.GetDown(Map(p), hand);
        injectDown[i] = false;
        bool ok = false;
        if (raw)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastActed[i] >= ControlSettings.Current.debounceSeconds) { lastActed[i] = now; ok = true; }
        }
        edgeValue[i] = ok;
        return ok;
    }

    /// <summary>True on the frame this button is released (no debounce).</summary>
    public static bool Up(Pad p, OVRInput.Controller hand)
    {
        int i = Index(p, hand);
        int frame = Time.frameCount;
        if (upFrame[i] == frame) return upValue[i];
        upFrame[i] = frame;
        upValue[i] = injectUp[i] || OVRInput.GetUp(Map(p), hand);
        injectUp[i] = false;
        return upValue[i];
    }

    /// <summary>Test hook: the next Down() for this button and hand (from the next frame it is first asked in) is a press.</summary>
    public static void InjectDown(Pad p, OVRInput.Controller hand) { injectDown[Index(p, hand)] = true; }

    /// <summary>Test hook: the next Up() for this button and hand is a release.</summary>
    public static void InjectUp(Pad p, OVRInput.Controller hand) { injectUp[Index(p, hand)] = true; }
}
