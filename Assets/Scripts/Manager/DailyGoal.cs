// DailyGoal.cs
// Purpose: The headset's view of the player's daily goal (backend: daily_goals, GET/PUT /api/players/:name/goal...). One metric and
//          one target for the player's local day (Asia/Kathmandu, the backend's constant), chosen on the pre-battle goal panel
//          (PlayerNamePicker.Goal.cs), shown as one HUD line during the fight (CombatUIController) and as a result line afterwards.
//          Progress = the backend's total for today at battle start (Baseline) + this fight's live counters (BattleStats /
//          XPManager), so the HUD never waits on the network. Fetching and saving run in the background through SessionReporter
//          (5 s timeout); a goal that could not be saved is kept in PlayerPrefs (one pending goal) and retried at the next battle
//          start. Nothing here ever blocks Start.
//          kcal is the project's ESTIMATE; the HUD and panels always label it "(est.)".
// Dependencies: SessionReporter, BattleStats, XPManager, PlayerPrefs
// Attach to: nothing (static)

using System;
using UnityEngine;

public static class DailyGoal
{
    // Named constants (mirror web/backend/goals.js: TARGET_RANGE, FALLBACK_PRESETS, BONUS_XP).
    public static readonly string[] Metrics = { "kcal", "fullSwings", "activeMinutes", "xp" };
    public static readonly string[] Labels = { "Calories (est.)", "Full swings", "Active minutes", "XP" };
    public static readonly string[] Units = { "kcal (est.)", "full swings", "active min", "XP" };
    public static readonly float[] Steps = { 5f, 10f, 1f, 100f };
    public static readonly float[] Min = { 5f, 10f, 1f, 100f };
    public static readonly float[] Max = { 500f, 1000f, 120f, 20000f };
    static readonly float[,] FallbackPresets = { { 20f, 40f, 70f }, { 30f, 60f, 120f }, { 5f, 10f, 20f }, { 500f, 1000f, 2000f } };
    public const int FallbackBonusXp = 75;
    /// <summary>Goal days follow the backend's default time zone (Asia/Kathmandu, UTC+5:45), like the streak and achievements.</summary>
    public static readonly TimeSpan LocalDayOffset = new TimeSpan(5, 45, 0);
    const string PendingKey = "strikefit.goal.pending";
    const string SkipKey = "strikefit.goal.skipDay";

    // ── state (the backend's answer, kept in memory) ──
    public static bool Known { get; private set; }          // a fetch has answered this session
    public static bool Offline { get; private set; }        // the last fetch / save did not answer
    public static string Player { get; private set; }
    public static bool HasGoal { get; private set; }
    public static string Metric { get; private set; } = "";
    public static float Target { get; private set; }
    public static float Baseline { get; private set; }      // today's value for Metric before this fight
    public static bool Completed { get; private set; }      // already reached today (bonus paid)
    public static bool CompletedAtBattleStart { get; private set; }
    public static int BonusXp { get; private set; }
    public static int BonusOnCompletion { get; private set; } = FallbackBonusXp;
    public static bool PresetsFromData { get; private set; }
    static readonly float[,] presets = (float[,])FallbackPresets.Clone();

    public static event Action Changed;

    public static int MetricIndex(string metric) => Array.IndexOf(Metrics, metric);
    public static float Preset(int metric, int level) => presets[Mathf.Clamp(metric, 0, 3), Mathf.Clamp(level, 0, 2)];
    public static string LocalDay(DateTime utcNow) => (utcNow + LocalDayOffset).ToString("yyyy-MM-dd");
    public static string Today => LocalDay(DateTime.UtcNow);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Known = Offline = HasGoal = Completed = CompletedAtBattleStart = PresetsFromData = false;
        Metric = ""; Target = Baseline = 0f; BonusXp = 0; BonusOnCompletion = FallbackBonusXp; Player = null; Changed = null;
        Array.Copy(FallbackPresets, presets, FallbackPresets.Length);
    }

    // ───────────────────────── progress ─────────────────────────
    /// <summary>This fight's value for the goal's metric, from the same live counters the HUD and the telemetry use.</summary>
    public static float SessionValue(string metric)
    {
        switch (metric)
        {
            case "kcal": return BattleStats.TryGetKcal(GamePause.ActiveRealtime, out float k) ? k : 0f;
            case "fullSwings": return XPManager.Instance != null ? XPManager.Instance.FullSwings : 0f;
            case "activeMinutes": return BattleStats.ActiveMinutes;
            case "xp": return XPManager.Instance != null ? XPManager.Instance.CurrentXP : 0f;
            default: return 0f;
        }
    }

    /// <summary>Today's total so far: the backend's value at battle start plus this fight.</summary>
    public static float Live => HasGoal ? Baseline + SessionValue(Metric) : 0f;
    public static float LivePercent => HasGoal && Target > 0f ? Mathf.Min(100f, Live / Target * 100f) : 0f;

    /// <summary>"Goal 42 / 100 kcal (est.)" with the number format of the metric; "" without a goal.</summary>
    public static string HudLine()
    {
        if (!HasGoal) return "";
        int i = MetricIndex(Metric);
        if (i < 0) return "";
        return "Goal " + Format(i, Live) + " / " + Format(i, Target) + " " + Units[i];
    }

    /// <summary>Changes whenever the HUD line's text would (display precision), so the HUD builds a string only then.</summary>
    public static int HudKey => HasGoal ? MetricIndex(Metric) * 100000000 + Mathf.RoundToInt(Live * 10f) % 10000000 + (Completed ? 1 : 0) : -1;

    public static string Format(int metric, float v) => metric == 0 ? v.ToString("0.#") : Mathf.RoundToInt(v).ToString();

    // ───────────────────────── fetch / save ─────────────────────────
    static SessionReporter Reporter => SessionReporter.Instance;

    /// <summary>Background fetch of today's goal and progress for the player (5 s timeout). Safe to call repeatedly.</summary>
    public static void Fetch(string player, Action<bool> done = null)
    {
        var rep = Reporter;
        if (rep == null || string.IsNullOrEmpty(player)) { done?.Invoke(false); return; }
        rep.FetchDailyGoal(player, (ok, r) =>
        {
            if (!ok) { Offline = true; done?.Invoke(false); Changed?.Invoke(); return; }
            Apply(player, r);
            done?.Invoke(true);
        });
    }

    static void Apply(string player, SessionReporter.GoalTodayJson r)
    {
        Offline = false; Known = true; Player = player;
        bool has = r.goal != null && !string.IsNullOrEmpty(r.goal.metric);
        HasGoal = has;
        Metric = has ? r.goal.metric : "";
        Target = has ? r.goal.target : 0f;
        Baseline = has && r.progress != null ? r.progress.value : 0f;
        Completed = r.completed;
        BonusXp = r.bonusXp;
        BonusOnCompletion = r.bonusXpOnCompletion > 0 ? r.bonusXpOnCompletion : FallbackBonusXp;
        var pr = r.suggestions != null ? r.suggestions.presets : null;
        PresetsFromData = r.suggestions != null && r.suggestions.source == "data";
        if (pr != null)
        {
            SetPreset(0, pr.kcal); SetPreset(1, pr.fullSwings); SetPreset(2, pr.activeMinutes); SetPreset(3, pr.xp);
        }
        Changed?.Invoke();
    }

    static void SetPreset(int metric, SessionReporter.GoalTriple t)
    {
        if (t == null || t.easy <= 0f) return;
        presets[metric, 0] = t.easy; presets[metric, 1] = t.standard; presets[metric, 2] = t.hard;
    }

    /// <summary>Marks the start of a fight: the baseline is what the backend had counted, and "already reached" is remembered so the
    /// result only announces a bonus the fight itself earned.</summary>
    public static void BattleStarted()
    {
        CompletedAtBattleStart = Completed;
        RetryPending();
    }

    /// <summary>Sets today's goal. The local state changes at once; the save runs in the background. A goal that could not be
    /// saved stays pending in PlayerPrefs and is retried at the next battle start (and when the goal panel opens).</summary>
    public static void SetGoal(string player, string metric, float target)
    {
        int i = MetricIndex(metric);
        if (i < 0) return;
        target = Mathf.Clamp(target, Min[i], Max[i]);
        Player = player; HasGoal = true; Metric = metric; Target = target; Completed = false; BonusXp = 0;
        if (!Known) Baseline = 0f;
        PlayerPrefs.DeleteKey(SkipKey);
        Save(player, metric, target, pendingAlready: false);
        Changed?.Invoke();
    }

    static void Save(string player, string metric, float target, bool pendingAlready)
    {
        var rep = Reporter;
        if (rep == null) { StorePending(player, metric, target); return; }
        rep.PutDailyGoal(player, metric, target, status =>
        {
            if (status == 200 || status == 201 || status == 409 || status == 400)
            {
                ClearPending();            // saved, refused, or already reached today: nothing to retry
                Offline = false;
                if (status == 409) Fetch(player);
                else if (status < 300) Fetch(player);   // pick up the baseline the backend counted
            }
            else
            {
                Offline = true;
                StorePending(player, metric, target);
            }
            Changed?.Invoke();
        });
    }

    // One pending goal (the latest). {"n":name,"m":metric,"t":target,"d":day}
    static void StorePending(string player, string metric, float target)
    {
        try
        {
            PlayerPrefs.SetString(PendingKey, "{\"n\":\"" + SessionReporter.Escape(player) + "\",\"m\":\"" + metric + "\",\"t\":" + target.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ",\"d\":\"" + Today + "\"}");
            PlayerPrefs.Save();
        }
        catch (Exception) { /* storage unavailable: the goal simply is not retried */ }
    }

    static void ClearPending() { try { PlayerPrefs.DeleteKey(PendingKey); } catch (Exception) { } }

    [Serializable] class PendingJson { public string n, m, d; public float t; }

    /// <summary>Retries a goal that was not saved earlier (same local day only). Never blocks; failures stay pending.</summary>
    public static void RetryPending()
    {
        try
        {
            if (!PlayerPrefs.HasKey(PendingKey)) return;
            var p = JsonUtility.FromJson<PendingJson>(PlayerPrefs.GetString(PendingKey));
            if (p == null || p.d != Today || string.IsNullOrEmpty(p.n) || MetricIndex(p.m) < 0) { ClearPending(); return; }
            Save(p.n, p.m, p.t, pendingAlready: true);
        }
        catch (Exception) { ClearPending(); }
    }

    public static bool HasPending
    {
        get { try { return PlayerPrefs.HasKey(PendingKey); } catch (Exception) { return false; } }
    }

    // ── "Skip today": the goal panel is not shown again until the next local day ──
    public static bool SkippedToday
    {
        get { try { return PlayerPrefs.GetString(SkipKey, "") == Today; } catch (Exception) { return false; } }
    }
    public static void SkipToday() { try { PlayerPrefs.SetString(SkipKey, Today); PlayerPrefs.Save(); } catch (Exception) { } }

    // ───────────────────────── result ─────────────────────────
    /// <summary>After the battle: wait (bounded) for the result POST to leave the queue, then fetch goal/today once. done(ok, awardedNow,
    /// bonusXp). Falls back to the caller's locally computed value when ok is false.</summary>
    public static System.Collections.IEnumerator FetchAfterBattle(Action<bool, bool, int> done, float maxWaitSeconds = 10f)
    {
        var rep = Reporter;
        if (rep == null || !HasGoal) { done?.Invoke(false, false, 0); yield break; }
        float end = Time.unscaledTime + maxWaitSeconds;
        // The result POST is queued behind the last events (one request at a time); wait for it, bounded.
        while (rep != null && !rep.ResultAnswered && Time.unscaledTime < end) yield return null;
        bool answered = false, ok = false;
        bool before = CompletedAtBattleStart;
        Fetch(Player, r => { answered = true; ok = r; });
        float timeout = Time.unscaledTime + 7f;
        while (!answered && Time.unscaledTime < timeout) yield return null;
        bool awarded = ok && Completed && !before;
        done?.Invoke(ok, awarded, awarded ? BonusXp : 0);
    }
}
