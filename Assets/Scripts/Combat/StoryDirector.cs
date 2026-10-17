// StoryDirector.cs
// Purpose: Story Mode inside a level. Created at runtime by BattleManager (like the name picker) when a story run is active and
//          this scene is the run's current chapter, so no level scene needs a copy. Before the fight: the chapter card (StoryStage),
//          then the usual name picker / goal / Start flow. After a Victory: the chapter is saved (StoryProgress) and the end panel
//          offers "Continue story": the interlude, then the next chapter loads (async, "Loading..."); after chapter 3 the ending
//          slides and the "Story complete" panel, then the menu. After a Defeat: the defeat line, Retry or Story menu, nothing
//          saved. Free play (no run) never creates this component. The pause menu stays closed while a card or slide is up.
//          Telemetry: SessionReporter.SessionMode = "story" for the session start. One [StrikeFitStory] log line per event.
// Dependencies: StoryProgress, StoryContent, StoryStage, BattleManager, PlayerNamePicker (hold), SceneFlow, SessionReporter,
//               MainMenuController (page to open), XPManager / BattleStats (complete-panel stats)
// Attach to: created at runtime on the BattleSystems object

using UnityEngine;
using UnityEngine.SceneManagement;

public class StoryDirector : MonoBehaviour
{
    public static StoryDirector Instance { get; private set; }

    /// <summary>Card, interlude, ending or complete panel open: the pause menu must not open.</summary>
    public static bool BlocksPause => Instance != null && Instance.stage != null && Instance.stage.IsOpen;

    [Tooltip("Show the chapter card before the name picker.")]
    public bool chapterCard = true;

    public int Chapter { get; private set; }
    public string Player { get; private set; }
    public bool VictorySaved { get; private set; }
    public StoryStage Stage => stage;

    BattleManager bm;
    StoryStage stage;
    int fightScore;
    float fightDamage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    /// <summary>True when this scene should run as a story chapter: a run is active and the scene is the run's next chapter.</summary>
    public static bool ShouldRun(out int chapter)
    {
        chapter = StoryContent.LevelNumber(SceneManager.GetActiveScene().name);
        if (!StoryProgress.RunActive || chapter < 1) return false;
        int next = StoryProgress.NextChapter(StoryProgress.RunPlayer);
        // A finished story replayed from the menu is free play; otherwise only the run's own chapter is a story fight.
        return next == chapter;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        bm = GetComponent<BattleManager>();
        ShouldRun(out int ch);
        Chapter = ch;
        Player = StoryProgress.RunPlayer;
        SessionReporter.SessionMode = "story";
        // The card comes first: the name picker waits until Begin.
        if (chapterCard) PlayerNamePicker.HoldOpen = true;
        stage = gameObject.AddComponent<StoryStage>();
        var story = StoryContent.Load();
        stage.slideSeconds = story.slideSeconds;
    }

    void OnEnable()
    {
        if (bm != null) { bm.OnVictory += HandleVictory; bm.OnDefeat += HandleDefeat; }
    }

    void OnDisable()
    {
        if (bm != null) { bm.OnVictory -= HandleVictory; bm.OnDefeat -= HandleDefeat; }
    }

    void Start()
    {
        Debug.Log($"{StoryProgress.Tag} chapter {Chapter} scene '{SceneManager.GetActiveScene().name}' player '{Player}' (cleared {StoryProgress.Cleared(Player)}/3)");
        if (chapterCard) stage.ShowChapterCard(Chapter, Begin);
    }

    void Begin()
    {
        stage.Hide();
        Debug.Log($"{StoryProgress.Tag} chapter {Chapter} card: Begin");
        PlayerNamePicker.ReleaseHold();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        PlayerNamePicker.HoldOpen = false;
    }

    void HandleVictory(int score)
    {
        fightScore = score;
        fightDamage = bm != null ? bm.TotalDamageDealt : 0f;
        // Saved under the name that fought this chapter (the run's player unless the picker changed it).
        var rep = SessionReporter.Instance;
        string fighter = rep != null && !string.IsNullOrEmpty(rep.sessionPlayerName) ? rep.sessionPlayerName : Player;
        StoryProgress.SaveChapter(fighter, Chapter);
        if (fighter != Player) StoryProgress.SaveChapter(Player, Chapter);
        VictorySaved = true;
        Debug.Log($"{StoryProgress.Tag} chapter {Chapter} victory, score {score}");
    }

    void HandleDefeat(int score)
    {
        Debug.Log($"{StoryProgress.Tag} chapter {Chapter} defeat, score {score} (nothing saved)");
    }

    // ───────────────────────── end-panel buttons (OutcomeMenuButtons) ─────────────────────────

    /// <summary>The prompt line above the end-panel buttons in a story run.</summary>
    public string PromptFor(bool victory)
    {
        if (!victory) return StoryContent.Load().interludes.defeat;
        return Chapter >= StoryProgress.Chapters ? "The last chapter is won" : "Chapter " + Chapter + " cleared";
    }

    /// <summary>Victory: interlude then the next chapter, or the ending and the complete panel after chapter 3.</summary>
    public void ContinueStory()
    {
        if (SceneFlow.Busy || stage.IsOpen) return;
        var it = StoryContent.Load().interludes;
        Debug.Log($"{StoryProgress.Tag} continue after chapter {Chapter}");
        // The end panel goes away while the slides are up (its buttons would sit above the slide panel, still clickable).
        if (CombatUIController.Instance != null) CombatUIController.Instance.HideOutcomePanels();
        if (Chapter >= StoryProgress.Chapters)
        {
            stage.ShowSlides("The end of the Trials", it.ending, "Continue", ShowComplete);
            return;
        }
        string text = Chapter == 1 ? it.afterLevel1 : it.afterLevel2;
        stage.ShowSlides("Chapter " + Chapter + " cleared", new[] { text }, "Continue", LoadNextChapter);
    }

    void LoadNextChapter()
    {
        string next = Chapter == 1 ? SceneFlow.Level2 : SceneFlow.Level3;
        stage.ShowLoading();
        Debug.Log($"{StoryProgress.Tag} loading chapter {Chapter + 1} ({next})");
        if (!SceneFlow.Load(next)) Debug.LogWarning($"{StoryProgress.Tag} load of {next} refused");
    }

    void ShowComplete()
    {
        var xp = XPManager.Instance;
        int cleared = StoryProgress.Cleared(Player);
        var body = "Chapters cleared: " + cleared + " / 3\n\nThis fight\nScore " + fightScore +
                   "\nDamage dealt " + Mathf.RoundToInt(fightDamage) +
                   (xp != null ? "\nHits landed " + xp.HitsLanded + "\nFull swings " + xp.FullSwings : "") +
                   (BattleStats.PeakSwingSpeed > 0f ? "\nFastest swing " + BattleStats.PeakSwingSpeed.ToString("0.0") + " m/s" : "");
        Debug.Log($"{StoryProgress.Tag} story complete for '{Player}' ({cleared}/3)");
        stage.ShowComplete(body, ReturnToMenu);
    }

    void ReturnToMenu()
    {
        StoryProgress.EndRun("story complete");
        MainMenuController.PageOnLoad = MainMenuController.StartPage.Home;
        stage.ShowLoading();
        SceneFlow.LevelSelect();
    }

    /// <summary>Defeat (or a victory the player leaves): back to the menu's Story Mode page; progress is kept.</summary>
    public void StoryMenu()
    {
        if (SceneFlow.Busy) return;
        StoryProgress.EndRun("story menu");
        MainMenuController.PageOnLoad = MainMenuController.StartPage.StoryMode;
        Debug.Log($"{StoryProgress.Tag} story menu from chapter {Chapter}");
        SceneFlow.LevelSelect();
    }
}
