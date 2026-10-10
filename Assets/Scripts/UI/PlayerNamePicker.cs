// PlayerNamePicker.cs
// Purpose: Pre-battle "Who's playing?" step. Shows the known player names on CombatCanvas (the backend's
//          players plus the names remembered on this headset, then the presets) and hides the Start
//          button until one is chosen (laser click), or auto-picks the default after autoSelectSeconds.
//          An in-panel laser keyboard adds a new player or renames the current one (the rename is sent
//          to the backend so the history moves with it). The name goes to SessionReporter for the next
//          battle's requests.
// Dependencies: SessionReporter (every backend request), PlayerNameStore (PlayerPrefs), BattleManager,
//               CombatCanvas/StartBattleUIButton (cloned as the template for every button and key, so
//               they use the exact raycast/click setup the laser already uses).
// Attach to: nothing — BattleManager adds it to BattleSystems at runtime (every level, no scene setup).
//
// The network never delays a fight: the panel opens at once with the remembered names and is refreshed
// when (if) the backend answers. Only a rename waits for its answer, up to the request timeout.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerNamePicker : MonoBehaviour
{
    public static PlayerNamePicker Instance;

    /// <summary>True while the player still has to pick a name; BattleManager refuses to start meanwhile.</summary>
    public static bool BlocksStart => Instance != null && Instance.isActiveAndEnabled && Instance.picking;

    [Tooltip("Names offered after the known players (and the only ones on a fresh, offline headset).")]
    public string[] presetNames = { "Player 1", "Player 2", "Player 3", "Guest" };
    [Tooltip("Seconds before the default name is picked automatically (first panel of each scene load only).")]
    public float autoSelectSeconds = 10f;
    [Tooltip("Scene path of the Start button to gate and clone.")]
    public string startButtonPath = "CombatCanvas/StartBattleUIButton";

    enum Mode { List, NewPlayer, Rename }
    enum Shift { Off, Once, Caps }

    const int NamesPerPage = 6;
    // A list that was just re-ordered by the backend's answer ignores name clicks for this long, so a
    // trigger pull already under way cannot land on a different name than the one aimed at.
    const float ReorderClickGuard = 0.4f;

    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.94f);
    static readonly Color NameColor = new Color(0.16f, 0.32f, 0.62f, 0.95f);
    static readonly Color DefaultNameColor = new Color(0.85f, 0.55f, 0.1f, 0.95f);
    static readonly Color ChangeColor = new Color(0.25f, 0.25f, 0.3f, 0.9f);
    static readonly Color ActionColor = new Color(0.13f, 0.45f, 0.3f, 0.95f);
    static readonly Color KeyColor = new Color(0.2f, 0.23f, 0.32f, 0.97f);
    static readonly Color FieldColor = new Color(0f, 0f, 0f, 0.6f);

    static readonly string[] KeyRows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };

    bool picking;
    bool autoSelectArmed;
    float autoSelectLeft;
    int lastShownSeconds = -1;

    Mode mode = Mode.List;
    string defaultName;
    bool defaultIsGuess;      // nothing remembered yet: the default is simply the first name on the list
    string currentName;       // name chosen in this scene, if any
    List<string> names = new List<string>();
    int page;
    string note;              // result / offline message shown on the panel
    bool serverUnreachable;
    int fetchGeneration;
    float nameClicksBlockedUntil;

    // Keyboard state.
    string typed = "";
    Shift shift = Shift.Off;
    string renameTarget;
    bool renameInFlight;
    bool renameUnanswered;    // a rename got no answer: re-read the player list when back on the list
    readonly List<Button> keyButtons = new List<Button>();
    readonly List<KeyValuePair<Text, char>> letterLabels = new List<KeyValuePair<Text, char>>();
    Text typedText;
    Text counterText;
    Text keyboardTitle;
    GameObject shiftKey;

    GameObject startButton;
    Text startLabel;
    string startLabelOriginal;
    GameObject panel;
    GameObject changeButton;
    Text statusText;

    void Awake()
    {
        Instance = this;
        picking = true; // closed from the first frame; Start builds the panel or falls back to the default
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnDestroyGoal();
    }

    void Start()
    {
        if (presetNames == null || presetNames.Length == 0) presetNames = new[] { SessionReporter.DefaultPlayerName };

        // Remembered across app restarts (PlayerPrefs), so the same person keeps their name by default.
        string remembered = PlayerNameStore.LastUsed;
        defaultIsGuess = string.IsNullOrEmpty(remembered);
        defaultName = defaultIsGuess ? presetNames[0] : remembered;

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
        // Never while typing or while a rename is waiting for the backend.
        if (!picking || !autoSelectArmed || mode != Mode.List || renameInFlight) return;

        // Per-frame clamp: time spent paused (headset off) never counts towards the auto-pick.
        autoSelectLeft -= Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        if (autoSelectLeft <= 0f)
        {
            Choose(defaultName);
            return;
        }
        int secs = Mathf.CeilToInt(autoSelectLeft);
        if (secs != lastShownSeconds)
        {
            lastShownSeconds = secs;
            ShowStatus();
        }
    }

    void OpenPanel(bool autoSelect)
    {
        picking = true;
        autoSelectArmed = autoSelect && autoSelectSeconds > 0f;
        autoSelectLeft = autoSelectSeconds;
        lastShownSeconds = -1;
        note = null;
        page = 0;
        if (!string.IsNullOrEmpty(currentName))
        {
            defaultName = currentName;
            defaultIsGuess = false;
        }

        startButton.SetActive(false);
        if (changeButton != null) changeButton.SetActive(false);
        HideGoalButton();
        goalStepOpen = false;
        ShowList();
        RefreshFromBackend();
    }

    void Choose(string name)
    {
        picking = false;
        autoSelectArmed = false;
        mode = Mode.List;

        string used = name;
        if (BattleManager.Instance != null) BattleManager.Instance.EnsureSessionReporter();
        if (SessionReporter.Instance != null) used = SessionReporter.Instance.SetPlayerName(name);
        else used = SessionReporter.CleanName(name);
        currentName = used;
        PlayerNameStore.MarkUsed(used);

        ClosePanel();
        Debug.Log($"[PlayerNamePicker] Playing as '{used}'");

        // The daily goal step (PlayerNamePicker.Goal.cs): after the name, before Start. Never blocks on the network.
        if (BeginGoalStep(used)) return;
        ShowStartButton(used);
    }

    void ShowStartButton(string used)
    {
        if (startButton != null)
        {
            startButton.SetActive(true);
            if (startLabel != null)
                startLabel.text = $"{startLabelOriginal}\n<size=22>as {Short(used, 22)}</size>";
        }
        if (changeButton != null) changeButton.SetActive(true);
        ShowGoalButton();
    }

    // Headset put back on (e.g. handed to the next player): give them the full countdown again.
    void OnApplicationFocus(bool focused)
    {
        if (focused && picking && autoSelectArmed) autoSelectLeft = autoSelectSeconds;
    }

    void HandleBattleStarted()
    {
        picking = false;
        goalStepOpen = false;
        ClosePanel();
        if (changeButton != null) changeButton.SetActive(false);
        HideGoalButton();
        DailyGoal.BattleStarted();
    }

    void OnDisable()
    {
        if (BattleManager.Instance != null)
            BattleManager.Instance.OnBattleStarted -= HandleBattleStarted;
    }

    // ─────────────────────────── Names (remembered + backend) ───────────────────────────

    List<string> CurrentNames()
    {
        var list = PlayerNameStore.Ordered(defaultIsGuess ? null : defaultName, presetNames);
        if (defaultIsGuess && list.Count > 0) defaultName = list[0];
        return list;
    }

    // Asks the backend for its players. The panel is already usable; the answer only refreshes it.
    void RefreshFromBackend()
    {
        if (BattleManager.Instance != null) BattleManager.Instance.EnsureSessionReporter();
        var reporter = SessionReporter.Instance;
        if (reporter == null) return;

        int generation = ++fetchGeneration;
        reporter.FetchPlayerNames((ok, serverNames) =>
        {
            if (this == null || generation != fetchGeneration) return;
            serverUnreachable = !ok;
            if (!ok)
            {
                if (picking && mode == Mode.List) ShowStatus();
                return;
            }

            string before = PlayerNameStore.LastUsed;
            string sessionToLookUp = PlayerNameStore.ApplyServerList(serverNames);
            if (sessionToLookUp != null) FollowDashboardRename(reporter, before, sessionToLookUp);
            RefreshListIfChanged();
        });
    }

    // The last-used name is gone from the backend but this headset played under it: the session it
    // posted still exists under the player's new name, which replaces the old one here.
    void FollowDashboardRename(SessionReporter reporter, string oldName, string sessionId)
    {
        reporter.FetchSessionPlayerName(sessionId, (answered, found, newName) =>
        {
            if (this == null || !answered) return;
            if (!found)
            {
                PlayerNameStore.ForgetSession(); // deleted on the dashboard; the old name is just a local name now
                return;
            }
            if (newName == oldName) return;

            PlayerNameStore.Replace(oldName, newName);
            Debug.Log($"[PlayerNamePicker] '{oldName}' was renamed to '{newName}' on the dashboard; using the new name.");
            if (renameTarget == oldName)
            {
                renameTarget = newName;
                if (keyboardTitle != null) keyboardTitle.text = RenameTitle();
            }
            if (picking)
            {
                if (defaultName == oldName) defaultName = newName;
                note = $"\"{oldName}\" is now \"{newName}\" (renamed on the dashboard)";
                RefreshListIfChanged();
            }
            else if (currentName == oldName && (BattleManager.Instance == null || !BattleManager.Instance.IsBattleActive))
            {
                Choose(newName); // already picked the old name: same player, new name
            }
        });
    }

    void RefreshListIfChanged()
    {
        if (!picking || mode != Mode.List) return;
        string oldDefault = defaultName;
        var fresh = CurrentNames();
        bool same = fresh.Count == names.Count && oldDefault == defaultName;
        for (int i = 0; same && i < fresh.Count; i++) same = fresh[i] == names[i];
        if (same)
        {
            ShowStatus();
            return;
        }
        ShowList();
        nameClicksBlockedUntil = Time.unscaledTime + ReorderClickGuard;
    }

    // ─────────────────────────── Panel: name list ───────────────────────────

    void ShowList()
    {
        mode = Mode.List;
        names = CurrentNames();
        int pageCount = Mathf.Max(1, (names.Count + NamesPerPage - 1) / NamesPerPage);
        page = Mathf.Clamp(page, 0, pageCount - 1);

        var rt = NewPanel();
        MakeLabel(rt, "Title", "WHO'S PLAYING?", 46, new Vector2(0f, 205f), new Vector2(860f, 60f));

        const float colX = 175f;
        for (int i = 0; i < NamesPerPage; i++)
        {
            int index = page * NamesPerPage + i;
            if (index >= names.Count) break;
            string n = names[index];
            float x = (i % 2 == 0) ? -colX : colX;
            float y = 120f - (i / 2) * 92f;
            var go = MakeButton(rt, "Name_" + n, n, new Vector2(x, y), new Vector2(320f, 84f),
                n == defaultName ? DefaultNameColor : NameColor, 34, () => PickName(n));
            FitText(go, 16, 34); // server names can be up to 40 characters
        }

        if (pageCount > 1)
        {
            if (page > 0)
                MakeButton(rt, "PagePrev", "<", new Vector2(-392f, 28f), new Vector2(84f, 84f), ChangeColor, 40, () => TurnPage(-1));
            if (page < pageCount - 1)
                MakeButton(rt, "PageNext", ">", new Vector2(392f, 28f), new Vector2(84f, 84f), ChangeColor, 40, () => TurnPage(1));
            MakeLabel(rt, "PageLabel", $"{page + 1}/{pageCount}", 22, new Vector2(392f, -40f), new Vector2(100f, 30f));
        }

        MakeButton(rt, "NewPlayerButton", "New player", new Vector2(-colX, -152f), new Vector2(320f, 62f),
            ActionColor, 28, () => OpenKeyboard(Mode.NewPlayer));
        var rename = MakeButton(rt, "RenameButton", $"Rename \"{Short(defaultName, 16)}\"", new Vector2(colX, -152f), new Vector2(320f, 62f),
            ChangeColor, 28, () => OpenKeyboard(Mode.Rename));
        FitText(rename, 18, 28);

        statusText = MakeLabel(rt, "Countdown", "", 24, new Vector2(0f, -218f), new Vector2(860f, 60f));
        ShowStatus();
    }

    void PickName(string n)
    {
        if (Time.unscaledTime < nameClicksBlockedUntil) return;
        Choose(n);
    }

    void TurnPage(int delta)
    {
        autoSelectArmed = false; // browsing: no auto-pick behind the player's back
        page += delta;
        ShowList();
    }

    // One status line under the list: the hint (with the countdown while it runs) and/or the latest note.
    void ShowStatus()
    {
        if (statusText == null || mode != Mode.List) return;
        string hint = autoSelectArmed
            ? $"Aim at your name and pull the trigger  -  {defaultName} in {Mathf.CeilToInt(autoSelectLeft)}s"
            : "Aim at your name and pull the trigger";
        string extra = note;
        if (extra == null && serverUnreachable) extra = "Server not reachable - showing the names saved on this headset";

        if (extra == null) statusText.text = hint;
        else if (autoSelectArmed) statusText.text = hint + "\n" + extra;
        else statusText.text = extra;
    }

    // ─────────────────────────── Panel: keyboard (new player / rename) ───────────────────────────

    void OpenKeyboard(Mode keyboardMode)
    {
        autoSelectArmed = false; // typing: the countdown is over for this panel
        mode = keyboardMode;
        renameTarget = keyboardMode == Mode.Rename ? defaultName : null;
        typed = "";
        shift = Shift.Once;
        note = null;
        keyButtons.Clear();
        letterLabels.Clear();

        var rt = NewPanel();
        keyboardTitle = MakeLabel(rt, "Title", keyboardMode == Mode.Rename ? RenameTitle() : "NEW PLAYER", 30,
            new Vector2(0f, 228f), new Vector2(860f, 38f));
        keyboardTitle.resizeTextForBestFit = true;
        keyboardTitle.resizeTextMinSize = 18;
        keyboardTitle.resizeTextMaxSize = 30;

        // Text field.
        var field = new GameObject("TypedField", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var frt = (RectTransform)field.transform;
        frt.SetParent(rt, false);
        frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
        frt.anchoredPosition = new Vector2(0f, 172f);
        frt.sizeDelta = new Vector2(854f, 56f);
        var fimg = field.GetComponent<Image>();
        fimg.color = FieldColor;
        fimg.raycastTarget = false;
        typedText = MakeLabel(rt, "TypedText", "", 36, new Vector2(-40f, 172f), new Vector2(760f, 56f));
        typedText.supportRichText = false;
        typedText.resizeTextForBestFit = true; // 40 characters still fit on one line
        typedText.resizeTextMinSize = 22;
        typedText.resizeTextMaxSize = 36;
        counterText = MakeLabel(rt, "TypedCount", "", 22, new Vector2(382f, 172f), new Vector2(80f, 56f));

        // Letter and digit rows: 80 x 58 keys on an 86 x 64 grid.
        const float pitchX = 86f, pitchY = 64f, keyW = 80f, keyH = 58f, top = 100f;
        for (int r = 0; r < KeyRows.Length; r++)
        {
            string row = KeyRows[r];
            float y = top - r * pitchY;
            float x0 = -(row.Length - 1) * pitchX * 0.5f;
            for (int i = 0; i < row.Length; i++)
            {
                char c = row[i];
                var key = MakeKey(rt, "Key_" + c, c.ToString(), new Vector2(x0 + i * pitchX, y), new Vector2(keyW, keyH), KeyColor, 32, () => TypeChar(c));
                if (char.IsLetter(c)) letterLabels.Add(new KeyValuePair<Text, char>(key.GetComponentInChildren<Text>(true), c));
            }
        }

        // Shift and backspace flank the bottom letter row.
        float rowY = top - 3 * pitchY;
        shiftKey = MakeKey(rt, "Key_Shift", "Shift", new Vector2(-366f, rowY), new Vector2(122f, keyH), KeyColor, 26, CycleShift);
        MakeKey(rt, "Key_Backspace", "Del", new Vector2(366f, rowY), new Vector2(122f, keyH), KeyColor, 26, Backspace);

        float bottomY = top - 4 * pitchY;
        MakeKey(rt, "Key_Cancel", "Cancel", new Vector2(-332f, bottomY), new Vector2(190f, keyH), ChangeColor, 28, CancelKeyboard);
        MakeKey(rt, "Key_Space", "Space", new Vector2(-25f, bottomY), new Vector2(400f, keyH), KeyColor, 28, () => TypeChar(' '));
        MakeKey(rt, "Key_Done", "Done", new Vector2(307f, bottomY), new Vector2(240f, keyH), ActionColor, 28, SubmitKeyboard);

        statusText = MakeLabel(rt, "KeyboardStatus", "", 22, new Vector2(0f, -217f), new Vector2(860f, 60f));
        RefreshKeyboard();
    }

    string RenameTitle() => $"RENAME \"{renameTarget}\"";

    GameObject MakeKey(RectTransform parent, string goName, string label, Vector2 pos, Vector2 size, Color color,
        int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var go = MakeButton(parent, goName, label, pos, size, color, fontSize, onClick);
        keyButtons.Add(go.GetComponent<Button>());
        return go;
    }

    void TypeChar(char c)
    {
        if (renameInFlight) return;
        note = null;
        if (typed.Length >= SessionReporter.MaxPlayerNameLength)
        {
            note = $"{SessionReporter.MaxPlayerNameLength} characters is the limit";
        }
        else if (c == ' ')
        {
            // No leading or double spaces, so what is shown is exactly the name that is used.
            if (typed.Length > 0 && typed[typed.Length - 1] != ' ')
            {
                typed += ' ';
                if (shift == Shift.Off) shift = Shift.Once; // next word starts with a capital
            }
        }
        else
        {
            typed += shift != Shift.Off ? char.ToUpperInvariant(c) : c;
            if (shift == Shift.Once) shift = Shift.Off;
        }
        RefreshKeyboard();
    }

    void Backspace()
    {
        if (renameInFlight) return;
        note = null;
        if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1);
        if (typed.Length == 0 && shift == Shift.Off) shift = Shift.Once;
        RefreshKeyboard();
    }

    // Off -> next letter only -> caps lock -> off.
    void CycleShift()
    {
        if (renameInFlight) return;
        shift = shift == Shift.Off ? Shift.Once : shift == Shift.Once ? Shift.Caps : Shift.Off;
        RefreshKeyboard();
    }

    void CancelKeyboard()
    {
        if (renameInFlight) return;
        note = null;
        ShowList();
        if (renameUnanswered)
        {
            // The backend may have applied a rename whose answer never arrived: read its list again.
            renameUnanswered = false;
            RefreshFromBackend();
        }
    }

    void SubmitKeyboard()
    {
        if (renameInFlight) return;
        string name = typed.Trim();
        if (name.Length == 0)
        {
            note = "Type a name first";
            RefreshKeyboard();
            return;
        }

        if (mode == Mode.NewPlayer)
        {
            Choose(KnownSpelling(name));
            return;
        }

        if (name == renameTarget)
        {
            note = "That is already this player's name";
            RefreshKeyboard();
            return;
        }
        SendRename(renameTarget, name);
    }

    // "dikshanta" typed while "Dikshanta" exists: play as the existing player rather than splitting the history.
    string KnownSpelling(string name)
    {
        foreach (string known in CurrentNames())
            if (string.Equals(known, name, System.StringComparison.OrdinalIgnoreCase)) return known;
        return name;
    }

    void SendRename(string oldName, string newName)
    {
        if (BattleManager.Instance != null) BattleManager.Instance.EnsureSessionReporter();
        var reporter = SessionReporter.Instance;
        if (reporter == null)
        {
            note = $"No connection to the server - still \"{oldName}\"";
            RefreshKeyboard();
            return;
        }

        renameInFlight = true;
        note = $"Renaming \"{oldName}\" to \"{newName}\" ...";
        RefreshKeyboard();

        reporter.RenamePlayer(oldName, newName, result =>
        {
            if (this == null) return;
            renameInFlight = false;
            if (!picking) return; // panel closed meanwhile (cannot happen while it blocks Start)

            switch (result.status)
            {
                case SessionReporter.RenameStatus.Renamed:
                    Debug.Log($"[PlayerNamePicker] Renamed '{oldName}' to '{newName}' on the backend.");
                    PlayerNameStore.Replace(oldName, newName);
                    Choose(newName);
                    return;
                case SessionReporter.RenameStatus.NotOnServer:
                    // No history to move: the name only existed on this headset.
                    Debug.Log($"[PlayerNamePicker] '{oldName}' has no sessions on the backend; renamed to '{newName}' locally.");
                    PlayerNameStore.Replace(oldName, newName);
                    Choose(newName);
                    return;
                case SessionReporter.RenameStatus.Conflict:
                    note = !string.IsNullOrEmpty(result.message) ? result.message : $"\"{newName}\" is already taken - pick another name";
                    break;
                case SessionReporter.RenameStatus.Unreachable:
                    renameUnanswered = true;
                    note = $"Server not reachable - still \"{oldName}\". Done = try again, Cancel = keep \"{oldName}\".";
                    break;
                case SessionReporter.RenameStatus.InBattle:
                    note = "Names can't be changed during a battle";
                    break;
                default:
                    note = $"The server refused the rename (HTTP {result.httpStatus}): {result.message} - still \"{oldName}\"";
                    break;
            }
            Debug.LogWarning($"[PlayerNamePicker] Rename '{oldName}' -> '{newName}' failed: {result.status} ({result.httpStatus}) {result.message}");
            RefreshKeyboard();
        });
    }

    void RefreshKeyboard()
    {
        if (mode == Mode.List) return;
        if (typedText != null) typedText.text = typed + "_";
        if (counterText != null) counterText.text = $"{typed.Length}/{SessionReporter.MaxPlayerNameLength}";
        if (statusText != null) statusText.text = note ?? "";

        bool upper = shift != Shift.Off;
        foreach (var pair in letterLabels)
            if (pair.Key != null) pair.Key.text = (upper ? char.ToUpperInvariant(pair.Value) : pair.Value).ToString();

        if (shiftKey != null)
        {
            var img = shiftKey.GetComponent<Image>();
            if (img != null) img.color = shift == Shift.Off ? KeyColor : DefaultNameColor;
            var label = shiftKey.GetComponentInChildren<Text>(true);
            if (label != null) label.text = shift == Shift.Caps ? "CAPS" : "Shift";
        }

        foreach (var b in keyButtons)
            if (b != null) b.interactable = !renameInFlight;
    }

    // ─────────────────────────── UI (built from the scene's Start button) ───────────────────────────

    RectTransform NewPanel()
    {
        ClosePanel();
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
        panel = root;
        return rt;
    }

    void ClosePanel()
    {
        if (panel != null)
        {
            panel.SetActive(false); // Destroy is deferred to the end of the frame; stop raycasts now
            Destroy(panel);
        }
        panel = null;
        statusText = null;
        typedText = null;
        counterText = null;
        keyboardTitle = null;
        shiftKey = null;
        keyButtons.Clear();
        letterLabels.Clear();
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
            text.supportRichText = false; // names are shown as typed, never parsed as markup
            text.text = label;
            text.fontSize = fontSize;
        }
        return go;
    }

    // Names can be 40 characters; small labels show the start of a long one.
    static string Short(string name, int max)
    {
        return name != null && name.Length > max ? name.Substring(0, max - 3) + "..." : name;
    }

    // Long labels shrink instead of being cut off.
    static void FitText(GameObject button, int minSize, int maxSize)
    {
        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) return;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = minSize;
        text.resizeTextMaxSize = maxSize;
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
        text.supportRichText = false;
        text.text = value;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.verticalOverflow = VerticalWrapMode.Overflow; // a long note may take a third line
        text.raycastTarget = false;
        return text;
    }
}
