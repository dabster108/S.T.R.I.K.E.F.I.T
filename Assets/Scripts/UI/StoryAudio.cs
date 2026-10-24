// StoryAudio.cs
// Purpose: Optional narrator lines and stingers for the Story Mode cards. Every field may stay empty (none are generated or
//          invented: assign licensed clips when they exist); an empty field simply plays nothing. Loaded once from
//          Resources/StoryAudio.asset; a missing asset means no story audio at all. Play() uses one 2D AudioSource per caller.
// Dependencies: none
// Attach to: nothing (asset: Assets/Resources/StoryAudio.asset)

using UnityEngine;

[CreateAssetMenu(menuName = "StrikeFit/Story Audio", fileName = "StoryAudio")]
public class StoryAudio : ScriptableObject
{
    [Header("Narrator (optional)")]
    public AudioClip prologueNarration;
    [Tooltip("One per chapter card (1 to 3), read over the card.")]
    public AudioClip[] chapterNarration = new AudioClip[3];

    [Header("Stingers (optional)")]
    public AudioClip cardOpen;
    public AudioClip interlude;
    public AudioClip victory;
    public AudioClip defeat;

    [Range(0f, 1f)] public float volume = 0.8f;

    static StoryAudio cached;
    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; loaded = false; }

    public static StoryAudio Get()
    {
        if (!loaded) { cached = Resources.Load<StoryAudio>("StoryAudio"); loaded = true; }
        return cached;
    }

    public static AudioClip Chapter(int level)
    {
        var a = Get();
        return a != null && a.chapterNarration != null && level >= 1 && level <= a.chapterNarration.Length ? a.chapterNarration[level - 1] : null;
    }

    /// <summary>Plays a clip (when one is assigned) on <paramref name="source"/>, created 2D on <paramref name="owner"/> if needed.</summary>
    public static void Play(AudioClip clip, GameObject owner, ref AudioSource source)
    {
        if (clip == null || owner == null) return;
        if (source == null)
        {
            source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
        }
        var a = Get();
        source.Stop();
        source.clip = clip;
        source.volume = a != null ? a.volume : 0.8f;
        source.Play();
    }

    public static void Stop(AudioSource source)
    {
        if (source != null) source.Stop();
    }
}
