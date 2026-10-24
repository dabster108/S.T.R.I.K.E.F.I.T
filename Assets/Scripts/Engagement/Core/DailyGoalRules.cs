// DailyGoalRules.cs
// Purpose: Today's three goals. When the headset can reach the website, the goals are the website's daily challenges
//          (GET /api/players/:name/challenges, one easy, one medium, one stretch) and their progress so far; when it cannot, three
//          local goals are picked for the day from a fixed seed (the date), one per tier, from LocalGoals below. Either way the
//          same rules turn "progress before this fight" + "this fight" into live progress. Pure, tested in EngagementCoreTests.
//          Local goals use the headset's own date (the website's use Asia/Kathmandu).
// Attach to: nothing (static)

using System;

namespace StrikeFit.Engagement
{
    public enum GoalMetric { FullSwings, BestCombo, Wins, Fights, ClearLevel, ClearDamage, ActiveMinutes, Kcal }

    [Serializable]
    public struct GoalDef
    {
        public string key;      // stable id; the website's card key ("d-full-power:2026-10-24") or "local:<id>:<day>"
        public string title;
        public string description;
        public GoalMetric metric;
        public float target;
        public string level;     // ClearLevel / ClearDamage: "Easy" | "Medium" | "Hard" (ClearDamage: null = any level)
        public float maxDamagePercent;
        public int bonusXp;
        public string tier;      // "easy" | "medium" | "stretch"
        public bool fromServer;  // the website awards the bonus XP itself (the headset must not add it again)
    }

    /// <summary>This fight's contribution. <c>ended</c> is false while it is still being fought.</summary>
    public struct FightTally
    {
        public int fullSwings, bestCombo;
        public bool ended, won;
        public string level;
        public float damagePercent;   // of the player's max health
        public int activeMinutes;
        public float kcal;
    }

    public static class DailyGoalRules
    {
        static GoalDef Def(string id, string title, string desc, GoalMetric m, float target, int xp, string tier, string level = null, float maxDmg = 100f) =>
            new GoalDef { key = id, title = title, description = desc, metric = m, target = target, bonusXp = xp, tier = tier, level = level, maxDamagePercent = maxDmg };

        // The local pool (used offline). One of each tier is picked per day.
        public static readonly GoalDef[] Easy =
        {
            Def("full5", "Full power x5", "Land 5 full-power swings", GoalMetric.FullSwings, 5, 20, "easy"),
            Def("win1", "Win a fight", "Win one fight on any level", GoalMetric.Wins, 1, 25, "easy"),
            Def("fights2", "Two fights", "Play two fights today", GoalMetric.Fights, 2, 20, "easy"),
        };
        public static readonly GoalDef[] Medium =
        {
            Def("combo5", "5-hit combo", "Reach a 5-hit combo", GoalMetric.BestCombo, 5, 30, "medium"),
            Def("full15", "Full power x15", "Land 15 full-power swings today", GoalMetric.FullSwings, 15, 35, "medium"),
            Def("clear2", "Clear Level 2", "Win a fight on Level 2", GoalMetric.ClearLevel, 1, 40, "medium", "Medium"),
        };
        public static readonly GoalDef[] Stretch =
        {
            Def("combo10", "10-hit combo", "Reach a 10-hit combo", GoalMetric.BestCombo, 10, 50, "stretch"),
            Def("full30", "Full power x30", "Land 30 full-power swings today", GoalMetric.FullSwings, 30, 50, "stretch"),
            Def("clear3", "Clear Level 3", "Win a fight on Level 3", GoalMetric.ClearLevel, 1, 60, "stretch", "Hard"),
            Def("guarded", "Guarded win", "Win losing less than half your health", GoalMetric.ClearDamage, 1, 50, "stretch", null, 50f),
        };

        /// <summary>Days since 2000-01-01 for a local calendar date.</summary>
        public static int DayNumber(DateTime localDate) => (int)(localDate.Date - new DateTime(2000, 1, 1)).TotalDays;

        // A small integer hash (deterministic on every platform) so each tier rotates on its own.
        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return x;
        }

        static GoalDef Pick(GoalDef[] pool, int day, uint salt)
        {
            var d = pool[(int)(Hash((uint)day * 2654435761U ^ salt) % (uint)pool.Length)];
            d.key = "local:" + d.key + ":" + day;
            return d;
        }

        /// <summary>The day's three local goals (easy, medium, stretch): the same three all day, a new set tomorrow.</summary>
        public static GoalDef[] ForDay(int day) => new[] { Pick(Easy, day, 0x9e37u), Pick(Medium, day, 0x85ebu), Pick(Stretch, day, 0xc2b2u) };

        /// <summary>The website's challenge metric names (web/backend/challenges.js METRICS).</summary>
        public static bool TryMetric(string name, out GoalMetric m)
        {
            switch (name)
            {
                case "fullSwings": m = GoalMetric.FullSwings; return true;
                case "bestCombo": m = GoalMetric.BestCombo; return true;
                case "sessions": m = GoalMetric.Fights; return true;
                case "clearLevel": m = GoalMetric.ClearLevel; return true;
                case "clearDamage": m = GoalMetric.ClearDamage; return true;
                case "activeMinutes": m = GoalMetric.ActiveMinutes; return true;
                case "kcal": m = GoalMetric.Kcal; return true;
                default: m = GoalMetric.Fights; return false;
            }
        }

        /// <summary>Live progress: <paramref name="before"/> is the value before this fight (the website's value, or the local one),
        /// plus what this fight adds. Counts and sums add, a combo is the best of the two, a "clear" is 0 or 1.</summary>
        public static float Combine(in GoalDef g, float before, in FightTally f)
        {
            switch (g.metric)
            {
                case GoalMetric.FullSwings: return before + f.fullSwings;
                case GoalMetric.BestCombo: return before > f.bestCombo ? before : f.bestCombo;
                case GoalMetric.Wins: return before + (f.ended && f.won ? 1 : 0);
                case GoalMetric.Fights: return before + (f.ended ? 1 : 0);
                case GoalMetric.ClearLevel:
                    return before >= 1f || (f.ended && f.won && f.level == g.level) ? 1f : 0f;
                case GoalMetric.ClearDamage:
                    return before >= 1f || (f.ended && f.won && (string.IsNullOrEmpty(g.level) || f.level == g.level) && f.damagePercent <= g.maxDamagePercent) ? 1f : 0f;
                case GoalMetric.ActiveMinutes: return before + f.activeMinutes;
                case GoalMetric.Kcal: return before + f.kcal;
                default: return before;
            }
        }

        public static bool Met(in GoalDef g, float value) => g.target > 0f && value >= g.target;

        /// <summary>0..1 for a progress bar.</summary>
        public static float Fraction(in GoalDef g, float value) => g.target > 0f ? (value >= g.target ? 1f : value <= 0f ? 0f : value / g.target) : 0f;
    }
}
