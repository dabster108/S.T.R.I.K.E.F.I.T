// MenuVerification.cs
// Purpose: Play-mode probes for the menu and the scene flow (Editor only, never in a build). Clicks go through the real laser
//          pipeline: the rig's UIRayOrigin is aimed at the button, OVRRaycaster.Raycast resolves the hit exactly as the headset
//          would, then the pointer down / up / click events run on the hit object.
//          Jobs: "menu"      from the open MainMenu: counts, a capture, a laser hit on every button, the exit confirm (No).
//                "roundtrip" MainMenu -> each level -> real Victory -> Exit confirm (No) -> Play Again (fresh fight) -> real Defeat
//                            -> Level Select -> menu, with counts at every stop and a capture of each panel.
//                "exit"      MainMenu -> Exit Game -> Yes: the Editor path must stop Play mode.
// Dependencies: SceneFlow, MainMenuController, OutcomeMenuButtons, ExitConfirmPanel, CombatVerification.Prepare, BattleManager
// Attach to: nothing (static, driven over MCP)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MenuVerification
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string captureDir = "Captures/Menu";

    public static string Status() => (job != null ? $"RUNNING t={Time.time:0.0} scene={SceneManager.GetActiveScene().name}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0;
        if (name == "menu") job = MenuJob();
        else if (name == "roundtrip") job = RoundTrip();
        else if (name == "exit") job = ExitJob();
        else return "unknown job " + name;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop() { job = null; stack.Clear(); EditorApplication.update -= Tick; }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
                if (stack.Count > 0) { stack.Pop(); continue; }
                log.AppendLine("DONE"); Stop(); break;
            }
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Seconds(float s) { float end = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < end) yield return null; }

    // ───────────────────────── counts ─────────────────────────
    public static string Counts()
    {
        var sb = new StringBuilder();
        sb.Append($"scene {SceneManager.GetActiveScene().name}: rigs {Object.FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include).Length}, ")
          .Append($"AudioListeners {Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length}, ")
          .Append($"SessionReporters {Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include).Length}, ")
          .Append($"EventSystems {Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include).Length}, ")
          .Append($"canvases {Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include).Length}, ")
          .Append($"lights {Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Length}, ")
          .Append($"timeScale {Time.timeScale:0.##}");
        int playing = 0, particles = 0;
        foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude)) if (a.isPlaying) playing++;
        foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude)) particles += p.particleCount;
        sb.Append($", audio sources playing {playing}, live particles {particles}");
        // objects that survive scene loads
        var ddol = new List<string>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t.parent == null && t.gameObject.scene.name == "DontDestroyOnLoad") ddol.Add(t.name);
        ddol.Sort();
        sb.Append($", DontDestroyOnLoad roots [{string.Join(", ", ddol)}]");
        return sb.ToString();
    }

    // The part of Counts() that must be the same after a round trip.
    static string Fingerprint()
    {
        int listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length;
        int rigs = Object.FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include).Length;
        int rep = Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include).Length;
        int es = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include).Length;
        var playing = new List<string>(); int particles = 0;
        foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude)) if (a.isPlaying) playing.Add(a.name + (a.loop ? "(loop)" : ""));
        playing.Sort();
        foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude)) particles += p.particleCount;
        return $"listeners {listeners} rigs {rigs} reporters {rep} eventSystems {es} audio [{string.Join(",", playing)}] particles {particles} timeScale {Time.timeScale:0.##}";
    }

    // ───────────────────────── laser click ─────────────────────────
    static readonly List<RaycastResult> hits = new List<RaycastResult>();

    static string LaserHit(Button b, out bool ok)
    {
        ok = false;
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var origin = rig.rightHandAnchor.Find("UIRayOrigin");
        if (origin == null) return "no UIRayOrigin";
        var canvas = b.GetComponentInParent<Canvas>().rootCanvas;
        var raycaster = canvas.GetComponent<OVRRaycaster>();
        if (raycaster == null) return "no OVRRaycaster";
        var rt = (RectTransform)b.transform;
        var corners = new Vector3[4]; rt.GetWorldCorners(corners);
        Vector3 target = (corners[0] + corners[2]) * 0.5f;
        origin.rotation = Quaternion.LookRotation((target - origin.position).normalized);
        var ped = new OVRPointerEventData(EventSystem.current) { worldSpaceRay = new Ray(origin.position, origin.forward), position = Vector2.zero };
        hits.Clear();
        raycaster.Raycast(ped, hits);
        if (hits.Count == 0) return "laser hit nothing";
        hits.Sort((x, y) => y.depth != x.depth ? y.depth.CompareTo(x.depth) : x.distance.CompareTo(y.distance));
        var top = hits[0];
        ok = top.gameObject == b.gameObject || top.gameObject.transform.IsChildOf(b.transform);
        return $"laser -> {top.gameObject.name} at {top.distance:0.00} m" + (ok ? "" : $" (EXPECTED {b.name})");
    }

    static string LaserClick(Button b)
    {
        string msg = LaserHit(b, out bool ok);
        if (!ok) return msg + ", NOT clicked";
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var origin = rig.rightHandAnchor.Find("UIRayOrigin");
        var ped = new OVRPointerEventData(EventSystem.current) { worldSpaceRay = new Ray(origin.position, origin.forward), position = Vector2.zero, button = PointerEventData.InputButton.Left };
        ped.pointerEnter = b.gameObject; ped.pointerPress = b.gameObject; ped.rawPointerPress = b.gameObject; ped.eligibleForClick = true; ped.clickCount = 1; ped.pointerCurrentRaycast = hits[0];
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerEnterHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerClickHandler);
        return msg + (b.interactable ? ", clicked" : ", clicked (now greyed out)");
    }

    static Button Find(string name)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude)) if (b.name == name) return b;
        return null;
    }

    // ───────────────────────── capture ─────────────────────────
    static void Capture(string file, Vector3 look)
    {
        System.IO.Directory.CreateDirectory(captureDir);
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var head = rig.centerEyeAnchor;
        var go = new GameObject("MV_Cam");
        try
        {
            var cam = go.AddComponent<Camera>();
            var main = head.GetComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            // The Editor has no headset (head at the floor origin); a floor-level Quest head is about 1.6 m up.
            Vector3 eye = head.position + (head.position.y < 0.5f ? Vector3.up * 1.6f : Vector3.zero);
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            cam.fieldOfView = 70f;
            var rt = new RenderTexture(900, 640, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(900, 640, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 900, 640), 0, 0); tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        finally { Object.DestroyImmediate(go); }
    }

    static Vector3 CanvasCentre()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)) if (c.renderMode == RenderMode.WorldSpace && c.GetComponent<OVRRaycaster>() != null) return c.transform.position;
        return Vector3.forward * 2f;
    }

    // ───────────────────────── menu ─────────────────────────
    static IEnumerator MenuJob()
    {
        yield return Frames(5);
        log.AppendLine("MENU: " + Counts());
        Capture(captureDir + "/menu.png", CanvasCentre());
        var mc = Object.FindAnyObjectByType<MainMenuController>();
        foreach (var card in mc.levels)
        {
            LaserHit(card.button, out bool ok);
            log.AppendLine($"  {card.button.name} -> {card.scene}: {LaserHit(card.button, out ok)}");
        }
        log.AppendLine($"  ExitGameButton: {LaserHit(mc.exitButton, out _)}");
        log.AppendLine("  " + LaserClick(mc.exitButton));
        yield return Frames(3);
        log.AppendLine($"  confirm open {mc.confirm.IsOpen}; content interactable while open {mc.content.interactable}");
        Capture(captureDir + "/menu_confirm.png", CanvasCentre());
        log.AppendLine("  " + LaserClick(mc.confirm.noButton));
        yield return Frames(3);
        log.AppendLine($"  after No: confirm open {mc.confirm.IsOpen}; content interactable {mc.content.interactable}");
    }

    // ───────────────────────── round trip ─────────────────────────
    static IEnumerator WaitScene(string name, float timeout = 30f)
    {
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < timeout && (SceneManager.GetActiveScene().name != name || SceneFlow.Busy)) yield return null;
        yield return Frames(5);
    }

    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    static IEnumerator StartAndFinish(bool victory, string tag)
    {
        CombatVerification.frameDt = 1f / 30f;
        CombatVerification.Prepare();
        HideNameBoard();
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        BattleManager.Instance.StartBattleFromInspector();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 20f && (M == null || (M.GetComponent<MonsterEntrance>() != null && !M.GetComponent<MonsterEntrance>().Complete))) yield return null;
        yield return Frames(10);
        var mon = M;
        var health = mon.GetComponent<MonsterHealth>();
        log.AppendLine($"  {tag}: fight started, monster {mon.name} HP {health.currentHealth:0}/{health.maxHealth:0}, player HP {PlayerHealth.Instance.currentHealth:0}/{PlayerHealth.Instance.maxHealth:0}");
        if (victory)
        {
            health.currentHealth = 1f;
            mon.OnTakeDamage(60f, mon.transform.position + Vector3.up * 1.2f, 9f, false);
        }
        else PlayerHealth.Instance.TakeDamage(1e6f, true);
        var ui = CombatUIController.Instance;
        var panel = victory ? ui.victoryPanel : ui.defeatPanel;
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 25f && !panel.activeSelf) yield return null;
        yield return Seconds(1.5f);
        log.AppendLine($"  {tag}: {(victory ? "Victory" : "Defeat")} panel shown {panel.activeSelf}, timeScale {Time.timeScale:0.##}, row built {panel.transform.Find("PlayAgainRow") != null}");
    }

    // The harness starts the battle without picking a name, so the name board (a full-canvas panel whose labels the OVR raycaster
    // would hit) is still up. In real play it closes when a name is chosen. Hidden here only; no preference is written.
    static void HideNameBoard()
    {
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
        if (panel != null) panel.SetActive(false);
    }

    static IEnumerator RoundTrip()
    {
        yield return Frames(5);
        string[] scenes = { SceneFlow.Level1, SceneFlow.Level2, SceneFlow.Level3 };
        string[] buttons = { "Level1Button", "Level2Button", "Level3Button" };
        if (SceneManager.GetActiveScene().name != SceneFlow.MenuScene) { log.AppendLine("start from the MainMenu scene"); yield break; }
        string menuBefore = Fingerprint();
        log.AppendLine("MENU first load: " + Counts());
        for (int i = 0; i < 3; i++)
        {
            string level = scenes[i];
            log.AppendLine($"=== {level}");
            var mc = Object.FindAnyObjectByType<MainMenuController>();
            var card = mc.levels[i];
            log.AppendLine("  " + LaserClick(card.button));
            yield return null;
            log.AppendLine($"  label while loading: '{card.status.text}', buttons interactable {mc.content.interactable}, SceneFlow.Busy {SceneFlow.Busy}");
            // a second click while it loads must do nothing
            LaserClick(card.button);
            yield return WaitScene(level);
            yield return Seconds(2f);
            log.AppendLine("  " + Counts());
            string baseline = Fingerprint();

            // Real Victory -> panel row
            yield return StartAndFinish(true, "run 1");
            var ui = CombatUIController.Instance;
            var vp = ui.victoryPanel;
            Capture($"{captureDir}/{level}_victory.png", vp.transform.position);
            var obm = Object.FindAnyObjectByType<OutcomeMenuButtons>();
            var exitBtn = vp.transform.Find("PlayAgainRow/ExitGameButton").GetComponent<Button>();
            log.AppendLine("  Exit Game: " + LaserClick(exitBtn));
            yield return Frames(3);
            log.AppendLine($"  confirm open {obm.confirm.IsOpen}");
            Capture($"{captureDir}/{level}_victory_confirm.png", vp.transform.position);
            log.AppendLine("  No: " + LaserClick(obm.confirm.noButton));
            yield return Frames(3);
            log.AppendLine($"  after No: confirm open {obm.confirm.IsOpen}, row interactable {vp.transform.Find("PlayAgainRow").GetComponent<CanvasGroup>().interactable}");

            // Play Again
            var oldManager = BattleManager.Instance;
            var again = vp.transform.Find("PlayAgainRow/PlayAgainButton").GetComponent<Button>();
            log.AppendLine("  Play Again: " + LaserClick(again));
            LaserClick(again);   // double click: ignored
            yield return null;
            log.AppendLine($"  SceneFlow.Busy {SceneFlow.Busy}, row interactable {vp.transform.Find("PlayAgainRow").GetComponent<CanvasGroup>().interactable}");
            yield return WaitScene(level);
            yield return Seconds(2f);
            log.AppendLine($"  reloaded: new BattleManager {BattleManager.Instance != oldManager}, battle active {BattleManager.Instance.IsBattleActive}, monster {(M != null ? M.name : "none")}, panels victory/defeat {CombatUIController.Instance.victoryPanel.activeSelf}/{CombatUIController.Instance.defeatPanel.activeSelf}");
            log.AppendLine("  " + Counts());
            string after = Fingerprint();
            log.AppendLine($"  fingerprint before {(baseline == after ? "==" : "!=")} after: [{baseline}] vs [{after}]");

            // fresh fight + real Defeat -> Level Select
            yield return StartAndFinish(false, "run 2");
            var mon = M;
            var dp = CombatUIController.Instance.defeatPanel;
            log.AppendLine($"  run 2 fresh state: monster HP {(mon != null ? mon.GetComponent<MonsterHealth>().currentHealth : -1):0} (a fresh fight starts at max), XP {XPManager.Instance.CurrentXP}, short entrance {(mon != null && mon.GetComponent<MonsterEntrance>() != null ? mon.GetComponent<MonsterEntrance>().ShortVersion.ToString() : "n/a")}");
            Capture($"{captureDir}/{level}_defeat.png", dp.transform.position);
            var select = dp.transform.Find("PlayAgainRow/LevelSelectButton").GetComponent<Button>();
            log.AppendLine("  Level Select: " + LaserClick(select));
            yield return WaitScene(SceneFlow.MenuScene);
            yield return Seconds(1.5f);
            log.AppendLine("  " + Counts());
            string menuAfter = Fingerprint();
            log.AppendLine($"  menu fingerprint {(menuBefore == menuAfter ? "==" : "!=")} first load: [{menuBefore}] vs [{menuAfter}]");
        }
    }

    // ───────────────────────── exit (Editor path) ─────────────────────────
    static IEnumerator ExitJob()
    {
        yield return Frames(5);
        var mc = Object.FindAnyObjectByType<MainMenuController>();
        log.AppendLine("Exit Game: " + LaserClick(mc.exitButton));
        yield return Frames(3);
        log.AppendLine("Yes: " + LaserClick(mc.confirm.yesButton));
        LaserClick(mc.confirm.yesButton);   // double click: ignored
        log.AppendLine($"quitting {mc.confirm.Quitting}, message '{mc.confirm.message.text}'");
        // Play mode stops from here: the job ends with the editor leaving Play mode.
        yield return Seconds(5f);
        log.AppendLine("STILL PLAYING after 5 s (Exit did not stop Play mode)");
    }
}
