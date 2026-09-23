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

    [Header("Swing Tier Thresholds (m/s)")]
    public float weakSwingSpeed = 1.5f;
    public float mediumSwingSpeed = 3.0f;
    public float fullSwingSpeed = 5.0f;

    [Header("Runtime (read-only)")]
    public int CurrentXP { get; private set; }
    public int HitsLanded { get; private set; }
    public int BlocksSuccessful { get; private set; }
    public int FullSwings { get; private set; }

    public event Action<int> OnXPChanged;

    void Awake()
    {
        Instance = this;
        CurrentXP = 0;
        HitsLanded = 0;
        BlocksSuccessful = 0;
        FullSwings = 0;
    }

    /// <summary>
    /// Called by HitDetection when the hammer lands on a monster.
    /// swingSpeed is the controller velocity at moment of impact.
    /// </summary>
    public void AwardHitXP(float swingSpeed, float damageDealt)
    {
        HitsLanded++;

        // Base XP scaled lightly by damage so stronger hits pay more
        int xp = xpPerHit + Mathf.RoundToInt(damageDealt * 0.5f);

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
        OnXPChanged?.Invoke(CurrentXP);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateXP(CurrentXP);
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
