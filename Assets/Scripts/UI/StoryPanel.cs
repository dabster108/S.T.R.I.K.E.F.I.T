// StoryPanel.cs
// Purpose: The story, as a page of the main menu hall (no separate scene): the slides of Resources/story.json (content/story.json)
//          one at a time on a world-space dark panel, slideSeconds each (7 s; 3 slides = 21 s, always under 45 s). Only the
//          text fades (CanvasGroup alpha, fadeSeconds in and out); the camera never moves or fades. While it plays, two torches
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

    public void Play(string reason)
    {
        if (!storyEnabled || Playing || menu == null || (reason == "button" && menu.Blocked)) return;
        story = StoryContent.Load();
        if (story.slides.Length == 0) { MenuLog.Event("story: no slides in Resources/story.json"); return; }
        menu.ShowPage(page, "story (" + reason + ")");
        Playing = true;
        startedAt = Time.unscaledTime;
        skipHovered = false;
        if (torches != null) torches.SetActive(torchFlicker);
        if (torchLight != null) torchLight.enabled = torchFlicker;
        if (ambient != null && ambient.clip != null) ambient.Play();
        ShowSlide(0);
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

        bool next = triggerAdvances && !skipHovered && Time.unscaledTime - startedAt > triggerGraceSeconds &&
                    (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) || OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger));
        if (next) MenuLog.Event("story next (trigger)");
        if (next || slideTime >= len)
        {
            if (index + 1 < story.slides.Length) ShowSlide(index + 1);
            else Finish("finished");
        }
    }

    /// <summary>Ends the story (skipped or finished) and returns to the menu home. Marks it seen.</summary>
    public void Finish(string how)
    {
        if (!Playing) return;
        Playing = false;
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();
        if (torchLight != null) { torchLight.enabled = false; torchLight.intensity = baseIntensity; }
        if (torches != null) torches.SetActive(false);
        if (ambient != null) ambient.Stop();
        MenuLog.Event($"story {how} at slide {index + 1} after {Time.unscaledTime - startedAt:0.0}s");
        menu.ShowHome();
    }
}
