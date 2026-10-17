// StoryStage.cs
// Purpose: The story-run panels inside a level: the chapter card before the fight (portrait, chapter, name, intro line, lore,
//          Begin), the interludes and the ending (slides in the same style as the menu story: dark panel, only the text fades),
//          and the "Story complete" panel. One world-space canvas, placed 2.5 m ahead of the head at the moment it opens (yaw
//          only) and then fixed in the world; the camera never moves or fades, nothing flashes. Skip on the laser for every
//          slide; the pointing hand's trigger (not on a button) shows the next slide. Built once, reused; per-frame work only
//          while open, no allocations.
// Dependencies: StoryContent, MenuUi, ControllerInput, ControllerMapping, OVRRaycaster / OVRInputModule
// Attach to: created at runtime by StoryDirector

using System;
using UnityEngine;
using UnityEngine.UI;

public class StoryStage : MonoBehaviour
{
    public float distance = 2.5f;
    public float slideSeconds = 7f;
    public float fadeSeconds = 1f;
    public float triggerGraceSeconds = 0.6f;

    public bool IsOpen => root != null && root.activeSelf;

    GameObject root;
    RectTransform panelRt;
    GameObject cardGroup, slidesGroup, completeGroup;
    // card
    Image portrait; Text cardChapter, cardName, cardWeapon, cardIntro, cardLore; Button beginButton;
    // slides
    Text slidesTitle, slideText, slideCounter; CanvasGroup slideFade; Button skipButton, doneButton; Text doneLabel;
    // complete
    Text completeTitle, completeBody; Button returnButton;
    Text loadingText;

    string[] texts;
    int index;
    float slideTime, openedAt;
    bool autoAdvance;
    Action onDone;

    static readonly Color Warm = new Color(0.9f, 0.72f, 0.35f);
    static readonly Color Body = new Color(0.94f, 0.95f, 1f);
    static readonly Color Muted = new Color(0.72f, 0.76f, 0.88f);

    // ───────────────────────── build ─────────────────────────

    void Build()
    {
        if (root != null) return;
        root = new GameObject("StoryStageCanvas", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 38;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1120f, 770f);
        rt.localScale = Vector3.one * 0.002f;   // 2.24 x 1.54 m: the menu canvas scale; text 40 = 5.7 cm cap height at 2.5 m
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        var rc = root.AddComponent<OVRRaycaster>();
        var module = FindAnyObjectByType<UnityEngine.EventSystems.OVRInputModule>();
        rc.pointer = module != null && module.rayTransform != null ? module.rayTransform.gameObject : null;
        rc.sortOrder = 90;

        var panel = MenuUi.Panel(rt, "Panel", Vector2.zero, new Vector2(1120f, 770f), new Color(0.008f, 0.01f, 0.018f, 0.97f));
        panelRt = (RectTransform)panel.transform;

        // Chapter card
        cardGroup = MenuUi.Rect(panelRt, "Card", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        var portraitRt = MenuUi.Centred(cardGroup.transform, "Portrait", new Vector2(-300f, 10f), new Vector2(400f, 500f));
        portrait = portraitRt.gameObject.AddComponent<Image>();
        portrait.preserveAspect = true; portrait.raycastTarget = false;
        cardChapter = MenuUi.Label(cardGroup.transform, "Chapter", "", 32, Warm, TextAnchor.MiddleLeft, new Vector2(200f, 300f), new Vector2(600f, 44f), FontStyle.Bold);
        cardName = MenuUi.Label(cardGroup.transform, "Name", "", 56, Color.white, TextAnchor.MiddleLeft, new Vector2(200f, 240f), new Vector2(600f, 72f), FontStyle.Bold);
        cardWeapon = MenuUi.Label(cardGroup.transform, "Weapon", "", 30, Muted, TextAnchor.MiddleLeft, new Vector2(200f, 186f), new Vector2(600f, 40f));
        cardIntro = MenuUi.Label(cardGroup.transform, "Intro", "", 34, new Color(1f, 0.92f, 0.7f), TextAnchor.UpperLeft, new Vector2(200f, 104f), new Vector2(600f, 100f), FontStyle.Italic);
        cardLore = MenuUi.Label(cardGroup.transform, "Lore", "", 32, Body, TextAnchor.UpperLeft, new Vector2(200f, -40f), new Vector2(600f, 170f));
        beginButton = MenuUi.MakeButton(cardGroup.transform, "BeginButton", "Begin", 46, new Color(0.1f, 0.42f, 0.3f, 0.97f), new Vector2(330f, -300f), new Vector2(340f, 96f));
        MenuUi.Label(cardGroup.transform, "Hint", "Trigger or Begin to start", 24, Muted, TextAnchor.MiddleLeft, new Vector2(-50f, -300f), new Vector2(380f, 34f));
        beginButton.onClick.AddListener(Finish);

        // Slides (interlude, ending)
        slidesGroup = MenuUi.Rect(panelRt, "Slides", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        slidesTitle = MenuUi.Label(slidesGroup.transform, "Title", "", 44, Warm, TextAnchor.MiddleCenter, new Vector2(0f, 318f), new Vector2(900f, 56f), FontStyle.Bold);
        slideCounter = MenuUi.Label(slidesGroup.transform, "Counter", "", 26, Muted, TextAnchor.MiddleRight, new Vector2(492f, 316f), new Vector2(220f, 34f));
        var fadeRt = MenuUi.Centred(slidesGroup.transform, "SlideGroup", new Vector2(0f, -10f), new Vector2(980f, 460f));
        slideFade = fadeRt.gameObject.AddComponent<CanvasGroup>();
        slideFade.blocksRaycasts = false;
        slideText = MenuUi.Label(fadeRt, "SlideText", "", 44, new Color(0.97f, 0.97f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(940f, 440f));
        slideText.lineSpacing = 1.25f;
        skipButton = MenuUi.MakeButton(slidesGroup.transform, "SkipButton", "Skip", 38, new Color(0.25f, 0.25f, 0.3f, 0.97f), new Vector2(398f, -322f), new Vector2(200f, 76f));
        doneButton = MenuUi.MakeButton(slidesGroup.transform, "DoneButton", "Continue", 42, new Color(0.1f, 0.42f, 0.3f, 0.97f), new Vector2(0f, -322f), new Vector2(380f, 86f));
        doneLabel = doneButton.GetComponentInChildren<Text>();
        skipButton.onClick.AddListener(Finish);
        doneButton.onClick.AddListener(Finish);

        // Story complete
        completeGroup = MenuUi.Rect(panelRt, "Complete", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        completeTitle = MenuUi.Label(completeGroup.transform, "Title", "Story complete", 62, Warm, TextAnchor.MiddleCenter, new Vector2(0f, 280f), new Vector2(1000f, 80f), FontStyle.Bold);
        completeBody = MenuUi.Label(completeGroup.transform, "Body", "", 36, Body, TextAnchor.UpperCenter, new Vector2(0f, 20f), new Vector2(940f, 400f));
        completeBody.lineSpacing = 1.2f;
        returnButton = MenuUi.MakeButton(completeGroup.transform, "ReturnButton", "Return to menu", 42, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(0f, -300f), new Vector2(460f, 92f));
        returnButton.onClick.AddListener(Finish);

        loadingText = MenuUi.Label(panelRt, "Loading", "Loading...", 40, new Color(1f, 0.9f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0f, -322f), new Vector2(600f, 60f), FontStyle.Bold);
        loadingText.gameObject.SetActive(false);
        MenuUi.DrawOnTop(root);   // never hidden behind the monster's body or the weapons in the player's hands
        root.SetActive(false);
    }

    void Place()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var head = cam.transform;
        Vector3 fwd = head.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        root.transform.position = head.position + fwd * distance + Vector3.down * 0.15f;
        root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    void Open(GameObject group, Action done)
    {
        Build();
        onDone = done;
        cardGroup.SetActive(group == cardGroup);
        slidesGroup.SetActive(group == slidesGroup);
        completeGroup.SetActive(group == completeGroup);
        loadingText.gameObject.SetActive(false);
        SetButtons(true);
        Place();
        root.SetActive(true);
        openedAt = Time.unscaledTime;
    }

    void SetButtons(bool on)
    {
        beginButton.interactable = on; skipButton.interactable = on; doneButton.interactable = on; returnButton.interactable = on;
    }

    // ───────────────────────── public API ─────────────────────────

    /// <summary>The chapter card for level 1 to 3. onBegin runs once (Begin, or the trigger off the buttons).</summary>
    public void ShowChapterCard(int level, Action onBegin)
    {
        Build();
        var g = StoryContent.GuardianFor(level);
        var l = StoryContent.ForLevel(level == 1 ? "Easy" : level == 2 ? "Medium" : "Hard");
        portrait.sprite = Resources.Load<Sprite>("Guardians/level" + level);
        portrait.enabled = portrait.sprite != null;
        cardChapter.text = "CHAPTER " + level;
        cardName.text = g != null ? g.name : (l != null ? l.guardian : "");
        cardWeapon.text = g != null ? "Weapon: " + g.weapon : "";
        cardIntro.text = l != null ? l.intro : "";
        cardLore.text = g != null ? g.lore : "";
        texts = null;
        Open(cardGroup, onBegin);
    }

    /// <summary>Slides (an interlude: one text; the ending: several). Each shows slideSeconds, then the next; the last stays until
    /// Continue. Skip or the done button ends at once.</summary>
    public void ShowSlides(string title, string[] slides, string doneText, Action done)
    {
        Build();
        texts = slides ?? new string[0];
        slidesTitle.text = title ?? "";
        doneLabel.text = string.IsNullOrEmpty(doneText) ? "Continue" : doneText;
        Open(slidesGroup, done);
        ShowSlide(0);
    }

    public void ShowComplete(string body, Action onReturn)
    {
        Build();
        completeBody.text = body ?? "";
        texts = null;
        Open(completeGroup, onReturn);
    }

    /// <summary>A scene load started from this panel: buttons off, "Loading..." shown (the panel stays until the scene changes).</summary>
    public void ShowLoading()
    {
        if (root == null) return;
        SetButtons(false);
        doneButton.gameObject.SetActive(false); skipButton.gameObject.SetActive(false);
        loadingText.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        onDone = null;
    }

    /// <summary>For tests (and the trigger): what the next press would do.</summary>
    public void Next()
    {
        if (!IsOpen) return;
        if (texts != null && slidesGroup.activeSelf && index + 1 < texts.Length) { ShowSlide(index + 1); return; }
        Finish();
    }

    public void Finish()
    {
        if (!IsOpen) return;
        var cb = onDone;
        onDone = null;
        if (cb == null) return;
        SetButtons(false);
        cb();
    }

    public int SlideIndex => index;
    public int SlideCount => texts != null ? texts.Length : 0;
    public bool CardShown => IsOpen && cardGroup.activeSelf;
    public bool SlidesShown => IsOpen && slidesGroup.activeSelf;
    public bool CompleteShown => IsOpen && completeGroup.activeSelf;

    void ShowSlide(int i)
    {
        index = i;
        slideTime = 0f;
        slideText.text = texts.Length > 0 ? texts[i] : "";
        slideCounter.text = texts.Length > 1 ? (i + 1) + " / " + texts.Length : "";
        slideFade.alpha = 0f;
        autoAdvance = i + 1 < texts.Length;
        // the done button only on the last slide; Skip on every slide
        doneButton.gameObject.SetActive(!autoAdvance);
        skipButton.gameObject.SetActive(true);
        Debug.Log($"{StoryProgress.Tag} slide {i + 1}/{texts.Length} '{slidesTitle.text}'");
    }

    void Update()
    {
        if (root == null || !root.activeSelf || onDone == null) return;
        float dt = Time.unscaledDeltaTime;
        if (slidesGroup.activeSelf && texts != null && texts.Length > 0)
        {
            slideTime += dt;
            float fade = Mathf.Min(fadeSeconds, slideSeconds * 0.5f);
            float a = Mathf.Clamp01(slideTime / fade);
            if (autoAdvance) a = Mathf.Min(a, Mathf.Clamp01((slideSeconds - slideTime) / fade));
            slideFade.alpha = a;
            if (autoAdvance && slideTime >= slideSeconds) { ShowSlide(index + 1); return; }
        }
        // The pointing hand's trigger, off the buttons, is "next" (the press that opened the panel is ignored).
        var cm = ControllerMapping.Instance;
        bool overButton = cm != null && cm.module != null && cm.module.Hovered != null;
        if (!overButton && Time.unscaledTime - openedAt > triggerGraceSeconds && ControllerInput.Down(Pad.Trigger, ControllerMapping.PointHand))
            Next();
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root);
    }
}
