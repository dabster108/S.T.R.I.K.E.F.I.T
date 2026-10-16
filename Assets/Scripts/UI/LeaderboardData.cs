// LeaderboardData.cs
// Purpose: The GET /api/leaderboard answer as JsonUtility classes, plus the last answer per board kept in PlayerPrefs so the
//          menu scoreboard can show something ("Updated <time>") when the headset is offline. Rule and fields: web/backend/
//          leaderboard.js (best score per player, per level on a level board; ties: faster run, then earlier).
// Dependencies: PlayerPrefs only. Used by ScoreboardPanel (menu) and OutcomeRankLine (Victory / Defeat).
// Attach to: nothing (data + static helpers)

using System;
using System.Globalization;
using UnityEngine;

public static class LeaderboardData
{
    public const string PrefsPrefix = "StrikeFit.Leaderboard.";
    /// <summary>Board keys in tab order: all levels, then the backend's level names.</summary>
    public static readonly string[] Boards = { "all", "Easy", "Medium", "Hard" };

    [Serializable]
    public class Row
    {
        public int rank;
        public string name;
        public float score;
        public string level;
        public string outcome;
        public string date;
        public bool you;
    }

    [Serializable]
    public class You
    {
        public int rank;      // 0 = the player has no scored run on this board
        public float score;
        public string level;
        public string date;
        public bool inTop;
    }

    [Serializable]
    public class Board
    {
        public string level;
        public string updatedAt;
        public int total;
        public Row[] rows;
        public You you;
    }

    /// <summary>Parses an answer; null when it is not a board.</summary>
    public static Board Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var b = JsonUtility.FromJson<Board>(json);
            if (b == null || b.rows == null) return null;
            return b;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Remembers the raw answer and when it arrived (UTC).</summary>
    public static void Save(string board, string json)
    {
        PlayerPrefs.SetString(PrefsPrefix + board, json);
        PlayerPrefs.SetString(PrefsPrefix + board + ".at", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.Save();
    }

    /// <summary>The cached board and when it was fetched, or null.</summary>
    public static Board Load(string board, out DateTime fetchedUtc)
    {
        fetchedUtc = DateTime.MinValue;
        string json = PlayerPrefs.GetString(PrefsPrefix + board, "");
        var b = Parse(json);
        if (b != null && long.TryParse(PlayerPrefs.GetString(PrefsPrefix + board + ".at", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
            fetchedUtc = new DateTime(ticks, DateTimeKind.Utc);
        return b;
    }

    /// <summary>"12 Oct" (local) from an ISO date, "" when unreadable.</summary>
    public static string ShortDate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t)
            ? t.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture)
            : "";
    }

    /// <summary>"L1" / "L2" / "L3" for a backend level name.</summary>
    public static string LevelChip(string level)
    {
        switch (level)
        {
            case "Easy": return "L1";
            case "Medium": return "L2";
            case "Hard": return "L3";
            default: return "?";
        }
    }

    public static int LevelIndex(string level)
    {
        switch (level)
        {
            case "Easy": return 0;
            case "Medium": return 1;
            case "Hard": return 2;
            default: return -1;
        }
    }
}
