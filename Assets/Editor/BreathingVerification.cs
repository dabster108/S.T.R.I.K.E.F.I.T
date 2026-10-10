// BreathingVerification.cs
// Purpose: Play-mode checks of the player's breathing after the 2026-10-10 fix (Editor only). Driven over Unity MCP.
//          Jobs: "menu"   (run in MainMenu) the hardware-test coroutine through the menu's PlayerExertionAudio: status line,
//                         log lines, measured listener output per tier.
//                "level"  (run in a level) breathing sources after a menu -> level load: count, parents, clips, listeners;
//                         then a steady-swing run with the live per-level speed histogram: time to the medium and fast tiers,
//                         fade after stopping, low-HP forced tier; swings attempted vs counted.
// Dependencies: PlayerExertionAudio, PlayerCombat, BattleManager, PlayerHealth, SceneFlow
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BreathingVerification
{
    static IEnumerator job;
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string Status() => (job != null ? "RUNNING\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0;
        job = name == "menu" ? Menu() : Level();
        EditorApplication.update += Tick;
        return "started " + name;
    }
    public static void Stop() { job = null; EditorApplication.update -= Tick; }
    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { if (!job.MoveNext()) { log.AppendLine("DONE"); Stop(); } }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static string SourcesReport()
    {
        var sb = new StringBuilder();
        var exAll = Object.FindObjectsByType<PlayerExertionAudio>(FindObjectsInactive.Include);
        sb.AppendLine($"  PlayerExertionAudio x{exAll.Length}, AudioListeners x{Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length}, AudioListener.pause {AudioListener.pause}, volume {AudioListener.volume:0.00}");
        foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (!s.name.ToLower().Contains("breath")) continue;
            sb.AppendLine($"  source '{s.name}' parent '{(s.transform.parent != null ? s.transform.parent.name : "-")}' clip {(s.clip != null ? s.clip.name + " " + s.clip.channels + "ch " + s.clip.frequency + " Hz" : "NULL")} loop {s.loop} spatialBlend {s.spatialBlend} spatialize {s.spatialize} mute {s.mute} mixer {(s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "none")} enabled {s.enabled} priority {s.priority} volume {s.volume:0.00}");
        }
        return sb.ToString();
    }

    static IEnumerator Menu()
    {
        yield return Frames(10);
        log.AppendLine("== MENU " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        log.Append(SourcesReport());
        var ex = PlayerExertionAudio.Instance;
        if (ex == null) { log.AppendLine("no PlayerExertionAudio on the menu"); yield break; }
        DevDebugGate.Set(true);
        yield return Frames(3);
        var panel = Object.FindAnyObjectByType<BreathingTestPanel>();
        log.AppendLine($"  debug gate on: panel active {(panel != null && panel.panel != null && panel.panel.activeSelf)} (BreathingTestPanel {(panel != null)})");
        // click the real button
        if (panel != null && panel.testButton != null) panel.testButton.onClick.Invoke();
        else panel.StartCoroutine(ex.HardwareTest(5f));
        var seen = new List<string>(); string last = null; float t0 = Time.realtimeSinceStartup;
        while (ex.HardwareTestRunning || Time.realtimeSinceStartup - t0 < 1f)
        {
            if (panel.readout != null && panel.readout.text != last) { last = panel.readout.text; seen.Add($"{Time.realtimeSinceStartup - t0:0.0}s: {last}"); }
            yield return null;
            if (Time.realtimeSinceStartup - t0 > 20f) break;
        }
        log.AppendLine("  readout changes: " + string.Join(" | ", seen));
        log.AppendLine($"  last measured listener output {ex.LastMeasuredDb:0.0} dBFS; final status '{ex.HardwareTestStatus}'");
        DevDebugGate.Set(false);
    }

    // Live per-level hit swing speeds (backend /api/analytics, 2026-10-10), 0.5 m/s bins.
    static readonly Dictionary<string, float[][]> mix = new Dictionary<string, float[][]>
    {
        { "Easy", new[] { new[] { 1.0f, 6 }, new[] { 1.5f, 7 }, new[] { 2.0f, 7 }, new[] { 2.5f, 2 }, new[] { 3.0f, 4 }, new[] { 3.5f, 5 }, new[] { 4.0f, 4 }, new[] { 4.5f, 9 }, new[] { 5.0f, 3 }, new[] { 5.5f, 2 }, new[] { 6.0f, 4 }, new[] { 6.5f, 5 }, new[] { 7.0f, 2 }, new[] { 8.0f, 8 } } },
        { "Medium", new[] { new[] { 1.0f, 63 }, new[] { 1.5f, 54 }, new[] { 2.0f, 42 }, new[] { 2.5f, 30 }, new[] { 3.0f, 20 }, new[] { 3.5f, 22 }, new[] { 4.0f, 11 }, new[] { 4.5f, 7 }, new[] { 5.0f, 4 }, new[] { 5.5f, 2 } } },
        { "Hard", new[] { new[] { 1.5f, 1 }, new[] { 2.0f, 18 }, new[] { 2.5f, 20 }, new[] { 3.0f, 13 }, new[] { 3.5f, 18 }, new[] { 4.0f, 12 }, new[] { 4.5f, 14 }, new[] { 5.0f, 15 }, new[] { 5.5f, 9 }, new[] { 6.0f, 6 }, new[] { 6.5f, 4 }, new[] { 7.0f, 5 }, new[] { 8.0f, 12 } } },
    };
    static float Draw(string level)
    {
        var m = mix[level]; float total = 0f; foreach (var b in m) total += b[1];
        float r = Random.value * total;
        foreach (var b in m) { r -= b[1]; if (r <= 0f) return b[0] + Random.Range(0f, 0.5f); }
        return 3f;
    }

    /// <summary>true: swings are registered directly at the histogram speeds (an ideal controller velocity). false: the weapon is
    /// moved and PlayerCombat's smoothed weapon velocity is read (the Editor has no controller velocity, so short strokes lose speed).</summary>
    public static bool directMode = true;

    static IEnumerator Level()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string level = LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";
        CombatVerification.frameDt = 1f / 72f; CombatVerification.Prepare();
        yield return Frames(5);
        log.AppendLine($"== {scene} ({level})  loaded through the menu: {(true)}");
        log.Append(SourcesReport());
        var ex = PlayerExertionAudio.Instance;
        var pc = PlayerCombat.Instance;
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var pnl = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null; if (pnl != null) pnl.SetActive(false);
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 20f) { var m = BattleManager.Instance.ActiveMonster; if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; } yield return null; }
        // keep the fight harmless and the monster away from the weapon: swing in the air on the player's side
        var mon = BattleManager.Instance.ActiveMonster;
        mon.attackCooldown = 9999f;
        var ai = mon.GetComponent<MonsterAI>(); ai.enabled = false;
        PlayerHealth.Instance.FullHeal();
        yield return Frames(10);
        log.AppendLine($"  battle active {BattleManager.Instance.IsBattleActive}; exertion component enabled {ex.enabled}; thresholds trigger {ex.swingTriggerSpeed} medium {ex.mediumOn} high {ex.highOn}");

        var root = pc.weaponTransform; var rest = root.localPosition; var restRot = root.localRotation;
        Vector3 baseP = root.position;
        float t = 0f, tMedium = -1f, tFast = -1f; int attempts = 0, belowTrigger = 0;
        float nextSwing = 0.3f; float swingEnd = -1f; Vector3 a = baseP, b = baseP; float swingStart = 0f; float swingDur = 0.2f;
        var timeline = new StringBuilder();
        int hand = 0; float lastLogT = -99f;
        float steady = 45f;
        while (t < steady)
        {
            float dt = Time.deltaTime; t += dt;
            if (directMode && swingEnd < 0f && t >= nextSwing) { }
            if (swingEnd < 0f && t >= nextSwing)
            {
                float speed = Draw(level);
                if (speed < ex.swingTriggerSpeed) belowTrigger++;
                else if (directMode) ex.RegisterSwing(speed);     // what a controller's own velocity would give DetectSwing
                attempts++;
                Vector3 dir = (hand++ % 2 == 0 ? Vector3.right : Vector3.left);
                a = baseP - dir * 0.0f; b = baseP + dir * Mathf.Clamp(speed * 0.18f, 0.3f, 1.2f) + Vector3.up * 0.1f;   // 0.18 s stroke
                swingDur = Vector3.Distance(a, b) / speed; swingStart = t; swingEnd = t + swingDur;
                if (directMode) { nextSwing = t + Random.Range(0.8f, 1.2f); swingEnd = -1f; }
            }
            if (swingEnd >= 0f && !directMode)
            {
                float k = Mathf.Clamp01((t - swingStart) / Mathf.Max(0.02f, swingDur));
                root.position = Vector3.Lerp(a, b, k);
                if (t >= swingEnd) { swingEnd = -1f; root.position = baseP; nextSwing = t + Random.Range(0.8f, 1.2f); }   // about one swing a second
            }
            if (ex.CurrentTier == PlayerExertionAudio.Tier.Medium && tMedium < 0f) tMedium = t;
            if (ex.CurrentTier == PlayerExertionAudio.Tier.High && tFast < 0f) tFast = t;
            if (t - lastLogT >= 5f) { lastLogT = t; timeline.Append($"{t:0}s e{ex.Exertion01:0.00} {ex.CurrentTier} heavy {ex.MediumVolume:0.00} fast {ex.FastVolume:0.00} | "); }
            yield return null;
        }
        log.AppendLine($"  steady swinging, ~1 swing/s, speeds from the live {level} histogram: attempts driven {attempts} (below the {ex.swingTriggerSpeed} m/s trigger {belowTrigger}), counted {ex.SwingsAttempted}; medium tier at {(tMedium >= 0 ? tMedium.ToString("0.0") + " s" : "never")}, fast tier at {(tFast >= 0 ? tFast.ToString("0.0") + " s" : "never")}");
        log.AppendLine("  timeline: " + timeline);
        // stop swinging
        root.position = baseP; float ts = 0f; float tCalm = -1f, tMedAfter = -1f; timeline.Length = 0; lastLogT = -99f;
        while (ts < 28f)
        {
            ts += Time.deltaTime;
            if (ex.CurrentTier == PlayerExertionAudio.Tier.Medium && ex.Exertion01 < ex.highOff && tMedAfter < 0f) tMedAfter = ts;
            if (ex.CurrentTier == PlayerExertionAudio.Tier.Calm && tCalm < 0f) tCalm = ts;
            if (ts - lastLogT >= 5f) { lastLogT = ts; timeline.Append($"{ts:0}s e{ex.Exertion01:0.00} {ex.CurrentTier} heavy {ex.MediumVolume:0.00} fast {ex.FastVolume:0.00} | "); }
            yield return null;
        }
        log.AppendLine($"  after stopping: fast -> medium at {(tMedAfter >= 0 ? tMedAfter.ToString("0.0") : "-")} s, calm at {(tCalm >= 0 ? tCalm.ToString("0.0") : "-")} s (volumes fade 4 s more); {timeline}");
        // low HP forces the fast tier
        var ph = PlayerHealth.Instance; ph.currentHealth = ph.maxHealth * 0.2f;
        float tl = 0f, tLow = -1f; while (tl < 4f) { tl += Time.deltaTime; if (ex.CurrentTier == PlayerExertionAudio.Tier.High && tLow < 0f) tLow = tl; yield return null; }
        log.AppendLine($"  HP 20 %: tier {ex.CurrentTier} after {(tLow >= 0 ? tLow.ToString("0.0") : "-")} s (lowHpActive {ex.LowHpActive}); fast volume {ex.FastVolume:0.00}");
        ph.FullHeal();
        root.localPosition = rest; root.localRotation = restRot;
    }
}
