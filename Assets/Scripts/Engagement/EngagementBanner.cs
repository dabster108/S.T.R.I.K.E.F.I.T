// EngagementBanner.cs
// Purpose: A short banner on a world-space canvas (GOAL COMPLETE, PERFECT CLASH, NEW BEST, RANK UP): pops in, holds, fades. Messages
//          queue (at most four waiting; the oldest waiting one is dropped when full). Built once with MenuUi in the game's own style;
//          the animation only moves scale and a CanvasGroup's alpha, so it allocates nothing per frame. Runs on unscaled time (a
//          slow-motion finale or the pause cannot freeze it). Visual only.
// Attach to: nothing (EngagementBanner.For(canvas) adds it under the canvas)

using UnityEngine;
using UnityEngine.UI;

public class EngagementBanner : MonoBehaviour
{
    struct Message { public string text; public Color color; public float seconds; }

    const int QueueSize = 4;
    readonly Message[] queue = new Message[QueueSize];
    int head, count;

    RectTransform box;
    CanvasGroup group;
    Image backing, stripe;
    Text label;
    float t, hold;
    bool showing;

    public int Shown { get; private set; }
    public bool Visible => showing && group != null && group.alpha > 0.9f;
    public string LastText { get; private set; } = "";

    /// <summary>The banner of a canvas (built on first use), centred at <paramref name="pos"/> in canvas units.</summary>
    public static EngagementBanner For(Canvas canvas, Vector2 pos, Vector2 size)
    {
        if (canvas == null) return null;
        var existing = canvas.GetComponentInChildren<EngagementBanner>(true);
        if (existing != null) return existing;
        var rt = MenuUi.Centred(canvas.transform, "EngagementBanner", pos, size);
        var b = rt.gameObject.AddComponent<EngagementBanner>();
        b.Build(rt, size);
        return b;
    }

    /// <summary>A banner under any UI parent (the result panel's own), centred at <paramref name="pos"/>.</summary>
    public static EngagementBanner Create(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var rt = MenuUi.Centred(parent, name, pos, size);
        var b = rt.gameObject.AddComponent<EngagementBanner>();
        b.Build(rt, size);
        return b;
    }

    void Build(RectTransform rt, Vector2 size)
    {
        box = rt;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;
        backing = MenuUi.Panel(rt, "Backing", Vector2.zero, size, new Color(0.03f, 0.04f, 0.08f, 0.9f));
        backing.raycastTarget = false;
        stripe = MenuUi.Panel(rt, "Stripe", new Vector2(0f, -size.y * 0.5f + 4f), new Vector2(size.x, 8f), Color.white);
        stripe.raycastTarget = false;
        label = MenuUi.Label(rt, "Text", "", Mathf.RoundToInt(size.y * 0.42f), Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 2f), new Vector2(size.x - 24f, size.y - 8f), FontStyle.Bold);
        label.raycastTarget = false;
        gameObject.SetActive(true);
    }

    public void Show(string text, Color color, float seconds = 2.4f)
    {
        if (count == QueueSize) { head = (head + 1) % QueueSize; count--; }
        queue[(head + count) % QueueSize] = new Message { text = text, color = color, seconds = Mathf.Max(0.8f, seconds) };
        count++;
        if (!showing) Next();
    }

    /// <summary>Drops what is showing and waiting.</summary>
    public void Clear()
    {
        count = 0; showing = false;
        if (group != null) group.alpha = 0f;
    }

    void Next()
    {
        if (count == 0) { showing = false; group.alpha = 0f; return; }
        var m = queue[head];
        head = (head + 1) % QueueSize; count--;
        label.text = m.text;
        label.color = Color.white;
        stripe.color = m.color;
        backing.color = new Color(m.color.r * 0.16f, m.color.g * 0.16f, m.color.b * 0.16f, 0.92f);
        hold = m.seconds;
        t = 0f;
        showing = true;
        transform.SetAsLastSibling();   // over a result panel built after the banner
        Shown++;
        LastText = m.text;
    }

    void Update()
    {
        if (!showing) return;
        t += Time.unscaledDeltaTime;
        // pop: 0 -> 1.08 -> 1 in 0.25 s, fade out over the last 0.35 s
        float pop = t < 0.15f ? Mathf.Lerp(0.6f, 1.08f, t / 0.15f) : t < 0.25f ? Mathf.Lerp(1.08f, 1f, (t - 0.15f) / 0.1f) : 1f;
        box.localScale = new Vector3(pop, pop, 1f);
        float fadeFrom = hold - 0.35f;
        group.alpha = t < 0.12f ? t / 0.12f : t > fadeFrom ? Mathf.Clamp01((hold - t) / 0.35f) : 1f;
        if (t >= hold) Next();
    }
}
