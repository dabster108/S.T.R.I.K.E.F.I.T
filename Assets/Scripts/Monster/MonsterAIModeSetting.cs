// MonsterAIModeSetting.cs
// Purpose: The player's Monster AI choice (Normal / Expert), saved in PlayerPrefs ("StrikeFit.MonsterAIMode", default Normal). Expert
//          only ever applies to Level 3 (MonsterAIModeRules). The mode is read once when a battle starts and frozen for that fight
//          (FightMode), so it cannot change during a fight; Story Mode's Level 3 follows the saved setting the same way.
// Dependencies: StrikeFit.ExpertAI (MonsterAIMode, MonsterAIModeRules), BattleManager (OnBattleStarted), LevelManager
// Attach to: nothing (static)

using StrikeFit.ExpertAI;
using UnityEngine;

public static class MonsterAIModeSetting
{
    public const string PrefKey = "StrikeFit.MonsterAIMode";

    /// <summary>The saved choice.</summary>
    public static MonsterAIMode Saved
    {
        get { return PlayerPrefs.GetInt(PrefKey, 0) == 1 ? MonsterAIMode.Expert : MonsterAIMode.Normal; }
    }

    /// <summary>True while a battle runs: the toggles refuse changes.</summary>
    public static bool Locked => BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;

    /// <summary>Saves a new choice. Refused during a fight.</summary>
    public static bool TrySet(MonsterAIMode mode)
    {
        if (Locked) return false;
        PlayerPrefs.SetInt(PrefKey, mode == MonsterAIMode.Expert ? 1 : 0);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>The mode frozen at the start of the current (or last) battle.</summary>
    public static MonsterAIMode FightMode { get; private set; } = MonsterAIMode.Normal;

    /// <summary>The scene's level, 1-3 (0 when unknown).</summary>
    public static int CurrentLevel
    {
        get
        {
            if (LevelManager.Instance == null) return 0;
            switch (LevelManager.Instance.sceneDifficulty)
            {
                case DifficultyLevel.Easy: return 1;
                case DifficultyLevel.Medium: return 2;
                case DifficultyLevel.Hard: return 3;
                default: return 0;
            }
        }
    }

    /// <summary>Does this fight use the model? Level 3 and Expert only.</summary>
    public static bool ExpertThisFight => MonsterAIModeRules.UseModel(CurrentLevel, FightMode);

    /// <summary>"normal" or "expert" for telemetry: the mode this fight actually runs (Levels 1-2 are always normal).</summary>
    public static string TelemetryName => ExpertThisFight ? "expert" : "normal";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { FightMode = MonsterAIMode.Normal; }

    /// <summary>Freezes the saved choice for the fight that is starting (BattleManager calls this before it spawns the monster).</summary>
    public static void FreezeForFight() => FightMode = Saved;
}

/// <summary>A layer that may adjust MonsterAI's attack choice and timing (Level 3 Expert). MonsterAI works the same without one.
/// Every method must keep the attack's full telegraph, damage, projectile speed and recovery.</summary>
public interface IMonsterAttackAdvisor
{
    /// <summary>The row to use instead of <paramref name="picked"/> (same eligibility), or <paramref name="picked"/> itself.</summary>
    MonsterAI.AttackDefinition Advise(MonsterAI.AttackDefinition picked);
    /// <summary>True to hold back an attack that is due this think (a short, capped delay).</summary>
    bool HoldAttack();
    /// <summary>Called as a table attack's wind-up starts (before the aim locks): may turn the monster a capped amount.</summary>
    void OnWindUpStart(MonsterAI.AttackDefinition a);
    /// <summary>Where a Rush runs to / a thrown axe flies to, or false for the player's current position.</summary>
    bool LeadTarget(MonsterAI.AttackDefinition a, out Vector3 target);
    /// <summary>May a chained follow-up come after <paramref name="a"/>? False keeps its recovery open (punishable).</summary>
    bool AllowChainAfter(MonsterAI.AttackDefinition a);
}
