// StoryModePanel.cs
// Purpose: The main menu's Story Mode page. Continue ("Chapter N: <guardian>", shown when 1 or 2 chapters are cleared), New Story
//          (asks first when there is progress to lose), Watch intro (the story slides, then back here), Back. A new story plays the
//          intro, then loads Chapter 1; Continue loads the next chapter. Both start a story run (StoryProgress), which the level
//          scenes read. Progress is per player name (the menu's "Playing as" name). A chapter load fades to black first and the
//          chapter scene opens black and fades in (ScreenFade). One [StrikeFitStory] log line per action.
// Dependencies: StoryProgress, StoryContent, StoryPanel, MainMenuController, SceneFlow, ScreenFade, UiBack, MenuLog
// Attach to: MenuSystems in MainMenu.unity (built by Editor/MenuSceneBuilder)

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StoryModePanel : MonoBehaviour
{
    [Header("Wiring (built by MenuSceneBuilder)")]
    public MainMenuController menu;
    public StoryPanel intro;
    public GameObject page;
    public Button openButton, continueButton, newStoryButton, watchIntroButton, backButton;
    public Text continueLabel, statusText;
    public GameObject confirm;
    public Button confirmYes, confirmNo;
    public CanvasGroup buttonsGroup;

    public string loadingText = "Loading...";
    public bool Loading { get; private set; }
    public bool ConfirmOpen => confirm != null && confirm.activeSelf;

    void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (continueButton != null) continueButton.onClick.AddListener(Continue);
        if (newStoryButton != null) newStoryButton.onClick.AddListener(NewStory);
        if (watchIntroButton != null) watchIntroButton.onClick.AddListener(WatchIntro);
        if (backButton != null) backButton.onClick.AddListener(() => { if (!Loading) menu.Back("story mode"); });
        if (confirmYes != null) confirmYes.onClick.AddListener(() => { CloseConfirm(); BeginNewStory(); });
        if (confirmNo != null) confirmNo.onClick.AddListener(CloseConfirm);
        if (confirm != null) confirm.SetActive(false);
    }

    void OnDestroy() { UiBack.Remove(ConfirmBack); }

    public void Open()
    {
        if (menu == null || menu.Blocked) return;
        menu.ShowPage(page, "story mode");
        Refresh();
    }

    public void Refresh()
    {
        string player = StoryProgress.CurrentPlayer;
        int cleared = StoryProgress.Cleared(player);
        int next = StoryProgress.NextChapter(player);
        bool canContinue = cleared >= 1 && next >= 1;
        if (continueButton != null) continueButton.gameObject.SetActive(canContinue);
        if (canContinue && continueLabel != null)
        {
            var g = StoryContent.GuardianFor(next);
            continueLabel.text = "Continue\nChapter " + next + ": " + (g != null ? g.name : "");
        }
        if (statusText != null)
            statusText.text = cleared >= StoryProgress.Chapters
                ? "Story complete for " + player + ". New Story starts again."
                : "Playing as " + player + "   Chapters cleared: " + cleared + " / 3";
        if (buttonsGroup != null) buttonsGroup.interactable = !Loading;
    }

    void Continue()
    {
        if (Loading || menu.Blocked || ConfirmOpen) return;
        string player = StoryProgress.CurrentPlayer;
        int next = StoryProgress.NextChapter(player);
        if (next < 1) return;
        MenuLog.Event("story mode: continue at chapter " + next);
        StoryProgress.BeginRun(player);
        LoadChapter(next);
    }

    void NewStory()
    {
        if (Loading || menu.Blocked || ConfirmOpen) return;
        if (StoryProgress.Cleared(StoryProgress.CurrentPlayer) > 0 && confirm != null)
        {
            confirm.SetActive(true);
            if (buttonsGroup != null) buttonsGroup.interactable = false;
            UiBack.Push(ConfirmBack);
            MenuLog.Event("story mode: new story (confirm open)");
            return;
        }
        BeginNewStory();
    }

    bool ConfirmBack()
    {
        if (!ConfirmOpen) return false;
        CloseConfirm();
        return true;
    }

    void CloseConfirm()
    {
        if (confirm != null) confirm.SetActive(false);
        if (buttonsGroup != null) buttonsGroup.interactable = !Loading;
        UiBack.Remove(ConfirmBack);
    }

    void BeginNewStory()
    {
        string player = StoryProgress.CurrentPlayer;
        StoryProgress.Reset(player);
        StoryProgress.BeginRun(player);
        MenuLog.Event("story mode: new story");
        // The intro first (Skip works as always), then Chapter 1.
        if (intro != null && intro.storyEnabled) intro.Play("new story", () => LoadChapter(1));
        else LoadChapter(1);
    }

    void WatchIntro()
    {
        if (Loading || menu.Blocked || ConfirmOpen || intro == null) return;
        MenuLog.Event("story mode: watch intro");
        intro.Play("watch intro", Open);
    }

    void LoadChapter(int chapter)
    {
        Open();
        Loading = true;
        Refresh();
        if (continueButton != null && continueButton.gameObject.activeSelf && continueLabel != null) continueLabel.text = loadingText;
        if (statusText != null) statusText.text = "Chapter " + chapter + ". " + loadingText;
        ScreenFade.CarryBlack = true;   // the chapter scene opens black and fades in
        if (ScreenFade.Alpha >= 0.99f) StartCoroutine(Load(chapter));   // already black (the prologue's Begin)
        else ScreenFade.Out(() => StartCoroutine(Load(chapter)));
    }

    IEnumerator Load(int chapter)
    {
        yield return null;   // the "Loading..." label draws before the load starts
        string scene = chapter == 1 ? SceneFlow.Level1 : chapter == 2 ? SceneFlow.Level2 : SceneFlow.Level3;
        Debug.Log($"{StoryProgress.Tag} menu loads chapter {chapter} ({scene})");
        if (!SceneFlow.Load(scene))
        {
            Loading = false;
            ScreenFade.CarryBlack = false;
            ScreenFade.In();
            StoryProgress.EndRun("load refused");
            Refresh();
        }
    }
}
