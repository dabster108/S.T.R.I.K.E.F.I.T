// MainMenuController.cs
// Purpose: The main menu scene's logic. A home column (Start Game, Change Name, the optional Scoreboard / Story pages, Exit Game,
//          "Playing as <name>") and its pages: Start Game opens the three level cards (select a card, then Play; the card shows
//          "Loading..." while its scene loads, nothing else moves or fades), Change Name opens the PlayerNamePicker laser
//          keyboard in menu mode, Exit Game opens the confirm panel. Every page has a Back button. Level buttons go straight to
//          SceneFlow, so every level is selectable (GameStateManager's "highest unlocked" gate is for the old progression and is
//          not used here). One [StrikeFitMenu] log line per event (MenuLog).
// Dependencies: SceneFlow, ExitConfirmPanel, PlayerNamePicker (menu mode), PlayerNameStore, SessionReporter, MenuLog
// Attach to: MenuSystems in MainMenu.unity (built by Editor/MenuSceneBuilder)

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Serializable]
    public class LevelCard
    {
        public string scene;
        public Button button;
        public Text status;    // "Loading..." goes here
        [Tooltip("Outline shown while this card is the selected one (select-then-Play flow).")]
        public GameObject selectedFrame;
    }

    public LevelCard[] levels;
    public Button exitButton;
    public ExitConfirmPanel confirm;
    public CanvasGroup content;
    public string loadingText = "Loading...";

    [Header("Pages (children of Content, one shown at a time)")]
    public GameObject homePage;
    public GameObject levelPage;
    public GameObject namePage;
    [Tooltip("Every page root, including ones added by other components (scoreboard, story). Showing one hides the rest.")]
    public GameObject[] pages;

    [Header("Home column")]
    public Button startGameButton;
    public Button changeNameButton;
    public Text playingAsLabel;

    [Header("Level page")]
    public Button playButton;
    public Text playLabel;
    public Button levelBackButton;
    public string selectPrompt = "Select a level";
    public string playText = "Play";

    [Header("Name page")]
    public PlayerNamePicker namePicker;

    [Header("Toggles")]
    [Tooltip("On: a level card only selects, Play starts it. Off: a card click starts its level at once (the old menu).")]
    public bool selectThenPlay = true;
    [Tooltip("Show \"Playing as <name>\" under the title.")]
    public bool showPlayingAs = true;
    [Tooltip("Change Name opens the laser keyboard name picker. Off: the button is hidden.")]
    public bool changeNameEnabled = true;

    public GameObject CurrentPage { get; private set; }
    /// <summary>Raised after a page is shown (its root).</summary>
    public event Action<GameObject> PageShown;

    int selected = -1;
    bool loading;

    void Awake()
    {
        for (int i = 0; i < levels.Length; i++)
        {
            int index = i;
            if (levels[i].button != null) levels[i].button.onClick.AddListener(() => OnLevel(index));
            if (levels[i].status != null) levels[i].status.text = "";
            if (levels[i].selectedFrame != null) levels[i].selectedFrame.SetActive(false);
        }
        if (exitButton != null) exitButton.onClick.AddListener(OnExit);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGame);
        if (changeNameButton != null)
        {
            changeNameButton.onClick.AddListener(OnChangeName);
            if (!changeNameEnabled || namePicker == null) changeNameButton.gameObject.SetActive(false);
        }
        if (playButton != null) playButton.onClick.AddListener(OnPlay);
        if (levelBackButton != null) levelBackButton.onClick.AddListener(() => Back("levels"));
    }

    void Start()
    {
        // The reporter carries the remembered name from the first frame (the scoreboard's "you" row reads it).
        string name = PlayerNameStore.LastUsed;
        if (!string.IsNullOrEmpty(name) && SessionReporter.Instance != null) SessionReporter.Instance.SetPlayerName(name);
        RefreshPlayingAs();
        if (homePage != null) ShowPage(homePage, null);
        MenuLog.Event($"menu loaded, playing as '{(string.IsNullOrEmpty(name) ? "(none)" : name)}'");
    }

    // ───────────────────────── pages ─────────────────────────

    /// <summary>True while a click must be ignored: a scene is loading or the exit confirm is open.</summary>
    public bool Blocked => loading || SceneFlow.Busy || (confirm != null && (confirm.IsOpen || confirm.Quitting));

    /// <summary>Shows one page and hides every other. logName null = no log line (used for the first home).</summary>
    public void ShowPage(GameObject page, string logName)
    {
        if (pages != null)
            foreach (var p in pages)
                if (p != null && p != page) p.SetActive(false);
        if (page != null) page.SetActive(true);
        CurrentPage = page;
        if (logName != null) MenuLog.Event("page " + logName);
        PageShown?.Invoke(page);
    }

    public void ShowHome()
    {
        RefreshPlayingAs();
        ShowPage(homePage, "home");
    }

    /// <summary>Back button of any page.</summary>
    public void Back(string from)
    {
        if (Blocked) return;
        MenuLog.Event("back from " + from);
        ShowHome();
    }

    public void RefreshPlayingAs()
    {
        if (playingAsLabel == null) return;
        playingAsLabel.gameObject.SetActive(showPlayingAs);
        string name = PlayerNameStore.LastUsed;
        playingAsLabel.text = string.IsNullOrEmpty(name) ? "No name yet: press Change Name" : "Playing as " + name;
    }

    // ───────────────────────── Start Game / levels ─────────────────────────

    void OnStartGame()
    {
        if (Blocked || levelPage == null) return;
        Select(-1);
        ShowPage(levelPage, "levels");
    }

    void OnLevel(int index)
    {
        if (Blocked) return;
        if (!selectThenPlay)
        {
            MenuLog.Event($"play {levels[index].scene} (direct)");
            StartCoroutine(Go(index));
            return;
        }
        Select(index);
        MenuLog.Event($"select {levels[index].scene}");
    }

    void Select(int index)
    {
        selected = index;
        for (int i = 0; i < levels.Length; i++)
            if (levels[i].selectedFrame != null) levels[i].selectedFrame.SetActive(i == index);
        if (playButton != null)
        {
            playButton.gameObject.SetActive(selectThenPlay);
            playButton.interactable = index >= 0;
        }
        if (playLabel != null) playLabel.text = index >= 0 ? $"{playText} Level {index + 1}" : selectPrompt;
    }

    void OnPlay()
    {
        if (Blocked || selected < 0 || selected >= levels.Length) return;
        MenuLog.Event($"play {levels[selected].scene}");
        StartCoroutine(Go(selected));
    }

    IEnumerator Go(int index)
    {
        var card = levels[index];
        loading = true;
        if (card.status != null) card.status.text = loadingText;
        SetInteractable(false);
        yield return null;   // one frame so the label is drawn before the load starts
        if (!SceneFlow.Load(card.scene))
        {
            if (card.status != null) card.status.text = "Not available";
            MenuLog.Event($"load refused {card.scene}");
            loading = false;
            SetInteractable(true);
        }
    }

    // ───────────────────────── Change Name ─────────────────────────

    void OnChangeName()
    {
        if (Blocked || namePicker == null || namePage == null) return;
        ShowPage(namePage, "change name");
        namePicker.OpenFromMenu(OnNameClosed);
    }

    void OnNameClosed(string chosen)
    {
        if (!string.IsNullOrEmpty(chosen)) MenuLog.Event($"name set '{chosen}'");
        ShowHome();
    }

    // ───────────────────────── Exit ─────────────────────────

    void OnExit()
    {
        if (SceneFlow.Busy || confirm == null || loading) return;
        MenuLog.Event("exit pressed (confirm open)");
        confirm.Show();
    }

    void SetInteractable(bool on)
    {
        if (content != null) content.interactable = on;
    }
}
