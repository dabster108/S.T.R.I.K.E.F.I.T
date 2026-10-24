// GlowCosmetics.cs
// Purpose: The weapon glow palette in use (cosmetic only, unlocked by rank: StrikeFit.Engagement.GlowPalettes). PlayerWeaponGlow reads
//          it for its resting look and re-evaluates when Version changes. Steel (index 0) = the weapons as authored (Active false).
// Attach to: nothing (static)

using StrikeFit.Engagement;
using UnityEngine;

public static class GlowCosmetics
{
    public static bool Active { get; private set; }
    public static Color Tint { get; private set; } = Color.white;
    public static Color Emission { get; private set; } = Color.black;
    public static int Index { get; private set; }
    public static int Version { get; private set; }

    public static void Apply(int paletteIndex)
    {
        if (paletteIndex < 0 || paletteIndex >= GlowPalettes.All.Length) paletteIndex = 0;
        if (paletteIndex == Index && Version > 0) return;
        var p = GlowPalettes.All[paletteIndex];
        Index = paletteIndex;
        Active = paletteIndex != 0;
        Tint = p.tint;
        Emission = p.emission;
        Version++;
    }

    /// <summary>The palette a player wears: their choice if their rank has unlocked it, else Steel.</summary>
    public static void ApplyFor(ProgressData data) =>
        Apply(data != null ? GlowPalettes.Effective(data.glowPalette, RankLadder.IndexFor(data.effortPoints)) : 0);
}
