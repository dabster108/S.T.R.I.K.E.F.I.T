// StoryStage.cs
// Purpose: The story-run cards inside a level, on one world-space canvas placed 2.2 m ahead of the head when a card opens (yaw
//          only) and then fixed in the world; the camera never moves. Cards:
//          - Chapter card before the fight: portrait, "Chapter N", guardian name and title, the level's one intro line, Begin.
//          - Lines card: a title and its lines faded in one at a time (the line between fights, the Victory ending after
//            Chapter 3, the Defeat card with Retry / Story menu). Its buttons appear once every line is shown.
//          - Story complete: the run's stats, Return to menu.
//          Every card has Skip (laser) and B skips too. The pointing hand's trigger off the buttons shows the next line, then
//          presses the card's main button. Changing card (or leaving one for the fight) goes through a fade to black
//          (ScreenFade). Optional narrator / stinger clips come from StoryAudio. All text comes from Resources/story.json; only
//          button labels live here. Built once, reused; per-frame work only while open, no allocations.
// Dependencies: StoryContent, StoryAudio, ScreenFade, MenuUi, UiBack, ControllerInput, ControllerMapping, OVRRaycaster
// Attach to: created at runtime by StoryDirector

using System;
using UnityEngine;
using UnityEngine.UI;

public class StoryStage : MonoBehaviour
{
    public float distance = 2.2f;
    public float lineSeconds = 2.5f;
    public float lineFadeSeconds = 0.8f;
    public float triggerGraceSeconds = 0.6f;
    public const int MaxLines = 5;

    public bool IsOpen => root != null && root.activeSelf;

    GameObject root;
    GameObject cardGroup, linesGroup, completeGroup;
    // chapter card
    Image portrait; Text cardChapter, cardName, cardTitle, cardIntro; Button beginButton, cardSkip;
    // lines card
    Text linesTitle; readonly Text[] lineTexts = new Text[MaxLines]; Button primaryButton, secondaryButton, linesSkip; Text primaryLabel, secondaryLabel;
    // complete
    Text completeTitle, completeBody; Button returnButton, completeSkip;
    Text loadingText;
    AudioSource voice;

    int lineCount, shown;
    float lineTime, openedAt;
    bool buttonsShown;
    Action onPrimary, onSecondary, onSkip;

    static readonly Color Warm = new Color(0.9f, 0.72f, 0.35f);
    static readonly Color Body = new Color(0.94f, 0.95f, 1f);
    static readonly Color Muted = new Color(0.72f, 0.76f, 0.88f);
    static readonly Color Go = new Color(0.1f, 0.42f, 0.3f, 0.97f);
    static readonly Color Second = new Color(0.2f, 0.35f, 0.6f, 0.97f);
    static readonly Color SkipColor = new Color(0.25f, 0.25f, 0.3f, 0.97f);

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
        rt.localScale = Vector3.one * 0.00176f;   // 1.97 x 1.36 m at 2.2 m: the angle the 2.5 m card had (text keeps its size)
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        var rc = root.AddComponent<OVRRaycaster>();
        var module = FindAnyObjectByType<UnityEngine.EventSystems.OVRInputModule>();
        rc.pointer = module != null && module.rayTransform != null ? module.rayTransform.gameObject : null;
        rc.sortOrder = 90;

        var panel = MenuUi.Panel(rt, "Panel", Vector2.zero, new Vector2(1120f, 770f), new Color(0.008f, 0.01f, 0.018f, 1f));
        var frame = panel.gameObject.AddComponent<Outline>();
        frame.effectColor = new Color(0.85f, 0.55f, 0.25f, 0.7f);
        frame.effectDistance = new Vector2(3f, -3f);
        var panelRt = (RectTransform)panel.transform;

        // Chapter card
        cardGroup = MenuUi.Rect(panelRt, "Card", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        var portraitRt = MenuUi.Centred(cardGroup.transform, "Portrait", new Vector2(-300f, 10f), new Vector2(400f, 500f));
        portrait = portraitRt.gameObject.AddComponent<Image>();
        portrait.preserveAspect = true; portrait.raycastTarget = false;
        cardChapter = MenuUi.Label(cardGroup.transform, "Chapter", "", 34, Warm, TextAnchor.MiddleLeft, new Vector2(200f, 270f), new Vector2(600f, 46f), FontStyle.Bold);
        cardName = MenuUi.Label(cardGroup.transform, "Name", "", 58, Color.white, TextAnchor.MiddleLeft, new Vector2(200f, 200f), new Vector2(600f, 76f), FontStyle.Bold);
        cardTitle = MenuUi.Label(cardGroup.transform, "Title", "", 32, Muted, TextAnchor.MiddleLeft, new Vector2(200f, 140f), new Vector2(600f, 44f), FontStyle.Italic);
        cardIntro = MenuUi.Label(cardGroup.transform, "Intro", "", 36, new Color(1f, 0.92f, 0.7f), TextAnchor.UpperLeft, new Vector2(200f, 10f), new Vector2(600f, 180f));
        cardIntro.lineSpacing = 1.15f;
        beginButton = MenuUi.MakeButton(cardGroup.transform, "BeginButton", "Begin", 46, Go, new Vector2(330f, -300f), new Vector2(340f, 96f));
        cardSkip = MenuUi.MakeButton(cardGroup.transform, "SkipButton", "Skip", 36, SkipColor, new Vector2(40f, -300f), new Vector2(180f, 76f));
        MenuUi.Label(cardGroup.transform, "Hint", "Trigger, Begin or B", 22, Muted, TextAnchor.MiddleCenter, new Vector2(40f, -355f), new Vector2(300f, 30f));
        beginButton.onClick.AddListener(() => Press(onPrimary, "begin"));
        cardSkip.onClick.AddListener(() => Press(onSkip, "skip"));

        // Lines card (between fights, victory, defeat)
        linesGroup = MenuUi.Rect(panelRt, "Lines", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        linesTitle = MenuUi.Label(linesGroup.transform, "Title", "", 56, Warm, TextAnchor.MiddleCenter, new Vector2(0f, 300f), new Vector2(1000f, 76f), FontStyle.Bold);
        for (int i = 0; i < MaxLines; i++)
        {
            var t = MenuUi.Label(linesGroup.transform, "Line" + i, "", 38, Body, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1000f, 96f));
            t.lineSpacing = 1.1f;
            t.raycastTarget = false;
            lineTexts[i] = t;
        }
        primaryButton = MenuUi.MakeButton(linesGroup.transform, "PrimaryButton", "Continue", 42, Go, new Vector2(-230f, -310f), new Vector2(320f, 88f));
        secondaryButton = MenuUi.MakeButton(linesGroup.transform, "SecondaryButton", "", 38, Second, new Vector2(110f, -310f), new Vector2(320f, 88f));
        linesSkip = MenuUi.MakeButton(linesGroup.transform, "SkipButton", "Skip", 34, SkipColor, new Vector2(430f, -310f), new Vector2(170f, 76f));
        primaryLabel = primaryButton.GetComponentInChildren<Text>();
        secondaryLabel = secondaryButton.GetComponentInChildren<Text>();
        primaryButton.onClick.AddListener(() => Press(onPrimary, "primary"));
        secondaryButton.onClick.AddListener(() => Press(onSecondary, "secondary"));
        linesSkip.onClick.AddListener(() => Press(onSkip, "skip"));

        // Story complete
        completeGroup = MenuUi.Rect(panelRt, "Complete", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
        completeTitle = MenuUi.Label(completeGroup.transform, "Title", "", 62, Warm, TextAnchor.MiddleCenter, new Vector2(0f, 280f), new Vector2(1000f, 80f), FontStyle.Bold);
        completeBody = MenuUi.Label(completeGroup.transform, "Body", "", 36, Body, TextAnchor.UpperCenter, new Vector2(0f, 20f), new Vector2(940f, 400f));
        completeBody.lineSpacing = 1.2f;
        returnButton = MenuUi.MakeButton(completeGroup.transform, "ReturnButton", "Return to menu", 42, Second, new Vector2(-80f, -300f), new Vector2(460f, 92f));
        completeSkip = MenuUi.MakeButton(completeGroup.transform, "SkipButton", "Skip", 34, SkipColor, new Vector2(300f, -300f), new Vector2(170f, 76f));
        returnButton.onClick.AddListener(() => Press(onPrimary, "return"));
        completeSkip.onClick.AddListener(() => Press(onSkip, "skip"));

        loadingText = MenuUi.Label(panelRt, "Loading", "Loading...", 40, new Color(1f, 0.9f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0f, -310f), new Vector2(600f, 60f), FontStyle.Bold);
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
        root.transform.position = head.position + fwd * distance + Vector3.down * 0.12f;
        root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    /// <summary>Shows a card. fade: through black (a card replacing gameplay or another card); false when the view is already
    /// black (the scene started black) or the caller fades.</summary>
    void Open(GameObject group, Action primary, Action secondary, Action skip, bool fade)
    {
        Build();
        onPrimary = primary; onSecondary = secondary; onSkip = skip ?? primary;
        Action show = () =>
        {
            cardGroup.SetActive(group == cardGroup);
            linesGroup.SetActive(group == linesGroup);
            completeGroup.SetActive(group == completeGroup);
            loadingText.gameObject.SetActive(false);
            SetButtons(true);
            Place();
            root.SetActive(true);
            openedAt = Time.unscaledTime;
            UiBack.Push(BackHandler);
        };
        if (fade) ScreenFade.Transition(show); else show();
    }

    void SetButtons(bool on)
    {
        beginButton.interactable = on; cardSkip.interactable = on;
        primaryButton.interactable = on; secondaryButton.interactable = on; linesSkip.interactable = on;
        returnButton.interactable = on; completeSkip.interactable = on;
    }

    bool BackHandler()
    {
        if (!IsOpen) return false;
        Press(onSkip, "skip (B)");
        return true;
    }

    // ───────────────────────── public API ─────────────────────────

    /// <summary>The chapter card for level 1 to 3. Begin, Skip, B or the trigger run onBegin once.</summary>
    public void ShowChapterCard(int level, Action onBegin, bool fade)
    {
        Build();
        var story = StoryContent.Load();
        var g = StoryContent.GuardianFor(level);
        var l = StoryContent.ForLevel(level == 1 ? "Easy" : level == 2 ? "Medium" : "Hard");
        portrait.sprite = Resources.Load<Sprite>("Guardians/level" + level);
        portrait.enabled = portrait.sprite != null;
        cardChapter.text = ((story.cards.chapter ?? "") + " " + level).Trim().ToUpperInvariant();
        cardName.text = g != null ? g.name : (l != null ? l.guardian : "");
        cardTitle.text = g != null && !string.IsNullOrEmpty(g.title) ? g.title : "";
        cardIntro.text = l != null ? l.intro : "";
        lineCount = 0;
        Open(cardGroup, onBegin, null, onBegin, fade);
        var a = StoryAudio.Get();
        StoryAudio.Play(StoryAudio.Chapter(level) ?? (a != null ? a.cardOpen : null), gameObject, ref voice);
        Debug.Log($"{StoryProgress.Tag} card: chapter {level} '{cardName.text}' ({cardTitle.text})");
    }

    /// <summary>A title and up to five lines faded in one at a time; then the buttons. secondary null = one main button. Skip
    /// (and B) run onSkip, or onPrimary when onSkip is null.</summary>
    public void ShowLines(string title, string[] lines, string primaryText, Action primary, string secondaryText, Action secondary,
                          Action skip, AudioClip stinger, bool fade = true)
    {
        Build();
        lines = lines ?? new string[0];
        lineCount = Mathf.Min(lines.Length, MaxLines);
        linesTitle.text = title ?? "";
        // Lines centred as a block between the title and the buttons.
        float step = lineCount > 3 ? 92f : 110f;
        float top = 20f + (lineCount - 1) * step * 0.5f;
        for (int i = 0; i < MaxLines; i++)
        {
            var t = lineTexts[i];
            bool used = i < lineCount;
            t.gameObject.SetActive(used);
            if (!used) continue;
            t.text = lines[i];
            t.rectTransform.anchoredPosition = new Vector2(0f, top - i * step);
            t.canvasRenderer.SetAlpha(0f);
        }
        primaryLabel.text = string.IsNullOrEmpty(primaryText) ? "Continue" : primaryText;
        bool two = secondary != null && !string.IsNullOrEmpty(secondaryText);
        secondaryLabel.text = two ? secondaryText : "";
        primaryButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(two ? -230f : 0f, -310f);
        secondaryButton.gameObject.SetActive(false);
        primaryButton.gameObject.SetActive(false);
        shown = 0; lineTime = 0f; buttonsShown = false;
        twoButtons = two;
        Open(linesGroup, primary, two ? secondary : null, skip, fade);
        StoryAudio.Play(stinger, gameObject, ref voice);
        Debug.Log($"{StoryProgress.Tag} card: '{linesTitle.text}' ({lineCount} lines)");
    }

    bool twoButtons;

    public void ShowComplete(string title, string body, Action onReturn, bool fade = true)
    {
        Build();
        completeTitle.text = title ?? "";
        completeBody.text = body ?? "";
        lineCount = 0;
        Open(completeGroup, onReturn, null, onReturn, fade);
        Debug.Log($"{StoryProgress.Tag} card: '{completeTitle.text}'");
    }

    /// <summary>A scene load started from this card: buttons off, "Loading..." shown (the card stays until the scene changes).</summary>
    public void ShowLoading()
    {
        if (root == null) return;
        SetButtons(false);
        primaryButton.gameObject.SetActive(false); secondaryButton.gameObject.SetActive(false);
        linesSkip.gameObject.SetActive(false);
        loadingText.gameObject.SetActive(true);
    }

    /// <summary>Closes the card. fade: through black (back to the fight).</summary>
    public void Hide(bool fade = false)
    {
        onPrimary = onSecondary = onSkip = null;
        UiBack.Remove(BackHandler);
        StoryAudio.Stop(voice);
        if (root == null) return;
        if (fade) ScreenFade.Transition(() => { if (root != null) root.SetActive(false); });
        else root.SetActive(false);
    }

    /// <summary>What the trigger does: the next line, then the card's main button.</summary>
    public void Next()
    {
        if (!IsOpen) return;
        if (linesGroup.activeSelf && shown < lineCount) { RevealNext(); return; }
        Press(onPrimary, "next");
    }

    /// <summary>The card's main button (tests).</summary>
    public void Finish() => Press(onPrimary, "finish");
    public void Skip() => Press(onSkip, "skip");

    public int SlideIndex => Mathf.Max(0, shown - 1);
    public int SlideCount => lineCount;
    public int LinesShown => shown;
    public bool CardShown => IsOpen && cardGroup.activeSelf;
    public bool SlidesShown => IsOpen && linesGroup.activeSelf;
    public bool CompleteShown => IsOpen && completeGroup.activeSelf;
    public string Title => !IsOpen ? "" : cardGroup.activeSelf ? cardName.text : linesGroup.activeSelf ? linesTitle.text : completeTitle.text;

    void Press(Action cb, string how)
    {
        if (!IsOpen || cb == null || ScreenFade.Busy) return;   // one press; nothing while a fade runs
        onPrimary = onSecondary = onSkip = null;
        UiBack.Remove(BackHandler);
        SetButtons(false);
        Debug.Log($"{StoryProgress.Tag} card '{Title}': {how}");
        cb();
    }

    void RevealNext()
    {
        if (shown >= lineCount) return;
        lineTexts[shown].canvasRenderer.SetAlpha(0f);
        lineTexts[shown].CrossFadeAlpha(1f, lineFadeSeconds, true);
        shown++;
        lineTime = 0f;
    }

    void ShowButtons()
    {
        buttonsShown = true;
        primaryButton.gameObject.SetActive(true);
        secondaryButton.gameObject.SetActive(twoButtons);
    }

    void Update()
    {
        if (root == null || !root.activeSelf || onPrimary == null) return;
        if (linesGroup.activeSelf)
        {
            lineTime += Time.unscaledDeltaTime;
            if (shown < lineCount && (shown == 0 ? lineTime >= 0.3f : lineTime >= lineSeconds)) RevealNext();
            else if (shown >= lineCount && !buttonsShown && lineTime >= Mathf.Min(1f, lineSeconds)) ShowButtons();
        }
        // The pointing hand's trigger, off the buttons, is "next" (the press that opened the card is ignored).
        var cm = ControllerMapping.Instance;
        bool overButton = cm != null && cm.module != null && cm.module.Hovered != null;
        if (!overButton && Time.unscaledTime - openedAt > triggerGraceSeconds && ControllerInput.Down(Pad.Trigger, ControllerMapping.PointHand))
            Next();
    }

    void OnDestroy()
    {
        UiBack.Remove(BackHandler);
        if (root != null) Destroy(root);
    }
}
