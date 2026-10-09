// CombatUIController.cs
// Purpose: Health bars, stamina, score, XP, floating damage numbers, victory/defeat panels
//          with full stats (XP, hits, damage taken), the live effort readout (est. kcal + effort bar) and the result extras
//          (XP count-up, medal, est. kcal, peak swing speed, best combo, NEW PB, feedback line).
// Dependencies: UnityEngine.UI, TMPro (optional)
// Attach to: Canvas or empty UI root in Battle scene

using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || true
    using TMPro;
#endif

public class CombatUIController : MonoBehaviour
{
    public static CombatUIController Instance;

    /// <summary>Look of a floating damage number: swing tier, or what happened to the hit.</summary>
    public enum DamageStyle { Default, Tap, Light, Medium, Full, Weak, Blocked, Resisted, Break, FullPower, Opening }

    /// <summary>Swing tier -> number style, with the same thresholds as XPManager's tiers.</summary>
    public static DamageStyle StyleForSpeed(float speed)
    {
        var xp = XPManager.Instance;
        float light = xp != null ? xp.weakSwingSpeed : 1.5f;
        float medium = xp != null ? xp.mediumSwingSpeed : 3f;
        float full = xp != null ? xp.fullSwingSpeed : 5f;
        if (speed >= full) return DamageStyle.Full;
        if (speed >= medium) return DamageStyle.Medium;
        if (speed >= light) return DamageStyle.Light;
        return DamageStyle.Tap;
    }

    [Header("Bars")]
    public Slider playerHealthSlider;
    public Slider monsterHealthSlider;
    public Slider staminaSlider;

    [Header("Texts")]
    public Text playerHealthText;
    public Text monsterHealthText;
    public Text scoreText;
    public Text timerText;
    public Text damageCounterText;
    public Text xpText;
    [Tooltip("Combo counter. Blank below x2 so the HUD stays quiet during normal trading.")]
    public Text comboText;

    [Header("TMP Alternatives (optional)")]
    public TMP_Text playerHealthTMP;
    public TMP_Text monsterHealthTMP;
    public TMP_Text scoreTMP;
    public TMP_Text outcomeTMP;

    [Header("Panels")]
    public GameObject battleHudRoot;
    public GameObject victoryPanel;
    public GameObject defeatPanel;
    public GameObject pausePanel;

    [Header("Victory Panel Stats")]
    public Text victoryXPText;
    public Text victoryHitsText;
    public Text victoryDamageText;
    public Text victoryFullSwingsText;
    public Text victoryBlocksText;

    [Header("Defeat Panel Stats")]
    public Text defeatXPText;
    public Text defeatHitsText;
    public Text defeatDamageText;
    public Text defeatRetryHintText;

    [Header("Floating Damage")]
    public GameObject damageNumberPrefab; // optional world-space text prefab
    public float damageNumberLifetime = 1.0f;
    public float damageNumberRiseSpeed = 1.2f;

    [Header("Legacy fallback")]
    public Transform damageNumberParent;

    [Header("Built-in damage numbers (used when no damageNumberPrefab is set)")]
    [Tooltip("Pooled world-space numbers over the monster. Off = no floating numbers.")]
    public bool builtInDamageNumbers = true;
    [Tooltip("Number size / colour follow the swing tier (tap < light < medium < full).")]
    public bool scaleNumbersWithTier = true;
    [Range(2, 16)] public int damageNumberPoolSize = 8;
    [Tooltip("Text height of a light hit (TextMeshPro font size); tiers scale from it.")]
    public float baseNumberSize = 1.1f;
    [Tooltip("Metres the number is pulled from the hit point toward the player (keeps it in front of the body).")]
    public float numberTowardViewer = 0.3f;

    [Tooltip("Adds a small \"FULL POWER\" word above the number of a full-tier swing (uses the same pool).")]
    public bool fullPowerPopup = true;

    [Header("Live effort HUD (built on this canvas)")]
    [Tooltip("Estimated kcal (labelled est.) and an effort bar driven by PlayerExertionAudio.Exertion01, the same value the breathing uses.")]
    public bool effortHud = true;
    [Tooltip("The readout is redrawn at most this many times a second, and only when what it shows has changed.")]
    [Range(1f, 10f)] public float effortHudRefreshHz = 4f;
    [Tooltip("Canvas position of the readout (centre anchored, like the other HUD texts). Default: under the right-hand Score / Time / Dmg column.")]
    public Vector2 effortHudPosition = new Vector2(220f, -10f);
    public float effortBarWidth = 170f;

    [Header("Result extras (Victory / Defeat panels)")]
    [Tooltip("XP count-up, medal, est. kcal, peak swing speed, best combo, NEW PB and the feedback line.")]
    public bool resultsExtras = true;
    public float xpCountUpSeconds = 1.2f;
    [Tooltip("Shown at the bottom of both panels with the player's name.")]
    public string ratePrompt = "Rate this session: strikefit.vercel.app/feedback";

    [Header("Medals (Victory only): gold needs BOTH limits, silver needs both, anything else is bronze")]
    [Tooltip("Fight time limits in seconds (kill time, not counting the panel delay).")]
    public float goldMaxSeconds = 35f;
    public float silverMaxSeconds = 50f;
    [Tooltip("Damage taken as a fraction of the player's max HP.")]
    [Range(0f, 1f)] public float goldMaxDamageTakenFraction = 0.15f;
    [Range(0f, 1f)] public float silverMaxDamageTakenFraction = 0.40f;

    [Header("HUD refresh")]
    [Tooltip("Stamina bar is redrawn only when it moved by this fraction of max (every change rebuilds the whole canvas).")]
    [Range(0.005f, 0.1f)] public float staminaUiStep = 0.02f;

    [Header("Debug")]
    [Tooltip("When true and no damageNumberPrefab is assigned, every hit is written to the Console. Keep OFF for builds.")]
    public bool verboseDamageLogs = false;

    [Header("Debug readout (headset checks)")]
    [Tooltip("Small text block on this HUD: clash count, last clash hand and time, last haptic call (hand, amplitude, duration, " +
             "backend, accepted), last clash sound, exertion 0-1, breathing tier and each breathing source's volume. Tells " +
             "'the logic fired' apart from 'the hardware did nothing'. Redrawn only when a shown value changes. Off by default.")]
    public bool debugReadout = false;
    [Tooltip("Development builds only: click both thumbsticks together to switch the readout on or off on the headset.")]
    public bool debugReadoutToggleInDevBuild = true;
    public Vector2 debugReadoutPosition = new Vector2(-300f, -120f);
    [Range(1f, 10f)] public float debugReadoutHz = 4f;

    private float displayedDamageTotal;
    private float shownStamina = -1f;
    private int shownTimerSeconds = -1;
    private int shownCombo = -1;

    // Effort HUD (built lazily on the canvas; redrawn only when the shown value changes).
    private const int EffortSteps = 40;
    private GameObject effortRoot;
    private Text kcalText;
    private RectTransform effortFill;
    private Image effortFillImage;
    private float nextHudTime;
    private int shownKcalTenths = int.MinValue;
    private bool shownHasKcal;
    private int shownEffortStep = -1;
    private PlayerExertionAudio.Tier shownTier = (PlayerExertionAudio.Tier)(-1);
    /// <summary>Effort bar fill as currently drawn, 0 to 1 (verification).</summary>
    public float EffortBarShown => shownEffortStep < 0 ? 0f : shownEffortStep / (float)EffortSteps;
    public string KcalTextShown => kcalText != null ? kcalText.text : null;
    public int EffortHudRedraws { get; private set; }

    // Result extras.
    private SessionReporter.PlayerBests priorBests;
    private bool bestsReady;
    private int bestsToken;
    private Text victoryExtra, victoryFooter, defeatExtra, defeatFooter;
    private Coroutine xpCountUp;
    private readonly StringBuilder resultSb = new StringBuilder(256);
    /// <summary>Text of the last result extras block (verification).</summary>
    public string LastResultExtras { get; private set; }

    // Pooled numbers: created once, animated in Update without allocating.
    private TextMeshPro[] numPool;
    private float[] numAge;
    private float[] numLife;
    private Color[] numColor;
    private int numNext;
    private Transform viewer;

    // Debug readout (built on first use, redrawn only when a shown value changed).
    private Text debugText;
    private int shownDebugKey = int.MinValue;
    private float nextDebugTime;
    private bool debugComboHeld;
    private readonly StringBuilder debugSb = new StringBuilder(320);
    /// <summary>Text of the debug readout as last drawn (verification).</summary>
    public string DebugReadoutShown => debugText != null && debugText.gameObject.activeSelf ? debugText.text : null;
    public int DebugReadoutRedraws { get; private set; }

    // Guard meter (thin bar under the monster HP bar), built on first use.
    private RectTransform guardFill;
    private GameObject guardBar;
    private int shownGuard = -1, shownGuardMax = -1;

    void Awake()
    {
        Instance = this;
        HideOutcomePanels();
    }

    void Start()
    {
        if (BattleManager.Instance != null) BattleManager.Instance.OnBattleStarted += OnBattleStarted;
        if (effortHud) BuildEffortHud();
    }

    void OnDestroy()
    {
        if (BattleManager.Instance != null) BattleManager.Instance.OnBattleStarted -= OnBattleStarted;
        if (Instance == this) Instance = null;
    }

    void OnBattleStarted()
    {
        CombatDebugInfo.ResetCounts();
        if (effortRoot != null) effortRoot.SetActive(true);
        shownKcalTenths = int.MinValue; // redraw the effort readout on the first refresh of the fight
        shownEffortStep = -1;
        nextHudTime = 0f;
        priorBests = default;
        bestsReady = false;
        // Records from earlier sessions, fetched in the background so nothing waits on them at the end.
        var rep = SessionReporter.Instance;
        if (resultsExtras && rep != null)
        {
            int token = ++bestsToken;
            rep.FetchPlayerBests(rep.sessionPlayerName, b =>
            {
                if (this == null || token != bestsToken) return;
                priorBests = b;
                bestsReady = true;
            });
        }
    }

    public void ShowBattleHUD(bool show)
    {
        if (battleHudRoot != null)
            battleHudRoot.SetActive(show);
    }

    public void UpdatePlayerHealth(float current, float max)
    {
        if (playerHealthSlider != null)
        {
            playerHealthSlider.maxValue = max;
            playerHealthSlider.value = current;
        }
        SetText(playerHealthText, playerHealthTMP, $"HP {Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}");
    }

    public void UpdateMonsterHealth(float current, float max)
    {
        if (monsterHealthSlider != null)
        {
            monsterHealthSlider.maxValue = max;
            monsterHealthSlider.value = current;
        }
        lastMonsterHp = current; lastMonsterMax = max;
        string phase = string.IsNullOrEmpty(monsterPhaseLabel) ? "" : $"  {monsterPhaseLabel}";
        SetText(monsterHealthText, monsterHealthTMP, $"Enemy {Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}{phase}");
    }

    [Header("Boss phases (Level 3)")]
    public Color phaseMarkerColor = new Color(1f, 0.92f, 0.6f, 0.95f);
    [Tooltip("Marker width as a fraction of the bar width.")]
    public float phaseMarkerWidth = 0.012f;
    private string monsterPhaseLabel;
    private float lastMonsterHp, lastMonsterMax = 1f;
    private readonly System.Collections.Generic.List<GameObject> phaseMarkers = new System.Collections.Generic.List<GameObject>();

    /// <summary>
    /// Segmented boss bar: a thin vertical break on the monster HP slider at each HP fraction (e.g. 0.66 / 0.33),
    /// the same thresholds the boss uses for its phases.
    /// </summary>
    public void SetMonsterPhaseMarkers(float[] fractions)
    {
        foreach (var m in phaseMarkers) if (m != null) Destroy(m);
        phaseMarkers.Clear();
        if (monsterHealthSlider == null || fractions == null) return;

        var bar = monsterHealthSlider.transform as RectTransform;
        foreach (float f in fractions)
        {
            var go = new GameObject($"PhaseMarker_{Mathf.RoundToInt(f * 100)}", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(bar, false);
            rt.anchorMin = new Vector2(Mathf.Clamp01(f) - phaseMarkerWidth * 0.5f, -0.15f);
            rt.anchorMax = new Vector2(Mathf.Clamp01(f) + phaseMarkerWidth * 0.5f, 1.15f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = phaseMarkerColor;
            img.raycastTarget = false;
            phaseMarkers.Add(go);
        }
    }

    /// <summary>Phase name appended to the enemy HP text ("PHASE 2", "ENRAGED").</summary>
    public void SetMonsterPhaseLabel(string label)
    {
        monsterPhaseLabel = label;
        UpdateMonsterHealth(lastMonsterHp, lastMonsterMax);
    }

    public int PhaseMarkerCount => phaseMarkers.Count;

    public void UpdateStamina(float current, float max)
    {
        if (staminaSlider == null) return;

        // Regeneration calls this every frame. Empty / full are always shown; in between only visible steps are.
        bool atEdge = current <= 0f || current >= max;
        if (!atEdge && staminaSlider.maxValue == max && Mathf.Abs(current - shownStamina) < max * staminaUiStep)
            return;

        shownStamina = current;
        staminaSlider.maxValue = max;
        staminaSlider.value = current;
    }

    public void UpdateScore(int score)
    {
        SetText(scoreText, scoreTMP, $"Score {score}");
    }

    public void UpdateXP(int xp)
    {
        if (xpText != null)
            xpText.text = $"XP {xp}";
    }

    /// <summary>
    /// Combo counter. Rewrites the label only when the value really changes, so a long
    /// chain does not rebuild the canvas every hit (the HUD-rebuild lag found earlier).
    /// </summary>
    public void UpdateCombo(int combo)
    {
        if (comboText == null) return;
        if (combo == shownCombo) return;
        shownCombo = combo;
        comboText.text = combo >= 2 ? $"Combo x{combo}" : string.Empty;
    }

    public void UpdateTimer(float secondsRemaining)
    {
        if (timerText == null) return;

        // Called every frame by RoundManager; the label only changes once a second.
        int seconds = Mathf.CeilToInt(secondsRemaining);
        if (seconds == shownTimerSeconds) return;
        shownTimerSeconds = seconds;
        timerText.text = $"Time {seconds}s";
    }

    /// <param name="countAsDealt">False for damage the player TAKES, so the "Dmg" HUD counter only tracks damage dealt.</param>
    public void ShowDamageNumber(Vector3 worldPos, float amount, Color color, bool countAsDealt = true)
    {
        ShowDamageNumber(worldPos, amount, color, countAsDealt, DamageStyle.Default);
    }

    public void ShowDamageNumber(Vector3 worldPos, float amount, Color color, bool countAsDealt, DamageStyle style)
    {
        if (countAsDealt)
        {
            displayedDamageTotal += amount;
            if (damageCounterText != null)
                damageCounterText.text = $"Dmg {Mathf.RoundToInt(displayedDamageTotal)}";
        }

        if (damageNumberPrefab != null)
        {
            Transform parent = damageNumberParent != null ? damageNumberParent : null;
            var go = Instantiate(damageNumberPrefab, worldPos + Vector3.up * 0.3f, Quaternion.identity, parent);
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = $"-{Mathf.RoundToInt(amount)}";
                tmp.color = color;
            }
            var ui = go.GetComponentInChildren<Text>();
            if (ui != null)
            {
                ui.text = $"-{Mathf.RoundToInt(amount)}";
                ui.color = color;
            }
            StartCoroutine(FloatAndDie(go.transform));
        }
        else if (builtInDamageNumbers && countAsDealt)
        {
            // Damage the player takes is shown by the red flash, never as a number inside their own head.
            string text = style == DamageStyle.Resisted ? $"RESISTED {Mathf.RoundToInt(amount)}"
                        : style == DamageStyle.Blocked ? $"BLOCKED {Mathf.RoundToInt(amount)}"
                        : Mathf.RoundToInt(amount).ToString();
            SpawnNumber(worldPos, text, style);
            if (fullPowerPopup && style == DamageStyle.Full)
                SpawnNumber(worldPos + Vector3.up * 0.4f, "FULL POWER", DamageStyle.FullPower);
        }
        else if (verboseDamageLogs)
        {
            Debug.Log($"Damage: {amount:0.0} at {worldPos}");
        }
    }

    /// <summary>One-off word over the monster ("GUARD BREAK!") through the same pool.</summary>
    public void ShowCallout(Vector3 worldPos, string text, DamageStyle style)
    {
        if (builtInDamageNumbers && damageNumberPrefab == null) SpawnNumber(worldPos, text, style);
    }

    void EnsureNumberPool()
    {
        if (numPool != null) return;
        numPool = new TextMeshPro[damageNumberPoolSize];
        numAge = new float[damageNumberPoolSize];
        numLife = new float[damageNumberPoolSize];
        numColor = new Color[damageNumberPoolSize];
        var font = TMP_Settings.defaultFontAsset;
        for (int i = 0; i < numPool.Length; i++)
        {
            var go = new GameObject("DamageNumber_" + i);
            go.transform.SetParent(transform, false);
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontStyle = FontStyles.Bold;
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(0, 0, 0, 200);
            t.rectTransform.sizeDelta = new Vector2(3f, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            numPool[i] = t;
        }
    }

    void SpawnNumber(Vector3 worldPos, string text, DamageStyle style)
    {
        EnsureNumberPool();
        if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
        int i = numNext;
        numNext = (numNext + 1) % numPool.Length;
        var t = numPool[i];
        StyleFor(style, out float size, out Color color, out float life);
        Vector3 pos = worldPos + Vector3.up * 0.25f;
        if (viewer != null)
        {
            Vector3 toViewer = viewer.position - pos; toViewer.y = 0f;
            if (toViewer.sqrMagnitude > 1e-4f) pos += toViewer.normalized * numberTowardViewer;
        }
        // Small sideways scatter so a combo does not stack every number on one spot.
        pos += Vector3.right * UnityEngine.Random.Range(-0.12f, 0.12f);
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.transform.position = pos;
        numAge[i] = 0f; numLife[i] = life; numColor[i] = color;
        t.gameObject.SetActive(true);
    }

    void StyleFor(DamageStyle style, out float size, out Color color, out float life)
    {
        float s = baseNumberSize;
        life = damageNumberLifetime;
        if (!scaleNumbersWithTier && style <= DamageStyle.Full) { size = s; color = Color.white; return; }
        switch (style)
        {
            case DamageStyle.Tap: size = s * 0.75f; color = Color.white; break;
            case DamageStyle.Light: size = s; color = new Color(1f, 0.9f, 0.25f); break;
            case DamageStyle.Medium: size = s * 1.25f; color = new Color(1f, 0.55f, 0.12f); break;
            case DamageStyle.Full: size = s * 1.6f; color = new Color(1f, 0.16f, 0.14f); life *= 1.2f; break;
            case DamageStyle.FullPower: size = s * 0.85f; color = new Color(1f, 0.32f, 0.2f); life *= 1.3f; break;
            case DamageStyle.Weak: size = s * 1.7f; color = new Color(0.55f, 1f, 0.35f); life *= 1.2f; break;
            case DamageStyle.Blocked: size = s * 0.85f; color = new Color(0.55f, 0.75f, 1f); break;
            case DamageStyle.Resisted: size = s * 0.85f; color = new Color(0.62f, 0.62f, 0.66f); break;
            case DamageStyle.Break: size = s * 1.5f; color = new Color(0.85f, 0.95f, 1f); life *= 1.5f; break;
            case DamageStyle.Opening: size = s * 1.1f; color = new Color(1f, 0.9f, 0.35f); break;
            default: size = s; color = Color.white; break; // Light / Default
        }
    }

    void Update()
    {
        if (debugReadoutToggleInDevBuild && Debug.isDebugBuild) PollDebugToggle();
        if (debugReadout) UpdateDebugReadout();
        else if (debugText != null && debugText.gameObject.activeSelf) debugText.gameObject.SetActive(false);
        if (effortRoot != null) UpdateEffortHud();
        if (numPool == null) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < numPool.Length; i++)
        {
            var t = numPool[i];
            if (!t.gameObject.activeSelf) continue;
            numAge[i] += dt;
            float k = numAge[i] / numLife[i];
            if (k >= 1f) { t.gameObject.SetActive(false); continue; }
            var tr = t.transform;
            tr.position += Vector3.up * (damageNumberRiseSpeed * 0.5f * dt);
            if (viewer != null) tr.rotation = Quaternion.LookRotation(tr.position - viewer.position);
            if (k > 0.6f)
            {
                Color c = numColor[i];
                c.a = 1f - (k - 0.6f) / 0.4f;
                t.color = c;
            }
        }
    }

    /// <summary>
    /// Guard meter: <paramref name="filled"/> of <paramref name="max"/> absorbed hits, as a thin bar under the monster HP
    /// bar. Hidden while empty. Redrawn only when the value changes.
    /// </summary>
    public void SetGuardMeter(int filled, int max)
    {
        if (filled == shownGuard && max == shownGuardMax) return;
        shownGuard = filled; shownGuardMax = max;
        if (monsterHealthSlider == null) return;
        if (guardBar == null)
        {
            if (max <= 0 || filled <= 0) return;
            var bar = monsterHealthSlider.transform as RectTransform;
            guardBar = new GameObject("GuardMeter", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)guardBar.transform;
            rt.SetParent(bar, false);
            rt.anchorMin = new Vector2(0f, -0.32f);
            rt.anchorMax = new Vector2(1f, -0.14f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var bg = guardBar.GetComponent<Image>();
            bg.color = new Color(0.08f, 0.1f, 0.16f, 0.75f);
            bg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            guardFill = (RectTransform)fill.transform;
            guardFill.SetParent(rt, false);
            guardFill.anchorMin = Vector2.zero;
            guardFill.offsetMin = guardFill.offsetMax = Vector2.zero;
            var fi = fill.GetComponent<Image>();
            fi.color = new Color(0.62f, 0.82f, 1f, 0.95f);
            fi.raycastTarget = false;
        }
        bool show = max > 0 && filled > 0;
        if (guardBar.activeSelf != show) guardBar.SetActive(show);
        if (show) guardFill.anchorMax = new Vector2(Mathf.Clamp01((float)filled / max), 1f);
    }

    public int GuardMeterShown => shownGuard;
    public int ActiveDamageNumbers
    {
        get
        {
            int n = 0;
            if (numPool != null) foreach (var t in numPool) if (t.gameObject.activeSelf) n++;
            return n;
        }
    }

    IEnumerator FloatAndDie(Transform t)
    {
        float tLife = 0f;
        while (tLife < damageNumberLifetime && t != null)
        {
            t.position += Vector3.up * damageNumberRiseSpeed * Time.deltaTime;
            if (Camera.main != null)
                t.rotation = Quaternion.LookRotation(t.position - Camera.main.transform.position);
            tLife += Time.deltaTime;
            yield return null;
        }
        if (t != null)
            Destroy(t.gameObject);
    }

    /// <summary>
    /// Victory panel with full session stats from XPManager.
    /// </summary>
    public void ShowVictory(int score, float totalDamage)
    {
        HideOutcomePanels();
        if (victoryPanel != null) victoryPanel.SetActive(true);

        SetText(null, outcomeTMP, $"VICTORY\nScore {score}\nDamage {totalDamage:0}");

        if (XPManager.Instance != null)
        {
            if (victoryXPText != null) victoryXPText.text = $"XP Earned: {XPManager.Instance.CurrentXP}";
            if (victoryHitsText != null) victoryHitsText.text = $"Hits Landed: {XPManager.Instance.HitsLanded}";
            if (victoryFullSwingsText != null) victoryFullSwingsText.text = $"Full Swings: {XPManager.Instance.FullSwings}";
            if (victoryBlocksText != null) victoryBlocksText.text = $"Blocks: {XPManager.Instance.BlocksSuccessful}";
        }
        if (victoryDamageText != null) victoryDamageText.text = $"Damage Dealt: {Mathf.RoundToInt(totalDamage)}";
        if (scoreText != null) scoreText.text = $"Score {score}";
        FinishEffortHud();
        if (resultsExtras) ShowResultExtras(true, score);
    }

    /// <summary>
    /// Defeat panel with stats + retry hint.
    /// </summary>
    public void ShowDefeat(int score, string reason)
    {
        HideOutcomePanels();
        if (defeatPanel != null) defeatPanel.SetActive(true);

        SetText(null, outcomeTMP, $"DEFEAT\n{reason}\nScore {score}");

        if (XPManager.Instance != null)
        {
            if (defeatXPText != null) defeatXPText.text = $"XP Earned: {XPManager.Instance.CurrentXP}";
            if (defeatHitsText != null) defeatHitsText.text = $"Hits Landed: {XPManager.Instance.HitsLanded}";
        }
        if (defeatDamageText != null) defeatDamageText.text = $"Score: {score}";
        if (defeatRetryHintText != null) defeatRetryHintText.text = "Press A to Retry";
        FinishEffortHud();
        if (resultsExtras) ShowResultExtras(false, score);
    }

    // ─────────────────────────── Live effort HUD ───────────────────────────

    static Text MakeText(Transform parent, string name, Font font, int size, Color color, TextAnchor align, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false; // never in the way of the laser pointer
        return t;
    }

    Font HudFont()
    {
        if (xpText != null && xpText.font != null) return xpText.font;
        if (scoreText != null && scoreText.font != null) return scoreText.font;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    Transform HudParent()
    {
        if (xpText != null) return xpText.transform.parent;
        if (scoreText != null) return scoreText.transform.parent;
        if (playerHealthText != null) return playerHealthText.transform.parent;
        return null;
    }

    /// <summary>Builds the estimated-kcal readout and the effort bar on the existing combat canvas (once).</summary>
    void BuildEffortHud()
    {
        if (effortRoot != null) return;
        var parent = HudParent();
        if (parent == null) return;
        var font = HudFont();

        effortRoot = new GameObject("EffortHud", typeof(RectTransform));
        var rootRt = (RectTransform)effortRoot.transform;
        rootRt.SetParent(parent, false);
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = effortHudPosition;
        rootRt.sizeDelta = new Vector2(Mathf.Max(effortBarWidth, 220f), 80f);

        kcalText = MakeText(rootRt, "EstKcal", font, 24, new Color(1f, 0.82f, 0.4f), TextAnchor.MiddleCenter, new Vector2(0f, 22f), new Vector2(240f, 34f));
        kcalText.text = "est. \u2014 kcal";

        var back = new GameObject("EffortBar", typeof(RectTransform), typeof(Image));
        var backRt = (RectTransform)back.transform;
        backRt.SetParent(rootRt, false);
        backRt.anchorMin = backRt.anchorMax = new Vector2(0.5f, 0.5f);
        backRt.anchoredPosition = new Vector2(0f, -8f);
        backRt.sizeDelta = new Vector2(effortBarWidth, 14f);
        var backImg = back.GetComponent<Image>();
        backImg.color = new Color(0.08f, 0.1f, 0.16f, 0.75f);
        backImg.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        effortFill = (RectTransform)fill.transform;
        effortFill.SetParent(backRt, false);
        effortFill.anchorMin = Vector2.zero;
        effortFill.anchorMax = new Vector2(0f, 1f);
        effortFill.offsetMin = effortFill.offsetMax = Vector2.zero;
        effortFillImage = fill.GetComponent<Image>();
        effortFillImage.raycastTarget = false;
        effortFillImage.color = TierColor(PlayerExertionAudio.Tier.Calm);

        MakeText(rootRt, "EffortLabel", font, 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, new Vector2(0f, -30f), new Vector2(effortBarWidth, 20f)).text = "EFFORT";
    }

    static Color TierColor(PlayerExertionAudio.Tier tier)
    {
        switch (tier)
        {
            case PlayerExertionAudio.Tier.High: return new Color(1f, 0.35f, 0.25f);
            case PlayerExertionAudio.Tier.Medium: return new Color(1f, 0.8f, 0.3f);
            default: return new Color(0.45f, 0.75f, 1f);
        }
    }

    // At most effortHudRefreshHz per second, and each part is only written when the value it shows has changed
    // (every UI change rebuilds the canvas). Stops while no battle runs, so the last value stays on screen after the fight.
    void UpdateEffortHud(bool force = false)
    {
        var bm = BattleManager.Instance;
        if (!force && (bm == null || !bm.IsBattleActive)) return;
        float now = Time.unscaledTime;
        if (!force && now < nextHudTime) return;
        nextHudTime = now + 1f / Mathf.Max(1f, effortHudRefreshHz);

        var ex = PlayerExertionAudio.Instance;
        int step = Mathf.RoundToInt((ex != null ? ex.Exertion01 : 0f) * EffortSteps);
        if (step != shownEffortStep)
        {
            shownEffortStep = step;
            effortFill.anchorMax = new Vector2(step / (float)EffortSteps, 1f);
            EffortHudRedraws++;
        }
        var tier = ex != null ? ex.CurrentTier : PlayerExertionAudio.Tier.Calm;
        if (tier != shownTier)
        {
            shownTier = tier;
            effortFillImage.color = TierColor(tier);
            EffortHudRedraws++;
        }
        bool has = BattleStats.TryGetKcal(Time.realtimeSinceStartup, out float kcal);
        int tenths = has ? Mathf.RoundToInt(kcal * 10f) : 0;
        if (has != shownHasKcal || tenths != shownKcalTenths)
        {
            shownHasKcal = has;
            shownKcalTenths = tenths;
            kcalText.text = has ? $"est. {tenths / 10f:0.0} kcal" : "est. \u2014 kcal";
            EffortHudRedraws++;
        }
    }

    // ─────────────────────────── Debug readout ───────────────────────────

    void PollDebugToggle()
    {
        bool both = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) &&
                    OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch);
        if (both && !debugComboHeld)
        {
            debugReadout = !debugReadout;
            shownDebugKey = int.MinValue;
            Debug.Log("[StrikeFitDebug] readout " + (debugReadout ? "on" : "off"));
        }
        debugComboHeld = both;
    }

    void BuildDebugReadout()
    {
        var parent = HudParent();
        if (parent == null) return;
        debugText = MakeText(parent, "DebugReadout", HudFont(), 15, new Color(0.75f, 1f, 0.8f, 0.95f), TextAnchor.UpperLeft,
                             debugReadoutPosition, new Vector2(420f, 140f));
        debugText.horizontalOverflow = HorizontalWrapMode.Wrap;
        debugText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    // At most debugReadoutHz, and only when one of the shown (rounded) values changed.
    void UpdateDebugReadout()
    {
        float now = Time.unscaledTime;
        if (now < nextDebugTime) return;
        nextDebugTime = now + 1f / Mathf.Max(1f, debugReadoutHz);
        if (debugText == null) BuildDebugReadout();
        if (debugText == null) return;
        if (!debugText.gameObject.activeSelf) { debugText.gameObject.SetActive(true); shownDebugKey = int.MinValue; }

        var ex = PlayerExertionAudio.Instance;
        int effort = ex != null ? Mathf.RoundToInt(ex.Exertion01 * 100f) : -1;
        int heavy = ex != null ? Mathf.RoundToInt(ex.MediumVolume * 100f) : -1;
        int fast = ex != null ? Mathf.RoundToInt(ex.FastVolume * 100f) : -1;
        int tier = ex != null ? (int)ex.CurrentTier : -1;
        int key;
        unchecked { key = ((((CombatDebugInfo.Version * 397) ^ effort) * 397 ^ heavy) * 397 ^ fast) * 397 ^ tier; }
        if (key == shownDebugKey) return;
        shownDebugKey = key;

        var sb = debugSb;
        sb.Length = 0;
        sb.Append("DEBUG  clashes ").Append(CombatDebugInfo.Clashes);
        if (CombatDebugInfo.LastClashTime >= 0f)
            sb.Append("  last ").Append(CombatDebugInfo.LastClashHand == Hand.Left ? "L" : "R").Append(" @").Append(CombatDebugInfo.LastClashTime.ToString("0.0")).Append("s");
        sb.Append('\n');
        sb.Append("haptic #").Append(CombatDebugInfo.HapticCalls).Append(' ').Append(CombatDebugInfo.LastHapticKind).Append(' ')
          .Append(CombatDebugInfo.LastHapticHand == Hand.Left ? "L" : "R").Append(" amp ").Append(CombatDebugInfo.LastHapticAmplitude.ToString("0.00"))
          .Append(' ').Append(CombatDebugInfo.LastHapticSeconds.ToString("0.00")).Append("s ").Append(CombatDebugInfo.LastHapticBackend)
          .Append(CombatDebugInfo.LastHapticAccepted ? " ok" : " REFUSED").Append('\n');
        sb.Append("clash sfx ").Append(CombatDebugInfo.LastClashClip != null ? CombatDebugInfo.LastClashClip.name : "-");
        if (CombatDebugInfo.LastClashClip != null) sb.Append(" pitch ").Append(CombatDebugInfo.LastClashPitch.ToString("0.00"));
        sb.Append('\n');
        if (ex != null)
            sb.Append("effort ").Append((effort / 100f).ToString("0.00")).Append(' ').Append(ex.CurrentTier.ToString().ToUpperInvariant())
              .Append("  heavy ").Append((heavy / 100f).ToString("0.00")).Append("  fast ").Append((fast / 100f).ToString("0.00"));
        else sb.Append("effort -");
        debugText.text = sb.ToString();
        DebugReadoutRedraws++;
    }

    // ─────────────────────────── Result extras ───────────────────────────

    // Right-hand column of the panel (the existing stat rows only use the left half) and a footer line, built once per panel.
    Text EnsurePanelText(GameObject panel, ref Text cache, string name, Vector2 aMin, Vector2 aMax, int size, TextAnchor align, Color color, bool bestFit)
    {
        if (cache != null) return cache;
        if (panel == null) return null;
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(panel.transform, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = new Vector2(8f, 0f); rt.offsetMax = new Vector2(-14f, 0f);
        var t = go.GetComponent<Text>();
        t.font = HudFont();
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        if (bestFit) { t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = size; }
        cache = t;
        return t;
    }

    // The panel replaces the live readout: one last exact refresh (so its final value is the one the panel and the backend use),
    // then it is hidden so it never draws over the panel. OnBattleStarted brings it back.
    void FinishEffortHud()
    {
        if (effortRoot == null) return;
        UpdateEffortHud(true);
        effortRoot.SetActive(false);
    }

    void ShowResultExtras(bool victory, int score)
    {
        var panel = victory ? victoryPanel : defeatPanel;
        if (panel == null) return;
        var rep = SessionReporter.Instance;
        string player = rep != null && !string.IsNullOrEmpty(rep.sessionPlayerName) ? rep.sessionPlayerName : "";
        float now = Time.realtimeSinceStartup;
        var sb = resultSb;
        sb.Clear();

        // Medal (victory): fight time up to the kill and damage taken against the per-level limits on this component.
        if (victory)
        {
            float delay = BattleManager.Instance != null ? BattleManager.Instance.victoryPanelDelay : 0f;
            float seconds = Mathf.Max(0f, now - BattleStats.StartedAt - delay);
            float maxHp = PlayerHealth.Instance != null ? Mathf.Max(1f, PlayerHealth.Instance.maxHealth) : 1f;
            float dmgFrac = BattleStats.DamageTaken / maxHp;
            string medal, hex;
            if (seconds <= goldMaxSeconds && dmgFrac <= goldMaxDamageTakenFraction) { medal = "GOLD"; hex = "FFD24A"; }
            else if (seconds <= silverMaxSeconds && dmgFrac <= silverMaxDamageTakenFraction) { medal = "SILVER"; hex = "D5DAE2"; }
            else { medal = "BRONZE"; hex = "D9904F"; }
            sb.Append("<b><color=#").Append(hex).Append('>').Append(medal).Append(" MEDAL</color></b>\n");
            sb.Append("<size=18>").Append(Mathf.RoundToInt(seconds)).Append("s, ").Append(Mathf.RoundToInt(dmgFrac * 100f)).Append("% HP lost</size>\n");
        }

        bool hasKcal = BattleStats.TryGetKcal(now, out float kcal);
        sb.Append("Energy: ").Append(hasKcal ? "est. " + kcal.ToString("0.0") + " kcal" : "est. \u2014").Append('\n');
        if (BattleStats.PeakSwingSpeed > 0f) sb.Append("Peak swing: ").Append(BattleStats.PeakSwingSpeed.ToString("0.0")).Append(" m/s\n");
        int bestCombo = XPManager.Instance != null ? XPManager.Instance.BestCombo : 0;
        if (bestCombo >= 2) sb.Append("Best combo: x").Append(bestCombo).Append('\n');

        // NEW PB: only against a record that exists, and only when the background fetch answered.
        if (bestsReady && priorBests.ok)
        {
            if (priorBests.swingSpeed > 0f && BattleStats.PeakSwingSpeed > priorBests.swingSpeed) sb.Append("<color=#7CFF7C>NEW PB: fastest swing</color>\n");
            if (hasKcal && priorBests.kcal > 0f && kcal > priorBests.kcal) sb.Append("<color=#7CFF7C>NEW PB: energy</color>\n");
            if (priorBests.score > 0f && score > priorBests.score) sb.Append("<color=#7CFF7C>NEW PB: score</color>\n");
        }

        Text extra = victory ? EnsurePanelText(panel, ref victoryExtra, "ResultExtras", new Vector2(0.5f, 0.07f), new Vector2(1f, 0.68f), 26, TextAnchor.UpperLeft, Color.white, true)
                             : EnsurePanelText(panel, ref defeatExtra, "ResultExtras", new Vector2(0.5f, 0.07f), new Vector2(1f, 0.68f), 26, TextAnchor.UpperLeft, Color.white, true);
        Text footer = victory ? EnsurePanelText(panel, ref victoryFooter, "ResultFooter", new Vector2(0f, 0f), new Vector2(1f, 0.07f), 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f), true)
                              : EnsurePanelText(panel, ref defeatFooter, "ResultFooter", new Vector2(0f, 0f), new Vector2(1f, 0.07f), 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f), true);
        LastResultExtras = sb.ToString();
        if (extra != null) extra.text = LastResultExtras;
        if (footer != null) footer.text = string.IsNullOrEmpty(player) ? ratePrompt : player + "  \u00b7  " + ratePrompt;

        // XP count-up on the existing XP line.
        var xpLabel = victory ? victoryXPText : defeatXPText;
        if (xpLabel != null && XPManager.Instance != null && xpCountUpSeconds > 0.05f)
        {
            if (xpCountUp != null) StopCoroutine(xpCountUp);
            xpCountUp = StartCoroutine(CountUpXP(xpLabel, XPManager.Instance.CurrentXP));
        }
    }

    // Real time, so a slow-motion finale cannot stretch it; the text is rewritten only when the whole number changes.
    IEnumerator CountUpXP(Text label, int target)
    {
        float t = 0f;
        int shown = -1;
        while (t < xpCountUpSeconds && label != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / xpCountUpSeconds);
            int v = Mathf.RoundToInt(target * (1f - (1f - k) * (1f - k))); // ease-out
            if (v != shown) { shown = v; label.text = "XP Earned: " + v; }
            yield return null;
        }
        if (label != null) label.text = "XP Earned: " + target;
        xpCountUp = null;
    }

    public void HideOutcomePanels()
    {
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (defeatPanel != null) defeatPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    void SetText(Text legacy, TMP_Text tmp, string value)
    {
        if (legacy != null) legacy.text = value;
        if (tmp != null) tmp.text = value;
    }
}
