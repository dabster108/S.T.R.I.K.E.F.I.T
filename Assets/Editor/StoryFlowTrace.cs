// StoryFlowTrace.cs
// Purpose: End-to-end Story Mode trace in Play mode, driven over Unity MCP (Editor only: it lives in Assets/Editor, so it is never
//          in a build). Clicks the real buttons (onClick of the menu, cards, end panels and pause menu), lets every card, fade and
//          scene load run, and fast-forwards only the fights: a debug win (the monster takes lethal damage until the Victory
//          sequence runs) or a debug death (the player takes lethal unblockable damage). Paths:
//            "new"       Main Menu > Story Mode > New Story > Prologue > Chapter 1 card > fight (win) > between-fights card >
//                        Chapter 2 card (stops there: the run continues in "defeat")
//            "defeat"    Chapter 2 fight, the player dies > Defeat card > Retry > Chapter 2 card again
//            "abandon"   Chapter 2 fight > pause (checks pause / resume) > Level Select > Yes (fight abandoned) > Main Menu
//            "continue"  Main Menu > Story Mode > Continue (Chapter 2) > win > Chapter 3 card > weapon pick > win > Victory card >
//                        Story complete > Return to menu
//            "free"      Main Menu > Start Game > Level 1 > Play (free play: no story) > win > Level Select > Main Menu > Start Game
//                        (the level select page)
//            "cards"     progress set to 1 chapter > Continue > Chapter 2 card [shot] > die > Defeat card [shot] > its Retry >
//                        die again > its Story menu (the menu opens on Story Mode) > Continue > win > between-fights card
//                        [shot] > Chapter 3 card [shot]. At each [shot] the trace unhooks itself from the player loop
//                        (WaitingForShot) so a screenshot can be taken safely, then Resume() carries on.
//          Run("new;defeat;abandon;continue;free") then poll Status() until DONE; Save(name) writes Captures/StoryFlow/<name>.txt.
//          Recorded: every scene load (time, frame), the console ([StrikeFitStory] / [StrikeFitMenu] / [SceneFlow] /
//          [BattleManager] / [SessionReporter] lines, every warning and error), and per scene: story directors, battle managers,
//          menu music, reporters, B-button handlers, time scale, audio pause, screen fade. Telemetry goes to a local sink
//          (sinkUrl, set on every reporter before its Start), never to the production backend.
// Dependencies: StoryDirector, StoryStage, StoryPanel, StoryModePanel, MainMenuController, BattleManager, PlayerNamePicker,
//               PauseMenu, SessionReporter, ScreenFade, MenuMusic, CombatVerification (l3Weapon)
// Attach to: nothing (static, Editor only)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class StoryFlowTrace
{
    public static string sinkUrl = "http://127.0.0.1:8899";
    public static string captureDir = "Captures/StoryFlow";
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static string jobName = "";
    static int lastFrame = -1, errors, warnings;
    static float t0;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    struct TraceStep { }

    public static bool Running => job != null;
    public static string WaitingForShot { get; private set; }

    /// <summary>After a screenshot: hook back into the player loop and carry on.</summary>
    public static string Resume()
    {
        if (job == null || WaitingForShot == null) return "nothing to resume";
        WaitingForShot = null;
        Install(true);
        return "resumed";
    }

    static IEnumerator Shot(string name)
    {
        yield return Frames(20);   // fades and line fades settle
        WaitingForShot = name;
        Line("SHOT " + name + " (waiting)");
        Install(false);            // a screenshot re-enters the player loop: never with this hook installed
        yield return null;
    }
    public static string Status() => (job != null ? "RUNNING " + jobName + "\n" : "IDLE\n") + log;

    public static string Run(string names)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0; jobName = names; errors = warnings = 0; t0 = Time.realtimeSinceStartup;
        Application.runInBackground = true;
        Time.captureDeltaTime = 1f / 30f;   // game time moves per frame even while the unfocused editor ticks slowly
        job = Main(names);
        Install(true);
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        Redirect();
        return "started " + names;
    }

    public static void Stop()
    {
        job = null; stack.Clear();
        Install(false);
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Time.captureDeltaTime = 0f;
    }

    public static string Save(string name)
    {
        System.IO.Directory.CreateDirectory(captureDir);
        string path = captureDir + "/" + name + ".txt";
        System.IO.File.WriteAllText(path, log.ToString());
        return path;
    }

    static void Install(bool on)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PreUpdate)) continue;
            var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ?? new PlayerLoopSystem[0]);
            list.RemoveAll(s => s.type == typeof(TraceStep));
            if (on) list.Add(new PlayerLoopSystem { type = typeof(TraceStep), updateDelegate = Tick });
            loop.subSystemList[i].subSystemList = list.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { if (!EditorApplication.isPlaying) Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            if (stack.Count == 0) stack.Push(job);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
                stack.Pop();
            }
            if (stack.Count == 0) Finish("DONE");
        }
        catch (Exception e) { log.AppendLine("HARNESS EXCEPTION " + e); Finish("FAILED"); }
    }

    static void Finish(string how)
    {
        log.AppendLine(how + " " + jobName + " — console errors " + errors + ", warnings " + warnings + ", real time " + (Time.realtimeSinceStartup - t0).ToString("0") + " s");
        Stop();
    }

    // ───────────────────────── recording ─────────────────────────

    static string T => (Time.realtimeSinceStartup - t0).ToString("000.0") + "s ";

    static void Line(string s) { log.AppendLine(T + s); }

    static void OnLog(string msg, string stack, LogType type)
    {
        bool bad = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (bad) errors++;
        if (type == LogType.Warning) warnings++;
        bool keep = bad || type == LogType.Warning || msg.StartsWith("[StrikeFitStory]") || msg.StartsWith("[StrikeFitMenu]") ||
                    msg.StartsWith("[SceneFlow]") || msg.StartsWith("[BattleManager]") || msg.StartsWith("[SessionReporter]") || msg.StartsWith("[MenuMusic]");
        if (!keep) return;
        string first = msg.Split('\n')[0];
        if (first.Length > 260) first = first.Substring(0, 260) + "...";
        log.AppendLine(T + "  console " + (bad ? "ERROR " : type == LogType.Warning ? "WARN " : "") + first + (bad && stack != null ? " @ " + stack.Split('\n')[0] : ""));
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Redirect();
        Line("SCENE LOADED " + s.name + " (frame " + Time.frameCount + ")");
    }

    /// <summary>Every reporter in the open scenes sends to the local sink (called before their Start: no production traffic).</summary>
    static void Redirect()
    {
        foreach (var r in UnityEngine.Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include)) r.baseUrl = sinkUrl;
    }

    static string Snapshot()
    {
        int directors = UnityEngine.Object.FindObjectsByType<StoryDirector>(FindObjectsInactive.Include).Length;
        int managers = UnityEngine.Object.FindObjectsByType<BattleManager>(FindObjectsInactive.Include).Length;
        int music = UnityEngine.Object.FindObjectsByType<MenuMusic>(FindObjectsInactive.Include).Length;
        int reporters = UnityEngine.Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include).Length;
        int stages = UnityEngine.Object.FindObjectsByType<StoryStage>(FindObjectsInactive.Include).Length;
        var playing = new List<string>();
        foreach (var a in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude))
            if (a.isPlaying && a.clip != null && a.loop) playing.Add(a.name + ":" + a.clip.name);
        var ls = typeof(SceneFlow).GetField("LoadStarting", F);
        var del = ls != null ? ls.GetValue(null) as Delegate : null;
        int loadListeners = del != null ? del.GetInvocationList().Length : 0;
        int fades = UnityEngine.Object.FindObjectsByType<ScreenFade>(FindObjectsInactive.Include).Length;
        return "scene " + SceneManager.GetActiveScene().name + " | SceneFlow.LoadStarting listeners " + loadListeners + ", screen fades " + fades + " | directors " + directors + ", stages " + stages + ", battle managers " + managers +
               ", menu music " + music + ", reporters " + reporters + " (url " + (SessionReporter.Instance != null ? SessionReporter.Instance.baseUrl : "-") + ")" +
               " | B handlers " + UiBack.Count + " | timeScale " + Time.timeScale + ", AudioListener.pause " + AudioListener.pause +
               ", paused " + GamePause.IsPaused + " | fade " + ScreenFade.Alpha.ToString("0.00") + " | story run " + StoryProgress.RunActive +
               " cleared " + StoryProgress.Cleared(StoryProgress.CurrentPlayer) + " | looping audio [" + string.Join(", ", playing.ToArray()) + "]";
    }

    // ───────────────────────── helpers ─────────────────────────

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static IEnumerator Until(Func<bool> cond, float seconds, string what)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!cond())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("timed out waiting for " + what);
            yield return null;
        }
    }

    static IEnumerator WaitScene(string name)
    {
        yield return Until(() => SceneManager.GetActiveScene().name == name && !SceneFlow.Busy, 90f, "scene " + name);
        yield return Frames(6);
        Line("  " + Snapshot());
    }

    static IEnumerator WaitClear() { yield return Until(() => !ScreenFade.Busy, 20f, "fade to clear"); }

    static Button Find(string name, Transform under = null)
    {
        foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude))
            if (b.name == name && b.gameObject.activeInHierarchy && (under == null || b.transform.IsChildOf(under))) return b;
        return null;
    }

    static IEnumerator Click(string name, Transform under = null, float wait = 20f)
    {
        Button b = null;
        yield return Until(() => (b = Find(name, under)) != null && b.interactable, wait, "button " + name);
        var label = b.GetComponentInChildren<Text>();
        Line("click " + name + (label != null ? " '" + label.text.Replace("\n", " ") + "'" : ""));
        b.onClick.Invoke();
    }

    static StoryStage Stage => StoryDirector.Instance != null ? StoryDirector.Instance.Stage : null;

    static IEnumerator Card(string what)
    {
        yield return Until(() => Stage != null && Stage.IsOpen && !ScreenFade.Busy, 30f, what);
        Line("CARD " + what + ": '" + Stage.Title + "' card " + Stage.CardShown + " lines " + Stage.SlidesShown + " complete " + Stage.CompleteShown);
    }

    /// <summary>Chapter card Begin, the name picker (remembered name), the daily-goal step, the Level 3 weapon pick, Start.</summary>
    static IEnumerator BeginFight(bool weaponPick)
    {
        yield return Card("chapter card");
        yield return Click("BeginButton", null);
        yield return WaitClear();
        var pnp = UnityEngine.Object.FindAnyObjectByType<PlayerNamePicker>();
        yield return Frames(10);
        if (pnp != null && (bool)typeof(PlayerNamePicker).GetField("picking", F).GetValue(pnp))
        {
            string name = (string)typeof(PlayerNamePicker).GetField("defaultName", F).GetValue(pnp);
            typeof(PlayerNamePicker).GetMethod("Choose", F).Invoke(pnp, new object[] { name });
            Line("name picker: chose '" + name + "'");
            yield return Frames(10);
            var goal = typeof(PlayerNamePicker).GetField("goalStepOpen", F);
            if (goal != null && (bool)goal.GetValue(pnp)) { typeof(PlayerNamePicker).GetMethod("FinishGoalStep", F).Invoke(pnp, null); Line("daily goal step: closed"); }
        }
        if (weaponPick)
        {
            var pc = PlayerCombat.Instance;
            yield return Until(() => WeaponSelectionStage.Instance != null, 10f, "weapon selection stage");
            Line("weapon pick stage: blocks start " + WeaponSelectionStage.BlocksStart);
            pc.EquipWeaponSet(CombatVerification.l3Weapon);
            yield return Until(() => !WeaponSelectionStage.BlocksStart, 10f, "weapon armed");
            Line("weapon picked: " + CombatVerification.l3Weapon + ", blocks start " + WeaponSelectionStage.BlocksStart);
        }
        yield return Click("StartBattleUIButton", null, 30f);
        yield return Until(() => BattleManager.Instance.IsBattleActive, 20f, "battle start");
        Line("FIGHT started: " + BattleManager.Instance.ActiveMonster.name + ", session mode " + SessionReporter.SessionMode);
    }

    /// <summary>Debug win: lethal damage each frame until the Victory sequence runs (phases and entrances included).</summary>
    static IEnumerator WinFight()
    {
        var bm = BattleManager.Instance;
        bool won = false;
        Action<int> h = s => won = true;
        bm.OnVictory += h;
        float end = Time.realtimeSinceStartup + 120f;
        while (!won)
        {
            var m = bm.ActiveMonster;
            if (m != null && m.isAlive)
            {
                // Straight to the health (the controller caps single hits and bursts), then the controller's own death.
                var hp = m.GetComponent<MonsterHealth>();
                if (hp != null && !hp.IsDead) hp.TakeDamage(hp.maxHealth, m.transform.position + Vector3.up);
                if (hp != null && hp.IsDead && m.isAlive) m.OnDeath();
            }
            if (Time.realtimeSinceStartup > end) { bm.OnVictory -= h; throw new Exception("debug win timed out"); }
            yield return null;
        }
        bm.OnVictory -= h;
        Line("FIGHT won (debug): score " + bm.CurrentScore);
        yield return Until(() => CombatUIController.Instance.victoryPanel.activeInHierarchy, 20f, "victory panel");
        yield return Frames(10);
    }

    static IEnumerator LoseFight()
    {
        var bm = BattleManager.Instance;
        var ph = UnityEngine.Object.FindAnyObjectByType<PlayerHealth>();
        float end = Time.realtimeSinceStartup + 60f;
        while (bm.IsBattleActive)
        {
            ph.TakeDamage(9999f, true);
            if (Time.realtimeSinceStartup > end) throw new Exception("debug death timed out");
            yield return null;
        }
        Line("FIGHT lost (debug)");
    }

    // ───────────────────────── paths ─────────────────────────

    static IEnumerator Main(string names)
    {
        Line("START " + names + " | " + Snapshot());
        foreach (var n in names.Split(';'))
        {
            Line("===== PATH " + n + " =====");
            switch (n.Trim())
            {
                case "new": yield return NewStory(); break;
                case "defeat": yield return DefeatRetry(); break;
                case "abandon": yield return Abandon(); break;
                case "continue": yield return ContinueStory(); break;
                case "free": yield return FreePlay(); break;
                case "cards": yield return Cards(); break;
                case "finish3": yield return Finish3(); break;
                default: Line("unknown path " + n); break;
            }
        }
    }

    static IEnumerator ToMenu()
    {
        if (SceneManager.GetActiveScene().name != SceneFlow.MenuScene || SessionReporter.Instance == null || SessionReporter.Instance.baseUrl != sinkUrl)
        {
            Line("load MainMenu (fresh, so its reporter starts on the sink)");
            SceneFlow.LevelSelect();
        }
        yield return WaitScene(SceneFlow.MenuScene);
        yield return WaitClear();
    }

    static IEnumerator NewStory()
    {
        yield return ToMenu();
        yield return Click("StoryModeButton");
        yield return Frames(5);
        var smp = UnityEngine.Object.FindAnyObjectByType<StoryModePanel>();
        Line("Story Mode page: continue shown " + (smp.continueButton != null && smp.continueButton.gameObject.activeSelf) + ", status '" + smp.statusText.text + "'");
        yield return Click("NewStoryButton");
        yield return Frames(3);
        if (smp.ConfirmOpen) { Line("new story confirm open (progress exists)"); yield return Click(smp.confirmYes.name); }
        var sp = UnityEngine.Object.FindAnyObjectByType<StoryPanel>();
        yield return Until(() => sp.Playing, 10f, "prologue");
        Line("PROLOGUE playing, prologue mode " + sp.PrologueMode);
        yield return Until(() => sp.LinesShown >= 2, 30f, "two prologue lines faded in on their own");
        Line("prologue: " + sp.LinesShown + " lines shown by time");
        yield return Click("BeginButton", sp.page.transform, 30f);
        yield return WaitScene(SceneFlow.Level1);
        yield return BeginFight(false);
        yield return WinFight();
        yield return Click("ContinueStoryButton");
        yield return Card("between fights");
        yield return Click("PrimaryButton", null, 30f);
        yield return WaitScene(SceneFlow.Level2);
        yield return Card("chapter 2 card");
    }

    static IEnumerator DefeatRetry()
    {
        yield return BeginFight(false);
        yield return LoseFight();
        yield return Card("defeat card");
        Line("defeat card buttons: Retry " + (Find("PrimaryButton") != null) + ", Story menu " + (Find("SecondaryButton") != null) + ", Skip " + (Find("SkipButton") != null));
        // Skip first (back to the usual defeat panel), then Retry from that panel.
        yield return Click("SkipButton");
        yield return WaitClear();
        Line("after Skip: defeat panel " + CombatUIController.Instance.defeatPanel.activeInHierarchy + ", card open " + Stage.IsOpen);
        yield return Click("PlayAgainButton", CombatUIController.Instance.defeatPanel.transform);
        yield return WaitScene(SceneFlow.Level2);
        yield return Card("chapter 2 card after retry");
    }

    static IEnumerator Abandon()
    {
        if (StoryDirector.Instance == null || !StoryDirector.Instance.Stage.CardShown)
        {
            // Started on its own: into the run's next chapter through Story Mode > Continue.
            yield return ToMenu();
            yield return Click("StoryModeButton");
            yield return Frames(5);
            yield return Click("ContinueButton");
            yield return Until(() => StoryDirector.Instance != null, 90f, "a story chapter scene");
            yield return WaitScene(SceneManager.GetActiveScene().name);
        }
        yield return BeginFight(false);
        yield return Frames(30);
        var pm = PauseMenu.Instance;
        Line("pause: can pause " + pm.CanPause());
        pm.Pause("trace");
        yield return Frames(5);
        Line("paused: GamePause " + GamePause.IsPaused + ", timeScale " + Time.timeScale + ", AudioListener.pause " + AudioListener.pause);
        yield return Click("ResumeButton");
        yield return Frames(5);
        Line("resumed: GamePause " + GamePause.IsPaused + ", timeScale " + Time.timeScale + ", battle " + BattleManager.Instance.IsBattleActive);
        yield return Frames(30);
        pm.Pause("trace");
        yield return Frames(5);
        yield return Click("LevelSelectButton");   // the pause panel is the only one up (its canvas is a root of its own)
        yield return Frames(3);
        if (Find("YesButton") != null) yield return Click("YesButton");
        yield return WaitScene(SceneFlow.MenuScene);
    }

    static IEnumerator ContinueStory()
    {
        yield return ToMenu();
        yield return Click("StoryModeButton");
        yield return Frames(5);
        var smp = UnityEngine.Object.FindAnyObjectByType<StoryModePanel>();
        Line("Story Mode page: continue shown " + smp.continueButton.gameObject.activeSelf + " '" + smp.continueLabel.text.Replace("\n", " ") + "', status '" + smp.statusText.text + "'");
        yield return Click("ContinueButton");
        yield return WaitScene(SceneFlow.Level2);
        yield return BeginFight(false);
        yield return WinFight();
        yield return Click("ContinueStoryButton");
        yield return Card("between fights");
        yield return Click("PrimaryButton", null, 30f);
        yield return WaitScene(SceneFlow.Level3);
        yield return BeginFight(true);
        yield return WinFight();
        yield return Click("ContinueStoryButton");
        yield return Card("victory");
        Line("Start button visible over the Victory card: " + (GameObject.Find("CombatCanvas/StartBattleUIButton") != null));
        yield return Until(() => Stage.LinesShown >= Stage.SlideCount, 40f, "victory lines");
        Line("victory card: " + Stage.LinesShown + "/" + Stage.SlideCount + " lines shown");
        yield return Click("PrimaryButton", null, 30f);
        yield return Card("story complete");
        yield return Click("ReturnButton", null, 30f);
        yield return WaitScene(SceneFlow.MenuScene);
        var mc = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
        Line("menu page after the story: '" + (mc.CurrentPage != null ? mc.CurrentPage.name : "null") + "'");
    }

    static IEnumerator DefeatCardButtons()
    {
        yield return BeginFight(false);
        yield return LoseFight();
        yield return Card("defeat card");
        yield return Until(() => Find("PrimaryButton") != null && Find("SecondaryButton") != null, 30f, "defeat card buttons");
        Line("defeat card: Retry " + (Find("PrimaryButton") != null) + ", Story menu " + (Find("SecondaryButton") != null) + ", Skip " + (Find("SkipButton") != null) +
             ", end panel hidden " + !CombatUIController.Instance.defeatPanel.activeInHierarchy + ", pause allowed " + PauseMenu.Instance.CanPause());
    }

    /// <summary>From an open Chapter 3 card: weapon pick, win, Victory card (Start must stay hidden), Story complete, menu.</summary>
    static IEnumerator Finish3()
    {
        yield return BeginFight(true);
        yield return WinFight();
        yield return Frames(30);
        Line("Start button visible after the Level 3 victory (end panel up): " + (GameObject.Find("CombatCanvas/StartBattleUIButton") != null));
        // A weapon swap after the fight re-showed Start once the story card hid the end panel: swap now, with the card up.
        yield return Click("ContinueStoryButton");
        yield return Card("victory");
        PlayerCombat.Instance.EquipWeaponSet(CombatVerification.l3Weapon == WeaponType.Mace ? WeaponType.Katana : WeaponType.Mace);
        yield return Frames(10);
        Line("after a weapon swap over the Victory card: Start visible " + (GameObject.Find("CombatCanvas/StartBattleUIButton") != null) + ", card queue " + CardQueue());
        yield return Shot("card_victory");
        yield return Until(() => Find("PrimaryButton") != null, 40f, "Continue on the Victory card");
        yield return Click("PrimaryButton");
        yield return Card("story complete");
        yield return Shot("card_complete");
        yield return Click("ReturnButton");
        yield return WaitScene(SceneFlow.MenuScene);
    }

    static int CardQueue()
    {
        var c = GameObject.Find("StoryStageCanvas");
        var g = c != null ? c.GetComponentInChildren<Graphic>() : null;
        return g != null ? g.materialForRendering.renderQueue : -1;
    }

    static IEnumerator Cards()
    {
        string p = StoryProgress.CurrentPlayer;
        StoryProgress.Reset(p);
        StoryProgress.SaveChapter(p, 1);
        yield return ToMenu();
        yield return Click("StoryModeButton");
        yield return Frames(5);
        yield return Click("ContinueButton");
        yield return WaitScene(SceneFlow.Level2);
        yield return Card("chapter 2 card");
        yield return Shot("card_chapter2");
        yield return DefeatCardButtons();
        yield return Shot("card_defeat");
        yield return Click("PrimaryButton");
        yield return WaitScene(SceneFlow.Level2);
        yield return Card("chapter 2 card after the card's Retry");
        yield return DefeatCardButtons();
        yield return Click("SecondaryButton");
        yield return WaitScene(SceneFlow.MenuScene);
        var mc = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
        Line("menu after the card's Story menu: page '" + mc.CurrentPage.name + "', story run " + StoryProgress.RunActive + ", cleared " + StoryProgress.Cleared(p));
        yield return Click("ContinueButton");
        yield return WaitScene(SceneFlow.Level2);
        yield return BeginFight(false);
        yield return WinFight();
        yield return Click("ContinueStoryButton");
        yield return Card("between fights");
        yield return Until(() => Find("PrimaryButton") != null, 30f, "Continue on the between-fights card");
        Line("Start button visible over the between-fights card: " + (GameObject.Find("CombatCanvas/StartBattleUIButton") != null) +
             "; card material render queue " + CardQueue());
        yield return Shot("card_between_fights");
        yield return Click("PrimaryButton");
        yield return WaitScene(SceneFlow.Level3);
        yield return Card("chapter 3 card");
        yield return Shot("card_chapter3");
    }

    static IEnumerator FreePlay()
    {
        yield return ToMenu();
        yield return Click("StartGameButton");
        yield return Frames(3);
        var mc = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
        Line("level select page: '" + mc.CurrentPage.name + "'");
        mc.levels[0].button.onClick.Invoke();
        Line("click level card 1");
        yield return Click("PlayButton");
        yield return WaitScene(SceneFlow.Level1);
        Line("free play: StoryDirector " + (StoryDirector.Instance != null) + ", session mode " + SessionReporter.SessionMode);
        // No chapter card in free play: the name picker comes first.
        var pnp = UnityEngine.Object.FindAnyObjectByType<PlayerNamePicker>();
        yield return Frames(10);
        if ((bool)typeof(PlayerNamePicker).GetField("picking", F).GetValue(pnp))
        {
            string name = (string)typeof(PlayerNamePicker).GetField("defaultName", F).GetValue(pnp);
            typeof(PlayerNamePicker).GetMethod("Choose", F).Invoke(pnp, new object[] { name });
            Line("name picker: chose '" + name + "'");
            yield return Frames(10);
            var goal = typeof(PlayerNamePicker).GetField("goalStepOpen", F);
            if (goal != null && (bool)goal.GetValue(pnp)) { typeof(PlayerNamePicker).GetMethod("FinishGoalStep", F).Invoke(pnp, null); Line("daily goal step: closed"); }
        }
        yield return Click("StartBattleUIButton", null, 30f);
        yield return Until(() => BattleManager.Instance.IsBattleActive, 20f, "battle start");
        Line("FIGHT started (free play), session mode " + SessionReporter.SessionMode);
        yield return WinFight();
        Line("end panel buttons: " + (Find("PlayAgainButton") != null) + " / LevelSelect " + (Find("LevelSelectButton") != null) + " / ContinueStory " + (Find("ContinueStoryButton") != null));
        yield return Click("LevelSelectButton", CombatUIController.Instance.victoryPanel.transform);
        yield return WaitScene(SceneFlow.MenuScene);
        yield return Click("StartGameButton");
        yield return Frames(3);
        mc = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
        Line("level select after free play: page '" + mc.CurrentPage.name + "', cards " + mc.levels.Length);
        mc.ShowHome();
    }
}
