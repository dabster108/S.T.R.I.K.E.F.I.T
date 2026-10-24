// ResultScreen.cs
// Purpose: The end-of-fight panel that makes you want another fight (Victory and Defeat). CombatUIController.ShowResultExtras calls
//          Show(); the panel is widened to most of the combat canvas and filled from EngagementTracker's result:
//            - title, level, medal (victory) and "A: play again";
//            - six tiles: score, best swing (peak m/s), full-power share of the counted swings, damage dealt, XP gained, best combo;
//            - the rank with its effort points: a bar fills from where this fight started to where it ended (a RANK UP fills it,
//              then starts the new rung), and how far the next rank is;
//            - one clear next goal ("Beat your best on Level 2: score 120 more points");
//            - today's three goals with progress (DONE / value of target), plus the player's own daily goal line if they set one;
//            - NEW BEST: a ribbon and a burst of sparks from HitBloodFx's pooled particle system (no new allocation) only when an
//              existing best on this level was beaten (a first fight is never a new best).
//          The old stat lines stay in the scene, hidden. Strings are written once per result; the animations only move rects.
//          Play Again's prominence is OutcomeMenuButtons'.
// Dependencies: EngagementTracker, CombatUIController, MenuUi, HitBloodFx, StrikeFit.Engagement
// Attach to: nothing (added to each result panel at runtime)

using StrikeFit.Engagement;
using UnityEngine;
using UnityEngine.UI;

public class ResultScreen : MonoBehaviour
{
    public static bool Enabled = true;

    const float W = 837f, H = 392f;   // the widened panel (canvas 900 x 500)
    const float BarW = 330f, BarH = 16f;
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.62f);
    static readonly Color TileBg = new Color(0f, 0f, 0f, 0.3f);

    RectTransform board;
    Image backing;
    Text title, subtitle, rankName, rankGain, rankNote, nextGoalLabel, nextGoal, todayLabel, playerGoal;
    readonly Text[] tileLabel = new Text[6], tileValue = new Text[6], tileSub = new Text[6];
    RectTransform barFill, barGain;
    readonly Text[] goalTitle = new Text[3], goalState = new Text[3];
    readonly RectTransform[] goalFill = new RectTransform[3];
    readonly Image[] goalFillImg = new Image[3];
    RectTransform ribbon;
    Text ribbonText;
    EngagementBanner resultBanner;

    EngagementTracker.FightResult r;
    CombatUIController ui;
    string shownGoalLine;
    float t;
    bool animating;
    float fromFrac, toFrac;
    bool crossedRung;
    bool sparked;

    public static bool Show(CombatUIController ui, GameObject panel, bool victory)
    {
        if (!Enabled || panel == null) return false;
        var tracker = EngagementTracker.Instance;
        if (tracker == null) return false;
        var result = tracker.Finish(victory ? "victory" : "defeat");
        if (result == null) return false;
        var rs = panel.GetComponent<ResultScreen>();
        if (rs == null) rs = panel.AddComponent<ResultScreen>();
        rs.ui = ui;
        tracker.HideHudExtras();
        HideHudNumbers(ui, panel.transform);
        rs.Build(victory);
        rs.Fill(result, victory);
        return true;
    }

    // The HUD's labels (HP, enemy, score, time, damage, XP, combo) sit around and behind the widened panel and read through its
    // edges; they are switched off until Play Again / Level Select loads a scene (the HUD is rebuilt with it).
    static void HideHudNumbers(CombatUIController ui, Transform panel)
    {
        if (ui == null) return;
        Graphic[] hud = { ui.playerHealthText, ui.monsterHealthText, ui.scoreText, ui.timerText, ui.damageCounterText, ui.xpText, ui.comboText,
                          ui.playerHealthTMP, ui.monsterHealthTMP, ui.scoreTMP };
        foreach (var g in hud) if (g != null && !g.transform.IsChildOf(panel)) g.enabled = false;
    }

    // ───────────────────────── layout (once per panel) ─────────────────────────
    void Build(bool victory)
    {
        var prt = (RectTransform)transform;
        prt.anchorMin = new Vector2(0.035f, 0.2f);
        prt.anchorMax = new Vector2(0.965f, 0.984f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        foreach (Transform c in transform)
            if (c.name != "PlayAgainRow" && c.name != "ResultFooter" && c.name != "ResultBoard") c.gameObject.SetActive(false);
        if (board != null) return;

        board = MenuUi.Rect(transform, "ResultBoard", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        board.offsetMin = board.offsetMax = Vector2.zero;
        board.SetAsFirstSibling();   // the footer and the Play Again row draw over it
        // an opaque backing: the HUD's own numbers sit behind the widened panel and must not show through
        backing = MenuUi.Panel(board, "Backing", Vector2.zero, new Vector2(W + 200f, H), Color.black);   // wider: the HP and XP labels sit past the panel's edges
        backing.raycastTarget = false;

        title = L("Title", 52, Color.white, TextAnchor.MiddleLeft, new Vector2(-W / 2f + 24f + 200f, H / 2f - 40f), new Vector2(400f, 62f), FontStyle.Bold);
        subtitle = L("Subtitle", 18, Dim, TextAnchor.MiddleLeft, new Vector2(-W / 2f + 24f + 200f, H / 2f - 82f), new Vector2(400f, 28f));

        // six tiles: two rows of three on the left half
        string[] labels = { "SCORE", "BEST SWING", "FULL POWER", "DAMAGE", "XP GAINED", "BEST COMBO" };
        for (int i = 0; i < 6; i++)
        {
            float x = -W / 2f + 24f + 64f + (i % 3) * 138f;
            float y = 20f - (i / 3) * 104f;
            var bg = MenuUi.Panel(board, "Tile" + i, new Vector2(x, y), new Vector2(128f, 94f), TileBg);
            bg.raycastTarget = false;
            tileLabel[i] = L("TileLabel" + i, 16, Dim, TextAnchor.MiddleCenter, new Vector2(x, y + 32f), new Vector2(126f, 22f), FontStyle.Bold);
            tileLabel[i].text = labels[i];
            tileValue[i] = L("TileValue" + i, 34, Color.white, TextAnchor.MiddleCenter, new Vector2(x, y + 2f), new Vector2(126f, 40f), FontStyle.Bold);
            tileSub[i] = L("TileSub" + i, 15, Dim, TextAnchor.MiddleCenter, new Vector2(x, y - 31f), new Vector2(126f, 20f));
        }

        // right half: rank, next goal, today's goals
        float rx = 205f;
        rankName = L("RankName", 32, new Color(1f, 0.88f, 0.5f), TextAnchor.MiddleLeft, new Vector2(rx - 30f, H / 2f - 42f), new Vector2(270f, 40f), FontStyle.Bold);
        rankGain = L("RankGain", 21, new Color(0.55f, 1f, 0.6f), TextAnchor.MiddleRight, new Vector2(rx + 100f, H / 2f - 42f), new Vector2(150f, 30f), FontStyle.Bold);
        var barBg = MenuUi.Panel(board, "RankBar", new Vector2(rx, H / 2f - 76f), new Vector2(BarW, BarH), new Color(1f, 1f, 1f, 0.14f));
        barBg.raycastTarget = false;
        barFill = Bar("RankFill", new Color(1f, 0.82f, 0.35f), new Vector2(rx, H / 2f - 76f));
        barGain = Bar("RankGainFill", new Color(0.55f, 1f, 0.6f), new Vector2(rx, H / 2f - 76f));
        rankNote = L("RankNote", 17, Dim, TextAnchor.MiddleLeft, new Vector2(rx, H / 2f - 100f), new Vector2(BarW, 24f));

        nextGoalLabel = L("NextGoalLabel", 16, new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleLeft, new Vector2(rx, H / 2f - 132f), new Vector2(BarW, 22f), FontStyle.Bold);
        nextGoalLabel.text = "NEXT GOAL";
        nextGoal = L("NextGoal", 21, Color.white, TextAnchor.UpperLeft, new Vector2(rx, H / 2f - 168f), new Vector2(BarW + 40f, 52f), FontStyle.Bold);

        todayLabel = L("TodayLabel", 16, new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleLeft, new Vector2(rx, H / 2f - 212f), new Vector2(BarW, 22f), FontStyle.Bold);
        for (int i = 0; i < 3; i++)
        {
            float y = H / 2f - 240f - i * 38f;
            goalTitle[i] = L("GoalTitle" + i, 19, Color.white, TextAnchor.MiddleLeft, new Vector2(rx - 40f, y), new Vector2(BarW - 80f, 24f));
            goalState[i] = L("GoalState" + i, 19, Color.white, TextAnchor.MiddleRight, new Vector2(rx + 125f, y), new Vector2(110f, 24f), FontStyle.Bold);
            var gbg = MenuUi.Panel(board, "GoalBar" + i, new Vector2(rx, y - 15f), new Vector2(BarW, 5f), new Color(1f, 1f, 1f, 0.12f));
            gbg.raycastTarget = false;
            goalFill[i] = Bar("GoalFill" + i, new Color(0.55f, 0.85f, 1f), new Vector2(rx, y - 15f), 5f);
            goalFillImg[i] = goalFill[i].GetComponent<Image>();
        }
        playerGoal = L("PlayerGoal", 15, Dim, TextAnchor.MiddleLeft, new Vector2(rx, H / 2f - 352f), new Vector2(BarW + 40f, 20f));

        // NEW BEST sticker on the score tile (hidden until a best is beaten)
        var rib = MenuUi.Panel(board, "NewBestRibbon", new Vector2(-W / 2f + 24f + 64f, 20f + 50f), new Vector2(126f, 30f), new Color(1f, 0.82f, 0.25f, 0.97f));
        rib.raycastTarget = false;
        ribbon = (RectTransform)rib.transform;
        ribbon.localRotation = Quaternion.Euler(0f, 0f, -6f);
        ribbonText = MenuUi.Label(ribbon, "Text", "NEW BEST", 20, new Color(0.15f, 0.08f, 0f), TextAnchor.MiddleCenter, Vector2.zero, new Vector2(126f, 30f), FontStyle.Bold);
        ribbonText.raycastTarget = false;
        ribbon.gameObject.SetActive(false);

        // NEW BEST / RANK UP announcements over the middle of the panel, then gone
        resultBanner = EngagementBanner.Create(board, "ResultBanner", new Vector2(0f, -6f), new Vector2(560f, 84f));
    }

    Text L(string name, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box, FontStyle style = FontStyle.Normal)
    {
        var t = MenuUi.Label(board, name, "", size, color, align, pos, box, style);
        t.raycastTarget = false;
        return t;
    }

    // A left-anchored bar: its width is set by SetBar.
    RectTransform Bar(string name, Color color, Vector2 centre, float h = BarH)
    {
        var img = MenuUi.Panel(board, name, new Vector2(centre.x - BarW / 2f, centre.y), new Vector2(0f, h), color);
        img.raycastTarget = false;
        var rt = (RectTransform)img.transform;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(centre.x - BarW / 2f, centre.y);
        return rt;
    }

    static void SetBar(RectTransform bar, float frac, float h = BarH)
    {
        bar.sizeDelta = new Vector2(BarW * Mathf.Clamp01(frac), h);
    }

    // ───────────────────────── content (once per result) ─────────────────────────
    void Fill(EngagementTracker.FightResult result, bool victory)
    {
        r = result;
        backing.color = victory ? new Color(0.03f, 0.11f, 0.06f, 0.96f) : new Color(0.12f, 0.035f, 0.035f, 0.96f);
        title.text = victory ? "VICTORY" : "DEFEAT";
        title.color = victory ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.45f, 0.4f);
        string medal = "";
        string extras = ui != null ? ui.LastResultExtras : null;
        if (victory && extras != null)
            medal = extras.Contains("GOLD MEDAL") ? "  ·  GOLD MEDAL" : extras.Contains("SILVER MEDAL") ? "  ·  SILVER MEDAL" : extras.Contains("BRONZE MEDAL") ? "  ·  BRONZE MEDAL" : "";
        subtitle.text = r.levelName + medal + "  ·  A: play again";

        tileLabel[0].text = r.bestKind == BestScore.Kind.NewBest ? "" : "SCORE";   // the NEW BEST sticker takes the label's place
        tileValue[0].text = r.score.ToString();
        tileSub[0].text = r.bestKind == BestScore.Kind.NewBest ? "was " + r.previousBest : r.previousBest > 0 ? "best " + Mathf.Max(r.previousBest, r.bestAfter) : r.counted ? "first score" : "no score";
        tileValue[1].text = r.peakSwing > 0f ? r.peakSwing.ToString("0.0") : "—";
        tileSub[1].text = "m/s";
        tileValue[2].text = r.countedSwings > 0 ? Mathf.RoundToInt(r.fullShare * 100f) + "%" : "—";
        tileSub[2].text = r.fullSwings + " of " + r.countedSwings + " hits";
        tileValue[3].text = r.damageDealt.ToString();
        tileSub[3].text = "dealt";
        tileValue[4].text = "+" + r.xp;
        tileSub[4].text = r.goalBonusXp + r.perfectClashXp > 0 ? "incl. +" + (r.goalBonusXp + r.perfectClashXp) + " bonus" : "this fight";
        tileValue[5].text = r.bestCombo > 1 ? "x" + r.bestCombo : "—";
        tileSub[5].text = r.perfectClashes > 0 ? r.perfectClashes + " perfect clash" + (r.perfectClashes > 1 ? "es" : "") : "hits in a row";

        var after = RankLadder.Rungs[r.rankAfter];
        rankName.text = after.name.ToUpperInvariant();
        rankGain.text = "+" + r.effortGained + " effort";
        int toNext = RankLadder.PointsToNext(r.effortAfter);
        rankNote.text = toNext > 0 ? r.effortAfter + " effort  ·  " + toNext + " to " + RankLadder.NextRung(r.effortAfter).name : r.effortAfter + " effort  ·  top rank";
        crossedRung = r.rankAfter > r.rankBefore;
        fromFrac = crossedRung ? RankLadder.Progress(r.effortBefore) : RankLadder.Progress(r.effortBefore);
        toFrac = RankLadder.Progress(r.effortAfter);
        SetBar(barFill, fromFrac); SetBar(barGain, fromFrac);

        nextGoal.text = r.nextGoal;

        todayLabel.text = r.goalsFromServer ? "TODAY'S GOALS" : "TODAY'S GOALS (on this headset)";
        for (int i = 0; i < 3; i++)
        {
            bool has = r.goals != null && i < r.goals.Length;
            goalTitle[i].gameObject.SetActive(has); goalState[i].gameObject.SetActive(has); goalFill[i].parent.Find("GoalBar" + i)?.gameObject.SetActive(has);
            goalFill[i].gameObject.SetActive(has);
            if (!has) continue;
            var g = r.goals[i];
            bool done = r.goalDone[i];
            goalTitle[i].text = g.title;
            goalTitle[i].color = done ? new Color(0.55f, 1f, 0.6f) : Color.white;
            goalState[i].text = done ? (r.goalDoneThisFight[i] ? "DONE +" + g.bonusXp : "DONE") : Fmt(r.goalValues[i], g.metric) + " / " + Fmt(g.target, g.metric);
            goalState[i].color = done ? new Color(0.55f, 1f, 0.6f) : Dim;
            goalFillImg[i].color = done ? new Color(0.55f, 1f, 0.6f) : new Color(0.55f, 0.85f, 1f);
            SetBar(goalFill[i], DailyGoalRules.Fraction(g, r.goalValues[i]), 5f);
        }
        shownGoalLine = null;
        playerGoal.text = "";

        ribbon.gameObject.SetActive(r.bestKind == BestScore.Kind.NewBest);
        ribbon.localScale = Vector3.zero;
        t = 0f; animating = true; sparked = false;
        var cfg = EngagementConfig.Get();
        if (r.bestKind == BestScore.Kind.NewBest) resultBanner.Show("NEW BEST  ·  " + r.levelName + "  ·  " + r.score, cfg.newBestColor, 2.6f);
        if (r.rankUp) resultBanner.Show("RANK UP  ·  " + after.name.ToUpperInvariant(), cfg.rankUpColor, 3f);
        if (r.goals != null && r.goalDoneAtEnd != null)
            for (int i = 0; i < r.goals.Length && i < r.goalDoneAtEnd.Length; i++)
                if (r.goalDoneAtEnd[i])
                    resultBanner.Show("GOAL COMPLETE  ·  " + r.goals[i].title + "  ·  +" + r.goals[i].bonusXp + " XP" + (r.goals[i].fromServer ? " on your profile" : ""), cfg.goalColor, 2.2f);
    }

    static string Fmt(float v, GoalMetric m) => m == GoalMetric.Kcal ? v.ToString("0.0") : Mathf.RoundToInt(v).ToString();

    void Update()
    {
        // the player's own daily goal line arrives from the backend a few seconds later (CombatUIController's goal routine)
        if (ui != null && !ReferenceEquals(ui.GoalLine, shownGoalLine))
        {
            shownGoalLine = ui.GoalLine;
            playerGoal.text = string.IsNullOrEmpty(shownGoalLine) ? "" : "Your goal: " + shownGoalLine.Replace("Goal: ", "").Trim();
        }
        if (!animating) return;
        t += Time.unscaledDeltaTime;
        // rank bar: 0.3 s pause, then 1.1 s fill (a rank up fills to the end, then the new rung from 0)
        float k = Mathf.Clamp01((t - 0.3f) / 1.1f);
        k = 1f - (1f - k) * (1f - k);
        if (crossedRung)
        {
            if (k < 0.55f) SetBar(barGain, Mathf.Lerp(fromFrac, 1f, k / 0.55f));
            else { SetBar(barFill, 0f); SetBar(barGain, Mathf.Lerp(0f, toFrac, (k - 0.55f) / 0.45f)); }
        }
        else SetBar(barGain, Mathf.Lerp(fromFrac, toFrac, k));

        // NEW BEST: the ribbon pops in at 0.4 s with one burst of sparks
        if (ribbon.gameObject.activeSelf)
        {
            float p = Mathf.Clamp01((t - 0.4f) / 0.25f);
            float s = p < 1f ? Mathf.Lerp(0f, 1.15f, p) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.65f) / 0.15f));
            ribbon.localScale = new Vector3(s, s, 1f);
            if (!sparked && t >= 0.4f)
            {
                sparked = true;
                var fx = FindAnyObjectByType<HitBloodFx>();
                var cfg = EngagementConfig.Get();
                if (fx != null) fx.Celebrate(ribbon.position - ribbon.forward * 0.12f, -ribbon.forward + Vector3.up * 0.6f, cfg.celebrationSparks, cfg.newBestColor, Color.white);
            }
        }
        if (t > 1.8f) animating = false;
    }
}
