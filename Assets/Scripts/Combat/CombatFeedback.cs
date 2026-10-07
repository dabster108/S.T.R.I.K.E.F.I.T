// CombatFeedback.cs
// Purpose: Cheap, VR-safe game feel shared by all three levels, each piece behind its own toggle:
//          - controller haptics on the hand that struck (light / medium / full by swing tier, a sharp pulse for a weapon
//            clash or parry) and on both hands when the player takes damage; every pulse is stopped by a timed coroutine;
//          - hit-stop on the MONSTER only (MonsterAI.HitStop: Animator + attack clock) on full-tier and weak-point hits, never while
//            the monster is winding up or attacking. One timer lives in MonsterAI (a second hit during a freeze is ignored, the
//            speed is always restored to the monster's own speed, which is 1 unless an enrage tempo or stagger is active).
//            Time.timeScale, the player's hands and head are never touched.
//          Hit-sound variation lives on HitDetection (varyHitSounds) and tiered damage numbers on CombatUIController.
// Dependencies: HitDetection (events), PlayerHealth (DamageTaken), MonsterAI, CombatUIController (tiers), OVRInput
// Attach to: BattleSystems (one per level scene)

using System.Collections;
using UnityEngine;

public class CombatFeedback : MonoBehaviour
{
    [Header("Controller haptics")]
    public bool haptics = true;
    [Tooltip("Amplitude / seconds for tap and light swings.")]
    public Vector2 lightPulse = new Vector2(0.25f, 0.04f);
    public Vector2 mediumPulse = new Vector2(0.5f, 0.06f);
    public Vector2 fullPulse = new Vector2(0.9f, 0.09f);
    [Tooltip("Body hit absorbed by the monster's guard.")]
    public Vector2 blockedPulse = new Vector2(0.35f, 0.05f);
    [Tooltip("Weapon clash / parry: short and sharp.")]
    public Vector2 clashPulse = new Vector2(1f, 0.03f);
    [Tooltip("Both hands when the player takes damage.")]
    public Vector2 damagePulse = new Vector2(0.7f, 0.12f);
    [Tooltip("OVRInput vibration frequency for normal pulses (0-1); clash uses 1.")]
    [Range(0f, 1f)] public float frequency = 0.5f;

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

    private readonly int[] generation = new int[2]; // newest pulse per hand; an older stop never cuts a newer pulse
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
        Pulse(source.hand, p, frequency);

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
        if (source != null) Pulse(source.hand, clashPulse, 1f);
    }

    void OnDamage(float amount, bool blocking, float hpAfter)
    {
        if (amount <= 0f) return;
        Pulse(Hand.Left, damagePulse, frequency);
        Pulse(Hand.Right, damagePulse, frequency);
    }

    void Pulse(Hand hand, Vector2 ampSeconds, float freq)
    {
        if (!haptics || ampSeconds.x <= 0f || ampSeconds.y <= 0f) return;
        int h = hand == Hand.Left ? 0 : 1;
        var controller = h == 0 ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        OVRInput.SetControllerVibration(freq, Mathf.Clamp01(ampSeconds.x), controller);
        Pulses++;
        LastAmplitude = ampSeconds.x;
        StartCoroutine(StopAfter(h, ++generation[h], controller, ampSeconds.y));
    }

    IEnumerator StopAfter(int h, int gen, OVRInput.Controller controller, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (generation[h] == gen) OVRInput.SetControllerVibration(0f, 0f, controller);
    }

    void StopAll()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }
}
