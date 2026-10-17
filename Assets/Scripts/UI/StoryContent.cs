// StoryContent.cs
// Purpose: The story text, read from Resources/story.json: a copy of content/story.json at the repo root (the single source of
//          truth, shared with the website; copy it with `node content/sync-story.js`, never edit the copy). Slides for the
//          story panel, and per level the guardian's name and one intro line (level cards, pre-battle line); guardians (lore, weapon,
//          tips: chapter cards and the Guardians screen) and interludes (the text between story-run fights). Parsed once. JsonUtility
//          ignores fields it has no class member for, so a file with newer fields still reads and an older reader skips these.
// Dependencies: Assets/Resources/story.json
// Attach to: nothing (static)

using System;
using UnityEngine;

public static class StoryContent
{
    [Serializable] public class Slide { public string id; public string text; }

    [Serializable]
    public class Level
    {
        public int level;
        public string key;       // backend level name: Easy / Medium / Hard
        public string scene;     // Level1_Easy / ...
        public string guardian;  // "The Gatekeeper"
        public string intro;     // one line
    }

    [Serializable]
    public class Guardian
    {
        public int level;        // 1 / 2 / 3
        public string id;        // gatekeeper / warden / tyrant
        public string name;      // same text as Level.guardian
        public string weapon;
        public string lore;
        public string[] tips;
    }

    [Serializable]
    public class Interludes
    {
        public string afterLevel1;
        public string afterLevel2;
        public string[] ending;  // slides after Level 3
        public string defeat;
    }

    [Serializable]
    public class Story
    {
        public int version;
        public string title;
        public float slideSeconds = 7f;
        public Slide[] slides;
        public Level[] levels;
        public Guardian[] guardians;
        public Interludes interludes;
    }

    static Story cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; }

    /// <summary>The story, or an empty one (no slides, no levels) when the file is missing or unreadable.</summary>
    public static Story Load()
    {
        if (cached != null) return cached;
        Story s = null;
        var asset = Resources.Load<TextAsset>("story");
        if (asset != null)
        {
            try { s = JsonUtility.FromJson<Story>(asset.text); }
            catch (Exception e) { Debug.LogWarning($"[StoryContent] Resources/story.json unreadable ({e.Message})."); }
        }
        else Debug.LogWarning("[StoryContent] Resources/story.json missing (run node content/sync-story.js).");
        if (s == null) s = new Story();
        if (s.slides == null) s.slides = new Slide[0];
        if (s.levels == null) s.levels = new Level[0];
        if (s.guardians == null) s.guardians = new Guardian[0];
        if (s.interludes == null) s.interludes = new Interludes();
        if (s.interludes.ending == null) s.interludes.ending = new string[0];
        if (s.slideSeconds <= 0f) s.slideSeconds = 7f;
        cached = s;
        return s;
    }

    /// <summary>The level entry for a scene name (Level1_Easy ...) or a backend key (Easy ...), else null.</summary>
    public static Level ForLevel(string sceneOrKey)
    {
        if (string.IsNullOrEmpty(sceneOrKey)) return null;
        foreach (var l in Load().levels)
            if (l != null && (l.scene == sceneOrKey || l.key == sceneOrKey)) return l;
        return null;
    }

    /// <summary>The guardian entry for a level number (1 to 3), else null.</summary>
    public static Guardian GuardianFor(int level)
    {
        foreach (var g in Load().guardians)
            if (g != null && g.level == level) return g;
        return null;
    }

    /// <summary>Level number (1 to 3) for a scene name or backend key, else 0.</summary>
    public static int LevelNumber(string sceneOrKey)
    {
        var l = ForLevel(sceneOrKey);
        return l != null ? l.level : 0;
    }
}
