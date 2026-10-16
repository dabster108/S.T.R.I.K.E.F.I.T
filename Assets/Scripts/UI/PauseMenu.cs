// PauseMenu.cs
// Purpose: The pause menu, one shared component for Levels 1, 2 and 3 (BattleManager creates it at runtime, like the name picker, so
//          there is no per-scene copy). It works from the moment a fight starts (the monster entrances included) until Victory or
//          Defeat, and does nothing before Start, on the end panels or in the menu scene.
//          Pausing: GamePause flag (the one owner), Time.timeScale 0 and AudioListener.pause. Gameplay systems also test
//          GamePause.IsPaused themselves, because Update still runs at time scale 0 (see the guards in HitDetection, PlayerCombat,
//          MonsterAI, MonsterDefense ...). Resuming restores everything and starts every weapon's swing history afresh.
//          Panel: world-space, placed ahead of the head at eye height (yaw only), then fixed in the world; Resume, Restart, Level
//          Select, Exit Game; the three destructive buttons ask once. The pause also closes the live session cleanly:
//          pause / resume events, no HP snapshots while paused, Restart / Level Select / Exit send an "abandoned" session.
// Dependencies: GamePause, ControlSettings, ControllerInput, ControllerMapping, BattleManager, SceneFlow, SessionReporter,
//               CombatFeedback, OVRManager (HMD / focus events), MenuUi
// Attach to: created at runtime on the BattleSystems object

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PauseMenu : MonoBehaviour
{
    public const string Tag = "[StrikeFitPause]";

    public static PauseMenu Instance { get; private set; }

    public enum View { Closed, Main, Confirm }
    enum Pending { None, Restart, LevelSelect, Exit }

    public View CurrentView { get; private set; } = View.Closed;
    public bool IsOpen => CurrentView != View.Closed;
    /// <summary>Why the current / last pause started ("B", "headset taken off", ...).</summary>
    public string LastCause { get; private set; } = "";
    /// <summary>Pauses opened by this component in this scene.</summary>
    public int PausesOpened { get; private set; }
    public bool Busy { get; private set; }

    static bool hintShownThisLaunch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { hintShownThisLaunch = false; Instance = null; }

    BattleManager bm;
    CombatFeedback feedback;
    Transform head;
    GameObject uiRay;
    AudioSource music;
    float musicVolume;
    bool musicIgnoredPause;
    float previousTimeScale = 1f;
    Pending pending;

    // UI (built on the first pause, then reused)
    Canvas canvas;
    CanvasGroup mainGroup, confirmGroup;
    Text subtitle, confirmText, mainHint, confirmHint;
    AudioSource uiAudio;
    AudioClip openClip, closeClip, clickClip;
    GameObject hintRoot;
    Text hintText;
    float hintEndsAt;

    // ───────────────────────── lifecycle ─────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        bm = GetComponent<BattleManager>();
    }

    void OnEnable()
    {
        OVRManager.HMDUnmounted += OnHmdUnmounted;
        OVRManager.HMDLost += OnHmdLost;
        OVRManager.VrFocusLost += OnVrFocusLost;
        if (bm != null) bm.OnBattleStarted += OnBattleStarted;
    }

    void OnDisable()
    {
        OVRManager.HMDUnmounted -= OnHmdUnmounted;
        OVRManager.HMDLost -= OnHmdLost;
        OVRManager.VrFocusLost -= OnVrFocusLost;
        if (bm != null) bm.OnBattleStarted -= OnBattleStarted;
    }

    void Start()
    {
        feedback = FindAnyObjectByType<CombatFeedback>();
        var rig = FindAnyObjectByType<OVRCameraRig>();
        head = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
        music = FindMusic();
    }

    void OnDestroy()
    {
        // The next scene, or the menu, must never start frozen or muted.
        if (GamePause.IsPaused)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            DuckMusic(false);
            GamePause.ForceClear();
        }
        if (canvas != null) Destroy(canvas.gameObject);
        if (hintRoot != null) Destroy(hintRoot);
        if (Instance == this) Instance = null;
    }

    static AudioSource FindMusic()
    {
        AudioSource best = null;
        foreach (var a in FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (a == null || !a.loop || a.clip == null) continue;
            if (a.gameObject.name == "BackgroundMusic") return a;
            if (a.clip.length > 30f && a.playOnAwake && best == null) best = a;
        }
        return best;
    }

    // ───────────────────────── input ─────────────────────────

    void Update()
    {
        if (bm == null) return;
        var s = ControlSettings.Current;
        bool pressed = ControllerInput.Down(Pad.Two, ControllerMapping.ButtonHand) ||
                       (s.leftMenuButtonPauses && ControllerInput.Down(Pad.Start, OVRInput.Controller.LTouch));
#if UNITY_EDITOR
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame) pressed = true;   // editor only, for testing without a headset
#endif
        if (hintRoot != null) UpdateHint();

        if (IsOpen)
        {
            if (!bm.IsBattleActive && !Busy) { Release("fight ended"); return; }
            if (pressed && !Busy)
            {
                if (CurrentView == View.Confirm) ShowMain();
                else Resume("B");
            }
            return;
        }
        if (pressed && CanPause()) Pause("B");
    }

    /// <summary>True during a fight (entrance included) and not while a scene is loading. The B press that started the fight is ignored.</summary>
    public bool CanPause()
    {
        if (bm == null || !bm.IsBattleActive || Busy || SceneFlow.Busy || GamePause.IsPaused) return false;
        return Time.frameCount != bm.BattleStartFrame;
    }

    // ───────────────────────── auto-pause (never auto-resumes) ─────────────────────────

    void OnHmdUnmounted() { Auto("headset taken off"); }
    void OnHmdLost() { Auto("headset lost"); }
    void OnVrFocusLost() { Auto("VR focus lost (system menu)"); }
    void OnApplicationPause(bool paused) { if (paused) Auto("app paused"); }

    void Auto(string cause)
    {
        if (!ControlSettings.Current.autoPause) return;
        if (CanPause()) Pause(cause);
    }

    // ───────────────────────── pause / resume ─────────────────────────

    /// <summary>Freeze the fight and open the panel.</summary>
    public void Pause(string cause)
    {
        if (!CanPause()) return;
        if (!GamePause.Set(true)) return;
        LastCause = cause ?? "";
        PausesOpened++;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        DuckMusic(true);
        StopHaptics();
        if (hintRoot != null) { Destroy(hintRoot); hintRoot = null; }

        EnsureBuilt();
        PlacePanel();
        canvas.gameObject.SetActive(true);
        ShowMain();
        Play(openClip);

        var rep = SessionReporter.Instance;
        if (rep != null) rep.ReportPause(true, LastCause);
        Log("paused", LastCause);
    }

    /// <summary>Close the panel and resume.</summary>
    public void Resume(string cause)
    {
        if (!GamePause.IsPaused) return;
        Release(cause);
    }

    // Everything a resume must restore. Also used when the fight ends while paused, and by the Restart / Level Select / Exit path.
    void Release(string cause)
    {
        bool wasPaused = GamePause.IsPaused;
        if (canvas != null) canvas.gameObject.SetActive(false);
        CurrentView = View.Closed;
        pending = Pending.None;
        Busy = false;
        if (mainGroup != null) { mainGroup.interactable = true; confirmGroup.interactable = true; }
        Time.timeScale = 1f;
        AudioListener.pause = false;
        DuckMusic(false);
        if (wasPaused) GamePause.Set(false);
        // Weapons forget where they were: the first frame after the resume can never read a huge swing or a hit.
        foreach (var h in FindObjectsByType<HitDetection>(FindObjectsInactive.Exclude)) h.ResetSweepHistory();
        if (wasPaused)
        {
            Play(closeClip);
            var rep = SessionReporter.Instance;
            if (rep != null) rep.ReportPause(false, cause);
            Log("resumed", cause);
        }
    }

    void StopHaptics()
    {
        if (feedback != null) feedback.StopAllHaptics();
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }

    // The level's music keeps playing, quieter, while the rest of the audio is paused.
    void DuckMusic(bool on)
    {
        if (music == null) return;
        if (on)
        {
            musicVolume = music.volume;
            musicIgnoredPause = music.ignoreListenerPause;
            music.ignoreListenerPause = true;
            music.volume = musicVolume * ControlSettings.Current.pauseMusicDuck;
        }
        else
        {
            music.ignoreListenerPause = musicIgnoredPause;
            if (musicVolume > 0f) music.volume = musicVolume;
        }
    }

    void Log(string what, string cause)
    {
        if (!ControlSettings.Current.logEvents) return;
        var rep = SessionReporter.Instance;
        string level = LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "?";
        Debug.Log($"{Tag} {what} cause='{cause}' level={level} player='{(rep != null ? rep.sessionPlayerName : "")}' pausedTotal={(rep != null ? rep.SessionPausedSeconds : GamePause.PausedSeconds):0.0}s pauses={GamePause.PauseCount}");
    }

    // ───────────────────────── panel ─────────────────────────

    void EnsureBuilt()
    {
        if (canvas != null) return;
        var cs = ControlSettings.Current;
        var root = new GameObject("PauseCanvas", typeof(RectTransform));
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = head != null ? head.GetComponent<Camera>() : Camera.main;
        canvas.sortingOrder = 40;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(1000f, 640f);
        rt.localScale = Vector3.one * 0.0018f;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        var rc = root.AddComponent<OVRRaycaster>();
        var module = FindAnyObjectByType<UnityEngine.EventSystems.OVRInputModule>();
        rc.pointer = module != null && module.rayTransform != null ? module.rayTransform.gameObject : null;
        rc.sortOrder = 100;   // above the HUD canvas

        var bg = MenuUi.Panel(root.transform, "Backing", Vector2.zero, new Vector2(1000f, 640f), new Color(0.04f, 0.05f, 0.09f, 0.94f));
        bg.raycastTarget = true;
        MenuUi.Label(bg.transform, "Title", "Paused", 62, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 262f), new Vector2(900f, 80f), FontStyle.Bold);
        subtitle = MenuUi.Label(bg.transform, "Subtitle", "", 34, new Color(0.75f, 0.82f, 0.95f), TextAnchor.MiddleCenter, new Vector2(0f, 200f), new Vector2(900f, 48f));

        mainGroup = MakeGroup(bg.transform, "Main");
        var resume = MenuUi.MakeButton(mainGroup.transform, "ResumeButton", "Resume", 46, new Color(0.12f, 0.55f, 0.25f, 0.97f), new Vector2(0f, 90f), new Vector2(560f, 86f));
        var restart = MenuUi.MakeButton(mainGroup.transform, "RestartButton", "Restart", 44, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(0f, -10f), new Vector2(560f, 82f));
        var select = MenuUi.MakeButton(mainGroup.transform, "LevelSelectButton", "Level Select", 44, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(0f, -104f), new Vector2(560f, 82f));
        var exit = MenuUi.MakeButton(mainGroup.transform, "ExitGameButton", "Exit Game", 44, new Color(0.45f, 0.45f, 0.5f, 0.97f), new Vector2(0f, -198f), new Vector2(560f, 82f));
        mainHint = MenuUi.Label(mainGroup.transform, "Hint", HintLine("resume"), 30, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, new Vector2(0f, -280f), new Vector2(700f, 40f));
        resume.onClick.AddListener(() => { if (!Busy) { Play(clickClip); Resume("button"); } });
        restart.onClick.AddListener(() => Ask(Pending.Restart));
        select.onClick.AddListener(() => Ask(Pending.LevelSelect));
        exit.onClick.AddListener(() => Ask(Pending.Exit));

        confirmGroup = MakeGroup(bg.transform, "Confirm");
        confirmText = MenuUi.Label(confirmGroup.transform, "Question", "This ends the current fight. Continue?", 44, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 80f), new Vector2(860f, 160f), FontStyle.Bold);
        var yes = MenuUi.MakeButton(confirmGroup.transform, "YesButton", "Yes", 46, new Color(0.65f, 0.15f, 0.15f, 0.97f), new Vector2(-220f, -90f), new Vector2(360f, 96f));
        var no = MenuUi.MakeButton(confirmGroup.transform, "NoButton", "No", 46, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(220f, -90f), new Vector2(360f, 96f));
        confirmHint = MenuUi.Label(confirmGroup.transform, "Hint", HintLine("back"), 30, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter, new Vector2(0f, -280f), new Vector2(700f, 40f));
        yes.onClick.AddListener(() => { if (!Busy && pending != Pending.None) { Play(clickClip); Do(pending); } });
        no.onClick.AddListener(() => { if (!Busy) { Play(clickClip); ShowMain(); } });

        // UI sounds: short synthesised tones on a 2D source that ignores the listener pause (no asset, nothing to include in a build).
        uiAudio = root.AddComponent<AudioSource>();
        uiAudio.playOnAwake = false;
        uiAudio.spatialBlend = 0f;
        uiAudio.ignoreListenerPause = true;
        uiAudio.volume = 0.45f;
        openClip = Tone("PauseOpen", 420f, 640f, 0.16f);
        closeClip = Tone("PauseClose", 640f, 420f, 0.16f);
        clickClip = Tone("PauseClick", 880f, 880f, 0.05f);
        root.SetActive(false);
    }

    static CanvasGroup MakeGroup(Transform parent, string name)
    {
        var rt = MenuUi.Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt.gameObject.AddComponent<CanvasGroup>();
    }

    // "B: resume" / "Y: resume" when the buttons were swapped to the left hand.
    static string HintLine(string verb) => (ControllerMapping.ButtonHand == OVRInput.Controller.LTouch ? "Y: " : "B: ") + verb;

    void ShowMain()
    {
        CurrentView = View.Main;
        pending = Pending.None;
        mainHint.text = HintLine("resume");   // B, or Y when the buttons were swapped to the left hand
        confirmHint.text = HintLine("back");
        mainGroup.gameObject.SetActive(true);
        confirmGroup.gameObject.SetActive(false);
        var rep = SessionReporter.Instance;
        string level = LevelManager.Instance != null ? LevelLabel(LevelManager.Instance.sceneDifficulty) : "";
        string player = rep != null ? rep.sessionPlayerName : "";
        subtitle.text = string.IsNullOrEmpty(player) ? level : level + "  ·  " + player;
    }

    static string LevelLabel(DifficultyLevel d)
    {
        switch (d)
        {
            case DifficultyLevel.Easy: return "Level 1 (Easy)";
            case DifficultyLevel.Medium: return "Level 2 (Medium)";
            case DifficultyLevel.Hard: return "Level 3 (Hard)";
            default: return d.ToString();
        }
    }

    /// <summary>World placement at the moment of the pause: ahead of the head, at eye height, facing the player (yaw only); then it stays put.</summary>
    void PlacePanel()
    {
        if (head == null) return;
        Vector3 fwd = head.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = head.up.y > 0f ? Vector3.forward : head.forward;   // looking straight up or down
        fwd.Normalize();
        var t = canvas.transform;
        t.position = head.position + fwd * ControlSettings.Current.panelDistance;
        t.rotation = Quaternion.LookRotation(fwd, Vector3.up);   // the canvas reads from behind its forward axis
    }

    // ───────────────────────── buttons ─────────────────────────

    void Ask(Pending p)
    {
        if (Busy || !IsOpen) return;
        Play(clickClip);
        if (!ControlSettings.Current.confirmDestructive) { Do(p); return; }
        pending = p;
        CurrentView = View.Confirm;
        mainGroup.gameObject.SetActive(false);
        confirmGroup.gameObject.SetActive(true);
    }

    void Do(Pending p)
    {
        if (Busy) return;
        Busy = true;   // blocks double clicks; the buttons grey out
        mainGroup.interactable = false;
        confirmGroup.interactable = false;
        StartCoroutine(RunAction(p));
    }

    IEnumerator RunAction(Pending p)
    {
        string reason = p == Pending.Restart ? "restart from pause" : p == Pending.LevelSelect ? "level select from pause" : "exit from pause";
        Log("leaving", reason);
        // The fight ends as "abandoned": the final session is queued and handed to the network now, before the scene changes.
        if (bm != null) bm.AbandonFight(reason);

        if (p == Pending.Exit)
        {
            var rep = SessionReporter.Instance;
            if (rep != null)
            {
                rep.FlushNow();
                float waited = 0f;   // real time: the time scale is still 0 here
                while (rep != null && rep.SendsInFlight > 0 && waited < 0.8f) { waited += Time.unscaledDeltaTime; yield return null; }
            }
            Release(reason);
            SceneFlow.QuitNow();
            yield break;
        }

        Release(reason);   // time scale 1, audio unpaused, panel hidden: the next scene starts clean
        yield return null;
        if (p == Pending.Restart) SceneFlow.PlayAgain(); else SceneFlow.LevelSelect();
    }

    void Play(AudioClip c)
    {
        if (uiAudio != null && c != null) uiAudio.PlayOneShot(c);
    }

    static AudioClip Tone(string name, float f0, float f1, float seconds)
    {
        const int rate = 24000;
        int n = Mathf.Max(1, (int)(rate * seconds));
        var data = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            double f = Mathf.Lerp(f0, f1, t);
            phase += 2.0 * System.Math.PI * f / rate;
            float env = (1f - Mathf.Exp(-i / (rate * 0.004f))) * Mathf.Exp(-3.2f * t);
            data[i] = (float)System.Math.Sin(phase) * env;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ───────────────────────── "B: pause" hint ─────────────────────────

    void OnBattleStarted()
    {
        if (!ControlSettings.Current.showPauseHint || hintShownThisLaunch || head == null) return;
        hintShownThisLaunch = true;
        hintRoot = new GameObject("PauseHint", typeof(RectTransform));
        var c = hintRoot.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = head.GetComponent<Camera>();
        c.sortingOrder = 35;
        var rt = (RectTransform)hintRoot.transform;
        rt.sizeDelta = new Vector2(500f, 90f);
        rt.localScale = Vector3.one * 0.0016f;
        Vector3 fwd = head.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        hintRoot.transform.position = head.position + fwd * 1.5f + Vector3.down * 0.42f;
        hintRoot.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        hintText = MenuUi.Label(rt, "Text", HintLine("pause"), 52, new Color(1f, 1f, 1f, 0.95f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(500f, 90f), FontStyle.Bold);
        hintEndsAt = Time.unscaledTime + Mathf.Max(1f, ControlSettings.Current.pauseHintSeconds);
    }

    void UpdateHint()
    {
        float left = hintEndsAt - Time.unscaledTime;
        if (left <= 0f) { Destroy(hintRoot); hintRoot = null; return; }
        var col = hintText.color; col.a = Mathf.Clamp01(left) * 0.95f;   // fades over the last second
        hintText.color = col;
    }
}
