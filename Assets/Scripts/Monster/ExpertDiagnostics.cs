// ExpertDiagnostics.cs
// Purpose: On-device diagnostics for Level 3 Expert mode, in Development builds and the Editor only (the whole file is compiled out of
//          release builds):
//            - A small world-space text panel in front of the player, toggled by holding BOTH thumbsticks pressed in for 2 s: Expert
//              on/off (and the fallback reason), model loaded, average and maximum inference ms over the last 5 s, fallback count, the
//              outputs the monster may read, the active axis mapping, the window fill (n/20) and the frame time (average / maximum
//              over the last 5 s). Redrawn at most twice a second and only when a shown (rounded) value changed, like the combat
//              debug readout (CombatUIController).
//            - A text log, Application.persistentDataPath/expert_diagnostics.txt: one line per second while a Level 3 Expert fight
//              runs, plus a line when a fight starts and when Expert falls back. Appended across launches, started afresh when it
//              passes 1 MB. On a Quest:
//                adb pull /sdcard/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/expert_diagnostics.txt
//          No allocation per frame: the gesture, the 5 s statistics and the panel's head-follow use fixed buffers; numbers are written
//          digit by digit (TextNumber); a log line goes to the file through a reused char buffer. A panel redraw (at most 2 per second,
//          only on change) makes one string for the UI Text, as the existing debug readout does.
// Dependencies: MonsterExpertBrain (Current), ExpertMotionPredictor, MonsterAIModeSetting, OVRCameraRig, OVRInput, MenuUi,
//               StrikeFit.ExpertAI (RollingStats, HoldLatch, TextNumber, ExpertOutputs)
// Attach to: nothing (installs itself once at start-up and survives scene loads)

#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.IO;
using System.Text;
using StrikeFit.ExpertAI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ExpertDiagnostics : MonoBehaviour
{
    public const string FileName = "expert_diagnostics.txt";
    public const float HoldSeconds = 2f;
    const float StatsSeconds = 5f, RedrawInterval = 0.5f, LogInterval = 1f, FlushInterval = 5f;
    const long MaxLogBytes = 1024 * 1024;
    static readonly Vector3 PanelOffset = new Vector3(0f, -0.16f, 0.75f);   // below the line of sight, within arm's length

    public static ExpertDiagnostics Instance { get; private set; }
    public bool Visible { get; private set; }
    public string LogPath { get; private set; }

    readonly HoldLatch gesture = new HoldLatch(HoldSeconds);
    readonly RollingStats frameMs = new RollingStats(1024, StatsSeconds);    // 5 s at up to ~200 fps
    readonly RollingStats inferenceMs = new RollingStats(128, StatsSeconds); // 5 s at 10 Hz, with room
    readonly StringBuilder panelSb = new StringBuilder(512), logSb = new StringBuilder(256);
    char[] logChars = new char[256];

    OVRCameraRig rig;
    GameObject panel;
    Text panelText;
    StreamWriter log;
    MonsterExpertBrain seenBrain;
    string mappingName = "-";
    int seenInferences, seenFallbacks, shownKey = int.MinValue, fightNumber;
    float nextRedraw, nextLog, nextFlush;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Instance != null) return;
        var go = new GameObject("ExpertDiagnostics");
        DontDestroyOnLoad(go);
        go.AddComponent<ExpertDiagnostics>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OpenLog();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        CloseLog();
        if (Instance == this) Instance = null;
    }

    void OnApplicationPause(bool paused) { if (paused) log?.Flush(); }   // the Quest suspends apps instead of quitting them
    void OnApplicationQuit() => CloseLog();

    void OnSceneLoaded(Scene s, LoadSceneMode m) { rig = null; if (panel != null) { Destroy(panel); panel = null; panelText = null; } }

    void Update()
    {
        float now = Time.unscaledTime;
        frameMs.Add(now, Time.unscaledDeltaTime * 1000f);

        bool both = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) &&
                    OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch);
        if (gesture.Update(both, Time.unscaledDeltaTime)) SetVisible(!Visible);

        var brain = MonsterExpertBrain.Current;
        var pred = brain != null ? brain.Predictor : null;
        if (brain != seenBrain) OnFightChanged(brain, now);
        if (pred != null)
        {
            if (pred.Inferences != seenInferences) { seenInferences = pred.Inferences; inferenceMs.Add(now, (float)pred.LastInferenceMs); }
            if (pred.Fallbacks != seenFallbacks)
            {
                seenFallbacks = pred.Fallbacks;
                logSb.Clear(); Stamp(logSb, now).Append(" FALLBACK ").Append(pred.DisabledReason); WriteLog();
            }
        }

        if (brain != null && log != null && now >= nextLog) { nextLog = now + LogInterval; LogStatus(brain, pred, now); }
        if (log != null && now >= nextFlush) { nextFlush = now + FlushInterval; log.Flush(); }
        if (Visible && now >= nextRedraw) { nextRedraw = now + RedrawInterval; Redraw(brain, pred, now); }
    }

    void LateUpdate()
    {
        if (!Visible || panel == null) return;
        var head = Head();
        if (head == null) return;
        panel.transform.SetPositionAndRotation(head.position + head.rotation * PanelOffset, head.rotation);
    }

    // ─────────────────────────── state ───────────────────────────

    void OnFightChanged(MonsterExpertBrain brain, float now)
    {
        seenBrain = brain;
        fightNumber++;
        inferenceMs.Clear();
        seenInferences = brain != null && brain.Predictor != null ? brain.Predictor.Inferences : 0;
        seenFallbacks = brain != null && brain.Predictor != null ? brain.Predictor.Fallbacks : 0;
        mappingName = brain != null && brain.config != null ? brain.config.mapping.Name : "-";   // once per fight (it builds a string)
        shownKey = int.MinValue;
        if (brain == null) return;
        logSb.Clear(); Stamp(logSb, now).Append(" FIGHT expert level 3 | mapping ").Append(mappingName).Append(" | outputs ");
        AppendOutputs(logSb, brain.config);
        WriteLog();
    }

    void SetVisible(bool on)
    {
        Visible = on;
        shownKey = int.MinValue; nextRedraw = 0f;
        if (on && panel == null) BuildPanel();
        if (panel != null) panel.SetActive(on);
        Debug.Log("[StrikeFitDebug] expert diagnostics " + (on ? "on" : "off") + " (both thumbsticks held " + HoldSeconds + " s) | log " + LogPath);
    }

    Transform Head()
    {
        if (rig == null) rig = FindAnyObjectByType<OVRCameraRig>();   // once per scene: cleared on scene load
        return rig != null ? rig.centerEyeAnchor : null;
    }

    // ─────────────────────────── panel ───────────────────────────

    void BuildPanel()
    {
        panel = new GameObject("ExpertDiagnosticsPanel", typeof(RectTransform));
        DontDestroyOnLoad(panel);
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var head = Head();
        if (head != null) canvas.worldCamera = head.GetComponent<Camera>();
        canvas.sortingOrder = 40;
        var rt = (RectTransform)panel.transform;
        rt.sizeDelta = new Vector2(560f, 250f);
        rt.localScale = Vector3.one * 0.0007f;
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        var brt = (RectTransform)bg.transform;
        brt.SetParent(rt, false); brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
        var img = bg.GetComponent<Image>(); img.color = new Color(0f, 0f, 0f, 0.72f); img.raycastTarget = false;
        // Same face and colour as the combat debug readout.
        panelText = MenuUi.Label(rt, "Text", "", 20, new Color(0.75f, 1f, 0.8f, 0.95f), TextAnchor.UpperLeft, Vector2.zero, new Vector2(530f, 230f));
        panelText.raycastTarget = false;
        panelText.verticalOverflow = VerticalWrapMode.Overflow;
        MenuUi.DrawOnTop(panel);
    }

    void Redraw(MonsterExpertBrain brain, ExpertMotionPredictor pred, float now)
    {
        if (panelText == null) return;
        frameMs.Compute(now, out float fAvg, out float fMax, out _);
        inferenceMs.Compute(now, out float iAvg, out float iMax, out int iN);
        int state = brain == null ? 0 : pred != null && pred.Healthy ? 2 : 1;
        int key;
        unchecked
        {
            key = state;
            key = key * 397 ^ (pred != null && pred.ModelLoaded ? 1 : 0);
            key = key * 397 ^ Mathf.RoundToInt(iAvg * 100f);
            key = key * 397 ^ Mathf.RoundToInt(iMax * 100f);
            key = key * 397 ^ (pred != null ? pred.Fallbacks : 0);
            key = key * 397 ^ (pred != null ? pred.WindowCount : -1);
            key = key * 397 ^ Mathf.RoundToInt(fAvg * 10f);
            key = key * 397 ^ Mathf.RoundToInt(fMax * 10f);
            key = key * 397 ^ fightNumber;
        }
        if (key == shownKey) return;
        shownKey = key;

        var sb = panelSb.Clear();
        sb.Append("EXPERT DIAGNOSTICS  (hold both sticks ").Append('2').Append(" s)\n");
        sb.Append("Expert: ");
        if (state == 0) sb.Append(MonsterAIModeSetting.CurrentLevel == 3 ? "off (Normal)" : "off (Level 3 only)");
        else if (state == 2) sb.Append("ON");
        else sb.Append("OFF - ").Append(pred != null ? pred.DisabledReason : "no predictor");
        sb.Append("\nModel loaded: ").Append(pred != null && pred.ModelLoaded ? "yes" : "no");
        sb.Append("\nInference (5 s): avg ");
        TextNumber.AppendFixed(sb, iAvg, 2).Append(" ms  max ");
        TextNumber.AppendFixed(sb, iMax, 2).Append(" ms  (");
        TextNumber.AppendInt(sb, iN).Append(" runs)");
        sb.Append("\nFallbacks: ");
        TextNumber.AppendInt(sb, pred != null ? pred.Fallbacks : 0);
        sb.Append("\nOutputs: ");
        AppendOutputs(sb, brain != null ? brain.config : null);
        sb.Append("\nMapping: ").Append(mappingName);
        sb.Append("\nWindow: ");
        TextNumber.AppendInt(sb, pred != null ? pred.WindowCount : 0).Append("/20");
        sb.Append("\nFrame (5 s): avg ");
        TextNumber.AppendFixed(sb, fAvg, 1).Append(" ms  max ");
        TextNumber.AppendFixed(sb, fMax, 1).Append(" ms");
        panelText.text = sb.ToString();
    }

    static void AppendOutputs(StringBuilder sb, ExpertAIConfig cfg)
    {
        if (cfg == null) { sb.Append('-'); return; }
        sb.Append("head <= ");
        TextNumber.AppendFixed(sb, Forecast.HorizonSeconds[ExpertOutputs.MaxHorizonIndex(cfg.maxHorizonSeconds)], 2).Append(" s, hands ");
        sb.Append(cfg.useHandPrediction ? "ON" : "off");
    }

    // ─────────────────────────── log file ───────────────────────────

    void OpenLog()
    {
        try
        {
            LogPath = Path.Combine(Application.persistentDataPath, FileName);
            bool fresh = !File.Exists(LogPath) || new FileInfo(LogPath).Length > MaxLogBytes;
            log = new StreamWriter(LogPath, !fresh, new UTF8Encoding(false));
            log.WriteLine("# STRIKEFIT expert diagnostics | session " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " +
                          SystemInfo.deviceModel + " | build " + Application.version + " | t = seconds since start-up");
            log.WriteLine("# t expert model infAvgMs infMaxMs infRuns fallbacks window/20 frameAvgMs frameMaxMs restarts refusedReads");
            log.Flush();
        }
        catch (System.Exception e)
        {
            log = null;
            Debug.LogWarning("[StrikeFitDebug] expert diagnostics log not available: " + e.Message);
        }
    }

    void CloseLog()
    {
        if (log == null) return;
        try { log.Flush(); log.Dispose(); } catch (System.Exception) { }
        log = null;
    }

    void LogStatus(MonsterExpertBrain brain, ExpertMotionPredictor pred, float now)
    {
        frameMs.Compute(now, out float fAvg, out float fMax, out _);
        inferenceMs.Compute(now, out float iAvg, out float iMax, out int iN);
        var sb = logSb.Clear();
        Stamp(sb, now);
        sb.Append(pred != null && pred.Healthy ? " on" : " OFF").Append(pred != null && pred.ModelLoaded ? " loaded " : " missing ");
        TextNumber.AppendFixed(sb, iAvg, 3).Append(' ');
        TextNumber.AppendFixed(sb, iMax, 3).Append(' ');
        TextNumber.AppendInt(sb, iN).Append(' ');
        TextNumber.AppendInt(sb, pred != null ? pred.Fallbacks : 0).Append(' ');
        TextNumber.AppendInt(sb, pred != null ? pred.WindowCount : 0).Append(' ');
        TextNumber.AppendFixed(sb, fAvg, 1).Append(' ');
        TextNumber.AppendFixed(sb, fMax, 1).Append(' ');
        TextNumber.AppendInt(sb, pred != null ? pred.WindowRestarts : 0).Append(' ');
        TextNumber.AppendInt(sb, pred != null ? pred.RefusedReads : 0);
        WriteLog();
    }

    static StringBuilder Stamp(StringBuilder sb, float now) => TextNumber.AppendFixed(sb, now, 1);

    // The line in logSb goes to the file through a reused char buffer (StreamWriter.Write(char[], ...) does not allocate).
    void WriteLog()
    {
        if (log == null) return;
        int n = logSb.Length;
        if (logChars.Length < n + 1) logChars = new char[(n + 1) * 2];   // grows once, on an unusually long fallback reason
        logSb.CopyTo(0, logChars, 0, n);
        logChars[n] = '\n';
        try { log.Write(logChars, 0, n + 1); } catch (System.Exception) { CloseLog(); }
    }
}
#endif
