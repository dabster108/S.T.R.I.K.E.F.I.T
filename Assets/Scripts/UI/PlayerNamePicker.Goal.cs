// PlayerNamePicker.Goal.cs
// Purpose: The daily goal step of the shared pre-battle flow (every level; no per-scene copy: it is built from the same Start
//          button template as the name list). After a name is chosen and before Start:
//            - a goal for today exists  -> the goal with its progress, "Change" and "Continue";
//            - none yet                 -> four metric chips (calories est. / full swings / active minutes / XP), three presets
//                                          (Easy / Standard / Hard from the backend's suggestions, fixed fallbacks offline), a
//                                          minus / plus stepper, "Save goal" and "Skip today", and the one-line safety note.
//          The panel opens at once with whatever is cached; the fetch (5 s timeout, background) only refreshes it. Saving is a
//          background PUT; if it fails the goal stays pending in PlayerPrefs. Skipping hides the step until the next local day
//          (a small "Daily goal" button next to "Change name" reopens it). A serialized toggle turns the whole step off.
// Dependencies: DailyGoal, SessionReporter (fetch / save), the name picker's panel helpers.

using UnityEngine;
using UnityEngine.UI;

public partial class PlayerNamePicker
{
    [Header("Daily goal step")]
    [Tooltip("Off = no goal panel and no goal HUD line from the headset's side (the backend feature stays).")]
    public bool dailyGoalStep = true;

    const string SafetyLine = "Stop if you feel dizzy or in pain.";
    static readonly Color ChipOn = new Color(0.13f, 0.45f, 0.3f, 0.98f);

    bool goalStepOpen;
    bool goalEditing;
    int goalMetric;           // index into DailyGoal.Metrics
    float goalTarget;
    string goalPreset;        // "Easy" / "Standard" / "Hard" / null
    string goalPlayer;
    GameObject goalButton;
    Text goalInfoText;

    /// <summary>True when the goal panel took over from the name panel (the Start button stays hidden until it closes).</summary>
    bool BeginGoalStep(string player)
    {
        if (!dailyGoalStep || startButton == null) return false;
        goalPlayer = player;
        BuildGoalButton();
        DailyGoal.Changed -= OnGoalChanged;
        DailyGoal.Changed += OnGoalChanged;
        DailyGoal.RetryPending();
        // Cached state for this player is used at once; a fetch refreshes it in the background.
        if (DailyGoal.Player != player) { /* state belongs to another name: show the empty panel until the answer */ }
        DailyGoal.Fetch(player);
        if (DailyGoal.SkippedToday && !DailyGoal.HasGoal) return false;   // skipped today: straight to Start
        goalStepOpen = true;
        goalEditing = !(DailyGoal.HasGoal && DailyGoal.Player == player);
        if (goalEditing) SeedEditor();
        ShowGoalPanel();
        if (changeButton != null) changeButton.SetActive(false);
        return true;
    }

    void OnDestroyGoal() { DailyGoal.Changed -= OnGoalChanged; }

    void OnGoalChanged()
    {
        if (!goalStepOpen || panel == null) return;
        if (goalEditing && goalInfoText != null) { goalInfoText.text = EditorInfo(); return; }   // presets may have arrived
        if (!goalEditing) ShowGoalPanel();
    }

    void SeedEditor()
    {
        goalMetric = DailyGoal.HasGoal && DailyGoal.MetricIndex(DailyGoal.Metric) >= 0 ? DailyGoal.MetricIndex(DailyGoal.Metric) : 0;
        goalTarget = DailyGoal.HasGoal ? DailyGoal.Target : DailyGoal.Preset(goalMetric, 1);
        goalPreset = DailyGoal.HasGoal ? null : "Standard";
    }

    void ShowGoalPanel()
    {
        var rt = NewPanel();
        MakeLabel(rt, "Title", "DAILY GOAL", 46, new Vector2(0f, 205f), new Vector2(860f, 60f));
        goalInfoText = null;
        if (!goalEditing && DailyGoal.HasGoal) BuildGoalSummary(rt);
        else BuildGoalEditor(rt);
        MakeLabel(rt, "Safety", SafetyLine, 22, new Vector2(0f, -218f), new Vector2(860f, 36f));
    }

    void BuildGoalSummary(RectTransform rt)
    {
        int i = DailyGoal.MetricIndex(DailyGoal.Metric);
        string unit = i >= 0 ? DailyGoal.Units[i] : "";
        float value = DailyGoal.Baseline;
        int pct = DailyGoal.Target > 0f ? Mathf.RoundToInt(Mathf.Min(100f, value / DailyGoal.Target * 100f)) : 0;
        MakeLabel(rt, "Goal", $"{(i >= 0 ? DailyGoal.Labels[i] : "")}: {DailyGoal.Format(i, DailyGoal.Target)} {unit}", 38, new Vector2(0f, 130f), new Vector2(860f, 56f));
        string progress = DailyGoal.Completed
            ? $"Reached today! +{DailyGoal.BonusXp} XP"
            : $"Today so far: {DailyGoal.Format(i, value)} / {DailyGoal.Format(i, DailyGoal.Target)} {unit}  ({pct}%)\n+{DailyGoal.BonusOnCompletion} XP when you reach it";
        MakeLabel(rt, "Progress", progress, 28, new Vector2(0f, 50f), new Vector2(860f, 90f));
        string note = DailyGoal.Offline ? "Server not reachable - showing what this headset knows" : (DailyGoal.HasPending ? "Waiting to save your goal..." : "");
        if (note.Length > 0) MakeLabel(rt, "Note", note, 22, new Vector2(0f, -30f), new Vector2(860f, 36f));
        if (!DailyGoal.Completed)
            MakeButton(rt, "GoalChange", "Change", new Vector2(-170f, -120f), new Vector2(300f, 76f), ChangeColor, 32, () => { goalEditing = true; SeedEditor(); ShowGoalPanel(); });
        MakeButton(rt, "GoalContinue", "Continue", new Vector2(DailyGoal.Completed ? 0f : 170f, -120f), new Vector2(300f, 76f), ActionColor, 34, FinishGoalStep);
    }

    void BuildGoalEditor(RectTransform rt)
    {
        // Metric chips.
        for (int m = 0; m < DailyGoal.Metrics.Length; m++)
        {
            int index = m;
            float x = -345f + m * 230f;
            string label = DailyGoal.Labels[m];
            var chip = MakeButton(rt, "Metric_" + DailyGoal.Metrics[m], label, new Vector2(x, 140f), new Vector2(214f, 74f),
                m == goalMetric ? ChipOn : NameColor, 26, () => { goalMetric = index; goalTarget = DailyGoal.Preset(index, 1); goalPreset = "Standard"; ShowGoalPanel(); });
            FitText(chip, 16, 26);
        }

        // Stepper.
        MakeButton(rt, "TargetMinus", "-", new Vector2(-200f, 50f), new Vector2(110f, 84f), ChangeColor, 52, () => StepTarget(-1));
        MakeButton(rt, "TargetPlus", "+", new Vector2(200f, 50f), new Vector2(110f, 84f), ChangeColor, 52, () => StepTarget(1));
        goalInfoText = MakeLabel(rt, "Target", EditorInfo(), 36, new Vector2(0f, 50f), new Vector2(280f, 90f));
        goalInfoText.resizeTextForBestFit = true; goalInfoText.resizeTextMinSize = 20; goalInfoText.resizeTextMaxSize = 36;

        // Presets.
        string[] names = { "Easy", "Standard", "Hard" };
        for (int p = 0; p < 3; p++)
        {
            int level = p;
            string preset = names[p];
            float v = DailyGoal.Preset(goalMetric, p);
            var b = MakeButton(rt, "Preset_" + preset, $"{preset}\n{DailyGoal.Format(goalMetric, v)}", new Vector2(-230f + p * 230f, -55f), new Vector2(210f, 84f),
                goalPreset == preset ? ChipOn : NameColor, 26, () => { goalTarget = DailyGoal.Preset(goalMetric, level); goalPreset = names[level]; ShowGoalPanel(); });
            FitText(b, 16, 26);
        }

        MakeButton(rt, "GoalSave", "Save goal", new Vector2(-150f, -140f), new Vector2(280f, 70f), ActionColor, 32, SaveGoal);
        MakeButton(rt, "GoalSkip", "Skip today", new Vector2(150f, -140f), new Vector2(280f, 70f), ChangeColor, 30, () =>
        {
            DailyGoal.SkipToday();
            FinishGoalStep();
        });
    }

    string EditorInfo()
    {
        return $"{DailyGoal.Format(goalMetric, goalTarget)} {DailyGoal.Units[goalMetric]}";
    }

    void StepTarget(int dir)
    {
        float step = DailyGoal.Steps[goalMetric];
        goalTarget = Mathf.Clamp(goalTarget + dir * step, DailyGoal.Min[goalMetric], DailyGoal.Max[goalMetric]);
        goalPreset = null;
        if (goalInfoText != null) goalInfoText.text = EditorInfo();
        // The preset highlight follows the value: rebuild only when it changed.
        ShowGoalPanel();
    }

    void SaveGoal()
    {
        string metric = DailyGoal.Metrics[goalMetric];
        DailyGoal.SetGoal(goalPlayer, metric, goalTarget);
        if (SessionReporter.Instance != null) SessionReporter.Instance.ReportGoalSet(metric, goalTarget, goalPreset != null, goalPreset ?? "custom");
        goalEditing = false;
        FinishGoalStep();
    }

    void FinishGoalStep()
    {
        goalStepOpen = false;
        goalEditing = false;
        ClosePanel();
        ShowStartButton(goalPlayer);
    }

    // A small button next to "Change name": reopens the goal panel (after "Skip today", or to change the goal).
    void BuildGoalButton()
    {
        if (goalButton != null || startButton == null) return;
        var canvas = startButton.transform.parent as RectTransform;
        var startRt = (RectTransform)startButton.transform;
        var pos = startRt.anchoredPosition + new Vector2(0f, -startRt.sizeDelta.y * 0.5f - 92f);
        goalButton = MakeButton(canvas, "DailyGoalButton", "Daily goal", pos, new Vector2(220f, 50f), ChangeColor, 22, () =>
        {
            goalStepOpen = true;
            goalEditing = !DailyGoal.HasGoal;
            if (goalEditing) SeedEditor();
            startButton.SetActive(false);
            if (changeButton != null) changeButton.SetActive(false);
            goalButton.SetActive(false);
            DailyGoal.Fetch(goalPlayer);
            ShowGoalPanel();
        });
        goalButton.SetActive(false);
    }

    void ShowGoalButton()
    {
        if (dailyGoalStep && goalButton != null) goalButton.SetActive(true);
    }

    void HideGoalButton()
    {
        if (goalButton != null) goalButton.SetActive(false);
    }
}
