// XPManager.cs
// Purpose: Session-only XP tracking. Rewards good fitness form + defensive play, not spam.
// Dependencies: CombatUIController (optional). No persistence yet (future session item).
// Attach to: BattleSystems (same object as BattleManager)

using System;
using UnityEngine;

public class XPManager : MonoBehaviour
{
    public static XPManager Instance;

    [Header("XP Awards")]
    [Tooltip("Base XP per successful hit on monster.")]
    public int xpPerHit = 10;
    [Tooltip("Bonus XP for a full-power swing (speed >= fullSwingSpeedThreshold).")]
    public int xpFullSwingBonus = 25;
    [Tooltip("Bonus XP for a successful block (monster hit absorbed by shield).")]
    public int xpPerBlock = 15;
    [Tooltip("Bonus XP on monster kill.")]
    public int xpVictoryBonus = 200;

    [Header("Combo")]
    [Tooltip("Consecutive landed hits within this many seconds raise the combo counter.")]
    public float comboWindow = 2.0f;
    [Tooltip("Bonus XP per combo step above 1 (combo x3 pays 2 steps).")]
    public int xpComboStepBonus = 8;
    [Tooltip("Combo level at which the bonus stops growing, so a long chain cannot run away.")]
    public int comboBonusCap = 10;

    [Header("Swing Tier Thresholds (m/s)")]
    public float weakSwingSpeed = 1.5f;
    public float mediumSwingSpeed = 3.0f;
    public float fullSwingSpeed = 5.0f;

    [Header("Runtime (read-only)")]
    public int CurrentXP { get; private set; }
    public int HitsLanded { get; private set; }
    public int BlocksSuccessful { get; private set; }
    public int FullSwings { get; private set; }
    /// <summary>Hits landed back-to-back inside comboWindow. 0 = no live combo.</summary>
    public int CurrentCombo { get; private set; }
    public int BestCombo { get; private set; }

    public event Action<int> OnXPChanged;
    /// <summary>Raised only when the combo value actually changes.</summary>
    public event Action<int> OnComboChanged;

    private float lastComboHitTime = -999f;

    void Awake()
    {
        Instance = this;
        CurrentXP = 0;
        HitsLanded = 0;
        BlocksSuccessful = 0;
        FullSwings = 0;
        CurrentCombo = 0;
        BestCombo = 0;
    }

    /// <summary>
    /// Called by HitDetection when the hammer lands on a monster.
    /// swingSpeed is the controller velocity at moment of impact.
    /// </summary>
    public void AwardHitXP(float swingSpeed, float damageDealt)
    {
        HitsLanded++;

        // Combo: consecutive hits inside comboWindow. Chains effort, not spam — the
        // swing-speed gate in HitDetection still has to be passed for a hit to count.
        CurrentCombo = (Time.time - lastComboHitTime <= comboWindow) ? CurrentCombo + 1 : 1;
        lastComboHitTime = Time.time;
        if (CurrentCombo > BestCombo) BestCombo = CurrentCombo;
        PushCombo();

        // Base XP scaled lightly by damage so stronger hits pay more
        int xp = xpPerHit + Mathf.RoundToInt(damageDealt * 0.5f);

        // Combo bonus, capped so a long chain cannot run away with the score
        if (CurrentCombo > 1)
            xp += xpComboStepBonus * (Mathf.Min(CurrentCombo, comboBonusCap) - 1);

        // Tier bonus — reward full-arm swings over flicks
        if (swingSpeed >= fullSwingSpeed)
        {
            xp += xpFullSwingBonus;
            FullSwings++;
        }
        else if (swingSpeed >= mediumSwingSpeed)
        {
            xp += Mathf.RoundToInt(xpFullSwingBonus * 0.5f);
        }
        else if (swingSpeed < weakSwingSpeed)
        {
            // Weak tap — small penalty so spam isn't optimal
            xp = Mathf.Max(1, xp / 2);
        }

        AddXP(xp);
    }

    /// <summary>
    /// Called by PlayerHealth/PlayerCombat when a monster hit is blocked by the shield.
    /// </summary>
    public void AwardBlockXP()
    {
        BlocksSuccessful++;
        AddXP(xpPerBlock);
    }

    /// <summary>
    /// Called by BattleManager on monster death.
    /// </summary>
    public void AwardVictoryXP()
    {
        AddXP(xpVictoryBonus);
    }

    void Update()
    {
        // Cheap: returns on the first line unless a combo is actually live.
        if (CurrentCombo == 0) return;
        if (Time.time - lastComboHitTime <= comboWindow) return;
        CurrentCombo = 0;
        PushCombo();
    }

    void PushCombo()
    {
        OnComboChanged?.Invoke(CurrentCombo);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateCombo(CurrentCombo);
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;
        CurrentXP += amount;
        OnXPChanged?.Invoke(CurrentXP);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateXP(CurrentXP);
    }

    public void ResetSession()
    {
        CurrentXP = 0;
        HitsLanded = 0;
        BlocksSuccessful = 0;
        FullSwings = 0;
        CurrentCombo = 0;
        BestCombo = 0;
        lastComboHitTime = -999f;
        OnXPChanged?.Invoke(CurrentXP);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateXP(CurrentXP);
        PushCombo();
    }

    /// <summary>
    /// Returns a swing tier label for UI/debug.
    /// </summary>
    public string GetSwingTier(float speed)
    {
        if (speed >= fullSwingSpeed) return "FULL";
        if (speed >= mediumSwingSpeed) return "MEDIUM";
        if (speed >= weakSwingSpeed) return "LIGHT";
        return "TAP";
    }
}
