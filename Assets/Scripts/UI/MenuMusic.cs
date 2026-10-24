// MenuMusic.cs
// Purpose: The main menu's looping ambient music: fades in slowly when the menu opens, fades out when a scene load starts
//          (SceneFlow.LoadStarting: a level, the story's first chapter) and is gone with the menu scene, so it never plays
//          into a fight. Played through the "Music" group of Assets/Audio/StrikeFitMixer.mixer. The volume (0-100 %) and a mute
//          switch are remembered in PlayerPrefs and set from a small row on the home page (built at runtime in the menu's own
//          button style: Music  -  70 %  +  Mute). The volume goes to the mixer's exposed "MusicVolume" (dB); the fade runs on
//          the AudioSource. No clip assigned (none in the project fits: see the warning) = silence and one warning line.
// Dependencies: SceneFlow, MainMenuController (home page), MenuUi, MenuLog, UnityEngine.Audio
// Attach to: MenuSystems/MenuMusic in MainMenu.unity (added by Editor/MenuDungeonBuilder)

using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class MenuMusic : MonoBehaviour
{
    public const string VolumeKey = "StrikeFit.MusicVolume";
    public const string MuteKey = "StrikeFit.MusicMuted";
    public const string MissingClipWarning =
        "[MenuMusic] No menu music clip assigned. The project has no looping ambient dungeon track: Assets/Audio/Music holds only the " +
        "fight scores (music.mp3 = Level 1, level2gamemusic.mp3 = Levels 2 and 3). Add a licensed, loopable ambient clip and assign it " +
        "to MenuSystems/MenuMusic.clip in MainMenu.unity. The menu stays silent until then.";

    [Tooltip("Looping ambient dungeon track (licensed, loopable). Drop it in Assets/Audio/Music/Menu/ and assign it here. Empty: the menu is silent.")]
    public AudioClip clip;
    public AudioSource source;
    public AudioMixer mixer;
    public AudioMixerGroup musicGroup;
    public string volumeParameter = "MusicVolume";
    public float fadeInSeconds = 5f;
    public float fadeOutSeconds = 0.8f;
    [Range(0f, 1f)] public float defaultVolume = 0.6f;
    [Tooltip("Build the Music volume / Mute row on the home page.")]
    public bool showControls = true;
    public MainMenuController menu;

    public static float Volume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 0.6f));
        set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    public static bool Muted
    {
        get => PlayerPrefs.GetInt(MuteKey, 0) == 1;
        set { PlayerPrefs.SetInt(MuteKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public float Fade => fade;
    public bool FadingOut => target == 0f;
    public bool HasClip => clip != null;

    float fade, target = 1f;
    Text valueText, muteLabel;
    Button muteButton;

    void Awake()
    {
        if (!PlayerPrefs.HasKey(VolumeKey)) PlayerPrefs.SetFloat(VolumeKey, defaultVolume);
        if (source == null) source = GetComponent<AudioSource>();
    }

    void OnEnable() { SceneFlow.LoadStarting += OnLoadStarting; }
    void OnDisable() { SceneFlow.LoadStarting -= OnLoadStarting; }

    void Start()
    {
        if (showControls) BuildControls();
        ApplyVolume();
        BeginMusic();
    }

    /// <summary>Starts the clip from silence and fades it in (Start; tests can call it again after assigning a clip).</summary>
    public void BeginMusic()
    {
        if (source == null) return;
        source.outputAudioMixerGroup = musicGroup;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        // The missing clip is flagged in the Inspector (MenuMusicEditor) and once here as a development note, not a console warning
        // on every menu load: the menu is simply silent until a licensed clip is dropped into the slot.
        if (clip == null) { DevLog.Log(MissingClipWarning); return; }
        source.clip = clip;
        fade = 0f; target = 1f;
        source.volume = 0f;
        source.Play();
        MenuLog.Event($"music '{clip.name}' fading in over {fadeInSeconds:0.#}s (volume {Mathf.RoundToInt(Volume * 100f)} %, muted {Muted})");
    }

    void OnLoadStarting(string scene)
    {
        target = 0f;
        if (source != null && source.isPlaying) MenuLog.Event($"music fading out ({scene} loading)");
    }

    void Update()
    {
        if (source == null || !source.isPlaying) return;
        float len = target > fade ? fadeInSeconds : fadeOutSeconds;
        fade = Mathf.MoveTowards(fade, target, len > 0f ? Time.unscaledDeltaTime / len : 1f);
        source.volume = fade * fade;   // ease in: the first seconds are very quiet
        if (fade <= 0f && target <= 0f) source.Stop();
    }

    /// <summary>Volume and mute go to the mixer (the source volume carries only the fade).</summary>
    public void ApplyVolume()
    {
        float linear = Muted ? 0f : Volume;
        float db = linear > 0.0001f ? 20f * Mathf.Log10(linear) : -80f;
        bool viaMixer = mixer != null && mixer.SetFloat(volumeParameter, db);
        if (!viaMixer && source != null) source.mute = Muted;   // no mixer: mute at least
        if (valueText != null) valueText.text = Mathf.RoundToInt(Volume * 100f) + " %";
        if (muteLabel != null) muteLabel.text = Muted ? "Unmute" : "Mute";
    }

    public void Step(float delta)
    {
        Volume = Mathf.Round((Volume + delta) * 10f) / 10f;
        if (Muted && delta > 0f) Muted = false;
        ApplyVolume();
        MenuLog.Event($"music volume {Mathf.RoundToInt(Volume * 100f)} %");
    }

    public void ToggleMute()
    {
        Muted = !Muted;
        ApplyVolume();
        MenuLog.Event("music " + (Muted ? "muted" : "unmuted"));
    }

    void BuildControls()
    {
        if (menu == null || menu.homePage == null) return;
        var page = menu.homePage.transform;
        var color = new Color(0.25f, 0.25f, 0.3f, 0.96f);
        // One row under the hint line; the home panel grows down by one row to hold it.
        float y = -318f;
        var hint = page.Find("Hint") as RectTransform;
        if (hint != null) y = hint.anchoredPosition.y - 58f;
        var panel = page.Find("Panel") as RectTransform;
        if (panel != null && panel.Find("MusicRowSpace") == null)
        {
            panel.sizeDelta += new Vector2(0f, 76f);
            panel.anchoredPosition -= new Vector2(0f, 38f);
            new GameObject("MusicRowSpace", typeof(RectTransform)).transform.SetParent(panel, false);   // marker: grown once
        }
        var label = MenuUi.Label(page, "MusicLabel", "Music", 26, new Color(0.62f, 0.66f, 0.78f), TextAnchor.MiddleRight, new Vector2(-250f, y), new Vector2(140f, 44f));
        label.raycastTarget = false;
        MenuUi.MakeButton(page, "MusicDown", "-", 34, color, new Vector2(-140f, y), new Vector2(64f, 52f)).onClick.AddListener(() => Step(-0.1f));
        valueText = MenuUi.Label(page, "MusicValue", "", 26, Color.white, TextAnchor.MiddleCenter, new Vector2(-50f, y), new Vector2(110f, 44f));
        valueText.raycastTarget = false;
        MenuUi.MakeButton(page, "MusicUp", "+", 34, color, new Vector2(40f, y), new Vector2(64f, 52f)).onClick.AddListener(() => Step(0.1f));
        muteButton = MenuUi.MakeButton(page, "MusicMute", "Mute", 26, color, new Vector2(180f, y), new Vector2(150f, 52f));
        muteButton.onClick.AddListener(ToggleMute);
        muteLabel = muteButton.GetComponentInChildren<Text>();
    }
}
