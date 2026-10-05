// CombatUIController.cs
// Purpose: Health bars, stamina, score, XP, floating damage numbers, victory/defeat panels
//          with full stats (XP, hits, damage taken).
// Dependencies: UnityEngine.UI, TMPro (optional)
// Attach to: Canvas or empty UI root in Battle scene

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || true
    using TMPro;
#endif

public class CombatUIController : MonoBehaviour
{
    public static CombatUIController Instance;

    /// <summary>Look of a floating damage number: swing tier, or what happened to the hit.</summary>
    public enum DamageStyle { Default, Tap, Light, Medium, Full, Weak, Blocked, Resisted, Break }

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

    [Header("HUD refresh")]
    [Tooltip("Stamina bar is redrawn only when it moved by this fraction of max (every change rebuilds the whole canvas).")]
    [Range(0.005f, 0.1f)] public float staminaUiStep = 0.02f;

    [Header("Debug")]
    [Tooltip("When true and no damageNumberPrefab is assigned, every hit is written to the Console. Keep OFF for builds.")]
    public bool verboseDamageLogs = false;

    private float displayedDamageTotal;
    private float shownStamina = -1f;
    private int shownTimerSeconds = -1;
    private int shownCombo = -1;

    // Pooled numbers: created once, animated in Update without allocating.
    private TextMeshPro[] numPool;
    private float[] numAge;
    private float[] numLife;
    private Color[] numColor;
    private int numNext;
    private Transform viewer;

    // Guard meter (thin bar under the monster HP bar), built on first use.
    private RectTransform guardFill;
    private GameObject guardBar;
    private int shownGuard = -1, shownGuardMax = -1;

    void Awake()
    {
        Instance = this;
        HideOutcomePanels();
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
            case DamageStyle.Tap: size = s * 0.75f; color = new Color(0.8f, 0.8f, 0.8f); break;
            case DamageStyle.Medium: size = s * 1.25f; color = new Color(1f, 0.85f, 0.3f); break;
            case DamageStyle.Full: size = s * 1.6f; color = new Color(1f, 0.5f, 0.15f); life *= 1.2f; break;
            case DamageStyle.Weak: size = s * 1.7f; color = new Color(0.55f, 1f, 0.35f); life *= 1.2f; break;
            case DamageStyle.Blocked: size = s * 0.85f; color = new Color(0.55f, 0.75f, 1f); break;
            case DamageStyle.Resisted: size = s * 0.85f; color = new Color(0.62f, 0.62f, 0.66f); break;
            case DamageStyle.Break: size = s * 1.5f; color = new Color(0.85f, 0.95f, 1f); life *= 1.5f; break;
            default: size = s; color = Color.white; break; // Light / Default
        }
    }

    void Update()
    {
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
