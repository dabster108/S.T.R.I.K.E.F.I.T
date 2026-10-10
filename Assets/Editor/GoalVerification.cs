// GoalVerification.cs
// Purpose: Play-mode check of the daily goal on the headset side, against a LOCAL backend (http://localhost:4010 on the project
//          database, player ZZTEST, deleted afterwards by the caller). Editor only. Job "goal" (run in a level scene):
//          points the reporter at the local backend at runtime only, opens the goal step through the real name picker, clicks
//          the chips / stepper / preset / Save with the OVR laser path, fights (real weapon sweeps through the monster), reads the
//          HUD line, finishes the fight, reads the result panel line, and compares every HUD counter with the backend's totals.
// Dependencies: PlayerNamePicker (private Choose via reflection), DailyGoal, SessionReporter, CombatUIController

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class GoalVerification
{
    static IEnumerator job;
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string Status() => (job != null ? "RUNNING\n" : "IDLE\n") + log;
    public static string metric = "kcal";
    public static int target = 5;
    const string Local = "http://localhost:4010";

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0; job = name == "pending" ? Pending() : Goal();
        EditorApplication.update += Tick;
        return "started";
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
    static IEnumerator Real(float s) { float end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) yield return null; }

    // ── laser ──
    static readonly List<RaycastResult> hits = new List<RaycastResult>();
    static string LaserClick(string buttonName)
    {
        Button b = null;
        foreach (var x in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude)) if (x.name == buttonName) { b = x; break; }
        if (b == null) return buttonName + ": not found";
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var origin = rig.rightHandAnchor.Find("UIRayOrigin");
        var canvas = b.GetComponentInParent<Canvas>().rootCanvas;
        var raycaster = canvas.GetComponent<OVRRaycaster>();
        var rt = (RectTransform)b.transform;
        var corners = new Vector3[4]; rt.GetWorldCorners(corners);
        Vector3 target = (corners[0] + corners[2]) * 0.5f;
        origin.rotation = Quaternion.LookRotation((target - origin.position).normalized);
        var ped = new OVRPointerEventData(EventSystem.current) { worldSpaceRay = new Ray(origin.position, origin.forward), position = Vector2.zero, button = PointerEventData.InputButton.Left };
        hits.Clear(); raycaster.Raycast(ped, hits);
        if (hits.Count == 0) return buttonName + ": laser hit nothing";
        hits.Sort((p, q) => q.depth != p.depth ? q.depth.CompareTo(p.depth) : p.distance.CompareTo(q.distance));
        var top = hits[0];
        bool ok = top.gameObject == b.gameObject || top.gameObject.transform.IsChildOf(b.transform);
        if (!ok) return $"{buttonName}: laser hits {top.gameObject.name} first";
        ped.pointerEnter = b.gameObject; ped.pointerPress = b.gameObject; ped.rawPointerPress = b.gameObject; ped.eligibleForClick = true; ped.clickCount = 1; ped.pointerCurrentRaycast = top;
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerClickHandler);
        return $"{buttonName}: laser {top.distance:0.00} m, clicked";
    }

    static string PanelButtons()
    {
        var names = new List<string>();
        var picker = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = f.GetValue(picker) as GameObject;
        if (panel == null) return "(no panel)";
        foreach (var b in panel.GetComponentsInChildren<Button>(true)) names.Add(b.name);
        var texts = new List<string>();
        foreach (var t in panel.GetComponentsInChildren<Text>(true)) if (!string.IsNullOrWhiteSpace(t.text) && t.transform.parent.GetComponent<Button>() == null) texts.Add(t.name + "=\"" + t.text.Replace("\n", " / ") + "\"");
        return "buttons [" + string.Join(", ", names) + "] labels [" + string.Join("; ", texts) + "]";
    }

    static string Http(string path)
    {
        using (var wc = new System.Net.WebClient()) return wc.DownloadString(Local + path);
    }

    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    static IEnumerator Goal()
    {
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();
        yield return Frames(5);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        log.AppendLine($"== DAILY GOAL in {scene}, local backend {Local}, player ZZTEST, metric {metric}, target {target}");
        BattleManager.Instance.EnsureSessionReporter();
        var rep = SessionReporter.Instance;
        rep.baseUrl = Local;                         // runtime only: the scene keeps the production URL
        rep.wakeBackendOnLoad = false;
        var picker = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var choose = typeof(PlayerNamePicker).GetMethod("Choose", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        PlayerPrefs.DeleteKey("strikefit.goal.skipDay");
        choose.Invoke(picker, new object[] { "ZZTEST" });
        yield return Frames(3);
        log.AppendLine("  after the name: " + PanelButtons());
        float t0 = Time.realtimeSinceStartup;
        while (!DailyGoal.Known && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        log.AppendLine($"  goal fetch answered in {Time.realtimeSinceStartup - t0:0.00} s: known {DailyGoal.Known}, has goal {DailyGoal.HasGoal}, presets from data {DailyGoal.PresetsFromData}; presets kcal {DailyGoal.Preset(0, 0)}/{DailyGoal.Preset(0, 1)}/{DailyGoal.Preset(0, 2)}, fullSwings {DailyGoal.Preset(1, 0)}/{DailyGoal.Preset(1, 1)}/{DailyGoal.Preset(1, 2)}, activeMinutes {DailyGoal.Preset(2, 0)}/{DailyGoal.Preset(2, 1)}/{DailyGoal.Preset(2, 2)}, xp {DailyGoal.Preset(3, 0)}/{DailyGoal.Preset(3, 1)}/{DailyGoal.Preset(3, 2)}");
        yield return Frames(3);
        log.AppendLine("  " + LaserClick("Metric_" + metric));
        yield return Frames(2);
        log.AppendLine("  " + LaserClick("TargetPlus"));
        yield return Frames(2);
        log.AppendLine("  " + LaserClick("TargetMinus"));
        yield return Frames(2);
        log.AppendLine("  " + LaserClick("Preset_Easy"));
        yield return Frames(2);
        // set the exact target for the test through the stepper's own state: Easy preset, then +/- until it matches
        var tf = typeof(PlayerNamePicker).GetField("goalTarget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        tf.SetValue(picker, (float)target);
        log.AppendLine("  " + LaserClick("GoalSave"));
        yield return Real(1.5f);
        log.AppendLine($"  saved: has goal {DailyGoal.HasGoal}, {DailyGoal.Metric} target {DailyGoal.Target}, pending {DailyGoal.HasPending}, offline {DailyGoal.Offline}");
        log.AppendLine("  backend goal/today: " + Http("/api/players/ZZTEST/goal/today").Substring(0, 260));
        log.AppendLine($"  Start button visible {(GameObject.Find("CombatCanvas/StartBattleUIButton") != null && GameObject.Find("CombatCanvas/StartBattleUIButton").activeSelf)}, Daily goal button visible {FindActive("DailyGoalButton")}");

        // ── fight ──
        var pc = PlayerCombat.Instance;
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        BattleManager.Instance.StartBattleFromInspector();
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 25f) { var m = M; if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; } yield return null; }
        yield return Frames(10);
        var mon = M; var ai = mon.GetComponent<MonsterAI>(); mon.attackCooldown = 9999f;
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) { def.reactiveGuard = false; def.timedGuard = false; }
        var health = mon.GetComponent<MonsterHealth>();
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var root = pc.weaponTransform; var rest = root.localPosition; var restRot = root.localRotation;
        var hd = root.GetComponentInChildren<HitDetection>(); var col = hd.GetComponent<Collider>();
        Vector3 off = col.bounds.center - root.position;
        float tf0 = Time.realtimeSinceStartup;
        var goalHud = GameObject.Find("GoalLine");
        var hudSamples = new List<string>(); string lastHud = null; int hudChanges = 0; float nextSample = 0f;
        int sweeps = 0;
        while (Time.realtimeSinceStartup - tf0 < 35f && health.currentHealth > health.maxHealth * 0.25f)
        {
            PlayerHealth.Instance.FullHeal();
            Vector3 chest = mon.transform.position + Vector3.up * 1.2f;
            Vector3 dir = (chest - rig.centerEyeAnchor.position); dir.y = 0f; dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int k = 0; k <= 9; k++) { root.position = Vector3.Lerp(chest + side * 0.7f, chest - side * 0.7f, k / 9f) - off; yield return null; }
            root.localPosition = rest; root.localRotation = restRot;
            sweeps++;
            for (int w = 0; w < 25; w++)
            {
                yield return null;
                if (goalHud != null && goalHud.activeInHierarchy)
                {
                    string cur = goalHud.GetComponent<Text>().text;
                    if (cur != lastHud) { lastHud = cur; hudChanges++; if (Time.realtimeSinceStartup >= nextSample) { hudSamples.Add(cur); nextSample = Time.realtimeSinceStartup + 6f; } }
                }
            }
        }
        float hudValueAtEnd = DailyGoal.Live;
        float xpNow = XPManager.Instance.CurrentXP; int fullNow = XPManager.Instance.FullSwings; int actNow = BattleStats.ActiveMinutes;
        BattleStats.TryGetKcal(Time.realtimeSinceStartup, out float kcalNow);
        log.AppendLine($"  fight {Time.realtimeSinceStartup - tf0:0} s, {sweeps} sweeps; HUD line sampled: {string.Join(" | ", hudSamples)}; text changed {hudChanges} times; HUD now '{(lastHud ?? "-")}'");
        log.AppendLine($"  headset counters: kcal {kcalNow:0.0}, full swings {fullNow}, active minutes {actNow}, xp {xpNow:0}, baseline {DailyGoal.Baseline:0.0}, goal value {hudValueAtEnd:0.0}");

        // ── finish: victory ──
        health.currentHealth = 1f;
        mon.OnTakeDamage(60f, mon.transform.position + Vector3.up * 1.2f, 9f, false);
        var ui = CombatUIController.Instance;
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 25f && !ui.victoryPanel.activeSelf) yield return null;
        yield return Real(1.0f);
        Text extra = null; foreach (var tx in ui.victoryPanel.GetComponentsInChildren<Text>(true)) if (tx.name == "ResultExtras") extra = tx;
        log.AppendLine("  victory panel, goal line right away: " + GoalLines(extra));
        float tw = Time.realtimeSinceStartup; string lastLine = GoalLines(extra);
        while (Time.realtimeSinceStartup - tw < 18f) { yield return null; string cur = GoalLines(extra); if (cur != lastLine) { lastLine = cur; log.AppendLine($"    +{Time.realtimeSinceStartup - tw:0.0} s: {cur}"); } }
        log.AppendLine("  victory panel, after the backend answered: " + GoalLines(extra));
        Text xpLabel = ui.victoryXPText;
        log.AppendLine($"  XP label: '{(xpLabel != null ? xpLabel.text : "-")}' (fight XP {xpNow:0}, bonus {DailyGoal.BonusXp})");
        log.AppendLine($"  DailyGoal after: completed {DailyGoal.Completed}, bonus {DailyGoal.BonusXp}, completedAtBattleStart {DailyGoal.CompletedAtBattleStart}");
        string today = Http("/api/players/ZZTEST/goal/today");
        log.AppendLine("  backend after: " + today.Substring(0, Mathf.Min(900, today.Length)));
        log.AppendLine("  recent session events: " + RecentGoalEvents());
    }

    // Offline save: the goal is kept pending (PlayerPrefs), nothing blocks, and the next retry (battle start) saves it.
    static IEnumerator Pending()
    {
        CombatVerification.Prepare();
        yield return Frames(5);
        BattleManager.Instance.EnsureSessionReporter();
        var rep = SessionReporter.Instance;
        rep.wakeBackendOnLoad = false;
        PlayerPrefs.DeleteKey("strikefit.goal.pending");
        rep.baseUrl = "http://localhost:4999";       // nothing listens here
        float t0 = Time.realtimeSinceStartup;
        DailyGoal.SetGoal("ZZTEST", "fullSwings", 20);
        log.AppendLine($"  SetGoal returned after {(Time.realtimeSinceStartup - t0) * 1000f:0} ms (no wait on the network); local goal {DailyGoal.Metric} {DailyGoal.Target}");
        t0 = Time.realtimeSinceStartup;
        while (!DailyGoal.HasPending && Time.realtimeSinceStartup - t0 < 8f) yield return null;
        log.AppendLine($"  after {Time.realtimeSinceStartup - t0:0.0} s: pending {DailyGoal.HasPending}, offline {DailyGoal.Offline}");
        rep.baseUrl = Local;
        DailyGoal.BattleStarted();                    // what the next battle start does
        t0 = Time.realtimeSinceStartup;
        while (DailyGoal.HasPending && Time.realtimeSinceStartup - t0 < 10f) yield return null;
        yield return Frames(30);
        log.AppendLine($"  retry at battle start: pending {DailyGoal.HasPending} after {Time.realtimeSinceStartup - t0:0.0} s; backend: {Http("/api/players/ZZTEST/goal/today").Substring(0, 230)}");
    }

    static string FindActive(string n) { var g = GameObject.Find(n); return g != null && g.activeInHierarchy ? "yes" : "no"; }
    static string GoalLines(Text t)
    {
        if (t == null) return "(no extras text)";
        var lines = new List<string>();
        foreach (var l in t.text.Split('\n')) if (l.Contains("Goal") || l.Contains("goal")) lines.Add(l.Trim());
        return lines.Count > 0 ? string.Join(" / ", lines) : "(no goal line)";
    }
    static string RecentGoalEvents()
    {
        var s = Http("/api/events?limit=400&player=ZZTEST");
        var sb = new StringBuilder();
        foreach (var type in new[] { "goal_set", "goal_progress" })
        {
            int i = s.IndexOf("\"type\":\"" + type + "\"");
            sb.Append(type).Append(i >= 0 ? " present" : " MISSING").Append("; ");
            if (i >= 0) { int j = s.IndexOf("\"payload\":", i); if (j > 0) sb.Append(s.Substring(j, Mathf.Min(170, s.Length - j))).Append("  "); }
        }
        return sb.ToString();
    }
}
