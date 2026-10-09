// CombatDebugInfo.cs
// Purpose: The numbers the on-headset debug readout shows (CombatUIController.debugReadout), so the headset can tell "the
//          logic fired" apart from "the hardware did nothing": clash count, last clash hand and time, the last haptic call
//          (hand, amplitude, duration, backend, accepted or not) and the last clash sound. Written at the moment each thing
//          happens; the HUD reads it only when Version has changed, so nothing is formatted per frame.
// Dependencies: none
// Attach to: nothing (static)

using UnityEngine;

public static class CombatDebugInfo
{
    /// <summary>Bumped on every change: the HUD redraws only when this differs from what it last drew.</summary>
    public static int Version { get; private set; }

    public static int Clashes { get; private set; }
    public static Hand LastClashHand { get; private set; }
    public static float LastClashTime { get; private set; } = -1f;

    public static AudioClip LastClashClip { get; private set; }
    public static float LastClashPitch { get; private set; }

    public static int HapticCalls { get; private set; }
    public static Hand LastHapticHand { get; private set; }
    public static float LastHapticAmplitude { get; private set; }
    public static float LastHapticSeconds { get; private set; }
    public static string LastHapticKind { get; private set; } = "-";
    public static string LastHapticBackend { get; private set; } = "-";
    public static bool LastHapticAccepted { get; private set; }

    public static void NoteClash(Hand hand)
    {
        Clashes++; LastClashHand = hand; LastClashTime = Time.time; Version++;
    }

    public static void NoteClashSound(AudioClip clip, float pitch)
    {
        LastClashClip = clip; LastClashPitch = pitch; Version++;
    }

    public static void NoteHaptic(Hand hand, float amplitude, float seconds, string kind, string backend, bool accepted)
    {
        HapticCalls++; LastHapticHand = hand; LastHapticAmplitude = amplitude; LastHapticSeconds = seconds;
        LastHapticKind = kind; LastHapticBackend = backend; LastHapticAccepted = accepted; Version++;
    }

    /// <summary>New battle: counts start from zero.</summary>
    public static void ResetCounts()
    {
        Clashes = 0; HapticCalls = 0; LastClashTime = -1f; Version++;
    }
}
