// GamePause.cs
// Purpose: The ONE global pause flag. Only PauseMenu changes it (Set is internal); every gameplay system reads IsPaused. It
//          also keeps the pause clock: PausedSeconds is the total real time spent paused, and ActiveRealtime is
//          Time.realtimeSinceStartup minus that, so a session's duration, active minutes and kcal never include a pause.
//          Epoch changes on every resume so systems that keep a "previous position" or a rolling window can discard it
//          (a swing measured across a pause must never register).
// Dependencies: none
// Attach to: nothing (static)

using System;
using UnityEngine;

public static class GamePause
{
    public static bool IsPaused { get; private set; }
    /// <summary>Increases by one on every resume. Compare with a stored copy to know "a pause happened since I last looked".</summary>
    public static int Epoch { get; private set; }
    /// <summary>Real seconds spent paused since launch, including the pause that is open right now.</summary>
    public static float PausedSeconds => paused + (IsPaused ? Time.realtimeSinceStartup - pauseStartedAt : 0f);
    /// <summary>Time.realtimeSinceStartup without the paused time. Use it for durations; differences of it skip every pause.</summary>
    public static float ActiveRealtime => Time.realtimeSinceStartup - PausedSeconds;
    /// <summary>Number of pauses since launch.</summary>
    public static int PauseCount { get; private set; }

    /// <summary>Raised after the flag turned on / off (state is already set).</summary>
    public static event Action Paused, Resumed;

    static float paused;
    static float pauseStartedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        IsPaused = false; Epoch = 0; paused = 0f; pauseStartedAt = 0f; PauseCount = 0; Paused = null; Resumed = null;
    }

    /// <summary>PauseMenu only. Returns false when nothing changed.</summary>
    internal static bool Set(bool on)
    {
        if (on == IsPaused) return false;
        if (on)
        {
            IsPaused = true;
            pauseStartedAt = Time.realtimeSinceStartup;
            PauseCount++;
            Paused?.Invoke();
        }
        else
        {
            paused += Time.realtimeSinceStartup - pauseStartedAt;
            IsPaused = false;
            Epoch++;
            Resumed?.Invoke();
        }
        return true;
    }

    /// <summary>Scene change / teardown: the flag never survives a scene (statics outlive scenes). Does not raise Resumed.</summary>
    internal static void ForceClear()
    {
        if (IsPaused) { paused += Time.realtimeSinceStartup - pauseStartedAt; IsPaused = false; Epoch++; }
    }
}
