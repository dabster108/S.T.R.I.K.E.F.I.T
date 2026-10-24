// StoryPanel.cs
// Purpose: The Prologue card, as a page of the main menu's dungeon hall (no separate scene): the prologue title and its 3 to 4
//          lines from Resources/story.json (content/story.json), faded in one at a time (prologue.lineSeconds apart) and left
//          on the card, then Begin (a new story: fade to black, Chapter 1 loads) or Continue (back to the menu). Skip on the
//          laser and B end it at once; the trigger shows the next line, then presses Begin. Leaving goes through a fade to
//          black (ScreenFade); the camera never moves. Optional narration: StoryAudio.prologueNarration. A file without a
//          prologue falls back to the old slides: one at a time on the dark panel, slideSeconds each (7 s; 3 slides = 21 s,
//          always under 45 s), only the text fading (CanvasGroup alpha, fadeSeconds in and out). While it plays, two torches
//          beside the panel burn and the menu's single realtime light (no shadows) flickers slowly (Perlin noise at flickerHz,
//          under 1 Hz: nothing flashes faster than 2 Hz). Skip on the laser; the trigger (either hand) goes to the next slide.
//          Plays once on its own at the first launch (PlayerPrefs StrikeFit.StorySeen) and any time from the Story button,
//          then returns to the menu home. Per frame only while playing, no allocations.
// Dependencies: StoryContent, MainMenuController, OVRInput, MenuLog
// Attach to: MenuSystems in MainMenu.unity (built by Editor/MenuSceneBuilder)

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StoryPanel : MonoBehaviour
{
    public const string SeenKey = "StrikeFit.StorySeen";

    [Header("Toggles")]
    [Tooltip("Off: the Story button is hidden and the story never plays.")]
    public bool storyEnabled = true;
    [Tooltip("Play once by itself on the first launch of the app.")]
    public bool autoPlayOnFirstLaunch = true;
    [Tooltip("The index trigger (either hand) shows the next slide.")]
    public bool triggerAdvances = true;
    [Tooltip("Torches and the slow light flicker while the story plays.")]
    public bool torchFlicker = true;

    [Header("Timing")]
    public float fadeSeconds = 1f;
    [Tooltip("Trigger presses this soon after the story starts are ignored (the press that opened it).")]
    public float triggerGraceSeconds = 0.6f;
    [Tooltip("Flicker speed (Hz). Keep it under 1: comfort rule is nothing faster than 2 Hz.")]
    [Range(0.1f, 1f)] public float flickerHz = 0.6f;
    [Range(0f, 0.5f)] public float flickerDepth = 0.25f;

    [Header("Wiring (built by MenuSceneBuilder)")]
    public MainMenuController menu;
    public GameObject page;
    public Button openButton;
    public Button skipButton;
    public Text slideText;
    public CanvasGroup slideGroup;
    public Text counterText;
    public Graphic glow;
    public Light torchLight;
    public GameObject torches;
    [Tooltip("Optional quiet bed while the story plays (none on disk fits: see project_progress.md).")]
    public AudioSource ambient;

    public bool Playing { get; private set; }
    /// <summary>Prologue card (true) or the old slides (a story.json without prologue lines).</summary>
    public bool PrologueMode { get; private set; }
    public int LinesShown => shown;

    const int MaxLines = 5;
    readonly Text[] lineTexts = new Text[MaxLines];
    Button beginButton;
    Text beginLabel, titleText;
    string slidesTitle;
    int lineCount, shown;
    float lineTime;
    AudioSource voice;

    StoryContent.Story story;
    int index;
    float slideTime;
    float startedAt;
    float baseIntensity = 1f;
    bool skipHovered;

    void Awake()
    {
        if (openButton != null)
        {
            openButton.onClick.AddListener(() => Play("button"));
            openButton.gameObject.SetActive(storyEnabled);
        }
        if (skipButton != null)
        {
            skipButton.onClick.AddListener(() => Finish("skipped"));
            var hover = skipButton.gameObject.GetComponent<EventTrigger>();
            if (hover == null) hover = skipButton.gameObject.AddComponent<EventTrigger>();
            AddTrigger(hover, EventTriggerType.PointerEnter, () => skipHovered = true);
            AddTrigger(hover, EventTriggerType.PointerExit, () => skipHovered = false);
        }
        if (torchLight != null) { baseIntensity = torchLight.intensity; torchLight.enabled = false; }
        if (torches != null) torches.SetActive(false);
    }

    // B skips the story (same as the Skip button) and returns to the menu home.
    bool BackHandler()
    {
        if (!Playing) return false;
        Finish("skipped (B)");
        return true;
    }

    void OnDestroy() { UiBack.Remove(BackHandler); }

    static void AddTrigger(EventTrigger t, EventTriggerType type, System.Action action)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(_ => action());
        t.triggers.Add(e);
    }

    IEnumerator Start()
    {
        if (!storyEnabled || !autoPlayOnFirstLaunch || PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
        yield return null;   // after the menu has shown its home page
        Play("first launch");
    }

    System.Action finished;

    /// <summary>Plays the slides. onFinished (optional) runs when they end or are skipped instead of returning to the menu home
    /// (Story Mode: the intro of a new story, then Chapter 1; Watch intro: back to the Story Mode page).</summary>
    public void Play(string reason, System.Action onFinished = null)
    {
        if (!storyEnabled || Playing || menu == null || (reason == "button" && menu.Blocked)) return;
        finished = onFinished;
        story = StoryContent.Load();
        PrologueMode = story.prologue.lines.Length > 0;
        if (!PrologueMode && story.slides.Length == 0) { MenuLog.Event("story: no prologue or slides in Resources/story.json"); return; }
        menu.ShowPage(page, "story (" + reason + ")");
        Playing = true;
        UiBack.Push(BackHandler);
        startedAt = Time.unscaledTime;
        skipHovered = false;
        if (torches != null) torches.SetActive(torchFlicker);
        if (torchLight != null) torchLight.enabled = torchFlicker;
        if (ambient != null && ambient.clip != null) ambient.Play();
        if (PrologueMode) ShowPrologue(); else { SetPrologueUi(false); ShowSlide(0); }
    }

    // ───────────────────────── prologue card ─────────────────────────

    void BuildPrologueUi()
    {
        if (beginButton != null) return;
        var parent = slideGroup.transform.parent;   // the story page
        var t = page.transform.Find("Title");
        titleText = t != null ? t.GetComponent<Text>() : null;
        if (titleText != null) slidesTitle = titleText.text;
        for (int i = 0; i < MaxLines; i++)
        {
            var line = MenuUi.Label(parent, "PrologueLine" + i, "", 40, new Color(0.97f, 0.97f, 1f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(980f, 70f));
            line.raycastTarget = false;
            lineTexts[i] = line;
        }
        var skipRt = skipButton != null ? (RectTransform)skipButton.transform : null;
        var pos = new Vector2(0f, skipRt != null ? skipRt.anchoredPosition.y : -300f);   // centred, on the Skip button's row
        beginButton = MenuUi.MakeButton(parent, "BeginButton", "Begin", 42, new Color(0.1f, 0.42f, 0.3f, 0.97f), pos, new Vector2(320f, 84f));
        beginLabel = beginButton.GetComponentInChildren<Text>();
        beginButton.onClick.AddListener(() => Finish("begin"));
        beginButton.gameObject.SetActive(false);
    }

    void SetPrologueUi(bool on)
    {
        if (beginButton == null) { if (!on) return; BuildPrologueUi(); }
        for (int i = 0; i < MaxLines; i++) lineTexts[i].gameObject.SetActive(on && i < lineCount);
        if (!on) beginButton.gameObject.SetActive(false);
        slideText.gameObject.SetActive(!on);
        if (counterText != null) counterText.gameObject.SetActive(!on);
        if (titleText != null) titleText.text = on ? story.prologue.title : slidesTitle;
    }

    void ShowPrologue()
    {
        var p = story.prologue;
        lineCount = Mathf.Min(p.lines.Length, MaxLines);
        BuildPrologueUi();
        SetPrologueUi(true);
        float step = 86f, top = 40f + (lineCount - 1) * step * 0.5f;
        for (int i = 0; i < lineCount; i++)
        {
            lineTexts[i].text = p.lines[i];
            lineTexts[i].rectTransform.anchoredPosition = new Vector2(0f, top - i * step);
            lineTexts[i].canvasRenderer.SetAlpha(0f);
        }
        beginLabel.text = finished != null ? "Begin" : "Continue";
        shown = 0; lineTime = 0f; index = 0;
        slideGroup.alpha = 1f;
        var a = StoryAudio.Get();
        StoryAudio.Play(a != null ? a.prologueNarration : null, gameObject, ref voice);
        MenuLog.Event($"prologue '{p.title}' ({lineCount} lines, {p.lineSeconds:0.#}s apart)");
    }

    void RevealLine()
    {
        if (shown >= lineCount) return;
        lineTexts[shown].canvasRenderer.SetAlpha(0f);
        lineTexts[shown].CrossFadeAlpha(1f, Mathf.Min(fadeSeconds, story.prologue.lineSeconds), true);
        index = shown;
        shown++;
        lineTime = 0f;
    }

    void ShowSlide(int i)
    {
        index = i;
        slideTime = 0f;
        slideText.text = story.slides[i].text;
        if (counterText != null) counterText.text = (i + 1) + " / " + story.slides.Length;
        slideGroup.alpha = 0f;
        MenuLog.Event($"story slide {i + 1}/{story.slides.Length} '{story.slides[i].id}'");
    }

    void Update()
    {
        if (!Playing) return;
        float dt = Time.unscaledDeltaTime;
        if (PrologueMode) { UpdatePrologue(dt); return; }
        slideTime += dt;
        float len = story.slideSeconds;
        float fade = Mathf.Min(fadeSeconds, len * 0.5f);
        slideGroup.alpha = Mathf.Clamp01(Mathf.Min(slideTime / fade, (len - slideTime) / fade));

        if (torchFlicker)
        {
            // Smooth noise around 1: under 1 Hz, so no flash; the glow follows the light.
            float n = Mathf.PerlinNoise(Time.unscaledTime * flickerHz, 0.37f);
            float k = 1f - flickerDepth + 2f * flickerDepth * n;
            if (torchLight != null) torchLight.intensity = baseIntensity * k;
            if (glow != null) glow.canvasRenderer.SetAlpha(k);
        }

        // The pointing hand's trigger advances (right by default); with the mapping off either trigger does, as before.
        bool trig = ControllerMapping.Active
            ? ControllerInput.Down(Pad.Trigger, ControllerMapping.PointHand)
            : OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) || OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger);
        bool next = triggerAdvances && !skipHovered && Time.unscaledTime - startedAt > triggerGraceSeconds && trig;
        if (next) MenuLog.Event("story next (trigger)");
        if (next || slideTime >= len)
        {
            if (index + 1 < story.slides.Length) ShowSlide(index + 1);
            else Finish("finished");
        }
    }

    void UpdatePrologue(float dt)
    {
        lineTime += dt;
        float pace = story.prologue.lineSeconds;
        if (shown < lineCount && (shown == 0 ? lineTime >= 0.4f : lineTime >= pace)) RevealLine();
        else if (shown >= lineCount && !beginButton.gameObject.activeSelf && lineTime >= Mathf.Min(1f, pace)) beginButton.gameObject.SetActive(true);
        bool trig = ControllerMapping.Active
            ? ControllerInput.Down(Pad.Trigger, ControllerMapping.PointHand)
            : OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) || OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger);
        var cm = ControllerMapping.Instance;
        bool overButton = skipHovered || (cm != null && cm.module != null && cm.module.Hovered != null);
        if (!triggerAdvances || overButton || Time.unscaledTime - startedAt <= triggerGraceSeconds || !trig) return;
        MenuLog.Event("story next (trigger)");
        if (shown < lineCount) RevealLine(); else Finish("begin (trigger)");
    }

    /// <summary>Ends the story (skipped or finished) through a fade to black, then runs the caller's next step (a new story loads
    /// Chapter 1 and stays black; Watch intro returns to the Story Mode page) or returns to the menu home. Marks it seen.</summary>
    public void Finish(string how)
    {
        if (!Playing) return;
        Playing = false;
        UiBack.Remove(BackHandler);
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
        MenuLog.Event($"story {how} at {(PrologueMode ? "line" : "slide")} {index + 1} after {Time.unscaledTime - startedAt:0.0}s");
        var cb = finished;
        finished = null;
        ScreenFade.Out(() =>
        {
            if (torchLight != null) { torchLight.enabled = false; torchLight.intensity = baseIntensity; }
            if (torches != null) torches.SetActive(false);
            if (ambient != null) ambient.Stop();
            StoryAudio.Stop(voice);
            if (PrologueMode) SetPrologueUi(false);
            if (cb != null) cb(); else menu.ShowHome();
            if (!ScreenFade.CarryBlack) ScreenFade.In();   // a chapter load keeps the view black into the next scene
        });
    }
}
