// PlayerExertionAudio.cs
// Purpose: Heavy-breathing loop driven by how hard the player is working. Every swing attempt counts (hit or
//          miss); enough swings inside a rolling window starts the breathing quietly, sustained effort makes it
//          heavier, a few seconds of rest fades it down, and the end of the battle cuts it.
// Dependencies: PlayerCombat (per-hand swing speed), BattleManager (only during an active battle), AudioSource
// Attach to: OVRCameraRig (next to PlayerCombat). Uses its own looping AudioSource on a child object.

using System.Collections.Generic;
using UnityEngine;

public class PlayerExertionAudio : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("Dedicated looping source (2D). Created as a child at runtime if left empty.")]
    public AudioSource breathingSource;
    public AudioClip breathingClip;
    [Tooltip("Volume when breathing first starts.")]
    [Range(0f, 1f)] public float startVolume = 0.1f;
    [Tooltip("Hard cap, reached after rampSeconds of sustained effort. Keep below the combat SFX.")]
    [Range(0f, 1f)] public float maxVolume = 0.3f;

    [Header("Swing attempts (m/s, hand speed from PlayerCombat)")]
    [Tooltip("Hand speed that counts as one swing attempt.")]
    public float swingTriggerSpeed = 2f;
    [Tooltip("Hand must slow below this before the next attempt is counted (one count per swing).")]
    public float swingRearmSpeed = 1.2f;

    [Header("Exertion")]
    [Tooltip("Rolling window (s). Swings older than this no longer count, so exertion decays when inactive.")]
    public float exertionWindow = 12f;
    [Tooltip("Swings inside the window needed to START breathing.")]
    public int swingsToStart = 8;
    [Tooltip("Breathing keeps going while at least this many swings are inside the window.")]
    public int swingsToSustain = 5;
    [Tooltip("Seconds of sustained effort to go from startVolume to maxVolume.")]
    public float rampSeconds = 25f;
    [Tooltip("No swing for this long = resting: breathing fades down.")]
    public float restSeconds = 3f;
    [Tooltip("Sustained-effort seconds lost per second of rest.")]
    public float recoveryRate = 2.5f;

    [Header("Fades (seconds for a full 0 <-> maxVolume change)")]
    public float fadeInSeconds = 2f;
    public float fadeDownSeconds = 4f;
    [Tooltip("Victory / defeat: breathing is cut this fast.")]
    public float battleEndFadeSeconds = 0.3f;

    [Header("Runtime (read-only)")]
    [SerializeField] private int swingsInWindow;
    [SerializeField] private float sustainedSeconds;
    [SerializeField] private float currentVolume;
    [SerializeField] private bool breathing;

    public int SwingsInWindow => swingsInWindow;
    public float SustainedSeconds => sustainedSeconds;
    public float CurrentVolume => currentVolume;
    public bool IsBreathing => breathing;

    private readonly Queue<float> swingTimes = new Queue<float>();
    private readonly bool[] handArmed = { true, true };
    private float lastSwingTime = -999f;

    void Awake()
    {
        if (breathingSource == null)
        {
            var go = new GameObject("PlayerBreathingAudio");
            go.transform.SetParent(transform, false);
            breathingSource = go.AddComponent<AudioSource>();
        }
        breathingSource.playOnAwake = false;
        breathingSource.loop = true;
        breathingSource.spatialBlend = 0f;
        breathingSource.volume = 0f;
        if (breathingClip != null)
            breathingSource.clip = breathingClip;
    }

    void Update()
    {
        bool battleActive = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;
        if (!battleActive)
        {
            // Idle / pre-battle / victory / defeat: never breathe, forget the effort so far.
            swingTimes.Clear();
            swingsInWindow = 0;
            sustainedSeconds = 0f;
            breathing = false;
            handArmed[0] = handArmed[1] = true;
            ApplyVolume(0f, battleEndFadeSeconds);
            return;
        }

        if (PlayerCombat.Instance != null)
        {
            DetectSwing(0, PlayerCombat.Instance.CurrentSwingSpeed);
            DetectSwing(1, PlayerCombat.Instance.CurrentOffhandSwingSpeed);
        }

        while (swingTimes.Count > 0 && Time.time - swingTimes.Peek() > exertionWindow)
            swingTimes.Dequeue();
        swingsInWindow = swingTimes.Count;

        bool resting = Time.time - lastSwingTime > restSeconds;
        if (!breathing && swingsInWindow >= swingsToStart) breathing = true;
        else if (breathing && swingsInWindow < swingsToSustain) breathing = false;

        bool exerting = breathing && !resting;
        if (exerting) sustainedSeconds += Time.deltaTime;
        else sustainedSeconds = Mathf.Max(0f, sustainedSeconds - recoveryRate * Time.deltaTime);

        float target = exerting
            ? Mathf.Lerp(startVolume, maxVolume, Mathf.Clamp01(sustainedSeconds / Mathf.Max(0.01f, rampSeconds)))
            : 0f;
        ApplyVolume(target, target > currentVolume ? fadeInSeconds : fadeDownSeconds);
    }

    void DetectSwing(int hand, float speed)
    {
        if (!handArmed[hand])
        {
            if (speed < swingRearmSpeed) handArmed[hand] = true;
            return;
        }
        if (speed >= swingTriggerSpeed)
        {
            handArmed[hand] = false;
            RegisterSwing();
        }
    }

    /// <summary>Count one swing attempt. Called from the hand speeds above; public for other effort sources.</summary>
    public void RegisterSwing()
    {
        swingTimes.Enqueue(Time.time);
        lastSwingTime = Time.time;
    }

    void ApplyVolume(float target, float fullFadeSeconds)
    {
        float cap = Mathf.Max(0f, maxVolume);
        target = Mathf.Min(target, cap);
        float rate = cap / Mathf.Max(0.01f, fullFadeSeconds);
        currentVolume = Mathf.Min(Mathf.MoveTowards(currentVolume, target, rate * Time.deltaTime), cap);

        if (breathingSource == null || breathingSource.clip == null) return;
        breathingSource.volume = currentVolume;
        if (currentVolume > 0.0001f)
        {
            if (!breathingSource.isPlaying) breathingSource.Play();
        }
        else if (breathingSource.isPlaying)
        {
            breathingSource.Stop();
        }
    }
}
