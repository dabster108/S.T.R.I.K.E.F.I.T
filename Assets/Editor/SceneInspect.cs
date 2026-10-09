// SceneInspect.cs
// Purpose: Read-only scene / prefab inspection helpers for Unity MCP (hierarchy dumps with components). Editor only.
// Dependencies: none
// Attach to: nothing (static)

using System.Text;
using UnityEngine;

public static class SceneInspect
{
    /// <summary>Indented hierarchy of one transform with its components, to maxDepth.</summary>
    public static string Dump(Transform t, int maxDepth = 3, bool skipTransform = true)
    {
        var sb = new StringBuilder();
        Walk(sb, t, 0, maxDepth, skipTransform);
        return sb.ToString();
    }

    public static string DumpByName(string name, int maxDepth = 3)
    {
        var go = GameObject.Find(name);
        return go == null ? "not found: " + name : Dump(go.transform, maxDepth);
    }

    static void Walk(StringBuilder sb, Transform t, int d, int max, bool skipTransform)
    {
        if (d > max) return;
        sb.Append(new string(' ', d * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [off]").Append(" :");
        foreach (var c in t.GetComponents<Component>())
            if (c != null && !(skipTransform && c is Transform)) sb.Append(' ').Append(c.GetType().Name);
        sb.AppendLine();
        foreach (Transform ch in t) Walk(sb, ch, d + 1, max, skipTransform);
    }
}
