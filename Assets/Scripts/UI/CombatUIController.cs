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

    private float displayedDamageTotal;

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
        SetText(monsterHealthText, monsterHealthTMP, $"Enemy {Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}");
    }

    public void UpdateStamina(float current, float max)
    {
        if (staminaSlider != null)
        {
            staminaSlider.maxValue = max;
            staminaSlider.value = current;
        }
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

    public void UpdateTimer(float secondsRemaining)
    {
        if (timerText != null)
            timerText.text = $"Time {Mathf.CeilToInt(secondsRemaining)}s";
    }

    public void ShowDamageNumber(Vector3 worldPos, float amount, Color color)
    {
        displayedDamageTotal += amount;
        if (damageCounterText != null)
            damageCounterText.text = $"Dmg {Mathf.RoundToInt(displayedDamageTotal)}";

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
        else
        {
            Debug.Log($"Damage: {amount:0.0} at {worldPos}");
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
        if (defeatRetryHintText != null) defeatRetryHintText.text = "Press R to Retry  |  ESC for Menu";
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
