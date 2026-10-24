// MenuProgressPanel.cs
// Purpose: The Main Menu's "Your progress" panel, to the right of the menu (its own world-space canvas, turned to face the player,
//          laser-clickable like the pause menu): the current player's rank, effort points and how far the next rank is; today's
//          three goals with progress (the website's daily challenges when the headset can reach it, the seeded local goals
//          otherwise); and the cosmetic weapon glow selector (palettes unlock by rank; a locked one shows what unlocks it and is
//          not worn). Refreshes when the player changes (MainMenuController.PlayingAsChanged). Built once; text is written only on a
//          refresh or a click, nothing per frame.
// Dependencies: MainMenuController, PlayerProgressStore, SessionReporter, GlowCosmetics, MenuUi, StrikeFit.Engagement
// Attach to: nothing (adds itself in the Main Menu scene)

using StrikeFit.Engagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuProgressPanel : MonoBehaviour
{
    public static MenuProgressPanel Instance { get; private set; }

    const float PW = 600f, PH = 660f, Scale = 0.0016f;
    static readonly Color Gold = new Color(1f, 0.82f, 0.45f), Dim = new Color(1f, 1f, 1f, 0.62f), Blue = new Color(0.55f, 0.85f, 1f);

    Canvas canvas;
    Text playerLabel, rankLabel, effortLabel, rankNote, goalsHeader, glowName, glowStatus;
    readonly Text[] goalTitle = new Text[3], goalState = new Text[3];
    readonly RectTransform[] goalFill = new RectTransform[3];
    readonly Image[] goalFillImg = new Image[3];
    RectTransform rankFill;
    Image swatch;
    string player = "";
    ProgressData data;
    GoalDef[] goals = new GoalDef[0];
    float[] goalValues = new float[0];
    bool goalsFromServer;
    int viewPalette;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        var menu = FindAnyObjectByType<MainMenuController>();
        if (menu != null && menu.GetComponent<MenuProgressPanel>() == null) menu.gameObject.AddComponent<MenuProgressPanel>();
    }

    void Awake() { Instance = this; }
    void OnEnable() { MainMenuController.PlayingAsChanged += Refresh; }
    void OnDisable() { MainMenuController.PlayingAsChanged -= Refresh; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        Build();
        Refresh(PlayerNameStore.LastUsed);
    }

    public Canvas Canvas => canvas;
    public string Player => player;

    // ───────────────────────── panel ─────────────────────────
    void Build()
    {
        var menu = GetComponent<MainMenuController>();
        var menuCanvas = menu != null && menu.homePage != null ? menu.homePage.GetComponentInParent<Canvas>(true) : null;
        var rig = FindAnyObjectByType<OVRCameraRig>();
        Vector3 head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.position : new Vector3(0f, 1.6f, 0f);
        Vector3 centre = menuCanvas != null ? menuCanvas.transform.position : new Vector3(0f, 1.42f, 2f);
        // beside the menu, ~38 degrees to the right of straight ahead, at about the menu's distance, turned to face the player
        Vector3 fwd = centre - head; fwd.y = 0f; float dist = Mathf.Max(1.5f, fwd.magnitude * 0.93f); fwd.Normalize();
        Vector3 dir = Quaternion.Euler(0f, 38f, 0f) * fwd;
        Vector3 pos = new Vector3(head.x, centre.y, head.z) + dir * dist;

        var root = new GameObject("ProgressCanvas", typeof(RectTransform));
        root.layer = menuCanvas != null ? menuCanvas.gameObject.layer : 5;
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
        canvas.sortingOrder = menuCanvas != null ? menuCanvas.sortingOrder : 0;
        var rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(PW, PH);
        rt.localScale = Vector3.one * Scale;
        rt.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
        var rc = root.AddComponent<OVRRaycaster>();
        var module = FindAnyObjectByType<UnityEngine.EventSystems.OVRInputModule>();
        rc.pointer = module != null && module.rayTransform != null ? module.rayTransform.gameObject : null;

        var border = MenuUi.Panel(root.transform, "Border", Vector2.zero, new Vector2(PW, PH), new Color(0.78f, 0.47f, 0.18f, 0.95f));
        border.raycastTarget = false;
        var bg = MenuUi.Panel(root.transform, "Backing", Vector2.zero, new Vector2(PW - 6f, PH - 6f), new Color(0.04f, 0.05f, 0.09f, 0.95f));
        bg.raycastTarget = true;
        var p = bg.transform;

        L(p, "Header", "YOUR PROGRESS", 26, Gold, TextAnchor.MiddleCenter, new Vector2(0f, 292f), new Vector2(560f, 34f), FontStyle.Bold);
        playerLabel = L(p, "Player", "", 22, Dim, TextAnchor.MiddleCenter, new Vector2(0f, 258f), new Vector2(560f, 30f));
        rankLabel = L(p, "Rank", "", 42, Gold, TextAnchor.MiddleLeft, new Vector2(-90f, 212f), new Vector2(360f, 50f), FontStyle.Bold);
        effortLabel = L(p, "Effort", "", 22, Color.white, TextAnchor.MiddleRight, new Vector2(150f, 210f), new Vector2(220f, 30f), FontStyle.Bold);
        var barBg = MenuUi.Panel(p, "RankBar", new Vector2(0f, 172f), new Vector2(520f, 18f), new Color(1f, 1f, 1f, 0.14f)); barBg.raycastTarget = false;
        rankFill = LeftBar(p, "RankFill", new Vector2(0f, 172f), 520f, 18f, Gold);
        rankNote = L(p, "RankNote", "", 20, Dim, TextAnchor.MiddleLeft, new Vector2(0f, 144f), new Vector2(520f, 26f));

        Divider(p, 112f);
        goalsHeader = L(p, "GoalsHeader", "TODAY'S GOALS", 22, Blue, TextAnchor.MiddleLeft, new Vector2(0f, 86f), new Vector2(520f, 28f), FontStyle.Bold);
        for (int i = 0; i < 3; i++)
        {
            float y = 46f - i * 52f;
            goalTitle[i] = L(p, "GoalTitle" + i, "", 22, Color.white, TextAnchor.MiddleLeft, new Vector2(-60f, y), new Vector2(400f, 28f));
            goalState[i] = L(p, "GoalState" + i, "", 22, Dim, TextAnchor.MiddleRight, new Vector2(170f, y), new Vector2(180f, 28f), FontStyle.Bold);
            var gb = MenuUi.Panel(p, "GoalBar" + i, new Vector2(0f, y - 20f), new Vector2(520f, 6f), new Color(1f, 1f, 1f, 0.12f)); gb.raycastTarget = false;
            goalFill[i] = LeftBar(p, "GoalFill" + i, new Vector2(0f, y - 20f), 520f, 6f, Blue);
            goalFillImg[i] = goalFill[i].GetComponent<Image>();
        }

        Divider(p, -112f);
        L(p, "GlowHeader", "WEAPON GLOW", 22, Blue, TextAnchor.MiddleLeft, new Vector2(0f, -138f), new Vector2(520f, 28f), FontStyle.Bold);
        var prev = MenuUi.MakeButton(p, "GlowPrev", "<", 40, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(-220f, -196f), new Vector2(80f, 70f));
        var next = MenuUi.MakeButton(p, "GlowNext", ">", 40, new Color(0.2f, 0.35f, 0.6f, 0.97f), new Vector2(220f, -196f), new Vector2(80f, 70f));
        prev.onClick.AddListener(() => StepPalette(-1));
        next.onClick.AddListener(() => StepPalette(1));
        swatch = MenuUi.Panel(p, "Swatch", new Vector2(-120f, -196f), new Vector2(36f, 36f), Color.white); swatch.raycastTarget = false;
        glowName = L(p, "GlowName", "", 30, Color.white, TextAnchor.MiddleLeft, new Vector2(30f, -196f), new Vector2(260f, 40f), FontStyle.Bold);
        glowStatus = L(p, "GlowStatus", "", 20, Dim, TextAnchor.MiddleCenter, new Vector2(0f, -252f), new Vector2(540f, 28f));
        L(p, "Hint", "Cosmetic only: your weapons' resting glow. Hit flashes keep their colours.", 16, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter, new Vector2(0f, -294f), new Vector2(560f, 24f));
    }

    static Text L(Transform parent, string name, string text, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box, FontStyle style = FontStyle.Normal)
    {
        var t = MenuUi.Label(parent, name, text, size, color, align, pos, box, style);
        t.raycastTarget = false;
        return t;
    }

    static void Divider(Transform parent, float y)
    {
        var d = MenuUi.Panel(parent, "Divider", new Vector2(0f, y), new Vector2(540f, 2f), new Color(1f, 1f, 1f, 0.12f));
        d.raycastTarget = false;
    }

    static RectTransform LeftBar(Transform parent, string name, Vector2 centre, float width, float h, Color color)
    {
        var img = MenuUi.Panel(parent, name, centre, new Vector2(0f, h), color);
        img.raycastTarget = false;
        var rt = (RectTransform)img.transform;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(centre.x - width / 2f, centre.y);
        return rt;
    }

    static void SetBar(RectTransform bar, float width, float frac, float h) => bar.sizeDelta = new Vector2(width * Mathf.Clamp01(frac), h);

    // ───────────────────────── content ─────────────────────────
    public void Refresh(string name)
    {
        player = name ?? "";
        if (canvas == null) return;
        data = string.IsNullOrEmpty(player) ? new ProgressData() : PlayerProgressStore.Load(player);
        data.RollDay(PlayerProgressStore.Today);
        goals = DailyGoalRules.ForDay(data.day);
        goalValues = new float[goals.Length];
        for (int i = 0; i < goals.Length; i++) { var st = data.Goal(goals[i].key, false); goalValues[i] = st != null ? st.value : 0f; }
        goalsFromServer = false;
        viewPalette = GlowPalettes.Effective(data.glowPalette, RankLadder.IndexFor(data.effortPoints));
        GlowCosmetics.ApplyFor(data);
        Draw();

        var rep = SessionReporter.Instance;
        if (rep == null || string.IsNullOrEmpty(player)) return;
        string forPlayer = player;
        rep.FetchRank(forPlayer, (ok, pts) =>
        {
            if (this == null || !ok || forPlayer != player || pts <= data.effortPoints) return;
            data.effortPoints = pts;
            int idx = RankLadder.IndexFor(pts);
            if (idx > data.celebratedRank) data.celebratedRank = idx;
            PlayerProgressStore.Save(player, data);
            var menu = GetComponent<MainMenuController>();
            if (menu != null) menu.RefreshPlayingAs();
            else Draw();
        });
        if (EngagementConfig.Get().useServerChallenges)
            rep.FetchChallenges(forPlayer, (ok, cards) =>
            {
                if (this == null || !ok || forPlayer != player || cards == null) return;
                int n = 0; foreach (var c in cards) if (c != null && DailyGoalRules.TryMetric(c.metric, out _)) n++;
                if (n == 0) return;
                goals = new GoalDef[n]; goalValues = new float[n];
                int k = 0;
                foreach (var c in cards)
                {
                    if (c == null || !DailyGoalRules.TryMetric(c.metric, out var m)) continue;
                    goals[k] = new GoalDef { key = c.key, title = c.title, metric = m, target = c.target, bonusXp = c.bonusXp, fromServer = true };
                    goalValues[k] = c.completed ? Mathf.Max(c.value, c.target) : c.value;
                    k++;
                }
                goalsFromServer = true;
                Draw();
            });
    }

    void Draw()
    {
        int pts = data.effortPoints;
        var rank = RankLadder.RankFor(pts);
        playerLabel.text = string.IsNullOrEmpty(player) ? "No player yet: press Change Name" : player;
        rankLabel.text = rank.name.ToUpperInvariant();
        effortLabel.text = pts + " effort";
        SetBar(rankFill, 520f, RankLadder.Progress(pts), 18f);
        int toNext = RankLadder.PointsToNext(pts);
        rankNote.text = toNext > 0 ? toNext + " effort points to " + RankLadder.NextRung(pts).name + "  ·  " + data.fightsToday + " fight" + (data.fightsToday == 1 ? "" : "s") + " today"
                                   : "Top rank  ·  " + data.fightsToday + " fight" + (data.fightsToday == 1 ? "" : "s") + " today";
        goalsHeader.text = goalsFromServer ? "TODAY'S GOALS" : "TODAY'S GOALS (on this headset)";
        for (int i = 0; i < 3; i++)
        {
            bool has = i < goals.Length;
            goalTitle[i].text = has ? goals[i].title : "";
            if (!has) { goalState[i].text = ""; SetBar(goalFill[i], 520f, 0f, 6f); continue; }
            bool done = DailyGoalRules.Met(goals[i], goalValues[i]) || (!goals[i].fromServer && data.Goal(goals[i].key, false) is GoalState st && st.done);
            goalState[i].text = done ? "DONE" : Mathf.RoundToInt(goalValues[i]) + " / " + Mathf.RoundToInt(goals[i].target) + "  +" + goals[i].bonusXp + " XP";
            goalState[i].color = done ? new Color(0.55f, 1f, 0.6f) : Dim;
            goalTitle[i].color = done ? new Color(0.55f, 1f, 0.6f) : Color.white;
            goalFillImg[i].color = done ? new Color(0.55f, 1f, 0.6f) : Blue;
            SetBar(goalFill[i], 520f, DailyGoalRules.Fraction(goals[i], goalValues[i]), 6f);
        }
        DrawPalette();
    }

    void DrawPalette()
    {
        int rankIdx = RankLadder.IndexFor(data.effortPoints);
        var pal = GlowPalettes.All[viewPalette];
        bool unlocked = GlowPalettes.Unlocked(viewPalette, rankIdx);
        bool worn = unlocked && GlowPalettes.Effective(data.glowPalette, rankIdx) == viewPalette;
        glowName.text = pal.name;
        glowName.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        var e = pal.emission; var sw = viewPalette == 0 ? new Color(0.75f, 0.78f, 0.82f) : new Color(Mathf.Min(1f, e.r * 2.4f), Mathf.Min(1f, e.g * 2.4f), Mathf.Min(1f, e.b * 2.4f), 1f);
        swatch.color = unlocked ? sw : new Color(sw.r, sw.g, sw.b, 0.35f);
        int need = RankLadder.Rungs[pal.minRank].from - data.effortPoints;
        glowStatus.text = worn ? "Equipped" : unlocked ? "Unlocked (select to wear)" : "Unlocks at " + RankLadder.Rungs[pal.minRank].name + " (" + need + " effort to go)";
        glowStatus.color = worn ? new Color(0.55f, 1f, 0.6f) : Dim;
    }

    /// <summary>The arrows: show the next palette; wear it when the rank has unlocked it (saved for this player).</summary>
    public void StepPalette(int step)
    {
        if (data == null) return;
        viewPalette = GlowPalettes.Step(viewPalette, step);
        if (GlowPalettes.Unlocked(viewPalette, RankLadder.IndexFor(data.effortPoints)) && !string.IsNullOrEmpty(player))
        {
            data.glowPalette = GlowPalettes.All[viewPalette].key;
            PlayerProgressStore.Save(player, data);
            GlowCosmetics.ApplyFor(data);
        }
        DrawPalette();
    }
}
