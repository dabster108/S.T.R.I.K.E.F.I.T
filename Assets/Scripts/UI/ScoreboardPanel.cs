// ScoreboardPanel.cs
// Purpose: The main menu's Scoreboard page: tabs All / L1 / L2 / L3 on the laser, top 10 (rank, name, score, a level chip in
//          the level's colour, date), medals for the top 3, the current player's row highlighted, and "Your best: #N, score"
//          when the player is outside the top 10. Data: GET /api/leaderboard through SessionReporter (5 s timeout), fetched in
//          the background once the wake ping has finished, one board at a time. The last answer per board is kept in
//          PlayerPrefs and shown with "Updated <time>" when offline. States: loading, empty ("No scores yet. Be the first."),
//          unavailable. Nothing waits for it: no Update, no per-frame work, rows are pre-built and only their text changes.
// Dependencies: SessionReporter (FetchLeaderboard, WakeDone), LeaderboardData, PlayerNameStore, MainMenuController, MenuLog
// Attach to: MenuSystems in MainMenu.unity (always active; `page` is the ScoreboardPage root), built by Editor/MenuSceneBuilder

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScoreboardPanel : MonoBehaviour
{
    [Serializable]
    public class RowView
    {
        public GameObject root;
        public Image background;
        public Image medal;
        public Text rank;
        public Text name;
        public Text score;
        public Image chip;
        public Text chipText;
        public Text date;
    }

    [Header("Toggles")]
    [Tooltip("Off: the Scoreboard button is hidden and nothing is fetched.")]
    public bool scoreboardEnabled = true;
    [Tooltip("Fetch every board in the background as soon as the menu's wake ping has finished.")]
    public bool fetchOnMenuLoad = true;
    [Tooltip("Opening the page refetches the shown board when its data is older than this (seconds).")]
    public float refetchAfterSeconds = 60f;
    public int rowsShown = 10;

    [Header("Wiring (built by MenuSceneBuilder)")]
    public MainMenuController menu;
    public GameObject page;
    public Button openButton;
    public Button backButton;
    public Button[] tabs = new Button[4];
    public RowView[] rows;
    public Text messageText;   // loading / empty / unavailable, centred over the rows
    public Text youText;       // "Your best: #14, 820"
    public Text statusText;    // "Updated 14:05" / "Offline. Updated 12 Oct 14:05"

    [Header("Look")]
    public Color tabColor = new Color(0.2f, 0.22f, 0.3f, 0.96f);
    public Color tabSelectedColor = new Color(0.36f, 0.2f, 0.58f, 0.96f);
    public Color rowColor = new Color(1f, 1f, 1f, 0.04f);
    public Color youRowColor = new Color(1f, 0.85f, 0.3f, 0.28f);
    public Color[] medalColors = { new Color(0.95f, 0.75f, 0.2f), new Color(0.78f, 0.8f, 0.84f), new Color(0.8f, 0.5f, 0.25f) };
    public Color[] levelColors = { new Color(0.1f, 0.42f, 0.3f, 1f), new Color(0.36f, 0.2f, 0.58f, 1f), new Color(0.58f, 0.16f, 0.16f, 1f) };

    enum State { Idle, Fetching, Ok, Failed }

    readonly LeaderboardData.Board[] data = new LeaderboardData.Board[4];
    readonly DateTime[] fetchedUtc = new DateTime[4];
    readonly bool[] fromCache = new bool[4];
    readonly State[] state = new State[4];
    readonly string[] fetchedFor = new string[4];   // player name the board's "you" belongs to
    int shown;
    bool fetching;
    SessionReporter reporter;

    void Awake()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            if (tabs[i] != null) tabs[i].onClick.AddListener(() => ShowTab(index, true));
        }
        if (backButton != null) backButton.onClick.AddListener(() => { if (menu != null) menu.Back("scoreboard"); });
        if (openButton != null)
        {
            openButton.onClick.AddListener(Open);
            openButton.gameObject.SetActive(scoreboardEnabled);
        }
        for (int i = 0; i < 4; i++)
        {
            data[i] = LeaderboardData.Load(LeaderboardData.Boards[i], out fetchedUtc[i]);
            fromCache[i] = data[i] != null;
        }
    }

    void Start()
    {
        if (!scoreboardEnabled || !fetchOnMenuLoad) return;
        reporter = SessionReporter.Instance;
        if (reporter == null) return;
        if (reporter.WakeDone) FetchAll();
        else reporter.WakeFinished += OnWake;
    }

    void OnDestroy()
    {
        if (reporter != null) reporter.WakeFinished -= OnWake;
    }

    void OnWake(bool ok)
    {
        if (this != null) FetchAll();
    }

    void FetchAll()
    {
        if (fetching) return;
        StartCoroutine(FetchSequence(new[] { 0, 1, 2, 3 }));
    }

    IEnumerator FetchSequence(int[] boards)
    {
        fetching = true;
        foreach (int i in boards)
        {
            if (reporter == null) reporter = SessionReporter.Instance;
            if (reporter == null) break;
            state[i] = State.Fetching;
            if (i == shown) Render();
            string player = PlayerNameStore.LastUsed;
            float t0 = Time.realtimeSinceStartup;
            bool done = false;
            reporter.FetchLeaderboard(LeaderboardData.Boards[i], rowsShown, player, 0, 0f, (ok, body) =>
            {
                done = true;
                var board = ok ? LeaderboardData.Parse(body) : null;
                if (board != null)
                {
                    data[i] = board;
                    fetchedUtc[i] = DateTime.UtcNow;
                    fromCache[i] = false;
                    fetchedFor[i] = player;
                    state[i] = State.Ok;
                    LeaderboardData.Save(LeaderboardData.Boards[i], body);
                }
                else state[i] = State.Failed;
                MenuLog.Event($"scoreboard {LeaderboardData.Boards[i]}: {(board != null ? $"{board.rows.Length} rows" : "unavailable")} in {Time.realtimeSinceStartup - t0:0.0}s");
            });
            while (!done) yield return null;   // SendNow always answers (5 s timeout)
            if (this != null && i == shown) Render();
        }
        fetching = false;
    }

    // ───────────────────────── page ─────────────────────────

    public void Open()
    {
        if (menu == null || menu.Blocked) return;
        menu.ShowPage(page, "scoreboard");
        ShowTab(shown, false);
    }

    void ShowTab(int index, bool clicked)
    {
        shown = Mathf.Clamp(index, 0, 3);
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == null) continue;
            var img = tabs[i].targetGraphic as Image;
            var cb = tabs[i].colors;
            cb.normalColor = cb.selectedColor = i == shown ? tabSelectedColor : tabColor;
            tabs[i].colors = cb;
            if (img != null) img.color = Color.white;
        }
        if (clicked) MenuLog.Event($"scoreboard tab {LeaderboardData.Boards[shown]}");
        Render();
        // Stale, or fetched for another player name (Change Name since): fetch this board again in the background.
        bool stale = data[shown] == null || fromCache[shown] || (DateTime.UtcNow - fetchedUtc[shown]).TotalSeconds > refetchAfterSeconds
                     || fetchedFor[shown] != PlayerNameStore.LastUsed;
        if (reporter == null) reporter = SessionReporter.Instance;
        // Before the wake ping has finished the background fetch is still to come (Start); never start a second one.
        if (stale && scoreboardEnabled && !fetching && reporter != null && reporter.WakeDone)
            StartCoroutine(FetchSequence(new[] { shown }));
    }

    void Render()
    {
        var b = data[shown];
        int count = b != null && b.rows != null ? Mathf.Min(b.rows.Length, rows.Length) : 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var v = rows[i];
            if (i >= count) { v.root.SetActive(false); continue; }
            var r = b.rows[i];
            v.root.SetActive(true);
            v.background.color = r.you ? youRowColor : (i % 2 == 0 ? rowColor : Color.clear);
            bool medal = r.rank >= 1 && r.rank <= 3;
            v.medal.gameObject.SetActive(medal);
            if (medal) v.medal.color = medalColors[r.rank - 1];
            v.rank.text = r.rank.ToString();
            v.rank.color = medal ? new Color(0.08f, 0.08f, 0.1f) : new Color(0.75f, 0.78f, 0.88f);
            v.name.text = r.you ? r.name + "  (you)" : r.name;
            v.name.fontStyle = r.you ? FontStyle.Bold : FontStyle.Normal;
            v.score.text = Mathf.RoundToInt(r.score).ToString("N0");
            int li = LeaderboardData.LevelIndex(r.level);
            v.chip.color = li >= 0 ? levelColors[li] : Color.gray;
            v.chipText.text = LeaderboardData.LevelChip(r.level);
            v.date.text = LeaderboardData.ShortDate(r.date);
        }

        string message = null;
        if (b == null)
            message = state[shown] == State.Failed ? "Scoreboard unavailable right now" : "Loading scores...";
        else if (count == 0)
            message = "No scores yet. Be the first.";
        messageText.text = message ?? "";
        messageText.gameObject.SetActive(message != null);

        var you = b != null ? b.you : null;
        bool showYou = you != null && you.rank > rowsShown;
        youText.text = showYou ? $"Your best: #{you.rank}, {Mathf.RoundToInt(you.score):N0}" : "";
        youText.gameObject.SetActive(showYou);

        if (b == null) statusText.text = state[shown] == State.Fetching ? "Loading..." : "";
        else
        {
            string when = fetchedUtc[shown] > DateTime.MinValue ? fetchedUtc[shown].ToLocalTime().ToString(
                (DateTime.UtcNow - fetchedUtc[shown]).TotalHours < 20 ? "HH:mm" : "d MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "?";
            bool offline = fromCache[shown] && state[shown] == State.Failed;
            statusText.text = (offline ? "Offline. " : "") + "Updated " + when + (state[shown] == State.Fetching ? " (refreshing)" : "");
        }
    }
}
