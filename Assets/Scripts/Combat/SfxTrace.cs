// SfxTrace.cs
// Purpose: Editor-only audit hook. Combat sound sites call SfxTrace.Note(source, clip, cause) next to their PlayOneShot so a
//          Play-mode probe can log every sound with the reason it played. [Conditional("UNITY_EDITOR")]: the calls are
//          compiled out of player builds, and in the Editor they cost one null check when nothing listens.
// Dependencies: none
// Attach to: nothing (static)

using System;
using System.Diagnostics;
using UnityEngine;

public static class SfxTrace
{
    /// <summary>Args: the component that played it, the clip, a constant cause string.</summary>
    public static event Action<UnityEngine.Object, AudioClip, string> Played;

    [Conditional("UNITY_EDITOR")]
    public static void Note(UnityEngine.Object source, AudioClip clip, string cause)
    {
        var h = Played;
        if (h != null && clip != null) h(source, clip, cause);
    }
}
