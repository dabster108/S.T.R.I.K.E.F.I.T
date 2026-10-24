// PlayerNameStore.cs
// Purpose: What this headset remembers about player names between app launches (PlayerPrefs): the
//          last-used name, the names used here, the last player list the backend returned, and the
//          id of the last battle (so a rename made on the web dashboard can be followed).
// Dependencies: PlayerPrefs only. Used by PlayerNamePicker and SessionReporter.
// Attach to: nothing — static helper.

using System;
using System.Collections.Generic;
using UnityEngine;

public static class PlayerNameStore
{
    public const string PrefsKey = "StrikeFit.PlayerNames";
    /// <summary>Most names kept per list and offered by the picker (it pages 6 at a time).</summary>
    public const int MaxNames = 30;

    [Serializable]
    class Data
    {
        public string last = "";
        // Last battle started on this headset and the name it was started under.
        public string lastSessionId = "";
        public string lastSessionName = "";
        // Names used on this headset, most recent first.
        public List<string> recent = new List<string>();
        // Player names in the last list the backend returned, most recently played first.
        public List<string> server = new List<string>();
    }

    // Read on every call (a few times per scene at most), so nothing stale survives in statics.
    static Data Load()
    {
        Data d = null;
        try
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json)) d = JsonUtility.FromJson<Data>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerNameStore] Saved names unreadable ({e.Message}); starting fresh.");
        }
        if (d == null) d = new Data();
        if (d.recent == null) d.recent = new List<string>();
        if (d.server == null) d.server = new List<string>();
        if (d.last == null) d.last = "";
        if (d.lastSessionId == null) d.lastSessionId = "";
        if (d.lastSessionName == null) d.lastSessionName = "";
        return d;
    }

    static void Save(Data d)
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(d));
        PlayerPrefs.Save(); // Quest can kill the app without a clean quit
    }

    /// <summary>Last name played under on this headset, or "" on a fresh install.</summary>
    public static string LastUsed => Load().last;

    /// <summary>The name was chosen for the next battle: it becomes the default and moves to the front.</summary>
    public static void MarkUsed(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var d = Load();
        d.last = name;
        d.recent.Remove(name);
        d.recent.Insert(0, name);
        Trim(d.recent);
        Save(d);
    }

    /// <summary>A battle was started under this name; its id identifies the player after a web rename.</summary>
    public static void RecordSession(string name, string sessionId)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(sessionId)) return;
        var d = Load();
        d.lastSessionId = sessionId;
        d.lastSessionName = name;
        Save(d);
    }

    /// <summary>The player was renamed (here or on the dashboard): swap the name everywhere it is remembered.</summary>
    public static void Replace(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;
        var d = Load();
        if (d.last == oldName) d.last = newName;
        if (d.lastSessionName == oldName) d.lastSessionName = newName;
        ReplaceIn(d.recent, oldName, newName);
        ReplaceIn(d.server, oldName, newName);
        Save(d);
        PlayerProgressStore.Rename(oldName, newName);   // rank, bests and goals move with the name
    }

    /// <summary>
    /// Stores a fresh backend player list (most recently played first). Names the backend listed last
    /// time but not now were renamed or deleted on the dashboard, so they are forgotten here too.
    /// Returns the session id to look up when the last-used name itself is gone from the backend and
    /// this headset has played under it (the session still carries the player's current name), else null.
    /// </summary>
    public static string ApplyServerList(List<string> namesByRecency)
    {
        var d = Load();
        var now = new HashSet<string>(namesByRecency);
        foreach (string old in d.server)
            if (!now.Contains(old) && old != d.last) d.recent.Remove(old);

        d.server = new List<string>(namesByRecency);
        Trim(d.server);
        Save(d);

        bool lastGone = d.last.Length > 0 && !now.Contains(d.last);
        return lastGone && d.lastSessionId.Length > 0 && d.lastSessionName == d.last ? d.lastSessionId : null;
    }

    /// <summary>The recorded battle is not on the backend (deleted or never delivered): stop asking for it.</summary>
    public static void ForgetSession()
    {
        var d = Load();
        d.lastSessionId = "";
        d.lastSessionName = "";
        Save(d);
    }

    /// <summary>
    /// Names to offer, most relevant first: <paramref name="first"/> (the default), names used on this
    /// headset, the backend's players by recency, then the presets. No duplicates, at most MaxNames.
    /// </summary>
    public static List<string> Ordered(string first, string[] presets)
    {
        var d = Load();
        var result = new List<string>();
        var seen = new HashSet<string>();
        Add(result, seen, first);
        foreach (string n in d.recent) Add(result, seen, n);
        foreach (string n in d.server) Add(result, seen, n);
        if (presets != null)
            foreach (string n in presets) Add(result, seen, n);
        return result;
    }

    /// <summary>Forgets everything (tests / factory reset).</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    static void Add(List<string> list, HashSet<string> seen, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || list.Count >= MaxNames) return;
        if (seen.Add(name)) list.Add(name);
    }

    static void ReplaceIn(List<string> list, string oldName, string newName)
    {
        int i = list.IndexOf(oldName);
        if (i < 0) return;
        list.Remove(newName); // never two entries for one name
        i = list.IndexOf(oldName);
        list[i] = newName;
    }

    static void Trim(List<string> list)
    {
        if (list.Count > MaxNames) list.RemoveRange(MaxNames, list.Count - MaxNames);
    }
}
