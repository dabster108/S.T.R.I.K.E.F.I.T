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
    [Tooltip("Hand slowing below this re-arms the next count (chops that stop between swings).")]
    public float swingRearmSpeed = 1.2f;
    [Tooltip("Hand turning this far (deg) from its direction at the last count also re-arms, so fluid back-and-forth " +
             "or circular slashing that never slows below swingRearmSpeed still counts every swing.")]
    public float swingReversalAngle = 150f;
    [Tooltip("Shortest time between two counts on the same hand (filters tracking jitter).")]
    public float minSwingInterval = 0.35f;

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
    // Per hand: weapon motion direction (for reversal re-arm), its direction at the last count, last count time.
    private readonly Vector3[] lastHandPos = new Vector3[2];
    private readonly bool[] hasHandPos = new bool[2];
    private readonly Vector3[] handVelocity = new Vector3[2];
    private readonly Vector3[] countedDir = new Vector3[2];
    private readonly float[] handCountTime = { -999f, -999f };

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
            hasHandPos[0] = hasHandPos[1] = false;
            ApplyVolume(0f, battleEndFadeSeconds);
            return;
        }

        var pc = PlayerCombat.Instance;
        if (pc != null)
        {
            TrackHand(0, pc.weaponTransform);
            TrackHand(1, pc.offhandWeaponTransform);
            DetectSwing(0, pc.CurrentSwingSpeed);
            DetectSwing(1, pc.CurrentOffhandSwingSpeed);
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

    void TrackHand(int hand, Transform weapon)
    {
        if (weapon == null) { hasHandPos[hand] = false; handVelocity[hand] = Vector3.zero; return; }
        Vector3 p = weapon.position;
        if (hasHandPos[hand])
        {
            Vector3 raw = (p - lastHandPos[hand]) / Mathf.Max(Time.deltaTime, 0.0001f);
            handVelocity[hand] = Vector3.Lerp(handVelocity[hand], raw, 1f - Mathf.Exp(-Time.deltaTime / 0.05f));
        }
        lastHandPos[hand] = p;
        hasHandPos[hand] = true;
    }

    void DetectSwing(int hand, float speed)
    {
        if (!handArmed[hand])
        {
            // Re-arm when the hand slows down (chop) or turns back / around (continuous slashing never slows enough).
            bool slowed = speed < swingRearmSpeed;
            bool turned = Time.time - handCountTime[hand] >= minSwingInterval &&
                          handVelocity[hand].sqrMagnitude > 0.01f && countedDir[hand].sqrMagnitude > 0f &&
                          Vector3.Angle(handVelocity[hand], countedDir[hand]) >= swingReversalAngle;
            if (slowed || turned) handArmed[hand] = true;
            else return;
        }
        if (speed >= swingTriggerSpeed && Time.time - handCountTime[hand] >= minSwingInterval)
        {
            handArmed[hand] = false;
            handCountTime[hand] = Time.time;
            countedDir[hand] = handVelocity[hand];
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
