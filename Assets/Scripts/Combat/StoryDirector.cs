// StoryDirector.cs
// Purpose: Story Mode inside a level. Created at runtime by BattleManager (like the name picker) when a story run is active and
//          this scene is the run's current chapter, so no level scene needs a copy. Before the fight: the chapter card (StoryStage),
//          then the usual name picker / goal / Start flow. After a Victory: the chapter is saved (StoryProgress) and the end panel
//          offers "Continue story": the line between fights, then the next chapter loads (async, "Loading..."); after chapter 3
//          the Victory card (the ending lines) and the "Story complete" card, then the menu. After a Defeat: the Defeat card
//          (Retry / Story menu; Skip shows the usual defeat panel), nothing saved. Every change of card, and every scene change
//          started here, goes through a fade to black (ScreenFade; the next scene starts black and fades in). Free play (no run)
//          never creates this component. The pause menu stays closed while a card is up or a fade runs.
//          Telemetry: SessionReporter.SessionMode = "story" for the session start. One [StrikeFitStory] log line per event.
// Dependencies: StoryProgress, StoryContent, StoryStage, StoryAudio, ScreenFade, BattleManager, CombatUIController,
//               PlayerNamePicker (hold), SceneFlow, SessionReporter, MainMenuController (page to open), XPManager / BattleStats
// Attach to: created at runtime on the BattleSystems object

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StoryDirector : MonoBehaviour
{
    public static StoryDirector Instance { get; private set; }

    /// <summary>A card is open or a story fade runs: the pause menu must not open.</summary>
    public static bool BlocksPause => Instance != null && Instance.stage != null && (Instance.stage.IsOpen || ScreenFade.Busy);

    [Tooltip("Seconds after the defeat panel before the Defeat card replaces it.")]
    public float defeatCardDelay = 1.5f;

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
        stage.lineSeconds = StoryContent.Load().prologue.lineSeconds;
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
        // The scene opens on the card (it is black and fading in when a story load brought us here).
        if (chapterCard) stage.ShowChapterCard(Chapter, Begin, false);
    }

    void Begin()
    {
        Debug.Log($"{StoryProgress.Tag} chapter {Chapter} card: Begin");
        ScreenFade.Transition(() => { stage.Hide(); PlayerNamePicker.ReleaseHold(); });
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
        StartCoroutine(DefeatCard());
    }

    IEnumerator DefeatCard()
    {
        float until = Time.unscaledTime + defeatCardDelay;
        while (Time.unscaledTime < until) yield return null;
        if (SceneFlow.Busy || stage.IsOpen) yield break;   // the player already chose Retry / Story menu on the panel
        var story = StoryContent.Load();
        var a = StoryAudio.Get();
        ScreenFade.Transition(() =>
        {
            if (CombatUIController.Instance != null) CombatUIController.Instance.HideOutcomePanels();
            stage.ShowLines(story.cards.defeat, new[] { story.interludes.defeat }, "Retry", Retry, "Story menu", StoryMenu, DismissDefeat,
                            a != null ? a.defeat : null, false);
        });
    }

    /// <summary>Skip on the Defeat card: back to the usual defeat panel (stats, Retry / Story menu / Exit).</summary>
    void DismissDefeat()
    {
        ScreenFade.Transition(() =>
        {
            stage.Hide();
            var ui = CombatUIController.Instance;
            if (ui != null && ui.defeatPanel != null) ui.defeatPanel.SetActive(true);
        });
    }

    /// <summary>Retry this chapter (the card is shown again).</summary>
    public void Retry()
    {
        if (SceneFlow.Busy) return;
        Debug.Log($"{StoryProgress.Tag} retry chapter {Chapter}");
        stage.ShowLoading();
        LoadThroughBlack(() => SceneFlow.PlayAgain());
    }

    /// <summary>Fade to black, then start a load that opens the next scene black (it fades in by itself). A refused load fades
    /// back in.</summary>
    static void LoadThroughBlack(System.Func<bool> load)
    {
        ScreenFade.Out(() =>
        {
            ScreenFade.CarryBlack = true;
            if (!load()) { ScreenFade.CarryBlack = false; ScreenFade.In(); Debug.LogWarning($"{StoryProgress.Tag} load refused"); }
        });
    }

    // ───────────────────────── end-panel buttons (OutcomeMenuButtons) ─────────────────────────

    /// <summary>The prompt line above the end-panel buttons in a story run.</summary>
    public string PromptFor(bool victory)
    {
        var story = StoryContent.Load();
        if (!victory) return story.interludes.defeat;
        return Chapter >= StoryProgress.Chapters ? story.cards.victory : story.cards.Interlude(Chapter);
    }

    /// <summary>Victory: interlude then the next chapter, or the ending and the complete panel after chapter 3.</summary>
    public void ContinueStory()
    {
        if (SceneFlow.Busy || stage.IsOpen || ScreenFade.Busy) return;
        var story = StoryContent.Load();
        var it = story.interludes;
        var a = StoryAudio.Get();
        Debug.Log($"{StoryProgress.Tag} continue after chapter {Chapter}");
        // The end panel goes away while the card is up (its buttons would sit above the card, still clickable).
        ScreenFade.Transition(() =>
        {
            if (CombatUIController.Instance != null) CombatUIController.Instance.HideOutcomePanels();
            if (Chapter >= StoryProgress.Chapters)
                stage.ShowLines(story.cards.victory, it.ending, "Continue", ShowComplete, null, null, null, a != null ? a.victory : null, false);
            else
                stage.ShowLines(story.cards.Interlude(Chapter), new[] { Chapter == 1 ? it.afterLevel1 : it.afterLevel2 }, "Continue",
                                LoadNextChapter, null, null, null, a != null ? a.interlude : null, false);
        });
    }

    void LoadNextChapter()
    {
        string next = Chapter == 1 ? SceneFlow.Level2 : SceneFlow.Level3;
        stage.ShowLoading();
        Debug.Log($"{StoryProgress.Tag} loading chapter {Chapter + 1} ({next})");
        LoadThroughBlack(() => SceneFlow.Load(next));
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
        stage.ShowComplete(StoryContent.Load().cards.complete, body, ReturnToMenu);
    }

    void ReturnToMenu()
    {
        StoryProgress.EndRun("story complete");
        MainMenuController.PageOnLoad = MainMenuController.StartPage.Home;
        stage.ShowLoading();
        LoadThroughBlack(SceneFlow.LevelSelect);
    }

    /// <summary>Defeat (or a victory the player leaves): back to the menu's Story Mode page; progress is kept.</summary>
    public void StoryMenu()
    {
        if (SceneFlow.Busy) return;
        StoryProgress.EndRun("story menu");
        MainMenuController.PageOnLoad = MainMenuController.StartPage.StoryMode;
        Debug.Log($"{StoryProgress.Tag} story menu from chapter {Chapter}");
        if (stage.IsOpen) stage.ShowLoading();
        LoadThroughBlack(SceneFlow.LevelSelect);
    }
}
