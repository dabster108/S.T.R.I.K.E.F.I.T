// PlayerExertionAudio.cs
// Purpose: How hard the player is working, as ONE number (Exertion01, 0 to 1), and the breathing that follows it in three tiers:
//            calm   = silent
//            medium = the heavy-breathing loop
//            high   = the fast-breathing loop, also forced whenever the player's HP is under lowHpOn of max
//          Exertion01 is the single source of truth: the live effort bar (CombatUIController) and the session telemetry
//          ("effort") read it from here, nothing else computes exertion.
//          Every swing attempt counts (hit or miss), weighted by how fast the hand moved; swings inside a rolling window add up,
//          so exertion rises with a sustained flurry and decays when the player rests. The tier changes with hysteresis (separate
//          on/off thresholds plus a minimum hold) so it cannot flip-flop around a threshold, and tiers crossfade on two separate
//          AudioSources. Each source has its own volume cap, kept below the combat SFX and above the music. Silent outside an active
//          battle, and cut quickly at victory or defeat. A tier change is written to the log ("[StrikeFitBreath]", adb logcat).
//          Headset note (2026-10-08): the fast tier used to reach audibility only through a runtime x18 DSP gain
//          (AudioGainFilter / OnAudioFilterRead) on a clip recorded at -48.6 dBFS RMS. The clip is now levelled in the asset
//          (breathingfast_leveled.wav, peak -1 dBFS), so no script DSP is needed; fastGain stays available (1 = off).
//          Development builds: launch with  adb shell am start -n <package>/com.unity3d.player.UnityPlayerGameActivity
//          --es strikefit breathtest  to play each tier for a few seconds before the battle and log the measured output levels.
// Dependencies: PlayerCombat (per-hand swing speed), PlayerHealth (HP for the high tier), BattleManager (only during an active
//               battle), AudioSource, AudioGainFilter (optional gain; self-test only)
// Attach to: OVRCameraRig (next to PlayerCombat). Uses its own looping AudioSources on child objects.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerExertionAudio : MonoBehaviour
{
    public enum Tier { Calm, Medium, High }

    public static PlayerExertionAudio Instance { get; private set; }

    [Header("Medium tier: heavy breathing")]
    [Tooltip("Dedicated looping source (2D). Created as a child at runtime if left empty.")]
    public AudioSource breathingSource;
    public AudioClip breathingClip;
    [Tooltip("Volume when medium breathing first starts.")]
    [Range(0f, 1f)] public float startVolume = 0.1f;
    [Tooltip("Hard cap for this source, reached as exertion climbs toward the high tier. Keep below the combat SFX.")]
    [Range(0f, 1f)] public float maxVolume = 0.3f;

    [Header("High tier: fast breathing")]
    [Tooltip("Off = the high tier plays the heavy-breathing source at its cap instead.")]
    public bool fastBreathing = true;
    [Tooltip("Dedicated looping source (2D). Created as a child at runtime if left empty.")]
    public AudioSource fastSource;
    public AudioClip fastClip;
    [Tooltip("Hard cap for this source. Keep below the combat SFX.")]
    [Range(0f, 1f)] public float fastMaxVolume = 0.3f;
    [Tooltip("Linear gain applied to the fast source's output through a script DSP filter. 1 = off (no filter is added). The " +
             "levelled clip (breathingfast_leveled.wav) needs none; the original breathingfast.mp3 is 25 dB too quiet and needed 18.")]
    public float fastGain = 1f;

    [Header("Mixing")]
    [Tooltip("AudioSource priority of both breathing loops (0 = highest, 128 = default). High enough that a burst of one-shots " +
             "past the real-voice limit (Project Settings > Audio, 32) virtualises sound effects before the breathing.")]
    [Range(0, 256)] public int sourcePriority = 64;
    [Tooltip("Write each tier change to the log (one line, never per frame).")]
    public bool logTierChanges = true;

    [Header("Self-test (Development builds)")]
    [Tooltip("Development builds launched with the intent extra  --es strikefit breathtest  play each tier for a few seconds " +
             "before the battle and log the measured listener output (dBFS). Release builds ignore it.")]
    public bool selfTestInDevBuild = true;

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

    [Header("Exertion value (0-1)")]
    [Tooltip("Rolling window (s). Swings older than this no longer count, so exertion decays when inactive.")]
    public float exertionWindow = 12f;
    [Tooltip("Weighted swings inside the window that make exertion 1. A swing weighs minSwingWeight (a slow one) up to 1 " +
             "(a full-power one), so 14 means roughly 1.2 hard swings per second sustained for the whole window.")]
    public float swingsForFullEffort = 14f;
    [Tooltip("Weight of a swing that only just clears swingTriggerSpeed.")]
    [Range(0.1f, 1f)] public float minSwingWeight = 0.5f;
    [Tooltip("Hand speed (m/s) at which a swing weighs 1.")]
    public float fullWeightSpeed = 5f;
    [Tooltip("Time constant (s) that smooths the value, so the bar and the tiers do not jitter swing by swing.")]
    public float smoothSeconds = 0.6f;

    [Header("Tiers (hysteresis: on above, off below)")]
    [Range(0f, 1f)] public float mediumOn = 0.28f;
    [Range(0f, 1f)] public float mediumOff = 0.18f;
    [Range(0f, 1f)] public float highOn = 0.70f;
    [Range(0f, 1f)] public float highOff = 0.55f;
    [Tooltip("Player HP fraction below which the high tier is forced, whatever the effort.")]
    [Range(0f, 1f)] public float lowHpOn = 0.25f;
    [Tooltip("HP fraction the player must climb back above before the forced high tier ends.")]
    [Range(0f, 1f)] public float lowHpOff = 0.30f;
    [Tooltip("A tier change is never reversed sooner than this (s).")]
    public float minTierHoldSeconds = 1f;

    [Header("Fades (seconds for a full 0 <-> cap change)")]
    [Tooltip("Medium <-> high: both sources move at once.")]
    public float crossfadeSeconds = 1.5f;
    public float fadeInSeconds = 2f;
    public float fadeDownSeconds = 4f;
    [Tooltip("Victory / defeat: breathing is cut this fast.")]
    public float battleEndFadeSeconds = 0.3f;

    [Header("Runtime (read-only)")]
    [SerializeField] private int swingsInWindow;
    [SerializeField] private float exertion01;
    [SerializeField] private Tier tier;
    [SerializeField] private bool lowHpActive;
    [SerializeField] private float mediumVolume;
    [SerializeField] private float fastVolume;

    /// <summary>How hard the player is working, 0 to 1. The one value the breathing, the effort bar and the telemetry share.</summary>
    public float Exertion01 => exertion01;
    public Tier CurrentTier => tier;
    /// <summary>True while HP is under lowHpOn (with hysteresis): the fast tier is forced and the screen-edge pulse runs.</summary>
    public bool LowHpActive => lowHpActive;
    public int SwingsInWindow => swingsInWindow;
    public float MediumVolume => mediumVolume;
    public float FastVolume => fastVolume;
    public float CurrentVolume => Mathf.Max(mediumVolume, fastVolume);
    public bool IsBreathing => tier != Tier.Calm;
    /// <summary>Minimum medium-breathing volume during a battle, whatever the effort (PlayerLowHealthFeedback: hurt and panting).
    /// 0 = off (Levels 1-2). Ignored while the fast tier is active.</summary>
    public float ExternalFloorVolume { get; set; }

    private struct Swing { public float time; public float weight; }
    private readonly Queue<Swing> swings = new Queue<Swing>();
    private float weightedSum;
    private readonly bool[] handArmed = { true, true };
    private float tierChangedAt = -999f;
    // Per hand: weapon motion direction (for reversal re-arm), its direction at the last count, last count time.
    private readonly Vector3[] lastHandPos = new Vector3[2];
    private readonly bool[] hasHandPos = new bool[2];
    private readonly Vector3[] handVelocity = new Vector3[2];
    private readonly Vector3[] countedDir = new Vector3[2];
    private readonly float[] handCountTime = { -999f, -999f };
    private AudioGainFilter fastFilter;

    void Awake()
    {
        Instance = this;
        breathingSource = PrepareSource(breathingSource, transform, "PlayerBreathingAudio", breathingClip, sourcePriority);
        if (fastBreathing)
        {
            fastSource = PrepareSource(fastSource, transform, "PlayerBreathingFastAudio", fastClip, sourcePriority);
            if (fastSource != null)
            {
                fastFilter = fastSource.GetComponent<AudioGainFilter>();
                bool needGain = Mathf.Abs(fastGain - 1f) > 0.01f;
                if (needGain && fastFilter == null) fastFilter = fastSource.gameObject.AddComponent<AudioGainFilter>();
                if (fastFilter != null) { fastFilter.gain = Mathf.Max(0f, fastGain); fastFilter.enabled = needGain; }
            }
        }
    }

    static bool selfTestStarted;

    void Start()
    {
        if (!selfTestInDevBuild || !Debug.isDebugBuild || selfTestStarted) return;
        string extra = LaunchExtra();
        if (extra == null || !extra.StartsWith("breathtest")) return;
        // "breathtest" = this scene; "breathtest:Level3_Hard" = load that level first, then test there (",haptictest" may follow).
        int colon = extra.IndexOf(':');
        string scene = colon > 0 ? extra.Substring(colon + 1) : null;
        if (scene != null && scene.IndexOf(',') >= 0) scene = scene.Substring(0, scene.IndexOf(','));   // "breathtest:Level1_Easy,haptictest"
        string here = gameObject.scene.name;
        if (!string.IsNullOrEmpty(scene) && scene != here)
        {
            if (!switchedForTest) { switchedForTest = true; UnityEngine.SceneManagement.SceneManager.LoadScene(scene); }
            return;
        }
        selfTestStarted = true;
        Debug.Log("[StrikeFitBreath] selftest starting in " + here);
        StartCoroutine(SelfTest());
    }
    static bool switchedForTest;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    static AudioSource PrepareSource(AudioSource src, Transform parent, string childName, AudioClip clip, int priority)
    {
        if (src == null)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            src = go.AddComponent<AudioSource>();
        }
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;   // 2D: the spatializer plugin (Meta XR Audio) never touches it
        src.spatialize = false;
        src.bypassReverbZones = true;
        src.priority = priority;
        src.volume = 0f;
        if (clip != null) src.clip = clip;
        return src;
    }

    void Update()
    {
        if (selfTesting) return;
        float dt = Time.deltaTime;
        bool battleActive = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;
        if (!battleActive)
        {
            // Idle / pre-battle / victory / defeat: never breathe, forget the effort so far.
            swings.Clear();
            weightedSum = 0f;
            swingsInWindow = 0;
            exertion01 = 0f;
            tier = Tier.Calm;
            lowHpActive = false;
            handArmed[0] = handArmed[1] = true;
            hasHandPos[0] = hasHandPos[1] = false;
            mediumVolume = Step(breathingSource, mediumVolume, 0f, maxVolume, battleEndFadeSeconds, dt);
            fastVolume = Step(fastSource, fastVolume, 0f, fastMaxVolume, battleEndFadeSeconds, dt);
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

        while (swings.Count > 0 && Time.time - swings.Peek().time > exertionWindow)
            weightedSum -= swings.Dequeue().weight;
        if (swings.Count == 0) weightedSum = 0f;
        swingsInWindow = swings.Count;

        float raw = Mathf.Clamp01(weightedSum / Mathf.Max(0.01f, swingsForFullEffort));
        exertion01 += (raw - exertion01) * (1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothSeconds)));

        UpdateLowHp();
        UpdateTier();
        ApplyVolumes(dt);
    }

    void UpdateLowHp()
    {
        var ph = PlayerHealth.Instance;
        if (ph == null || ph.maxHealth <= 0f || ph.IsDead) { lowHpActive = false; return; }
        float f = ph.currentHealth / ph.maxHealth;
        if (!lowHpActive && f < lowHpOn) lowHpActive = true;
        else if (lowHpActive && f > lowHpOff) lowHpActive = false;
    }

    void UpdateTier()
    {
        Tier want = tier;
        float e = exertion01;
        // High: forced by low HP, entered at highOn, left below highOff. Medium: entered at mediumOn, left below mediumOff.
        if (lowHpActive || e >= highOn || (tier == Tier.High && e > highOff)) want = Tier.High;
        else if (e >= mediumOn || (tier != Tier.Calm && e > mediumOff)) want = Tier.Medium;
        else want = Tier.Calm;

        if (want == tier) return;
        // Going UP is immediate (the player is working harder or got hurt); going down waits out the minimum hold.
        bool up = want > tier;
        if (!up && Time.time - tierChangedAt < minTierHoldSeconds) return;
        tier = want;
        tierChangedAt = Time.time;
        if (logTierChanges)
            Debug.Log($"[StrikeFitBreath] tier {tier} exertion {exertion01:0.00} lowHp {lowHpActive} heavyVol {mediumVolume:0.00} fastVol {fastVolume:0.00}");
    }

    void ApplyVolumes(float dt)
    {
        bool fastOk = fastBreathing && fastSource != null && fastSource.clip != null;
        float mediumTarget = 0f, fastTarget = 0f;

        if (tier == Tier.High)
        {
            if (fastOk) fastTarget = fastMaxVolume;
            else mediumTarget = maxVolume; // no fast clip: the heavy loop at its cap
        }
        else if (tier == Tier.Medium)
        {
            mediumTarget = Mathf.Lerp(startVolume, maxVolume, Mathf.InverseLerp(mediumOn, highOn, exertion01));
        }
        // A floor (Level 3: hurt and panting) keeps the heavy loop audible at rest, but never under the fast tier.
        if (tier != Tier.High) mediumTarget = Mathf.Max(mediumTarget, ExternalFloorVolume);

        // Medium <-> high moves both sources together over crossfadeSeconds; from or to silence uses the slower fades.
        bool crossing = (tier == Tier.High && mediumVolume > 0.001f) || (tier == Tier.Medium && fastVolume > 0.001f);
        float mediumFade = crossing ? crossfadeSeconds : (mediumTarget > mediumVolume ? fadeInSeconds : fadeDownSeconds);
        float fastFade = crossing ? crossfadeSeconds : (fastTarget > fastVolume ? fadeInSeconds : fadeDownSeconds);
        mediumVolume = Step(breathingSource, mediumVolume, mediumTarget, maxVolume, mediumFade, dt);
        fastVolume = Step(fastSource, fastVolume, fastTarget, fastMaxVolume, fastFade, dt);
    }

    // Moves one source's volume toward target (never above its own cap), starts / stops playback with it.
    static float Step(AudioSource src, float current, float target, float cap, float fullFadeSeconds, float dt)
    {
        cap = Mathf.Max(0f, cap);
        target = Mathf.Clamp(target, 0f, cap);
        float rate = cap / Mathf.Max(0.01f, fullFadeSeconds);
        current = Mathf.Min(Mathf.MoveTowards(current, target, rate * dt), cap);
        if (src == null || src.clip == null) return current;
        src.volume = current;
        if (current > 0.0001f)
        {
            if (!src.isPlaying) src.Play();
        }
        else if (src.isPlaying)
        {
            src.Stop();
        }
        return current;
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
            RegisterSwing(speed);
        }
    }

    /// <summary>Count one swing attempt of the given hand speed. Called from the hand speeds above; public for other effort sources.</summary>
    public void RegisterSwing(float speed)
    {
        float t = Mathf.InverseLerp(swingTriggerSpeed, Mathf.Max(swingTriggerSpeed + 0.01f, fullWeightSpeed), speed);
        float w = Mathf.Lerp(minSwingWeight, 1f, t);
        swings.Enqueue(new Swing { time = Time.time, weight = w });
        weightedSum += w;
    }

    /// <summary>Count one swing of unknown speed (a mid-weight swing).</summary>
    public void RegisterSwing() => RegisterSwing(Mathf.Lerp(swingTriggerSpeed, fullWeightSpeed, 0.5f));

    // ───────────────────────── self-test (Development builds, before the battle) ─────────────────────────
    private bool selfTesting;
    /// <summary>Last self-test result line (also logged).</summary>
    public string SelfTestReport { get; private set; }

    /// <summary>Intent string extra "strikefit" (Android), or null.</summary>
    static string LaunchExtra()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = act.Call<AndroidJavaObject>("getIntent"))
                return intent.Call<string>("getStringExtra", "strikefit");
        }
        catch (System.Exception) { return null; }
#else
        return null;
#endif
    }

    /// <summary>Plays nothing, then the heavy loop at its cap, then the fast loop at its cap, then the fast loop through a
    /// x(1/18) script filter, measuring the AudioListener output each time. Proves on the headset that each source is heard
    /// (and whether OnAudioFilterRead runs there). Public so the Editor harness can run it too.</summary>
    public IEnumerator SelfTest()
    {
        selfTesting = true;
        yield return new WaitForSeconds(3f);
        float quiet = 0f, heavy = 0f, fast = 0f, filtered = 0f;
        int calls = 0;
        yield return Measure(2f, v => quiet = v);
        if (breathingSource != null && breathingSource.clip != null)
        {
            breathingSource.volume = maxVolume; breathingSource.Play();
            yield return Measure(3f, v => heavy = v);
            breathingSource.Stop(); breathingSource.volume = 0f;
        }
        if (fastSource != null && fastSource.clip != null)
        {
            fastSource.volume = fastMaxVolume; fastSource.Play();
            yield return Measure(3f, v => fast = v);
            var f = fastSource.GetComponent<AudioGainFilter>();
            bool added = f == null;
            if (added) f = fastSource.gameObject.AddComponent<AudioGainFilter>();
            float oldGain = f.gain; bool oldOn = f.enabled;
            f.gain = 1f / 18f; f.enabled = true;
            int c0 = f.Calls;
            yield return Measure(3f, v => filtered = v);
            calls = f.Calls - c0;
            f.gain = oldGain; f.enabled = oldOn;
            if (added) Destroy(f);
            fastSource.Stop(); fastSource.volume = 0f;
        }
        SelfTestReport = $"[StrikeFitBreath] selftest {gameObject.scene.name} listener RMS: nothing {quiet:0.0} dBFS | heavy@{maxVolume:0.00} {heavy:0.0} dBFS " +
                         $"({breathingSource?.clip?.name}) | fast@{fastMaxVolume:0.00} {fast:0.0} dBFS ({fastSource?.clip?.name}) | " +
                         $"fast through OnAudioFilterRead x0.056: {filtered:0.0} dBFS, callbacks {calls} " +
                         $"=> script DSP {(calls > 0 ? "RUNS" : "DOES NOT RUN")} on this device";
        Debug.Log(SelfTestReport);
        mediumVolume = fastVolume = 0f;
        selfTesting = false;
    }

    static readonly float[] probeBuf = new float[1024];

    static IEnumerator Measure(float seconds, System.Action<float> result)
    {
        double sum = 0; long n = 0;
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                AudioListener.GetOutputData(probeBuf, ch);
                for (int i = 0; i < probeBuf.Length; i++) sum += probeBuf[i] * probeBuf[i];
                n += probeBuf.Length;
            }
            yield return null;
        }
        result(n > 0 ? (float)(10.0 * System.Math.Log10(System.Math.Max(sum / n, 1e-12))) : -120f);
    }
}
