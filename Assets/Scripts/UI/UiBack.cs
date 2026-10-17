// UiBack.cs
// Purpose: "B = back one panel" for the menu, the pre-battle panels and the end panels. A panel registers a handler while it can
//          go back (the last one registered is asked first); ControllerMapping calls Back() on each B press (button-down,
//          debounced) whenever a panel is open and the pause menu is not. A handler returns true when it handled the press, so
//          the top menu (no handler, or one that returns false) does nothing.
// Dependencies: none
// Attach to: nothing (static)

using System;
using System.Collections.Generic;

public static class UiBack
{
    static readonly List<Func<bool>> handlers = new List<Func<bool>>();

    public static int Count => handlers.Count;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { handlers.Clear(); }

    /// <summary>Register (or move to the front) a handler. It must return false when it has nothing to go back from.</summary>
    public static void Push(Func<bool> handler)
    {
        if (handler == null) return;
        handlers.Remove(handler);
        handlers.Add(handler);
    }

    public static void Remove(Func<bool> handler) { if (handler != null) handlers.Remove(handler); }

    /// <summary>Ask the handlers, newest first; true when one of them took the press.</summary>
    public static bool Back()
    {
        for (int i = handlers.Count - 1; i >= 0; i--)
        {
            var h = handlers[i];
            if (h != null && h()) return true;
        }
        return false;
    }
}
