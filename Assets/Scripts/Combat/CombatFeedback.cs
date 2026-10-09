// CombatFeedback.cs
// Purpose: Cheap, VR-safe game feel shared by all three levels, each piece behind its own toggle:
//          - controller haptics on the hand that struck (light / medium / full by swing tier), a strong pulse with a weaker
//            tail on a weapon clash (plus a light echo on the other hand), and both hands when the player takes damage.
//            Every pulse is stopped by a timed coroutine; a new pulse refreshes an active one unless it is clearly weaker, so a
//            body hit never cuts a clash short. The hand is the anchor the weapon sits under (HitDetection.ControllerHand).
//            Backend: OVRPlugin.SetControllerVibration (what OVRInput.SetControllerVibration calls; Meta runtime through the
//            OpenXR "Meta XR Feature"), or the XR InputDevice haptic impulse when that runtime is not up.
//          - hit-stop on the MONSTER only (MonsterAI.HitStop: Animator + attack clock) on full-tier and weak-point hits, never while
//            the monster is winding up or attacking. One timer lives in MonsterAI (a second hit during a freeze is ignored, the
//            speed is always restored to the monster's own speed, which is 1 unless an enrage tempo or stagger is active).
//            Time.timeScale, the player's hands and head are never touched.
//          Hit-sound variation lives on HitDetection (varyHitSounds) and tiered damage numbers on CombatUIController.
// Dependencies: HitDetection (events, ControllerHand), PlayerHealth (DamageTaken), MonsterAI, CombatUIController (tiers),
//               OVRPlugin / OVRManager (Meta XR Core), UnityEngine.XR (fallback), CombatDebugInfo (debug readout)
// Attach to: BattleSystems (one per level scene)

using System.Collections;
using UnityEngine;
using UnityEngine.XR;

public class CombatFeedback : MonoBehaviour
{
    [Header("Controller haptics")]
    public bool haptics = true;
    [Tooltip("Amplitude / seconds for tap and light swings.")]
    public Vector2 lightPulse = new Vector2(0.25f, 0.04f);
    public Vector2 mediumPulse = new Vector2(0.5f, 0.06f);
    [Tooltip("Full-tier and weak-point hits. Kept below the clash pulse so a parry always feels stronger than any body hit.")]
    public Vector2 fullPulse = new Vector2(0.75f, 0.09f);
    [Tooltip("Body hit absorbed by the monster's guard.")]
    public Vector2 blockedPulse = new Vector2(0.35f, 0.05f);
    [Tooltip("Weapon clash / parry on the striking hand: amplitude / seconds. Short pulses under about 0.05 s or amplitudes " +
             "under about 0.2 are hard to feel on Quest controllers.")]
    public Vector2 clashPulse = new Vector2(1f, 0.15f);
    [Tooltip("Weaker tail right after the clash pulse (amplitude / seconds; 0 = none).")]
    public Vector2 clashTail = new Vector2(0.45f, 0.1f);
    [Tooltip("The other hand on a clash: a light echo (amplitude / seconds; 0 = none).")]
    public Vector2 clashOtherHand = new Vector2(0.3f, 0.1f);
    [Tooltip("Both hands when the player takes damage.")]
    public Vector2 damagePulse = new Vector2(0.7f, 0.12f);
    [Tooltip("OVRInput vibration frequency for normal pulses (0-1); clash uses 1.")]
    [Range(0f, 1f)] public float frequency = 0.5f;
    [Tooltip("A new pulse replaces the one still running only if it is at least this fraction of its amplitude.")]
    [Range(0f, 1f)] public float replaceFraction = 0.8f;
    [Tooltip("When the Meta runtime (OVRPlugin) is not running, send the pulse as an XR InputDevice haptic impulse instead.")]
    public bool xrImpulseFallback = true;

    [Header("Hit-stop (monster only)")]
    public bool hitStop = true;
    [Tooltip("Seconds the monster's Animator and attack clock freeze on a full-tier or weak-point hit.")]
    public float hitStopSeconds = 0.07f;
    [Tooltip("Skip the freeze while the monster is winding up or attacking, so its animation and damage timing stay in sync.")]
    public bool skipHitStopDuringAttacks = true;

    /// <summary>Pulses / hit-stops requested (verification).</summary>
    public int Pulses { get; private set; }
    public int HitStops { get; private set; }
    /// <summary>Hit-stops not applied because the monster was winding up or attacking (verification).</summary>
    public int HitStopsSkipped { get; private set; }
    public float LastAmplitude { get; private set; }
    /// <summary>Pulses not sent because a stronger one was still running on that hand.</summary>
    public int PulsesSuppressed { get; private set; }
    public int ClashPulses { get; private set; }

    private readonly int[] generation = new int[2]; // newest pulse per hand; an older stop never cuts a newer pulse
    private readonly float[] activeAmp = new float[2];
    private readonly float[] activeUntil = new float[2];
    private MonsterController lastMonster;
    private MonsterAI lastAi;

    void OnEnable()
    {
        HitDetection.HitLanded += OnHit;
        HitDetection.WeaponClashed += OnClash;
        PlayerHealth.DamageTaken += OnDamage;
    }

    void OnDisable()
    {
        HitDetection.HitLanded -= OnHit;
        HitDetection.WeaponClashed -= OnClash;
        PlayerHealth.DamageTaken -= OnDamage;
        StopAll();
    }

    void OnHit(HitDetection source, float speed, bool defended)
    {
        if (source == null) return;
        var style = CombatUIController.StyleForSpeed(speed);
        Vector2 p = defended ? blockedPulse
                  : style == CombatUIController.DamageStyle.Full || source.LastWasWeakPoint ? fullPulse
                  : style == CombatUIController.DamageStyle.Medium ? mediumPulse
                  : lightPulse;
        Pulse(source.ControllerHand, p.x, p.y, 0f, 0f, frequency, "hit");

        if (hitStop && !defended && (style == CombatUIController.DamageStyle.Full || source.LastWasWeakPoint))
        {
            var m = source.LastMonster;
            if (m != null)
            {
                if (m != lastMonster) { lastMonster = m; lastAi = m.GetComponent<MonsterAI>(); }
                bool attacking = skipHitStopDuringAttacks && m.isAttacking; // isAttacking covers the wind-up too
                if (lastAi != null && !attacking && !lastAi.InHitStop) { lastAi.HitStop(hitStopSeconds); HitStops++; }
                else if (attacking) HitStopsSkipped++;
            }
        }
    }

    void OnClash(HitDetection source, float speed)
    {
        if (source == null) return;
        Hand striking = source.ControllerHand;
        ClashPulses++;
        Pulse(striking, clashPulse.x, clashPulse.y, clashTail.x, clashTail.y, 1f, "clash");
        if (clashOtherHand.x > 0f && clashOtherHand.y > 0f)
            Pulse(striking == Hand.Left ? Hand.Right : Hand.Left, clashOtherHand.x, clashOtherHand.y, 0f, 0f, 1f, "clash echo");
    }

    // Development builds launched with  --es strikefit haptictest  (or breathtest+haptictest): send the clash pattern to each
    // hand once and log which backend took it and whether the runtime accepted it. Proves the API path on the headset; whether
    // it is FELT still needs a hand on the controller.
    void Start()
    {
        if (!Debug.isDebugBuild) return;
        string extra = null;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = act.Call<AndroidJavaObject>("getIntent"))
                extra = intent.Call<string>("getStringExtra", "strikefit");
        }
        catch (System.Exception) { }
#endif
        if (extra != null && extra.Contains("haptictest")) StartCoroutine(HapticSelfTest());
    }

    IEnumerator HapticSelfTest()
    {
        float end = Time.unscaledTime + 2f;
        while (Time.unscaledTime < end) yield return null;
        for (int h = 0; h < 2; h++)
        {
            bool ok = Send(h, clashPulse.x, 1f, clashPulse.y, out string backend);
            Debug.Log($"[StrikeFitHaptic] selftest {(h == 0 ? "LEFT" : "RIGHT")} clash pulse amp {clashPulse.x:0.00} {clashPulse.y:0.00} s via {backend}: {(ok ? "accepted" : "REFUSED")} (OVRManager.loadedXRDevice {OVRManager.loadedXRDevice}, OVRPlugin.initialized {OVRPlugin.initialized})");
            end = Time.unscaledTime + clashPulse.y; while (Time.unscaledTime < end) yield return null;
            Send(h, 0f, 0f, 0f, out _);
            end = Time.unscaledTime + 0.5f; while (Time.unscaledTime < end) yield return null;
        }
    }

    void OnDamage(float amount, bool blocking, float hpAfter)
    {
        if (amount <= 0f) return;
        Pulse(Hand.Left, damagePulse.x, damagePulse.y, 0f, 0f, frequency, "damage");
        Pulse(Hand.Right, damagePulse.x, damagePulse.y, 0f, 0f, frequency, "damage");
    }

    void Pulse(Hand hand, float amp, float seconds, float tailAmp, float tailSeconds, float freq, string kind)
    {
        if (!haptics || amp <= 0f || seconds <= 0f) return;
        int h = hand == Hand.Left ? 0 : 1;
        float now = Time.unscaledTime;
        if (now < activeUntil[h] && amp < activeAmp[h] * replaceFraction) { PulsesSuppressed++; return; }
        amp = Mathf.Clamp01(amp);
        bool ok = Send(h, amp, freq, seconds, out string backend);
        activeAmp[h] = amp;
        activeUntil[h] = now + seconds + Mathf.Max(0f, tailSeconds);
        Pulses++;
        LastAmplitude = amp;
        CombatDebugInfo.NoteHaptic(hand, amp, seconds + (tailAmp > 0f ? tailSeconds : 0f), kind, backend, ok);
        StartCoroutine(PulseRoutine(h, ++generation[h], seconds, Mathf.Clamp01(tailAmp), tailSeconds, freq));
    }

    IEnumerator PulseRoutine(int h, int gen, float seconds, float tailAmp, float tailSeconds, float freq)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end) yield return null;
        if (generation[h] != gen) yield break;
        if (tailAmp > 0f && tailSeconds > 0f)
        {
            Send(h, tailAmp, freq, tailSeconds, out _);
            activeAmp[h] = tailAmp;
            end = Time.unscaledTime + tailSeconds;
            while (Time.unscaledTime < end) yield return null;
            if (generation[h] != gen) yield break;
        }
        Send(h, 0f, 0f, 0f, out _);
        activeAmp[h] = 0f;
    }

    /// <summary>One vibration command on one hand (amplitude 0 = stop). Returns whether the runtime accepted it.</summary>
    bool Send(int h, float amp, float freq, float seconds, out string backend)
    {
        if (OVRManager.loadedXRDevice == OVRManager.XRDevice.Oculus)
        {
            backend = "OVRPlugin";
            var mask = h == 0 ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            return OVRPlugin.SetControllerVibration((uint)mask, amp > 0f ? freq : 0f, amp);
        }
        if (xrImpulseFallback)
        {
            backend = "XR impulse";
            var dev = InputDevices.GetDeviceAtXRNode(h == 0 ? XRNode.LeftHand : XRNode.RightHand);
            if (!dev.isValid) { backend = "XR (no device)"; return false; }
            if (amp <= 0f) { dev.StopHaptics(); return true; }
            return dev.TryGetHapticCapabilities(out var cap) && cap.supportsImpulse && dev.SendHapticImpulse(0u, amp, seconds);
        }
        backend = "none";
        return false;
    }

    void StopAll()
    {
        for (int h = 0; h < 2; h++) { generation[h]++; activeAmp[h] = 0f; activeUntil[h] = 0f; Send(h, 0f, 0f, 0f, out _); }
    }
}
