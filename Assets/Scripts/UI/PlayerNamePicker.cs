// PlayerNamePicker.cs
// Purpose: Pre-battle "Who's playing?" step. Shows preset name buttons on CombatCanvas and hides the
//          Start button until a name is chosen (laser click), or auto-picks the default after
//          autoSelectSeconds. The name goes to SessionReporter for the next battle's requests.
// Dependencies: SessionReporter, BattleManager, CombatCanvas/StartBattleUIButton (cloned as the button
//               template, so the new buttons use the exact raycast/click setup the laser already uses).
// Attach to: nothing — BattleManager adds it to BattleSystems at runtime (every level, no scene setup).

using UnityEngine;
using UnityEngine.UI;

public class PlayerNamePicker : MonoBehaviour
{
    public static PlayerNamePicker Instance;

    /// <summary>True while the player still has to pick a name; BattleManager refuses to start meanwhile.</summary>
    public static bool BlocksStart => Instance != null && Instance.isActiveAndEnabled && Instance.picking;

    [Tooltip("Names offered on the panel.")]
    public string[] presetNames = { "Player 1", "Player 2", "Player 3", "Guest" };
    [Tooltip("Seconds before the default name is picked automatically (first panel of each scene load only).")]
    public float autoSelectSeconds = 10f;
    [Tooltip("Scene path of the Start button to gate and clone.")]
    public string startButtonPath = "CombatCanvas/StartBattleUIButton";

    // Survives the scene reload on Retry, so the same person keeps their name by default.
    static string lastChosen;

    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.94f);
    static readonly Color NameColor = new Color(0.16f, 0.32f, 0.62f, 0.95f);
    static readonly Color DefaultNameColor = new Color(0.85f, 0.55f, 0.1f, 0.95f);
    static readonly Color ChangeColor = new Color(0.25f, 0.25f, 0.3f, 0.9f);

    bool picking;
    bool autoSelectArmed;
    float autoSelectLeft;
    string defaultName;
    GameObject startButton;
    Text startLabel;
    string startLabelOriginal;
    GameObject panel;
    GameObject changeButton;
    Text countdownText;
    int lastShownSeconds = -1;

    void Awake()
    {
        Instance = this;
        picking = true; // closed from the first frame; Start builds the panel or falls back to the default
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (presetNames == null || presetNames.Length == 0) presetNames = new[] { SessionReporter.DefaultPlayerName };
        defaultName = !string.IsNullOrEmpty(lastChosen) ? lastChosen : presetNames[0];

        startButton = GameObject.Find(startButtonPath);
        if (startButton == null || startButton.GetComponent<Button>() == null)
        {
            // No laser UI in this scene: never block the fight, just use the default name.
            Debug.LogWarning($"[PlayerNamePicker] '{startButtonPath}' not found; using '{defaultName}'.");
            Choose(defaultName);
            return;
        }
        startLabel = startButton.GetComponentInChildren<Text>(true);
        if (startLabel != null) startLabelOriginal = startLabel.text;

        if (BattleManager.Instance != null)
            BattleManager.Instance.OnBattleStarted += HandleBattleStarted;

        BuildChangeButton();
        OpenPanel(autoSelect: true);
    }

    void Update()
    {
        if (!picking || !autoSelectArmed) return;

        // Per-frame clamp: time spent paused (headset off) never counts towards the auto-pick.
        autoSelectLeft -= Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float left = autoSelectLeft;
        if (left <= 0f)
        {
            Choose(defaultName);
            return;
        }
        int secs = Mathf.CeilToInt(left);
        if (secs != lastShownSeconds && countdownText != null)
        {
            lastShownSeconds = secs;
            countdownText.text = $"Aim at your name and pull the trigger  -  {defaultName} in {secs}s";
        }
    }

    void OpenPanel(bool autoSelect)
    {
        picking = true;
        autoSelectArmed = autoSelect && autoSelectSeconds > 0f;
        autoSelectLeft = autoSelectSeconds;
        lastShownSeconds = -1;

        if (panel != null) Destroy(panel);
        panel = BuildPanel();

        startButton.SetActive(false);
        if (changeButton != null) changeButton.SetActive(false);
        if (countdownText != null && !autoSelectArmed) countdownText.text = "Aim at your name and pull the trigger";
    }

    void Choose(string name)
    {
        picking = false;
        autoSelectArmed = false;
        lastChosen = name;

        string used = name;
        if (BattleManager.Instance != null) BattleManager.Instance.EnsureSessionReporter();
        if (SessionReporter.Instance != null) used = SessionReporter.Instance.SetPlayerName(name);

        if (panel != null) Destroy(panel);
        panel = null;

        if (startButton != null)
        {
            startButton.SetActive(true);
            if (startLabel != null)
                startLabel.text = $"{startLabelOriginal}\n<size=22>as {used}</size>";
        }
        if (changeButton != null) changeButton.SetActive(true);
        Debug.Log($"[PlayerNamePicker] Playing as '{used}'");
    }

    // Headset put back on (e.g. handed to the next player): give them the full countdown again.
    void OnApplicationFocus(bool focused)
    {
        if (focused && picking && autoSelectArmed) autoSelectLeft = autoSelectSeconds;
    }

    void HandleBattleStarted()
    {
        picking = false;
        if (panel != null) Destroy(panel);
        if (changeButton != null) changeButton.SetActive(false);
    }

    void OnDisable()
    {
        if (BattleManager.Instance != null)
            BattleManager.Instance.OnBattleStarted -= HandleBattleStarted;
    }

    // ─────────────────────────── UI (built from the scene's Start button) ───────────────────────────

    GameObject BuildPanel()
    {
        var canvas = startButton.transform.parent as RectTransform;
        var root = new GameObject("NamePickerPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)root.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling(); // above the HUD text, so nothing behind it catches the laser
        var bg = root.GetComponent<Image>();
        bg.color = PanelColor;
        bg.raycastTarget = false;

        MakeLabel(rt, "Title", "WHO'S PLAYING?", 46, new Vector2(0f, 175f), new Vector2(860f, 70f));

        const float colX = 175f;
        for (int i = 0; i < presetNames.Length && i < 6; i++)
        {
            string n = presetNames[i];
            float x = (i % 2 == 0) ? -colX : colX;
            float y = 70f - (i / 2) * 115f;
            MakeButton(rt, "Name_" + n, n, new Vector2(x, y), new Vector2(320f, 95f),
                n == defaultName ? DefaultNameColor : NameColor, 34, () => Choose(n));
        }

        countdownText = MakeLabel(rt, "Countdown", "", 24, new Vector2(0f, -215f), new Vector2(860f, 50f));
        return root;
    }

    void BuildChangeButton()
    {
        var canvas = startButton.transform.parent as RectTransform;
        var startRt = (RectTransform)startButton.transform;
        var pos = startRt.anchoredPosition + new Vector2(0f, -startRt.sizeDelta.y * 0.5f - 32f);
        changeButton = MakeButton(canvas, "ChangeNameButton", "Change name", pos, new Vector2(220f, 50f),
            ChangeColor, 22, () => OpenPanel(autoSelect: false));
        changeButton.SetActive(false);
    }

    GameObject MakeButton(RectTransform parent, string goName, string label, Vector2 pos, Vector2 size,
        Color color, int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var go = Instantiate(startButton, parent, false);
        go.name = goName;
        go.SetActive(true);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        if (img != null) img.color = color;

        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent(); // drop the cloned StartBattleFromInspector call
        button.onClick.AddListener(onClick);

        var text = go.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = label;
            text.fontSize = fontSize;
        }
        return go;
    }

    Text MakeLabel(RectTransform parent, string goName, string value, int fontSize, Vector2 pos, Vector2 size)
    {
        var template = startLabel != null ? startLabel.gameObject : null;
        GameObject go;
        Text text;
        if (template != null)
        {
            go = Instantiate(template, parent, false);
            text = go.GetComponent<Text>();
        }
        else
        {
            go = new GameObject(goName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            text = go.GetComponent<Text>();
        }
        go.name = goName;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }
}
