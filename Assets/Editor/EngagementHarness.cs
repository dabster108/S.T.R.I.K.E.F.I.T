// EngagementHarness.cs
// Purpose: Play-mode checks and captures for the engagement features (result screen, rank, daily goals, perfect clash, telegraph rim,
//          glow palettes), driven over Unity MCP (Editor only). Configure() seeds the test player's saved progress first (fresh,
//          near a rank threshold, a best to beat, goals one step from done); Run(job, dir) then plays it, ending with DONE in Status():
//            "menu"      MainMenu: capture the home page and the progress panel, print their texts, step the glow palette arrow.
//            "fight"     level: a scripted fight with real swings (CombatVerification "sim"), then the outcome (victory / defeat);
//                        the HUD banner and the result panel are captured; the tracker's result and the engagement summary logged.
//            "abandon"   level: a short fight, pause -> Level Select -> Yes; the abandoned result and summary (no result screen).
//            "perfect"   level: a forced attack, a clash raised through HitDetection.WeaponClashed (every listener runs) far too
//                        early, then one just before the hit frame; XP, flash, banner, telemetry and the later stagger are checked.
//            "outline"   level: the telegraph with the rim, then with it off (runtime palette copy), captured from the head.
//            "resultgc"  level: fight, outcome, then the GC allocated per frame while the result screen is up (profiler).
//          Captures go to the folder passed as dir (default Captures/Engagement). Telemetry: arm EditorTelemetryGuard first.
// Attach to: nothing (static, Editor only)

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using StrikeFit.Engagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class EngagementHarness
{
    static IEnumerator job;
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string Dir = "Captures/Engagement";
    public static string Status() => (job != null ? "RUNNING t=" + Time.time.ToString("0.0") + "\n" : "IDLE\n") + log;

    /// <summary>The name the fights are played under (a disposable ZZTEST name).</summary>
    public static string TestPlayer = "ZZTEST-engage";

    // seeds and fight options, set by Configure before Run
    static bool seedReset, seedGoalsNear;
    static int seedEffort = -1, seedBest = -1;
    static string outcome = "victory", tag = "";
    static float simSeconds = 20f;

    /// <param name="effort">Saved effort points to start from (-1 leaves them).</param>
    /// <param name="best">Saved best on this level (-1 leaves it).</param>
    public static string Configure(bool reset, int effort, int best, bool goalsNearDone, string outcomeName, float seconds, string captureTag)
    {
        seedReset = reset; seedEffort = effort; seedBest = best; seedGoalsNear = goalsNearDone;
        outcome = outcomeName; simSeconds = seconds; tag = captureTag ?? "";
        return $"configured reset {reset} effort {effort} best {best} goalsNear {goalsNearDone} outcome {outcome} sim {seconds} s tag '{tag}'";
    }

    public static string Run(string name, string dir = null)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        if (!string.IsNullOrEmpty(dir)) Dir = dir;
        Directory.CreateDirectory(Dir);
        job = null; log.Length = 0;
        switch (name)
        {
            case "menu": job = MenuJob(); break;
            case "fight": job = FightJob(); break;
            case "abandon": job = AbandonJob(); break;
            case "perfect": job = PerfectJob(); break;
            case "outline": job = OutlineJob(); break;
            case "resultgc": job = ResultGcJob(); break;
            default: return "unknown job " + name;
        }
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        return "started " + name;
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { job = null; EditorApplication.update -= Tick; return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { if (!job.MoveNext()) { log.AppendLine("DONE"); job = null; EditorApplication.update -= Tick; } }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); job = null; EditorApplication.update -= Tick; }
    }

    // ── helpers ──
    public static Transform Head()
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
        return Camera.main != null ? Camera.main.transform : null;
    }

    /// <summary>Renders the view from the head toward a world point (or straight ahead when look is null).</summary>
    public static void Capture(string name, Vector3? look = null, float fov = 70f)
    {
        var head = Head(); if (head == null) return;
        var go = new GameObject("ZZEngagementCam"); var cam = go.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.fieldOfView = fov; cam.nearClipPlane = 0.05f;
        go.transform.SetPositionAndRotation(head.position, look.HasValue ? Quaternion.LookRotation(look.Value - head.position) : Quaternion.Euler(0f, head.eulerAngles.y, 0f));
        var rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render(); cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(1280, 960, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
        cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        log.AppendLine("  capture " + name + ".png");
    }

    static Vector3 AheadOfHead(float up)
    {
        var head = Head();
        return head.position + Quaternion.Euler(0f, head.eulerAngles.y, 0f) * Vector3.forward * 2f + Vector3.up * up;
    }

    /// <summary>Every visible Text / TMP text under a root, top to bottom, one line each (for checking what a panel says).</summary>
    public static string Texts(GameObject root)
    {
        if (root == null) return "(none)";
        var sb = new StringBuilder();
        foreach (var t in root.GetComponentsInChildren<Text>(false)) if (t.enabled && !string.IsNullOrEmpty(t.text)) sb.Append("    [").Append(t.name).Append("] ").AppendLine(t.text.Replace("\n", " | "));
        foreach (var t in root.GetComponentsInChildren<TMPro.TMP_Text>(false)) if (t.enabled && !string.IsNullOrEmpty(t.text)) sb.Append("    [").Append(t.name).Append("] ").AppendLine(t.text.Replace("\n", " | "));
        return sb.ToString();
    }

    public static MonsterController Monster => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
    static string Level => LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";

    static void ApplySeeds()
    {
        if (seedReset) PlayerProgressStore.Forget(TestPlayer);
        var d = PlayerProgressStore.Load(TestPlayer);
        d.RollDay(PlayerProgressStore.Today);
        if (seedEffort >= 0) { d.effortPoints = seedEffort; d.celebratedRank = RankLadder.IndexFor(seedEffort); }
        if (seedBest >= 0) d.SetBest(Level, seedBest);
        if (seedGoalsNear)
            foreach (var g in DailyGoalRules.ForDay(d.day))
            {
                var st = d.Goal(g.key, true); st.done = false;
                st.value = g.metric == GoalMetric.FullSwings || g.metric == GoalMetric.Fights ? g.target - 1f : 0f;
            }
        PlayerProgressStore.Save(TestPlayer, d);
        log.AppendLine($"seeded {TestPlayer}: effort {d.effortPoints} (celebrated {RankLadder.Rungs[d.celebratedRank].name}), best on {Level} {d.BestOn(Level)}, fights today {d.fightsToday}, goals near done {seedGoalsNear}");
    }

    /// <summary>Chooses the test player in the name picker, applies the seeds, equips a Level 3 weapon pair and starts the fight;
    /// returns once the monster's entrance is over.</summary>
    public static IEnumerator BeginFight(string weapon = "Mace")
    {
        Application.runInBackground = true;
        Time.captureDeltaTime = 1f / 30f;
        foreach (var r in Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include))
            if (r.baseUrl != EditorTelemetryGuard.DeadUrl) { log.AppendLine("WARNING reporter was " + r.baseUrl + " -> redirected"); r.baseUrl = EditorTelemetryGuard.DeadUrl; }
        ApplySeeds();
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        if (pnp != null)
        {
            var choose = typeof(PlayerNamePicker).GetMethod("Choose", BindingFlags.NonPublic | BindingFlags.Instance);
            if (choose != null && !string.IsNullOrEmpty(TestPlayer)) choose.Invoke(pnp, new object[] { TestPlayer });
            pnp.enabled = false;
            var f = typeof(PlayerNamePicker).GetField("panel", BindingFlags.NonPublic | BindingFlags.Instance);
            var panel = f != null ? f.GetValue(pnp) as GameObject : null;
            if (panel != null) panel.SetActive(false);
        }
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0 && System.Enum.TryParse(weapon, out WeaponType wt)) pc.EquipWeaponSet(wt);
        yield return null; yield return null;
        BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 30f)
        {
            var m = Monster;
            if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        for (int i = 0; i < 10; i++) yield return null;
        var tr = EngagementTracker.Instance;
        log.AppendLine("fight ready: " + (Monster != null ? Monster.name : "no monster") + " as " + (SessionReporter.Instance != null ? SessionReporter.Instance.sessionPlayerName : "?") +
                       (tr != null ? $"; tracker running {tr.Running}, effort {tr.EffortNow}, goals: {GoalList(tr.Goals)}" : "; no tracker"));
    }

    static string GoalList(GoalDef[] g)
    {
        if (g == null) return "(none)";
        var sb = new StringBuilder();
        foreach (var x in g) sb.Append(x.title).Append(" (").Append(x.metric).Append(' ').Append(x.target).Append(x.fromServer ? ", server" : ", local").Append(") ");
        return sb.ToString();
    }

    static EngagementBanner HudBanner()
    {
        var ui = CombatUIController.Instance;
        if (ui == null || ui.victoryPanel == null) return null;
        var c = ui.victoryPanel.GetComponentInParent<Canvas>(true);
        if (c == null) return null;
        foreach (var b in c.GetComponentsInChildren<EngagementBanner>(true)) if (b.name == "EngagementBanner") return b;
        return null;
    }

    /// <summary>CombatVerification's scripted fight (real swings through the game's hit detection) for `seconds`; the HUD banner is
    /// captured the first time it is fully shown.</summary>
    static IEnumerator Sim(float seconds, string captureName)
    {
        CombatVerification.simSeconds = seconds;
        CombatVerification.Run("sim");
        bool shot = false;
        while (CombatVerification.Status().StartsWith("RUNNING"))
        {
            var b = HudBanner();
            if (!shot && b != null && b.Visible)
            {
                shot = true;
                Capture(captureName, AheadOfHead(0.55f), 62f);
                log.AppendLine("  HUD banner: " + b.LastText);
            }
            yield return null;
        }
        var xp = XPManager.Instance; var hb = HudBanner();
        log.AppendLine($"  sim done: hits {SessionReporter.Instance.hitCount}, full {xp.FullSwings}, best combo {xp.BestCombo}, XP {xp.CurrentXP}, score {BattleManager.Instance.CurrentScore}" +
                       (hb != null ? $", HUD banners shown {hb.Shown} (last '{hb.LastText}')" : ", no HUD banner"));
    }

    static IEnumerator Outcome(bool win)
    {
        var m = Monster; if (m == null) { log.AppendLine("no monster"); yield break; }
        if (win)
        {
            // As StoryFlowTrace's debug win: straight to the health (the controller caps single hits), then the controller's death.
            var h = m.GetComponent<MonsterHealth>();
            if (!h.IsDead) h.TakeDamage(h.maxHealth, m.transform.position + Vector3.up);
            if (h.IsDead && m.isAlive) m.OnDeath();
        }
        else { var ph = PlayerHealth.Instance; ph.TakeDamage(ph.currentHealth + 1f, true); }
        var ui = CombatUIController.Instance;
        float t0 = Time.time;   // game time: the victory delay and the finale run on it
        GameObject panel = null;
        while (Time.time - t0 < 20f)
        {
            panel = win ? ui.victoryPanel : ui.defeatPanel;
            if (panel != null && panel.activeInHierarchy) break;
            yield return null;
        }
        if (panel == null || !panel.activeInHierarchy) { log.AppendLine("no " + (win ? "Victory" : "Defeat") + " panel after 20 s; battle active " + BattleManager.Instance.IsBattleActive + ", outcome handled " + BattleManager.Instance.OutcomeHandled); yield break; }
        log.AppendLine((win ? "Victory" : "Defeat") + " panel up");
    }

    static void LogResult()
    {
        var r = EngagementTracker.Instance != null ? EngagementTracker.Instance.Result : null;
        if (r == null) { log.AppendLine("  no tracker result"); return; }
        log.AppendLine($"  result: {r.outcome} on {r.levelName}, counted {r.counted}, score {r.score}, best before {r.previousBest} -> {r.bestAfter} ({r.bestKind}), effort {r.effortBefore} -> {r.effortAfter} (+{r.effortGained}), rank {RankLadder.Rungs[r.rankBefore].name} -> {RankLadder.Rungs[r.rankAfter].name}, RANK UP {r.rankUp}");
        log.AppendLine($"  goals ({(r.goalsFromServer ? "server" : "local")}): completed this fight {r.goalsCompletedThisFight}, bonus XP {r.goalBonusXp}; perfect clashes {r.perfectClashes}; fights today {r.fightsToday}; next goal: {r.nextGoal}");
        if (r.goals != null)
            for (int i = 0; i < r.goals.Length; i++) log.AppendLine($"    {r.goals[i].title}: {r.goalValues[i]:0.#} / {r.goals[i].target} done {r.goalDone[i]} (this fight {r.goalDoneThisFight[i]})");
        log.AppendLine("  summary: " + SessionReporter.LastEngagementSummaryJson);
    }

    // ── jobs ──
    static IEnumerator MenuJob()
    {
        Application.runInBackground = true;
        ApplySeeds();
        PlayerNameStore.MarkUsed(TestPlayer);
        var menu = Object.FindAnyObjectByType<MainMenuController>();
        for (int i = 0; i < 20; i++) yield return null;
        if (menu != null) menu.RefreshPlayingAs();
        for (int i = 0; i < 20; i++) yield return null;
        var panel = MenuProgressPanel.Instance;
        log.AppendLine("menu page: " + (menu != null && menu.CurrentPage != null ? menu.CurrentPage.name : "none") + ", progress panel " + (panel != null && panel.Canvas != null));
        Capture("menu_home");
        if (menu != null && menu.homePage != null) log.Append(Texts(menu.homePage.transform.root.gameObject));
        if (panel != null && panel.Canvas != null)
        {
            Capture("menu_progress_panel", panel.Canvas.transform.position, 62f);
            log.AppendLine("progress panel:");
            log.Append(Texts(panel.Canvas.gameObject));
            int before = GlowCosmetics.Index;
            panel.StepPalette(1);
            var d = PlayerProgressStore.Load(TestPlayer);
            log.AppendLine($"  palette > : wearing {GlowPalettes.All[GlowCosmetics.Index].name} (was {GlowPalettes.All[before].name}), saved '{d.glowPalette}'");
            yield return null;
            Capture("menu_progress_panel_after_arrow", panel.Canvas.transform.position, 62f);
            log.Append(Texts(panel.Canvas.gameObject));
        }
    }

    static IEnumerator FightJob()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string pre = scene + (tag.Length > 0 ? "_" + tag : "");
        var b = BeginFight(); while (b.MoveNext()) yield return b.Current;
        Capture(pre + "_hud", AheadOfHead(0.55f), 62f);
        var s = Sim(simSeconds, pre + "_hud_banner"); while (s.MoveNext()) yield return s.Current;
        bool win = outcome == "victory";
        var o = Outcome(win); while (o.MoveNext()) yield return o.Current;
        var ui = CombatUIController.Instance;
        var panel = win ? ui.victoryPanel : ui.defeatPanel;
        if (panel == null || !panel.activeInHierarchy) yield break;
        float s0 = Time.time; bool bannerShot = false;
        while (Time.time - s0 < 3.5f)
        {
            var rb = panel.GetComponentInChildren<EngagementBanner>(true);
            if (!bannerShot && rb != null && rb.Visible && Time.time - s0 > 0.9f) { bannerShot = true; Capture(pre + "_result_banner", panel.transform.position, 62f); log.AppendLine("  result banner: " + rb.LastText); }
            yield return null;
        }
        Capture(pre + "_result", panel.transform.position, 62f);
        log.AppendLine("Result panel texts:");
        log.Append(Texts(panel));
        LogResult();
    }

    static IEnumerator AbandonJob()
    {
        var b = BeginFight(); while (b.MoveNext()) yield return b.Current;
        var s = Sim(simSeconds, "abandon_hud_banner"); while (s.MoveNext()) yield return s.Current;
        var pm = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
        if (pm == null) { log.AppendLine("no PauseMenu"); yield break; }
        string level = Level;
        pm.Pause("engagement test");
        Button ls = null, yes = null;
        foreach (var bt in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (bt.transform.root.name != "PauseCanvas") continue;
            if (bt.name == "LevelSelectButton") ls = bt;
            if (bt.name == "YesButton") yes = bt;
        }
        if (ls == null || yes == null) { log.AppendLine("pause buttons not found"); yield break; }
        ls.onClick.Invoke(); yield return null; yes.onClick.Invoke();
        var ui = CombatUIController.Instance;
        log.AppendLine($"abandoned through the pause menu (Level Select -> Yes); result panels shown: victory {ui != null && ui.victoryPanel.activeInHierarchy}, defeat {ui != null && ui.defeatPanel.activeInHierarchy}");
        LogResult();
        float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 4f) yield return null;
        var d = PlayerProgressStore.Load(TestPlayer);
        log.AppendLine($"scene now {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}; saved: effort {d.effortPoints}, best on {level} {d.BestOn(level)}, fights today {d.fightsToday}");
    }

    static readonly FieldInfo clashEvent = typeof(HitDetection).GetField("WeaponClashed", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    // Raises HitDetection.WeaponClashed as Clash() does, so every listener runs (parry, flashes, sparks, telemetry, the tracker).
    static void RaiseClash(HitDetection source, Transform monsterWeapon, float speed)
    {
        SetProp(source, "LastClashWeapon", monsterWeapon);
        SetProp(source, "LastClashPoint", monsterWeapon.position);
        var d = clashEvent != null ? clashEvent.GetValue(null) as System.Action<HitDetection, float> : null;
        d?.Invoke(source, speed);
    }

    static void SetProp(object o, string name, object v)
    {
        var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = p != null ? p.GetSetMethod(true) : null;
        if (setter != null) setter.Invoke(o, new[] { v });
    }

    static bool Force(MonsterController mon, MonsterAI ai, int row)
    {
        float cd = mon.attackCooldown;
        mon.attackCooldown = 0f;
        bool ok = ai.DebugForceAttack(row);
        mon.attackCooldown = cd;
        return ok;
    }

    static int FirstBlockableRow(MonsterAI ai)
    {
        for (int i = 0; i < ai.attackTable.Count; i++) if (!ai.attackTable[i].unblockable && ai.attackTable[i].rangedKind == MonsterAI.RangedKind.None) return i;
        return 0;
    }

    static IEnumerator WaitIdle(MonsterAI ai, float timeout)
    {
        float t0 = Time.time;
        while (Time.time - t0 < timeout && (ai.IsBusy || ai.CurrentAttack != null || ai.IsDefending || ai.IsStaggered)) yield return null;
    }

    static int CountPerfectEvents(List<string> buf, int from)
    {
        int n = 0;
        for (int i = Mathf.Min(from, buf.Count); i < buf.Count; i++) if (buf[i].Contains("perfect_clash")) n++;
        return n;
    }

    static IEnumerator PerfectJob()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var b = BeginFight(); while (b.MoveNext()) yield return b.Current;
        var m = Monster; var ai = m.GetComponent<MonsterAI>(); var comp = m.GetComponent<MonsterColorCompositor>();
        var source = PlayerCombat.Instance.GetComponentInChildren<HitDetection>();
        Transform monsterWeapon = null;
        foreach (var c in m.GetComponentsInChildren<Collider>(true)) if (c.CompareTag("MonsterWeapon")) { monsterWeapon = c.transform; break; }
        if (source == null || monsterWeapon == null) { log.AppendLine("no player HitDetection / monster weapon"); yield break; }
        var rep = SessionReporter.Instance;
        var buf = typeof(SessionReporter).GetField("eventBuffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rep) as List<string>;
        var tr = EngagementTracker.Instance;
        var cfg = EngagementConfig.Get();
        int row = FirstBlockableRow(ai);
        log.AppendLine($"attack row {row} {ai.attackTable[row].name}; window {cfg.perfectClashWindowSeconds} s, bonus {cfg.perfectClashXp} XP");

        // 1) a clash far too early (the wind-up): not perfect (it may still be the game's ordinary parry)
        var w = WaitIdle(ai, 8f); while (w.MoveNext()) yield return w.Current;
        bool forced = Force(m, ai, row);
        float t0 = Time.time; while (Time.time - t0 < 3f && !(ai.CurrentAttack != null && ai.CurrentHitTime > ai.AiTime + 0.3f)) yield return null;
        float early = ai.CurrentHitTime - ai.AiTime;
        int xp0 = XPManager.Instance.CurrentXP, pc0 = tr.PerfectClashes, ev0 = buf.Count;
        RaiseClash(source, monsterWeapon, 3.5f);
        log.AppendLine($"forced {forced}; early clash {early:0.00} s before the hit frame: perfect {tr.PerfectClashes > pc0}, XP +{XPManager.Instance.CurrentXP - xp0}, perfect_clash events {CountPerfectEvents(buf, ev0)}");
        w = WaitIdle(ai, 8f); while (w.MoveNext()) yield return w.Current;
        for (int i = 0; i < 20; i++) yield return null;

        // 2) a clash during the strike, just before the hit frame: perfect
        forced = Force(m, ai, row);
        t0 = Time.time;
        float off = -1f;
        while (Time.time - t0 < 6f)
        {
            if (ai.CurrentAttack != null && ai.InStrikeWindow) { off = ai.CurrentHitTime - ai.AiTime; if (off >= 0f && off <= cfg.perfectClashWindowSeconds * 0.6f) break; }
            yield return null;
        }
        xp0 = XPManager.Instance.CurrentXP; pc0 = tr.PerfectClashes; int compPc0 = comp != null ? comp.PerfectClashes : 0; ev0 = buf.Count;
        bool inStrike = ai.InStrikeWindow;
        RaiseClash(source, monsterWeapon, 3.5f);
        int events = CountPerfectEvents(buf, ev0);
        if (events > 0) log.AppendLine("  telemetry: " + buf[buf.Count - 1]);
        yield return null; yield return null;
        Capture(scene + "_perfect_clash_flash", m.transform.position + Vector3.up * 1.2f, 55f);
        var hb = HudBanner();
        log.AppendLine($"forced {forced}; timed clash {off:0.000} s before the hit frame, in strike {inStrike}: perfect {tr.PerfectClashes > pc0}, XP +{XPManager.Instance.CurrentXP - xp0}, monster flash count +{(comp != null ? comp.PerfectClashes - compPc0 : -1)}, banner '{(hb != null ? hb.LastText : "none")}' visible {(hb != null && hb.Visible)}, perfect_clash events {events}");
        for (int i = 0; i < 6; i++) yield return null;
        if (hb != null && hb.Visible) Capture(scene + "_perfect_clash_banner", AheadOfHead(0.55f), 62f);

        // 3) a second clash in the same attack: no second bonus
        pc0 = tr.PerfectClashes; xp0 = XPManager.Instance.CurrentXP;
        RaiseClash(source, monsterWeapon, 3.5f);
        log.AppendLine($"second clash in the same attack: another perfect {tr.PerfectClashes > pc0}, XP +{XPManager.Instance.CurrentXP - xp0}");

        // 4) the stagger comes once the attack is over (never cutting the swing, never touching its damage)
        bool staggered = false; float tEnd = -1f; t0 = Time.time;
        while (Time.time - t0 < 6f && !staggered) { if (ai.CurrentAttack == null && tEnd < 0f) tEnd = Time.time; if (ai.IsStaggered) staggered = true; yield return null; }
        log.AppendLine($"stagger: {staggered} ({(tEnd > 0f ? (Time.time - tEnd).ToString("0.00") + " s after the attack ended" : "attack still running")})");
        log.AppendLine($"tracker perfect clashes {tr.PerfectClashes}");
    }

    static bool Telegraphing(MonsterColorCompositor comp)
    {
        var l = comp.Stack.Get(StrikeFit.ColorFx.ColorLayer.Telegraph);
        float t = Time.time - l.start - l.delay;
        return l.active && t >= 0f && (l.duration <= 0f || t < l.duration);
    }

    static IEnumerator OutlineJob()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var b = BeginFight(); while (b.MoveNext()) yield return b.Current;
        var m = Monster; var ai = m.GetComponent<MonsterAI>(); var comp = m.GetComponent<MonsterColorCompositor>();
        int row = FirstBlockableRow(ai);
        var original = comp.palette;
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) { var copy = Object.Instantiate(original); copy.telegraphOutline = false; comp.palette = copy; }
            var w = WaitIdle(ai, 8f); while (w.MoveNext()) yield return w.Current;
            for (int i = 0; i < 10; i++) yield return null;
            Force(m, ai, row);
            // every frame (sampled after the frame's LateUpdate) until the telegraph is over: is the rim on exactly while it runs?
            int frames = 0, on = 0, mismatch = 0; bool shot = false; float t0 = Time.time;
            yield return null;
            while (Time.time - t0 < 4f)
            {
                bool tel = Telegraphing(comp);
                if (!tel && frames > 0) break;
                if (tel)
                {
                    frames++;
                    if (comp.OutlineShowing) on++;
                    if (comp.OutlineShowing != comp.palette.telegraphOutline) mismatch++;
                    if (!shot && comp.Stack.Strength(StrikeFit.ColorFx.ColorLayer.Telegraph, Time.time) > 0.85f)
                    {
                        shot = true;
                        Capture(scene + (pass == 0 ? "_telegraph_with_rim" : "_telegraph_without_rim"), m.transform.position + Vector3.up * 1.15f, 50f);
                    }
                }
                yield return null;
            }
            log.AppendLine($"pass {pass}: outline flag {comp.palette.telegraphOutline}: telegraph frames {frames}, rim on {on}, frames where the rim disagreed with the flag {mismatch}");
            yield return null;
            log.AppendLine($"  first frame after the telegraph: rim showing {comp.OutlineShowing}");
        }
        comp.palette = original;
    }

    static IEnumerator ResultGcJob()
    {
        var b = BeginFight(); while (b.MoveNext()) yield return b.Current;
        var s = Sim(simSeconds, "gc_hud_banner"); while (s.MoveNext()) yield return s.Current;
        var o = Outcome(outcome == "victory"); while (o.MoveNext()) yield return o.Current;
        for (int i = 0; i < 60; i++) yield return null;   // past the result screen's build and first animation frames
        UnityEditorInternal.ProfilerDriver.ClearAllFrames();
        UnityEditorInternal.ProfilerDriver.profileEditor = false;
        UnityEditorInternal.ProfilerDriver.enabled = true;
        for (int i = 0; i < 150; i++) yield return null;
        UnityEditorInternal.ProfilerDriver.enabled = false;
        var byName = new Dictionary<string, double>(); int frames = 0, allocFrames = 0; double total = 0, maxFrame = 0;
        for (int f = UnityEditorInternal.ProfilerDriver.firstFrameIndex; f >= 0 && f <= UnityEditorInternal.ProfilerDriver.lastFrameIndex; f++)
        {
            using (var v = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, false))
            {
                if (v == null || !v.valid) continue;
                frames++;
                var kids = new List<int>(); v.GetItemChildren(v.GetRootItemID(), kids);
                foreach (var id in kids)
                    if (v.GetItemName(id) == "PlayerLoop")
                    {
                        double g = v.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
                        total += g; if (g > 0) { allocFrames++; maxFrame = System.Math.Max(maxFrame, g); }
                        Walk(v, id, byName);
                    }
            }
        }
        log.AppendLine($"GC in PlayerLoop with the result screen up: {total / Mathf.Max(1, frames):0} B/frame average over {frames} frames; frames that allocated {allocFrames}, largest {maxFrame:0} B. Allocating markers (self):");
        foreach (var kv in byName) log.AppendLine($"  {kv.Value / Mathf.Max(1, frames),8:0.0} B/frame  {kv.Key}");
    }

    // Self GC per marker; a GC.Alloc leaf is charged to the marker path that called it (the last three names).
    static void Walk(UnityEditor.Profiling.HierarchyFrameDataView v, int id, Dictionary<string, double> byName, string path = "")
    {
        double gc = v.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
        if (gc <= 0) return;
        string n = v.GetItemName(id);
        var kids = new List<int>(); v.GetItemChildren(id, kids);
        double child = 0; foreach (var k in kids) child += v.GetItemColumnDataAsFloat(k, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
        if (gc - child > 0.5) { string key = n == "GC.Alloc" ? path : n; byName[key] = (byName.TryGetValue(key, out var a) ? a : 0) + gc - child; }
        string[] parts = (path.Length > 0 ? path + " > " + n : n).Split(new[] { " > " }, System.StringSplitOptions.None);
        string next = string.Join(" > ", parts, Mathf.Max(0, parts.Length - 3), Mathf.Min(3, parts.Length));
        foreach (var k in kids) Walk(v, k, byName, next);
    }
}
