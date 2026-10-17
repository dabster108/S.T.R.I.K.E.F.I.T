// MenuUi.cs
// Purpose: Small helpers that build the menu's uGUI pieces (labels, panels, laser-clickable buttons) with the same built-in
//          font and Image / Button / Text components the HUD uses, so the menu scene, the Victory / Defeat buttons and the exit
//          confirm all look and behave like the rest of the game. No per-frame work.
// Dependencies: UnityEngine.UI
// Attach to: nothing (static)

using UnityEngine;
using UnityEngine.UI;

public static class MenuUi
{
    public static Font BuiltinFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    static Material onTop;

    /// <summary>A panel that opens in the middle of an arena (pause, story cards and slides) must never be hidden behind the
    /// monster or the player's weapons: every Graphic under <paramref name="root"/> gets one shared copy of the default UI material
    /// with the depth test off (UI/Default is in the always-included shaders). Call after the panel is built.</summary>
    public static void DrawOnTop(GameObject root)
    {
        if (root == null) return;
        if (onTop == null)
        {
            var shader = Shader.Find("UI/Default");
            if (shader == null) return;
            onTop = new Material(shader) { name = "UI_OnTop" };
            onTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        foreach (var g in root.GetComponentsInChildren<Graphic>(true)) g.material = onTop;
    }

    public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
        return rt;
    }

    /// <summary>Centred rect (anchor and pivot in the middle of the parent).</summary>
    public static RectTransform Centred(Transform parent, string name, Vector2 pos, Vector2 size)
        => Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

    public static Image Panel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var rt = Centred(parent, name, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = true;   // a panel in front blocks the buttons behind it
        return img;
    }

    public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box, FontStyle style = FontStyle.Normal)
    {
        var rt = Centred(parent, name, pos, box);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = BuiltinFont;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.supportRichText = false;
        t.raycastTarget = false;
        return t;
    }

    /// <summary>A button the laser can click: Image + Button + a centred label. Colour tint on hover / press / disabled.</summary>
    public static Button MakeButton(Transform parent, string name, string label, int fontSize, Color color, Vector2 pos, Vector2 size)
    {
        var rt = Centred(parent, name, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = Color.white;   // tint comes from the ColorBlock
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.normalColor = color;
        cb.highlightedColor = Color.Lerp(color, Color.white, 0.35f);
        cb.pressedColor = Color.Lerp(color, Color.black, 0.3f);
        cb.selectedColor = color;
        cb.disabledColor = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, color.a * 0.7f);
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        var t = Label(rt, "Text", label, fontSize, Color.white, TextAnchor.MiddleCenter, Vector2.zero, size, FontStyle.Bold);
        var trt = (RectTransform)t.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        return b;
    }
}
