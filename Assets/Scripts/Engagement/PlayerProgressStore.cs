// PlayerProgressStore.cs
// Purpose: Per-player progress on this headset (StrikeFit.Engagement.ProgressData) in PlayerPrefs, key "StrikeFit.Progress.<name>",
//          JSON. One player is cached so a scene reads PlayerPrefs once. Saved at once (PlayerPrefs.Save) after every change, so a
//          crash or a scene change never loses a best score, a rank or a goal completion.
// Attach to: nothing (static)

using System;
using StrikeFit.Engagement;
using UnityEngine;

public static class PlayerProgressStore
{
    public const string Prefix = "StrikeFit.Progress.";
    static string cachedName;
    static ProgressData cached;

    static string KeyFor(string name) => Prefix + (name ?? "").Trim();

    /// <summary>Today's day number on this headset (local date).</summary>
    public static int Today => DailyGoalRules.DayNumber(DateTime.Now);

    public static ProgressData Load(string name)
    {
        if (string.IsNullOrEmpty(name)) return new ProgressData();
        if (cached != null && cachedName == name) return cached;
        ProgressData d = null;
        string json = PlayerPrefs.GetString(KeyFor(name), "");
        if (!string.IsNullOrEmpty(json))
        {
            try { d = JsonUtility.FromJson<ProgressData>(json); }
            catch (Exception) { d = null; }   // unreadable: start again rather than fail a fight
        }
        cached = d ?? new ProgressData();
        cachedName = name;
        return cached;
    }

    public static void Save(string name, ProgressData d)
    {
        if (string.IsNullOrEmpty(name) || d == null) return;
        PlayerPrefs.SetString(KeyFor(name), JsonUtility.ToJson(d));
        PlayerPrefs.Save();
        cached = d; cachedName = name;
    }

    /// <summary>A rename (PlayerNameStore.Replace) moves the progress with the name.</summary>
    public static void Rename(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;
        string json = PlayerPrefs.GetString(KeyFor(oldName), "");
        if (string.IsNullOrEmpty(json)) return;
        PlayerPrefs.SetString(KeyFor(newName), json);
        PlayerPrefs.DeleteKey(KeyFor(oldName));
        PlayerPrefs.Save();
        cached = null; cachedName = null;
    }

    /// <summary>Tests: forget a player's progress.</summary>
    public static void Forget(string name)
    {
        PlayerPrefs.DeleteKey(KeyFor(name));
        PlayerPrefs.Save();
        if (cachedName == name) { cached = null; cachedName = null; }
    }

    /// <summary>Drops the in-memory copy (tests that edit PlayerPrefs directly).</summary>
    public static void ClearCache() { cached = null; cachedName = null; }
}
