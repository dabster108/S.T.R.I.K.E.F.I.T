// DisableGameViewGizmosOnPlay.cs
// Editor-only utility: keeps Game view gizmos disabled on play enter.
// This prevents Meta XR debug gizmo overlays (purple bands/cones) from appearing in Game view.

#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DisableGameViewGizmosOnPlay
{
    static DisableGameViewGizmosOnPlay()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode &&
            state != PlayModeStateChange.EnteredEditMode)
            return;

        try
        {
            var editorAsm = typeof(EditorWindow).Assembly;
            var gameViewType = editorAsm.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;

            var views = Resources.FindObjectsOfTypeAll(gameViewType);
            for (int i = 0; i < views.Length; i++)
            {
                var view = views[i];
                var field = gameViewType.GetField("m_Gizmos", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(bool))
                    field.SetValue(view, false);

                var prop = gameViewType.GetProperty("showGizmos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
                    prop.SetValue(view, false, null);

                var repaint = gameViewType.GetMethod("Repaint", BindingFlags.Instance | BindingFlags.Public);
                if (repaint != null) repaint.Invoke(view, null);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DisableGameViewGizmosOnPlay] Failed to force GameView gizmos off: " + e.Message);
        }
    }
}
#endif
