// BestScore.cs
// Purpose: The player's best score on a level, compared the way the website picks a best run (web/backend/engage.js pickBest): only a
//          decided fight (victory or defeat, never abandoned) with a score above zero counts. A first counted fight SETS the best and
//          is never called a "NEW BEST" (there was nothing to beat). Also writes the one clear next goal for the result screen.
//          Pure, tested in EngagementCoreTests.
// Attach to: nothing (static)

namespace StrikeFit.Engagement
{
    public static class BestScore
    {
        public enum Kind
        {
            NotCounted, // abandoned, or no score: nothing recorded
            First,      // the first counted fight on this level: the best is set, no banner
            Below,
            Tied,
            NewBest,    // beat an existing best: the banner
        }

        public static bool Counts(string outcome, int score) =>
            score > 0 && (outcome == "victory" || outcome == "defeat" || outcome == "timeout");

        /// <summary><paramref name="previousBest"/> is the best before this fight, 0 or less when there is none.</summary>
        public static Kind Compare(bool counts, int previousBest, int score)
        {
            if (!counts) return Kind.NotCounted;
            if (previousBest <= 0) return Kind.First;
            if (score > previousBest) return Kind.NewBest;
            if (score == previousBest) return Kind.Tied;
            return Kind.Below;
        }

        /// <summary>The best after this fight.</summary>
        public static int After(Kind kind, int previousBest, int score) =>
            kind == Kind.First || kind == Kind.NewBest ? score : (previousBest > 0 ? previousBest : 0);

        /// <summary>One sentence for the result screen. <paramref name="pointsToNext"/> is -1 at the top rank.</summary>
        public static string NextGoal(Kind kind, string levelName, int previousBest, int score, int pointsToNext, string nextRankName)
        {
            switch (kind)
            {
                case Kind.Below:
                    return "Beat your best on " + levelName + ": score " + (previousBest - score + 1) + " more points (best " + previousBest + ")";
                case Kind.Tied:
                    return "You matched your best on " + levelName + " (" + previousBest + "). One more point beats it";
                case Kind.First:
                    return "Your first score on " + levelName + " is " + score + ". Play again and beat it";
                case Kind.NewBest:
                    return pointsToNext > 0
                        ? "New best: " + score + ". Next: " + pointsToNext + " effort points to " + nextRankName
                        : "New best: " + score + ". Next: beat it again on " + levelName;
                default:
                    return pointsToNext > 0
                        ? "Land hits to score on " + levelName + ". " + pointsToNext + " effort points to " + nextRankName
                        : "Land hits to score on " + levelName;
            }
        }
    }
}
