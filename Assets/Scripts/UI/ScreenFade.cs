// ScreenFade.cs
// Purpose: Fade to black and back for Story Mode (between cards, before a chapter loads, back to the menu). The camera never
//          moves: a black world-space panel sits 0.25 m in front of the eyes (child of the main camera, 1.4 x 1.4 m, so it
//          covers the whole view) and only its alpha changes, drawn on top of everything (depth test off). A story load sets
//          CarryBlack, so the next scene starts black and fades in by itself (no flash of the old frame or the new scene's
//          first frame). Unscaled time, so a paused or slowed game still fades. One per scene, built on first use; no per-frame
//          work while idle.
// Dependencies: MenuUi (DrawOnTop), Camera.main
// Attach to: nothing (created at runtime on the main camera)

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ScreenFade : MonoBehaviour
{
    public const float DefaultOut = 0.35f, DefaultIn = 0.45f;

    /// <summary>Set before a scene load that should start black and fade in (story chapters, back to the menu).</summary>
    public static bool CarryBlack;

    static ScreenFade instance;
    CanvasGroup group;
    float from, to, duration, t;
    Action done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; CarryBlack = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!CarryBlack) return;
        CarryBlack = false;
        var f = Get();
        if (f == null) return;
        f.SetAlpha(1f);
        f.Run(1f, 0f, DefaultIn * 1.6f, null);   // a little longer: the new scene settles first
    }

    /// <summary>Alpha 0 (clear) to 1 (black).</summary>
    public static float Alpha => instance != null && instance.group != null ? instance.group.alpha : 0f;
    /// <summary>A fade is running or the view is black: story buttons wait.</summary>
    public static bool Busy => instance != null && (instance.done != null || instance.duration > 0f || instance.group.alpha > 0.01f);

    /// <summary>Fades to black, then runs <paramref name="whenBlack"/> (the view stays black until In).</summary>
    public static void Out(Action whenBlack, float seconds = DefaultOut)
    {
        var f = Get();
        if (f == null) { whenBlack?.Invoke(); return; }
        f.Run(f.group.alpha, 1f, seconds, whenBlack);
    }

    /// <summary>Fades from black back to the scene.</summary>
    public static void In(float seconds = DefaultIn)
    {
        var f = Get();
        if (f == null) return;
        f.Run(f.group.alpha, 0f, seconds, null);
    }

    /// <summary>Black, swap what is shown, back: the fade-to-black transition between two cards.</summary>
    public static void Transition(Action swap, float outSeconds = DefaultOut, float inSeconds = DefaultIn)
    {
        Out(() => { swap?.Invoke(); In(inSeconds); }, outSeconds);
    }

    /// <summary>Clears at once (tests, or a scene that must not stay black).</summary>
    public static void Clear()
    {
        if (instance == null) return;
        instance.done = null; instance.duration = 0f;
        instance.SetAlpha(0f);
    }

    static ScreenFade Get()
    {
        if (instance != null) return instance;
        var cam = Camera.main;
        if (cam == null) return null;
        var root = new GameObject("ScreenFade", typeof(RectTransform));
        root.transform.SetParent(cam.transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, 0.25f);
        root.transform.localRotation = Quaternion.identity;
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 32000;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1000f, 1000f);
        rt.localScale = Vector3.one * 0.0014f;   // 1.4 m square at 0.25 m: +-70 deg, wider than the headset's view
        var img = MenuUi.Panel(rt, "Black", Vector2.zero, new Vector2(1000f, 1000f), Color.black);
        img.raycastTarget = false;
        var f = root.AddComponent<ScreenFade>();
        f.group = root.AddComponent<CanvasGroup>();
        f.group.blocksRaycasts = false;
        f.group.interactable = false;
        MenuUi.DrawOnTop(root);
        f.SetAlpha(0f);
        instance = f;
        return f;
    }

    void Run(float a, float b, float seconds, Action whenDone)
    {
        // A second request replaces the first; its callback still runs (nothing waiting on it is lost).
        var pending = done;
        done = null;
        pending?.Invoke();
        from = a; to = b; t = 0f;
        duration = Mathf.Max(0.0001f, seconds);
        done = whenDone;
        SetAlpha(a);
        enabled = true;
    }

    void SetAlpha(float a)
    {
        group.alpha = a;
        gameObject.SetActive(a > 0.001f || duration > 0f);
    }

    void Update()
    {
        if (duration <= 0f) return;
        t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(t / duration);
        group.alpha = Mathf.Lerp(from, to, k * k * (3f - 2f * k));
        if (k < 1f) return;
        duration = 0f;
        SetAlpha(to);
        var cb = done;
        done = null;
        cb?.Invoke();
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
