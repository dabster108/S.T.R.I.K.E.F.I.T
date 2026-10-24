// ProgressData.cs
// Purpose: What the headset remembers per player (PlayerPrefs "StrikeFit.Progress.<name>", JSON): effort points, the highest rank a
//          RANK UP was shown for, the best score per level, fights played today, today's goal progress and the chosen weapon glow.
//          Serializable with JsonUtility. The update rules here are pure (tested); PlayerProgressStore does the PlayerPrefs I/O.
// Attach to: nothing (data)

using System;
using System.Collections.Generic;

namespace StrikeFit.Engagement
{
    [Serializable]
    public class LevelBest
    {
        public string level;
        public int score;
    }

    [Serializable]
    public class GoalState
    {
        public string key;
        public float value;   // local goals: progress so far today
        public bool done;     // the completion (popup + bonus) has been given
    }

    [Serializable]
    public class ProgressData
    {
        public int version = 1;
        public int effortPoints;
        public int celebratedRank;        // index into RankLadder.Rungs
        public List<LevelBest> bests = new List<LevelBest>();
        public int day = -1;              // DailyGoalRules.DayNumber of the counters below
        public int fightsToday;
        public List<GoalState> goals = new List<GoalState>();
        public string glowPalette = "";

        public int BestOn(string level)
        {
            for (int i = 0; i < bests.Count; i++) if (bests[i].level == level) return bests[i].score;
            return 0;
        }

        public void SetBest(string level, int score)
        {
            for (int i = 0; i < bests.Count; i++) if (bests[i].level == level) { bests[i].score = score; return; }
            bests.Add(new LevelBest { level = level, score = score });
        }

        /// <summary>A new day clears the day's counters and goal progress (the best scores, points and palette stay).</summary>
        public void RollDay(int today)
        {
            if (day == today) return;
            day = today;
            fightsToday = 0;
            goals.Clear();
        }

        public GoalState Goal(string key, bool create)
        {
            for (int i = 0; i < goals.Count; i++) if (goals[i].key == key) return goals[i];
            if (!create) return null;
            var g = new GoalState { key = key };
            goals.Add(g);
            return g;
        }
    }
}
