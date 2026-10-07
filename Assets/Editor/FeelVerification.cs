// FeelVerification.cs
// Purpose: Editor-only Play-mode helpers for verifying the feel-and-fitness pass: a time-series sampler for the breathing tiers
//          and the HUD values, plus small probes. Driven from Unity MCP execute_code; never part of a build, saves nothing.
// Dependencies: PlayerExertionAudio, PlayerHealth, XPManager, BattleStats, CombatUIController (via reflection-free public API)

using System.Text;
using UnityEditor;
using UnityEngine;

public static class FeelVerification
{
    static readonly StringBuilder log = new StringBuilder();
    static float interval = 1f, next;
    static bool running;
    static int samples;

    public static string StartSampler(float seconds)
    {
        interval = Mathf.Max(0.05f, seconds);
        next = 0f; samples = 0; log.Length = 0;
        log.AppendLine("t(s)    exert  tier    lowHp  medVol fastVol medPlay fastPlay  hp%   combo  kcal   effortBar");
        if (!running) { EditorApplication.update += Tick; running = true; }
        return "sampler started";
    }

    public static string StopSampler()
    {
        EditorApplication.update -= Tick; running = false;
        return log.ToString();
    }

    public static string Log() => log.ToString();
    public static int Samples => samples;

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { StopSampler(); return; }
        if (Time.time < next) return;
        next = Time.time + interval;
        var ex = PlayerExertionAudio.Instance;
        var ph = PlayerHealth.Instance;
        var xp = XPManager.Instance;
        if (ex == null) return;
        bool med = ex.breathingSource != null && ex.breathingSource.isPlaying;
        bool fast = ex.fastSource != null && ex.fastSource.isPlaying;
        float kcal; bool has = BattleStats.TryGetKcal(Time.realtimeSinceStartup, out kcal);
        samples++;
        log.AppendLine(Time.time.ToString("0.0").PadLeft(6) + "  " + ex.Exertion01.ToString("0.00") + "   " + ex.CurrentTier.ToString().PadRight(7) + " " + (ex.LowHpActive ? "Y" : "-") + "      " +
            ex.MediumVolume.ToString("0.000") + "  " + ex.FastVolume.ToString("0.000") + "   " + (med ? "play" : "-") + "     " + (fast ? "play" : "-") + "     " +
            (ph != null ? (ph.currentHealth / ph.maxHealth * 100f).ToString("0") : "?").PadLeft(3) + "   " + (xp != null ? xp.CurrentCombo : -1) + "      " + (has ? kcal.ToString("0.00") : "-") + "   " +
            (CombatUIController.Instance != null ? CombatUIController.Instance.EffortBarShown.ToString("0.00") : "-"));
    }

    // ───────────────────────── per-frame render probe (draw calls vs live blood particles) ─────────────────────────
    static bool probing;
    static int pFramesWith, pFramesWithout, pMaxParticles, pLastFrame = -1, pMaxDcWith, pMaxDcWithout;
    static double pDcWith, pDcWithout, pParticleSum;
    static string pLabel = "";

    public static string StartProbe(string label)
    {
        pLabel = label; pFramesWith = pFramesWithout = pMaxParticles = pMaxDcWith = pMaxDcWithout = 0; pDcWith = pDcWithout = pParticleSum = 0; pLastFrame = -1;
        if (!probing) { EditorApplication.update += ProbeTick; probing = true; }
        return "probe started: " + label;
    }

    public static string StopProbe()
    {
        EditorApplication.update -= ProbeTick; probing = false;
        var fx = Object.FindAnyObjectByType<HitBloodFx>();
        return pLabel + ": mode=" + (fx != null ? fx.mode.ToString() : "n/a") + " bursts=" + (fx != null ? fx.Bursts : -1) +
               " | frames with live particles " + pFramesWith + ": drawCalls mean " + (pFramesWith > 0 ? (pDcWith / pFramesWith).ToString("0.00") : "-") + " max " + pMaxDcWith +
               " | frames without " + pFramesWithout + ": drawCalls mean " + (pFramesWithout > 0 ? (pDcWithout / pFramesWithout).ToString("0.00") : "-") + " max " + pMaxDcWithout +
               " | max live particles " + pMaxParticles + ", mean when alive " + (pFramesWith > 0 ? (pParticleSum / pFramesWith).ToString("0.0") : "-");
    }

    static void ProbeTick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= ProbeTick; probing = false; return; }
        if (Time.frameCount == pLastFrame) return;
        pLastFrame = Time.frameCount;
        var fx = Object.FindAnyObjectByType<HitBloodFx>();
        int live = fx != null ? fx.LiveParticles : 0;
        int dc = UnityStats.drawCalls;
        if (live > 0) { pFramesWith++; pDcWith += dc; pParticleSum += live; if (dc > pMaxDcWith) pMaxDcWith = dc; }
        else { pFramesWithout++; pDcWithout += dc; if (dc > pMaxDcWithout) pMaxDcWithout = dc; }
        if (live > pMaxParticles) pMaxParticles = live;
    }

    // ───────────────────────── tier job: fight, rest, low HP, recovery, victory ─────────────────────────
    static System.Collections.IEnumerator tierJob;
    static readonly StringBuilder tierLog = new StringBuilder();
    public static string TierJobStatus() => (tierJob != null ? "RUNNING\n" : "IDLE\n") + tierLog;
    public static bool TierJobBusy => tierJob != null;

    /// <summary>Runs the breathing-tier scenario on the open level (Play mode). Read progress with TierJobStatus().</summary>
    public static string RunTierJob(float simSeconds, float swingInterval, string backend)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        tierLog.Length = 0;
        CombatVerification.backendUrl = backend;
        CombatVerification.frameDt = 1f / 20f;
        CombatVerification.simSeconds = simSeconds;
        CombatVerification.swingInterval = swingInterval;
        CombatVerification.simForceAllAttacks = false;
        tierJob = TierSteps();
        EditorApplication.update -= TierTick;
        EditorApplication.update += TierTick;
        return "tier job started";
    }

    static int tierLastFrame = -1;
    static void TierTick()
    {
        if (tierJob == null || !EditorApplication.isPlaying) { tierJob = null; EditorApplication.update -= TierTick; return; }
        if (Time.frameCount == tierLastFrame) return;
        tierLastFrame = Time.frameCount;
        try { if (!tierJob.MoveNext()) { tierLog.AppendLine("TIER JOB DONE"); tierJob = null; EditorApplication.update -= TierTick; } }
        catch (System.Exception e) { tierLog.AppendLine("EXCEPTION " + e); tierJob = null; EditorApplication.update -= TierTick; }
    }

    // Game time with a fixed frame step (captureDeltaTime), so it does not depend on how often the Editor ticks while unfocused.
    static System.Collections.IEnumerator WaitSeconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    static void TLog(string line) { tierLog.AppendLine("[t=" + Time.time.ToString("0.0") + " rt=" + Time.realtimeSinceStartup.ToString("0.0") + " dt=" + Time.deltaTime.ToString("0.000") + " cap=" + Time.captureDeltaTime.ToString("0.000") + " f=" + Time.frameCount + "] " + line); }

    static System.Collections.IEnumerator TierSteps()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        TLog("== TIERS " + scene);
        StartSampler(2f);
        CombatVerification.Run("sim"); // prepares, starts the battle, swings with real hit contacts, keeps the player alive
        yield return null;
        while (CombatVerification.Busy) yield return null;
        TLog("sim finished; exertion=" + PlayerExertionAudio.Instance.Exertion01.ToString("0.00") + " tier=" + PlayerExertionAudio.Instance.CurrentTier);
        // rest: the monster stops attacking, the player is healed
        var mon = BattleManager.Instance.ActiveMonster;
        if (mon != null) mon.attackCooldown = 9999f;
        PlayerHealth.Instance.FullHeal();
        TLog("-- resting (no swings) 24 s");
        { float _e = Time.time + 24f; while (Time.time < _e) yield return null; }
        var ex = PlayerExertionAudio.Instance;
        TLog("after rest: exertion=" + ex.Exertion01.ToString("0.00") + " tier=" + ex.CurrentTier + " medium=" + ex.MediumVolume.ToString("0.000") + " fast=" + ex.FastVolume.ToString("0.000"));
        PlayerHealth.Instance.currentHealth = PlayerHealth.Instance.maxHealth * 0.20f;
        TLog("-- HP set to 20% (rest)");
        { float _e = Time.time + 6f; while (Time.time < _e) yield return null; }
        TLog("HP 20%: tier=" + ex.CurrentTier + " lowHp=" + ex.LowHpActive + " fast=" + ex.FastVolume.ToString("0.000") + " medium=" + ex.MediumVolume.ToString("0.000") + " fastPlaying=" + ex.fastSource.isPlaying);
        PlayerHealth.Instance.currentHealth = PlayerHealth.Instance.maxHealth * 0.28f;
        { float _e = Time.time + 4f; while (Time.time < _e) yield return null; }
        TLog("HP 28% (between 25 and 30): tier=" + ex.CurrentTier + " lowHp=" + ex.LowHpActive + " (hysteresis holds the fast tier)");
        PlayerHealth.Instance.currentHealth = PlayerHealth.Instance.maxHealth * 0.40f;
        { float _e = Time.time + 8f; while (Time.time < _e) yield return null; }
        TLog("HP 40%: tier=" + ex.CurrentTier + " lowHp=" + ex.LowHpActive + " fast=" + ex.FastVolume.ToString("0.000") + " medium=" + ex.MediumVolume.ToString("0.000"));
        // victory: breathing must be cut
        TLog("-- killing the monster (victory)");
        PlayerHealth.Instance.FullHeal();
        if (mon != null) mon.OnTakeDamage(1000000f, mon.transform.position + Vector3.up, 9f, false);
        { float _e = Time.time + 3f; while (Time.time < _e) yield return null; }
        TLog("after victory: battleActive=" + BattleManager.Instance.IsBattleActive + " tier=" + ex.CurrentTier + " medium=" + ex.MediumVolume.ToString("0.000") + " fast=" + ex.FastVolume.ToString("0.000") + " | victory panel extras: " + (CombatUIController.Instance.LastResultExtras ?? "null").Replace("\n", " | "));
        TLog("--- sampler ---");
        TLog(StopSampler());
    }
}
