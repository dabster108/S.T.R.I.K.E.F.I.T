// StoryProgress.cs
// Purpose: Story Mode progress, per player name, in PlayerPrefs: chapters cleared (0 to 3) and the story-run flag. The run flag
//          says "a campaign is being played right now"; it lives in PlayerPrefs too, so it survives the scene loads between
//          chapters (and is cleared by the main menu, so an app restart never drops the player into a chapter by surprise).
//          One [StrikeFitStory] log line per change.
// Dependencies: PlayerNameStore (the remembered player), SessionReporter (default name)
// Attach to: nothing (static)

using UnityEngine;

public static class StoryProgress
{
    public const string Tag = "[StrikeFitStory]";
    public const int Chapters = 3;
    const string ClearedPrefix = "StrikeFit.Story.Cleared.";
    const string RunKey = "StrikeFit.Story.Run";          // 1 while a campaign is being played
    const string RunPlayerKey = "StrikeFit.Story.RunPlayer";

    /// <summary>The name progress is kept under: the remembered player, else the default name.</summary>
    public static string CurrentPlayer
    {
        get
        {
            string n = PlayerNameStore.LastUsed;
            return string.IsNullOrEmpty(n) ? SessionReporter.DefaultPlayerName : n;
        }
    }

    static string Key(string player) => ClearedPrefix + (string.IsNullOrEmpty(player) ? SessionReporter.DefaultPlayerName : player.Trim());

    /// <summary>Chapters cleared by this player, 0 to 3.</summary>
    public static int Cleared(string player) => Mathf.Clamp(PlayerPrefs.GetInt(Key(player), 0), 0, Chapters);

    /// <summary>The chapter a Continue starts (1 to 3), or 0 when all three are cleared.</summary>
    public static int NextChapter(string player)
    {
        int c = Cleared(player);
        return c >= Chapters ? 0 : c + 1;
    }

    /// <summary>A Victory in chapter <paramref name="level"/>: cleared becomes at least that level. Never goes down.</summary>
    public static void SaveChapter(string player, int level)
    {
        level = Mathf.Clamp(level, 0, Chapters);
        int before = Cleared(player);
        if (level <= before) { Debug.Log($"{Tag} chapter {level} won again by '{player}' (cleared stays {before}/3)"); return; }
        PlayerPrefs.SetInt(Key(player), level);
        PlayerPrefs.Save();
        Debug.Log($"{Tag} chapter {level} saved for '{player}': cleared {level}/3");
    }

    /// <summary>New Story: back to 0 chapters for this player.</summary>
    public static void Reset(string player)
    {
        PlayerPrefs.SetInt(Key(player), 0);
        PlayerPrefs.Save();
        Debug.Log($"{Tag} progress reset for '{player}'");
    }

    public static bool RunActive => PlayerPrefs.GetInt(RunKey, 0) == 1;
    public static string RunPlayer => PlayerPrefs.GetString(RunPlayerKey, CurrentPlayer);

    public static void BeginRun(string player)
    {
        PlayerPrefs.SetInt(RunKey, 1);
        PlayerPrefs.SetString(RunPlayerKey, string.IsNullOrEmpty(player) ? CurrentPlayer : player);
        PlayerPrefs.Save();
        Debug.Log($"{Tag} run started for '{RunPlayer}' at chapter {Mathf.Max(1, NextChapter(RunPlayer))} (cleared {Cleared(RunPlayer)}/3)");
    }

    public static void EndRun(string why)
    {
        if (!RunActive) return;
        PlayerPrefs.SetInt(RunKey, 0);
        PlayerPrefs.Save();
        Debug.Log($"{Tag} run ended ({why})");
    }
}
