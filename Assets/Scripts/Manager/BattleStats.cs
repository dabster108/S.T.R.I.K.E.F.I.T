// BattleStats.cs
// Purpose: Per-battle tallies shared by the live effort HUD, the Victory/Defeat panels and the session telemetry, so each
//          of them reads ONE number instead of counting on its own: running calorie estimate, peak swing speed, damage taken.
//          Subscribes once to the same static gameplay events the telemetry uses; no component to place in a scene.
// Dependencies: HitDetection (HitLanded), PlayerHealth (DamageTaken), XPManager (tier names), BattleManager (IsBattleActive),
//               FitnessEstimateConfig (formula; mirrors web/backend/fitness.js)
// Begin() is called by SessionReporter.BeginSession with the same start time the reported duration uses.

using UnityEngine;

public static class BattleStats
{
    static int tieredSwings;
    static float tierWeightSum;
    // Distinct 60 s buckets of the fight that contain a landed swing (the daily-goal "active minutes"; same definition as the
    // backend: minute = floor(seconds since the session began / 60) of each hit). 128 minutes in two bit masks, no allocation.
    static ulong minutesLow, minutesHigh;
    static int activeMinutes;

    /// <summary>Realtime (Time.realtimeSinceStartup) the current battle began.</summary>
    public static float StartedAt { get; private set; }
    public static float PeakSwingSpeed { get; private set; }
    public static float DamageTaken { get; private set; }
    public static int TieredSwings => tieredSwings;
    public static float AverageTierWeight => tieredSwings > 0 ? tierWeightSum / tieredSwings : 0f;
    /// <summary>Minutes of this fight with at least one landed swing.</summary>
    public static int ActiveMinutes => activeMinutes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        HitDetection.HitLanded -= OnHit;
        HitDetection.HitLanded += OnHit;
        PlayerHealth.DamageTaken -= OnDamage;
        PlayerHealth.DamageTaken += OnDamage;
        tieredSwings = 0; tierWeightSum = 0f; PeakSwingSpeed = 0f; DamageTaken = 0f;
        minutesLow = minutesHigh = 0; activeMinutes = 0;
    }

    public static void Begin(float startedAtRealtime)
    {
        StartedAt = startedAtRealtime;
        tieredSwings = 0;
        tierWeightSum = 0f;
        PeakSwingSpeed = 0f;
        DamageTaken = 0f;
        minutesLow = minutesHigh = 0; activeMinutes = 0;
    }

    /// <summary>Running calorie estimate at <paramref name="nowRealtime"/>. False until a swing with a tier has landed
    /// (the backend reports no estimate for a fight without swings either).</summary>
    public static bool TryGetKcal(float nowRealtime, out float kcal)
    {
        kcal = 0f;
        if (tieredSwings == 0) return false;
        kcal = FitnessEstimateConfig.Kcal(nowRealtime - StartedAt, AverageTierWeight);
        return true;
    }

    static void OnHit(HitDetection source, float speed, bool defended)
    {
        var bm = BattleManager.Instance;
        if (bm == null || !bm.IsBattleActive) return; // the same gate BattleManager.RegisterSuccessfulHit applies to telemetry
        if (speed > PeakSwingSpeed) PeakSwingSpeed = speed;
        int minute = Mathf.Max(0, Mathf.FloorToInt((Time.realtimeSinceStartup - StartedAt) / 60f));
        if (minute < 128)
        {
            ulong bit = 1UL << (minute & 63);
            ref ulong mask = ref (minute < 64 ? ref minutesLow : ref minutesHigh);
            if ((mask & bit) == 0) { mask |= bit; activeMinutes++; }
        }
        var xp = XPManager.Instance;
        if (xp == null) return;
        float w = FitnessEstimateConfig.TierWeight(xp.GetSwingTier(speed)); // same tier name the hit event carries
        if (w <= 0f) return;
        tieredSwings++;
        tierWeightSum += w;
    }

    static void OnDamage(float amount, bool blocked, float healthAfter)
    {
        if (amount > 0f) DamageTaken += amount;
    }
}
