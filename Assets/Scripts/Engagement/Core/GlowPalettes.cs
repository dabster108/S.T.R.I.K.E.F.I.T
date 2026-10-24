// GlowPalettes.cs
// Purpose: Cosmetic weapon glow palettes, unlocked by rank. A palette changes only the weapons' RESTING look (tint and a faint glow,
//          PlayerWeaponGlow's base); the hit flash by swing tier, the clash / parry flash and the "you were hit" flash keep their colours
//          because they carry meaning. No gameplay value changes. Pure, tested in EngagementCoreTests.
// Attach to: nothing (static)

using UnityEngine;

namespace StrikeFit.Engagement
{
    public static class GlowPalettes
    {
        public struct Palette
        {
            public string key, name;
            public int minRank;          // RankLadder index needed
            public Color tint, emission; // resting look; tint white + emission black = the weapon as authored
            public Palette(string key, string name, int minRank, Color tint, Color emission) { this.key = key; this.name = name; this.minRank = minRank; this.tint = tint; this.emission = emission; }
        }

        public static readonly Palette[] All =
        {
            new Palette("steel", "Steel", 0, Color.white, Color.black),
            new Palette("ember", "Ember", 1, new Color(1f, 0.93f, 0.86f), new Color(0.34f, 0.11f, 0.02f)),
            new Palette("frost", "Frost", 2, new Color(0.88f, 0.95f, 1f), new Color(0.04f, 0.2f, 0.38f)),
            new Palette("royal", "Royal gold", 3, new Color(1f, 0.95f, 0.8f), new Color(0.38f, 0.28f, 0.04f)),
        };

        public static int IndexOf(string key)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].key == key) return i;
            return 0;
        }

        public static bool Unlocked(int paletteIndex, int rankIndex) => paletteIndex >= 0 && paletteIndex < All.Length && rankIndex >= All[paletteIndex].minRank;

        /// <summary>The palette to wear: the chosen one when its rank is reached, otherwise Steel (never a locked one).</summary>
        public static int Effective(string chosenKey, int rankIndex)
        {
            int i = IndexOf(chosenKey);
            return Unlocked(i, rankIndex) ? i : 0;
        }

        /// <summary>The next palette in <paramref name="step"/> direction (wraps), locked ones included so the player sees what is ahead.</summary>
        public static int Step(int index, int step) => ((index + step) % All.Length + All.Length) % All.Length;
    }
}
