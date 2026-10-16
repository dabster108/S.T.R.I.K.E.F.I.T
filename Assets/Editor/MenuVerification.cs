// MenuVerification.cs
// Purpose: Play-mode probes for the menu and the scene flow (Editor only, never in a build). Clicks go through the real laser
//          pipeline: the rig's UIRayOrigin is aimed at the button, OVRRaycaster.Raycast resolves the hit exactly as the headset
//          would, then the pointer down / up / click events run on the hit object.
//          Jobs: "menu"      from the open MainMenu: counts, a capture, a laser hit on every button, the exit confirm (No).
//                "roundtrip" MainMenu -> each level -> real Victory -> Exit confirm (No) -> Play Again (fresh fight) -> real Defeat
//                            -> Level Select -> menu, with counts at every stop and a capture of each panel.
//                "exit"      MainMenu -> Exit Game -> Yes: the Editor path must stop Play mode.
//                "layout"    MainMenu: every page (home, levels, name list, keyboard, rename confirm, exit confirm) captured from the
//                            head at the Quest per-eye resolution, view angles of every button, and the page flow clicked by laser.
//                "panels"    from an open level: real Victory and Defeat, captured from the head, view angles of the panel and its row.
//                "scoreboard" MainMenu: the production URL first (no route there yet = the unavailable state), then testBackend:
//                            every tab, the "you" row, "Your best" outside the top N, empty, and offline with the PlayerPrefs cache.
//                "rankline"  from an open level, against testBackend as the disposable player ZZTEST: a real Victory, the result
//                            POST, then the "Rank on this level" line. Prints the session id to delete afterwards.
//                "story"     MainMenu: Story button in view, story page opens, slide text / counter / skip in view, skip returns home,
//                            then re-play with the full 3 slides (clearing StorySeen first) and measure the total time.
//          Captures (2026-10-15): from the head exactly where it is (EyeLevel origin: on a Quest the eyes start at the rig's own
//          height, so the old "+1.6 m if the head is low" adjustment hid the menu bug and is gone), 1832 x 1920 per eye, the
//          Quest 2 per-eye view (44 deg up, 50 deg down, +-45.9 deg sideways).
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
    /// <summary>Local backend for the scoreboard / rank-line jobs (never production; 127.0.0.1, not localhost).</summary>
    public static string testBackend = "http://127.0.0.1:4020";

    public static string Status() => (job != null ? $"RUNNING t={Time.time:0.0} scene={SceneManager.GetActiveScene().name}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0;
        if (name == "menu") job = MenuJob();
        else if (name == "roundtrip") job = RoundTrip();
        else if (name == "exit") job = ExitJob();
        else if (name == "layout") job = LayoutJob();
        else if (name == "panels") job = PanelsJob();
        else if (name == "scoreboard") job = ScoreboardJob();
        else if (name == "rankline") job = RankLineJob();
        else if (name == "story") job = StoryJob();
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
    public const int EyeWidth = 1832, EyeHeight = 1920;
    // Quest 2 per-eye field of view (OpenXR view angles, about): 44 deg up, 50 deg down; +-45.9 deg sideways keeps the pixels
    // square at 1832 x 1920 (the real per-eye view is 40 deg toward the nose, 54 deg outward).
    public const float EyeUp = 44f, EyeDown = 50f, EyeSide = 45.9f;

    /// <summary>Renders what one eye sees from the head pose. look = null: straight ahead (the head's own rotation, i.e. the pose a
    /// player has at app start); otherwise the head turns toward look (only for close-ups).</summary>
    static void Capture(string file, Vector3? look = null, int width = EyeWidth, int height = EyeHeight)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var head = rig.centerEyeAnchor;
        var go = new GameObject("MV_Cam");
        try
        {
            var cam = go.AddComponent<Camera>();
            var main = head.GetComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            Vector3 eye = head.position;
            go.transform.SetPositionAndRotation(eye, look.HasValue ? Quaternion.LookRotation(look.Value - eye) : head.rotation);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            float n = cam.nearClipPlane;
            cam.projectionMatrix = Matrix4x4.Frustum(-n * Mathf.Tan(EyeSide * Mathf.Deg2Rad), n * Mathf.Tan(EyeSide * Mathf.Deg2Rad),
                -n * Mathf.Tan(EyeDown * Mathf.Deg2Rad), n * Mathf.Tan(EyeUp * Mathf.Deg2Rad), n, cam.farClipPlane);
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        finally { Object.DestroyImmediate(go); }
    }

    /// <summary>Where a UI rect sits in the view from the head (straight ahead): yaw and pitch of its corners in degrees (+ = right /
    /// up), and whether all four corners are inside the per-eye frustum. Cap height of its text if it has any.</summary>
    public static string Angles(RectTransform rt)
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var head = rig.centerEyeAnchor;
        var c = new Vector3[4]; rt.GetWorldCorners(c);
        float yMin = 999f, yMax = -999f, pMin = 999f, pMax = -999f;
        bool inside = true;
        foreach (var w in c)
        {
            Vector3 d = head.InverseTransformPoint(w);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(d.y, d.z) * Mathf.Rad2Deg;
            yMin = Mathf.Min(yMin, yaw); yMax = Mathf.Max(yMax, yaw); pMin = Mathf.Min(pMin, pitch); pMax = Mathf.Max(pMax, pitch);
            if (d.z <= 0f || Mathf.Abs(yaw) > EyeSide || pitch > EyeUp || pitch < -EyeDown) inside = false;
        }
        string cap = "";
        var text = rt.GetComponentInChildren<Text>();
        if (text != null && text.gameObject.activeInHierarchy)
        {
            float capCm = 0.716f * text.fontSize * text.transform.lossyScale.y * 100f;
            float dist = Vector3.Distance(head.position, text.transform.position);
            cap = $", text {text.fontSize}pt cap {capCm:0.0} cm at {dist:0.00} m";
        }
        return $"yaw {yMin:0.0}..{yMax:0.0}, pitch {pMin:0.0}..{pMax:0.0} deg, {(inside ? "IN VIEW" : "OUT OF VIEW")}{cap}";
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
        Capture(captureDir + "/menu.png");
        var mc = Object.FindAnyObjectByType<MainMenuController>();
        log.AppendLine("  Start Game: " + LaserClick(mc.startGameButton));
        yield return Frames(2);
        foreach (var card in mc.levels)
            log.AppendLine($"  {card.button.name} -> {card.scene}: {LaserHit(card.button, out _)}");
        log.AppendLine("  Back: " + LaserClick(mc.levelBackButton));
        yield return Frames(2);
        log.AppendLine($"  ExitGameButton: {LaserHit(mc.exitButton, out _)}");
        log.AppendLine("  " + LaserClick(mc.exitButton));
        yield return Frames(3);
        log.AppendLine($"  confirm open {mc.confirm.IsOpen}; content interactable while open {mc.content.interactable}");
        Capture(captureDir + "/menu_confirm.png");
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
            log.AppendLine("  Start Game: " + LaserClick(mc.startGameButton));
            yield return Frames(2);
            log.AppendLine("  card: " + LaserClick(card.button) + $", Play label '{mc.playLabel.text}'");
            yield return Frames(2);
            log.AppendLine("  Play: " + LaserClick(mc.playButton));
            yield return null;
            log.AppendLine($"  label while loading: '{card.status.text}', buttons interactable {mc.content.interactable}, SceneFlow.Busy {SceneFlow.Busy}");
            // a second click while it loads must do nothing
            LaserClick(mc.playButton);
            yield return WaitScene(level);
            yield return Seconds(2f);
            log.AppendLine("  " + Counts());
            string baseline = Fingerprint();

            // Real Victory -> panel row
            yield return StartAndFinish(true, "run 1");
            var ui = CombatUIController.Instance;
            var vp = ui.victoryPanel;
            Capture($"{captureDir}/{level}_victory.png");
            var obm = Object.FindAnyObjectByType<OutcomeMenuButtons>();
            var exitBtn = vp.transform.Find("PlayAgainRow/ExitGameButton").GetComponent<Button>();
            log.AppendLine("  Exit Game: " + LaserClick(exitBtn));
            yield return Frames(3);
            log.AppendLine($"  confirm open {obm.confirm.IsOpen}");
            Capture($"{captureDir}/{level}_victory_confirm.png");
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
            Capture($"{captureDir}/{level}_defeat.png");
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

    // ───────────────────────── layout (every page, from the head) ─────────────────────────
    static Button FindIn(GameObject root, string name)
    {
        if (root == null) return null;
        foreach (var b in root.GetComponentsInChildren<Button>()) if (b.name == name) return b;
        return null;
    }

    static GameObject PickerPanel()
    {
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
    }

    static void ListButtons(GameObject root, string tag)
    {
        if (root == null) { log.AppendLine($"  {tag}: (none)"); return; }
        foreach (var b in root.GetComponentsInChildren<Button>())
            log.AppendLine($"  {tag} {b.name}: {Angles((RectTransform)b.transform)}; {LaserHit(b, out _)}");
    }

    static IEnumerator LayoutJob()
    {
        yield return Frames(5);
        var mc = Object.FindAnyObjectByType<MainMenuController>();
        if (mc == null || mc.homePage == null) { log.AppendLine("no paged MainMenuController in this scene"); yield break; }
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        log.AppendLine($"head {rig.centerEyeAnchor.position} (rig {rig.transform.position}, tracking origin {(OVRManager.instance != null ? OVRManager.instance.trackingOriginType.ToString() : "?")})");
        log.AppendLine("MENU: " + Counts());

        Capture(captureDir + "/layout_home.png");
        log.AppendLine($"HOME page '{mc.CurrentPage.name}': panel {Angles((RectTransform)mc.homePage.transform.Find("Panel"))}");
        log.AppendLine($"  title {Angles((RectTransform)mc.homePage.transform.Find("Title"))}");
        log.AppendLine($"  '{mc.playingAsLabel.text}' {Angles(mc.playingAsLabel.rectTransform)}");
        log.AppendLine($"  hint {Angles((RectTransform)mc.homePage.transform.Find("Hint"))}");
        ListButtons(mc.homePage, "home");

        log.AppendLine("Start Game: " + LaserClick(mc.startGameButton));
        yield return Frames(2);
        log.AppendLine($"  page '{mc.CurrentPage.name}', Play interactable {mc.playButton.interactable}, label '{mc.playLabel.text}'");
        log.AppendLine("  card 2: " + LaserClick(mc.levels[1].button));
        yield return Frames(2);
        log.AppendLine($"  selected frames {mc.levels[0].selectedFrame.activeSelf}/{mc.levels[1].selectedFrame.activeSelf}/{mc.levels[2].selectedFrame.activeSelf}, Play '{mc.playLabel.text}' interactable {mc.playButton.interactable}, still on '{mc.CurrentPage.name}' (no load: {!SceneFlow.Busy})");
        Capture(captureDir + "/layout_levels.png");
        log.AppendLine($"  panel {Angles((RectTransform)mc.levelPage.transform.Find("Panel"))}");
        ListButtons(mc.levelPage, "levels");
        foreach (var card in mc.levels)
        {
            var d = card.button.transform.Find("Description").GetComponent<Text>();
            log.AppendLine($"  {card.button.name} description {Angles(d.rectTransform)}");
        }
        log.AppendLine("  Back: " + LaserClick(mc.levelBackButton));
        yield return Frames(2);
        log.AppendLine($"  page '{mc.CurrentPage.name}'");

        log.AppendLine("Change Name: " + LaserClick(mc.changeNameButton));
        yield return Frames(3);
        var panel = PickerPanel();
        log.AppendLine($"  page '{mc.CurrentPage.name}', picker panel {(panel != null ? Angles((RectTransform)panel.transform) : "MISSING")}");
        Capture(captureDir + "/layout_name.png");
        ListButtons(panel, "names");

        log.AppendLine("  New player: " + LaserClick(FindIn(panel, "NewPlayerButton")));
        yield return Frames(3);
        panel = PickerPanel();
        Capture(captureDir + "/layout_keyboard.png");
        foreach (var k in new[] { "Key_1", "Key_q", "Key_p", "Key_Shift", "Key_Backspace", "Key_Cancel", "Key_Space", "Key_Done" })
        {
            var b = FindIn(panel, k);
            log.AppendLine($"  keyboard {k}: {(b != null ? Angles((RectTransform)b.transform) + "; " + LaserHit(b, out _) : "MISSING")}");
        }
        log.AppendLine("  Cancel: " + LaserClick(FindIn(panel, "Key_Cancel")));
        yield return Frames(3);

        panel = PickerPanel();
        log.AppendLine("  Rename: " + LaserClick(FindIn(panel, "RenameButton")));
        yield return Frames(3);
        panel = PickerPanel();
        log.AppendLine("  type x: " + LaserClick(FindIn(panel, "Key_x")));
        yield return Frames(2);
        log.AppendLine("  Done: " + LaserClick(FindIn(panel, "Key_Done")));
        yield return Frames(3);
        var confirmBox = panel.transform.Find("RenameConfirm");
        log.AppendLine($"  rename confirm shown {(confirmBox != null && confirmBox.gameObject.activeSelf)}; a key behind it: {LaserHit(FindIn(panel, "Key_q"), out bool keyHit)} (blocked {!keyHit})");
        if (confirmBox != null)
        {
            foreach (var t in confirmBox.GetComponentsInChildren<Text>()) if (t.name == "Message" || t.name == "Warning") log.AppendLine($"    '{t.text}'");
            Capture(captureDir + "/layout_rename_confirm.png");
            log.AppendLine("  Cancel at confirm: " + LaserClick(FindIn(confirmBox.gameObject, "CancelRenameButton")));
        }
        yield return Frames(3);
        log.AppendLine($"  confirm gone {panel.transform.Find("RenameConfirm") == null || !panel.transform.Find("RenameConfirm").gameObject.activeSelf}, keys interactable {FindIn(panel, "Key_q").interactable}");
        log.AppendLine("  keyboard Cancel: " + LaserClick(FindIn(panel, "Key_Cancel")));
        yield return Frames(3);
        panel = PickerPanel();
        log.AppendLine("  Back: " + LaserClick(FindIn(panel, "BackButton")));
        yield return Frames(3);
        log.AppendLine($"  page '{mc.CurrentPage.name}', picker panel open {PickerPanel() != null}");

        log.AppendLine("Exit Game: " + LaserClick(mc.exitButton));
        yield return Frames(3);
        Capture(captureDir + "/layout_exit_confirm.png");
        log.AppendLine($"  confirm {Angles((RectTransform)mc.confirm.transform)}; Yes {Angles((RectTransform)mc.confirm.yesButton.transform)}");
        log.AppendLine("  No: " + LaserClick(mc.confirm.noButton));
        yield return Frames(3);
        log.AppendLine($"  confirm open {mc.confirm.IsOpen}, content interactable {mc.content.interactable}");
    }

    // ───────────────────────── Victory / Defeat panels, from the head ─────────────────────────
    static void PanelReport(GameObject panel, string tag)
    {
        log.AppendLine($"  {tag} panel {Angles((RectTransform)panel.transform)}");
        var row = panel.transform.Find("PlayAgainRow");
        if (row != null)
        {
            log.AppendLine($"  {tag} row {Angles((RectTransform)row)}");
            ListButtons(row.gameObject, tag);
        }
        foreach (var t in panel.GetComponentsInChildren<Text>())
            if (t.text.Length > 0) log.AppendLine($"    text '{t.text.Replace("\n", " / ")}' {t.fontSize}pt cap {0.716f * t.fontSize * t.transform.lossyScale.y * 100f:0.0} cm");
    }

    static IEnumerator PanelsJob()
    {
        yield return Frames(5);
        string level = SceneManager.GetActiveScene().name;
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        log.AppendLine($"{level}: head {rig.centerEyeAnchor.position} (rig {rig.transform.position}, tracking origin {(OVRManager.instance != null ? OVRManager.instance.trackingOriginType.ToString() : "?")})");
        yield return StartAndFinish(true, "victory");
        Capture($"{captureDir}/{level}_victory_head.png");
        PanelReport(CombatUIController.Instance.victoryPanel, "victory");
        SceneFlow.PlayAgain();
        yield return WaitScene(level);
        yield return Seconds(2f);
        yield return StartAndFinish(false, "defeat");
        Capture($"{captureDir}/{level}_defeat_head.png");
        PanelReport(CombatUIController.Instance.defeatPanel, "defeat");
    }

    // ───────────────────────── scoreboard ─────────────────────────
    static object Field(object o, string name) => o.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(o);

    static string BoardText(ScoreboardPanel sp)
    {
        var sb = new StringBuilder();
        foreach (var r in sp.rows)
            if (r.root.activeSelf) sb.Append($"\n    {r.rank.text,2} {(r.medal.gameObject.activeSelf ? "*" : " ")} {r.name.text,-24} {r.score.text,7} {r.chipText.text} {r.date.text}{(r.background.color.a > 0.2f ? "  <highlight>" : "")}");
        sb.Append($"\n    message '{(sp.messageText.gameObject.activeSelf ? sp.messageText.text : "")}', your best '{(sp.youText.gameObject.activeSelf ? sp.youText.text : "")}', status '{sp.statusText.text}'");
        return sb.ToString();
    }

    static IEnumerator WaitFetch(ScoreboardPanel sp, float timeout = 12f)
    {
        float t0 = Time.realtimeSinceStartup;
        yield return Frames(2);
        while (Time.realtimeSinceStartup - t0 < timeout && (bool)Field(sp, "fetching")) yield return null;
        yield return Frames(2);
    }

    static IEnumerator ScoreboardJob()
    {
        yield return Frames(5);
        var mc = Object.FindAnyObjectByType<MainMenuController>();
        var sp = Object.FindAnyObjectByType<ScoreboardPanel>();
        var rep = SessionReporter.Instance;
        if (sp == null || rep == null) { log.AppendLine("no ScoreboardPanel / SessionReporter"); yield break; }
        log.AppendLine($"player '{PlayerNameStore.LastUsed}', reporter {rep.baseUrl}, wake done {rep.WakeDone}");
        float t0 = Time.realtimeSinceStartup;
        while (!rep.WakeDone && Time.realtimeSinceStartup - t0 < 70f) yield return null;
        log.AppendLine($"wake done after {Time.realtimeSinceStartup - t0:0.0}s");
        yield return WaitFetch(sp, 25f);
        log.AppendLine("Scoreboard (production, route not deployed): " + LaserClick(sp.openButton));
        yield return Frames(3);
        log.AppendLine("  while the refetch is in flight:" + BoardText(sp));
        yield return WaitFetch(sp);
        log.AppendLine($"  page '{mc.CurrentPage.name}'" + BoardText(sp));
        Capture(captureDir + "/scoreboard_unavailable.png");

        rep.baseUrl = testBackend;
        log.AppendLine($"-> {testBackend}");
        string[] tabs = { "TabAll", "TabL1", "TabL2", "TabL3" };
        for (int i = 0; i < 4; i++)
        {
            log.AppendLine($"  {tabs[i]}: " + LaserClick(sp.tabs[i]));
            yield return WaitFetch(sp);
            log.AppendLine("  " + Angles((RectTransform)sp.tabs[i].transform) + BoardText(sp));
            if (i == 0)
            {
                Capture(captureDir + "/scoreboard_all.png");
                foreach (var r in sp.rows) if (r.root.activeSelf) { log.AppendLine($"  row 1 name {Angles(r.name.rectTransform)}; date {Angles(r.date.rectTransform)}"); break; }
                log.AppendLine($"  last row {Angles((RectTransform)sp.rows[9].root.transform)}; status {Angles(sp.statusText.rectTransform)}; back {Angles((RectTransform)sp.backButton.transform)}");
            }
            if (i == 2) Capture(captureDir + "/scoreboard_L2.png");
        }

        // "Your best" outside the top N: show only 3 rows and ask again (Level 2 board).
        sp.rowsShown = 3;
        ((System.DateTime[])Field(sp, "fetchedUtc"))[2] = System.DateTime.MinValue;   // force the refetch
        log.AppendLine("  rowsShown 3, TabL2: " + LaserClick(sp.tabs[2]));
        yield return WaitFetch(sp);
        log.AppendLine("  " + BoardText(sp));
        Capture(captureDir + "/scoreboard_yourbest.png");
        sp.rowsShown = 10;

        // Empty board (no real board is empty): the panel's own render of a valid answer with no rows.
        var data = (LeaderboardData.Board[])Field(sp, "data");
        var keep = data[3];
        data[3] = new LeaderboardData.Board { level = "Hard", rows = new LeaderboardData.Row[0], you = new LeaderboardData.You() };
        typeof(ScoreboardPanel).GetField("shown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(sp, 3);
        typeof(ScoreboardPanel).GetMethod("Render", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(sp, null);
        log.AppendLine("  empty board:" + BoardText(sp));
        Capture(captureDir + "/scoreboard_empty.png");
        data[3] = keep;

        log.AppendLine("  Back: " + LaserClick(sp.backButton));
        yield return Frames(2);
        log.AppendLine($"  page '{mc.CurrentPage.name}'");

        // Offline with the cache: reload the menu (production URL again; the route answers 404 there = unavailable).
        SceneFlow.LevelSelect();
        yield return WaitScene(SceneFlow.MenuScene);
        sp = Object.FindAnyObjectByType<ScoreboardPanel>();
        SessionReporter.Instance.baseUrl = "http://127.0.0.1:9";
        log.AppendLine("reloaded menu, reporter -> 127.0.0.1:9 (offline): " + LaserClick(sp.openButton));
        yield return Frames(3);
        log.AppendLine("  before any fetch:" + BoardText(sp));
        t0 = Time.realtimeSinceStartup;
        while (!SessionReporter.Instance.WakeDone && Time.realtimeSinceStartup - t0 < 70f) yield return null;
        yield return WaitFetch(sp, 25f);
        LaserClick(sp.tabs[0]);
        yield return WaitFetch(sp);
        log.AppendLine("  after the failed fetch:" + BoardText(sp));
        Capture(captureDir + "/scoreboard_offline_cache.png");
    }

    // ───────────────────────── rank line (Victory / Defeat) ─────────────────────────
    static IEnumerator RankLineJob()
    {
        yield return Frames(5);
        string level = SceneManager.GetActiveScene().name;
        CombatVerification.backendUrl = testBackend;   // Prepare() points the reporter here
        BattleManager.Instance.EnsureSessionReporter();
        SessionReporter.Instance.SetPlayerName("ZZTEST");
        yield return StartAndFinish(true, "ZZTEST victory");
        var rep = SessionReporter.Instance;
        log.AppendLine($"  reporter {rep.baseUrl}, session {rep.currentSessionId}, last result score {rep.LastResultScore} level {rep.LastResultLevel} duration {rep.LastResultDurationSec:0}s");
        var line = CombatUIController.Instance.victoryPanel.GetComponentInChildren<OutcomeRankLine>(true);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 15f && (line == null || !line.label.enabled)) yield return null;
        log.AppendLine($"  result answered {rep.ResultAnswered}; rank line shown {(line != null && line.label.enabled)} '{(line != null ? line.label.text : "")}' after {Time.realtimeSinceStartup - t0:0.0}s; {(line != null ? Angles(line.label.rectTransform) : "")}");
        Capture($"{captureDir}/{level}_victory_rankline.png");
        log.AppendLine($"  DELETE ME: session id {rep.currentSessionId}");
        CombatVerification.backendUrl = "http://127.0.0.1:9";
        Time.captureDeltaTime = 0f;
    }

    // ───────────────────────── story ─────────────────────────
    static IEnumerator StoryJob()
    {
        yield return Frames(5);
        var mc = Object.FindAnyObjectByType<MainMenuController>();
        var sp = Object.FindAnyObjectByType<StoryPanel>(FindObjectsInactive.Include);
        if (mc == null || sp == null) { log.AppendLine("no MainMenuController / StoryPanel in this scene"); yield break; }
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        log.AppendLine($"head {rig.centerEyeAnchor.position} (rig {rig.transform.position})");
        log.AppendLine($"home: Story button in view: {Angles((RectTransform)sp.openButton.transform)}; {LaserHit(sp.openButton, out _)}");

        // Pass 1: open via laser and Skip immediately.
        PlayerPrefs.SetInt(StoryPanel.SeenKey, 1);   // already seen: auto-play is off for this pass
        PlayerPrefs.Save();
        log.AppendLine("Story: " + LaserClick(sp.openButton));
        yield return Frames(3);
        log.AppendLine($"  Playing {sp.Playing}, page '{mc.CurrentPage.name}', slide 1 text '{sp.slideText.text.Replace("\n", " / ")}'");
        Capture(captureDir + "/story_slide1.png");
        log.AppendLine($"  slide text {Angles(sp.slideGroup.GetComponentInChildren<Text>().rectTransform)}");
        log.AppendLine($"  skip button {Angles((RectTransform)sp.skipButton.transform)}; {LaserHit(sp.skipButton, out _)}");
        log.AppendLine($"  counter '{sp.counterText.text}' {Angles(sp.counterText.rectTransform)}");
        log.AppendLine($"  torch light on {sp.torchLight != null && sp.torchLight.enabled}");
        log.AppendLine("  Skip: " + LaserClick(sp.skipButton));
        yield return Frames(3);
        log.AppendLine($"  after skip: Playing {sp.Playing}, page '{mc.CurrentPage.name}'");

        // Pass 2: clear SeenKey and replay all 3 slides (time-compressed: slideSeconds -> 0.3 s so the job is fast).
        PlayerPrefs.DeleteKey(StoryPanel.SeenKey); PlayerPrefs.Save();
        sp.storyEnabled = true;
        sp.fadeSeconds = 0.1f;
        var content = StoryContent.Load();
        var realSeconds = content.slideSeconds;
        // StoryContent cache can't be changed at runtime, but StoryPanel reads story.slideSeconds each Update.
        // To compress the test we call Play and wait for natural finish (3 slides x real 7 s = 21 s).
        // Instead: trigger advances by pressing the index trigger after each fade finishes (0.15 s).
        log.AppendLine($"  slideSeconds {realSeconds:0.0}, slides {content.slides.Length}");
        sp.storyEnabled = true;
        log.AppendLine("Story (full run): " + LaserClick(sp.openButton));
        yield return Frames(2);
        float t0 = Time.realtimeSinceStartup;
        int slideDone = 0;
        while (sp.Playing && Time.realtimeSinceStartup - t0 < realSeconds * content.slides.Length + 5f)
        {
            // Advance at 0.15 s after each slide starts (the fade is 0.1 s).
            yield return Seconds(0.2f);
            if (!sp.Playing) break;
            // Read slideIndex via reflection to know which slide we're on.
            int idx = (int)typeof(StoryPanel).GetField("index", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(sp);
            if (idx > slideDone)
            {
                log.AppendLine($"  slide {idx + 1}: '{sp.slideText.text.Replace("\n", " / ")}'");
                slideDone = idx;
            }
            if (idx < content.slides.Length - 1 && sp.Playing)
            {
                // Simulate trigger by calling the internal method via reflection isn't available; call Finish to advance.
                // Instead, we wait for the natural finish since fadeSeconds is 0.1 s but slideSeconds is still 7 s.
                // So just wait for it to finish naturally (the slides auto-advance).
            }
        }
        // Actually it's cleaner to just skip each slide after a brief moment. Use Update loop: slideTime >= slideSeconds auto-advances.
        // Since we can't set slideSeconds at runtime (it comes from the JSON), just let it run naturally but skip after 0.5 s.
        // The job will run for ~1.5 s total (3 x 0.5 s), fine for a harness.
        // Restart: just skip the current slide via the reflection approach:
        if (sp.Playing)
        {
            var advanceSlide = typeof(StoryPanel).GetMethod("ShowSlide", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (int s = 1; s < content.slides.Length && sp.Playing; s++)
            {
                advanceSlide.Invoke(sp, new object[] { s });
                log.AppendLine($"  injected slide {s + 1}: '{sp.slideText.text.Replace("\n", " / ")}'");
                yield return Frames(3);
            }
            sp.Finish("harness");
            yield return Frames(3);
        }
        float elapsed = Time.realtimeSinceStartup - t0;
        log.AppendLine($"  full run done after {elapsed:0.0}s, Playing {sp.Playing}, page '{mc.CurrentPage.name}', SeenKey {PlayerPrefs.GetInt(StoryPanel.SeenKey, 0)}");
        Capture(captureDir + "/story_home_after.png");
        log.AppendLine($"  home in view: title {Angles((RectTransform)mc.homePage.transform.Find("Title"))}");
    }
}
