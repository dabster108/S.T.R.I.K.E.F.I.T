// RankLadder.cs
// Purpose: The rank ladder, the same rules as the website (web/backend/ranks.js + config/ranks.js), so the headset and the
//          dashboard always agree: Rookie 0, Brawler 300, Warrior 1200, Guardian 3500 effort points, where one fight is worth
//          counted swings x 1 + full-power swings x 2 + 40 for a victory + 60 more for a Hard (Level 3) victory. "Counted swings"
//          is the session's hitCount (what the dashboard shows as Counted); a full-power swing is also a counted swing. Abandoned
//          and lost fights earn their swings, never the victory bonus. Pure (no Unity calls), tested in EngagementCoreTests.
// Attach to: nothing (static)

namespace StrikeFit.Engagement
{
    public static class RankLadder
    {
        public struct Rung
        {
            public string key, name;
            public int from;
            public Rung(string key, string name, int from) { this.key = key; this.name = name; this.from = from; }
        }

        public static readonly Rung[] Rungs =
        {
            new Rung("rookie", "Rookie", 0),
            new Rung("brawler", "Brawler", 300),
            new Rung("warrior", "Warrior", 1200),
            new Rung("guardian", "Guardian", 3500),
        };

        public const int PerCountedSwing = 1, PerFullPowerSwing = 2, PerVictory = 40, PerHardVictoryExtra = 60;

        /// <summary>Effort points one fight is worth.</summary>
        public static int SessionPoints(int countedSwings, int fullSwings, bool victory, bool hard)
        {
            int p = (countedSwings > 0 ? countedSwings : 0) * PerCountedSwing + (fullSwings > 0 ? fullSwings : 0) * PerFullPowerSwing;
            if (victory) p += PerVictory + (hard ? PerHardVictoryExtra : 0);
            return p;
        }

        /// <summary>Index of the highest rung the points reach (0 = Rookie).</summary>
        public static int IndexFor(int points)
        {
            int found = 0;
            for (int i = 0; i < Rungs.Length; i++) if (points >= Rungs[i].from) found = i;
            return found;
        }

        public static Rung RankFor(int points) => Rungs[IndexFor(points)];

        public static bool IsTop(int points) => IndexFor(points) == Rungs.Length - 1;

        /// <summary>0..1 within the current rung (exactly on a threshold = 0 on the new rung); 1 at the top rank.</summary>
        public static float Progress(int points)
        {
            int i = IndexFor(points);
            if (i == Rungs.Length - 1) return 1f;
            int from = Rungs[i].from, to = Rungs[i + 1].from;
            float p = (points - from) / (float)(to - from);
            return p < 0f ? 0f : p > 1f ? 1f : p;
        }

        /// <summary>Points still needed for the next rung, or -1 at the top.</summary>
        public static int PointsToNext(int points)
        {
            int i = IndexFor(points);
            return i == Rungs.Length - 1 ? -1 : Rungs[i + 1].from - (points > 0 ? points : 0);
        }

        /// <summary>The rung after the current one, or the current one at the top.</summary>
        public static Rung NextRung(int points)
        {
            int i = IndexFor(points);
            return Rungs[i == Rungs.Length - 1 ? i : i + 1];
        }

        /// <summary>A RANK UP is shown when this fight moved the player to a rung above both the one they had before it and the
        /// highest one already celebrated on this headset (so it fires once, and never for a rank reached elsewhere earlier).</summary>
        public static bool IsRankUp(int pointsBefore, int pointsAfter, int celebratedIndex)
        {
            int after = IndexFor(pointsAfter);
            return after > IndexFor(pointsBefore) && after > celebratedIndex;
        }
    }
}
