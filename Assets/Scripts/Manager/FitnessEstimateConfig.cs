// FitnessEstimateConfig.cs
// Purpose: The calorie estimate the live HUD and the session telemetry show, using the SAME constants and formula as the
//          dashboard's backend, so the number on screen at the end of a fight matches the one the dashboard stores.
// SOURCE OF TRUTH: web/backend/fitness.js (ASSUMED_BODY_MASS_KG, MET_MIN, MET_MAX, TIER_WEIGHT, estimateEffort).
//          If a constant changes there, change it here too: nothing keeps the two in sync automatically.
// HONESTY: this is an ESTIMATE (MET method, assumed 70 kg body mass, assumed MET range scaled by swing strength), not a
//          measurement; every place that shows it labels it "est.".

public static class FitnessEstimateConfig
{
    // fitness.js: ASSUMED_BODY_MASS_KG
    public const float AssumedBodyMassKg = 70f;
    // fitness.js: MET_MIN / MET_MAX. All-tap swings are priced at MET_MIN, all-full-power swings at MET_MAX, linearly between.
    public const float MetMin = 3f;
    public const float MetMax = 6f;

    // fitness.js: TIER_WEIGHT { tap: 1, light: 2, medium: 3, full: 4 }
    public const float WeightTap = 1f;
    public const float WeightLight = 2f;
    public const float WeightMedium = 3f;
    public const float WeightFull = 4f;

    /// <summary>Weight of one swing by the tier name the telemetry sends ("tap" / "light" / "medium" / "full", any case). 0 = unknown tier.</summary>
    public static float TierWeight(string tier)
    {
        if (string.IsNullOrEmpty(tier)) return 0f;
        switch (tier.ToLowerInvariant())
        {
            case "tap": return WeightTap;
            case "light": return WeightLight;
            case "medium": return WeightMedium;
            case "full": return WeightFull;
            default: return 0f;
        }
    }

    /// <summary>fitness.js estimateEffort(): kcal = MET x mass x hours, MET scaled by the average tier weight (1 to 4).</summary>
    public static float Kcal(float durationSec, float avgTierWeight)
    {
        if (!(durationSec > 0f)) return 0f;
        float met = MetMin + (MetMax - MetMin) * ((avgTierWeight - 1f) / (WeightFull - 1f));
        return met * AssumedBodyMassKg * (durationSec / 3600f);
    }
}
