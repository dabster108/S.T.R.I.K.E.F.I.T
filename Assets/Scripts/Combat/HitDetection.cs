// HitDetection.cs
// Purpose: Detect hammer collisions against monsters; compute damage from SWING VELOCITY
//          (not fixed collision). Min velocity gate prevents spam tapping. Awards XP.
// Dependencies: MonsterController, PlayerCombat, RoundManager, XPManager, optional VFX/SFX
// Attach to: Hammer head collider (tag this object "Hammer")

using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HitDetection : MonoBehaviour
{
    // Telemetry hooks (SessionReporter). Raised synchronously; listeners must not throw.
    /// <summary>A swing landed and counted. Args: source, swing speed (m/s), monster was defending.</summary>
    public static event Action<HitDetection, float, bool> HitLanded;
    /// <summary>The weapon touched the monster below the minimum speed. Args: source, speed, minimum.</summary>
    public static event Action<HitDetection, float, float> SwingTooSlow;
    /// <summary>The weapon struck the monster's weapon (parry / clash). Args: source, speed.</summary>
    public static event Action<HitDetection, float> WeaponClashed;

    [Header("Damage — Velocity Based")]
    public float baseDamage = 12f;
    [Tooltip("Below this controller speed (m/s) the hit is IGNORED. Anti-spam. " +
             "Mirrors PlayerCombat.minSwingVelocity; the larger of the two is used.")]
    public float minSwingSpeed = 1.2f;
    [Tooltip("Speed at which velocity scaling caps (uses PlayerCombat.maxSwingVelocity if larger).")]
    public float maxSwingSpeed = 6f;
    [Tooltip("Max damage multiplier from velocity alone (uses PlayerCombat.maxVelocityDamageMultiplier if > 0).")]
    public float maxForceMultiplier = 2.5f;
    public float facingBonusMultiplier = 1.25f; // bonus when hitting front of monster

    [Header("Hit Rate Limit")]
    public float hitCooldown = 0.35f;

    [Header("Feedback (optional)")]
    public ParticleSystem hitParticles;
    public AudioSource audioSource;
    public AudioClip hitSound;
    [Tooltip("Secondary impact sound for big/full swings.")]
    public AudioClip heavyHitSound;
    [Tooltip("Swing speed threshold that switches to heavyHitSound.")]
    public float heavySwingSpeedThreshold = 4.8f;
    [Tooltip("Played when hammer hits the monster's weapon (parry/clash).")]
    public AudioClip clashSound;
    public VRSoundButton soundButtonFallback;

    [Header("Who can deal hits")]
    public string hammerTag = "Hammer";
    public string playerHandTag = "PlayerHand";

    [Header("Dual-wield")]
    [Tooltip("Which hand this hammer is in. Right = primary (uses CurrentSwingSpeed). " +
             "Left = offhand (uses CurrentOffhandSwingSpeed).")]
    public Hand hand = Hand.Right;

    private float lastHitTime = -999f;
    private Vector3 lastPosition;
    private Vector3 velocity;

    void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.pitch = 1f;
            audioSource.dopplerLevel = 0f;
        }
        lastPosition = transform.position;
    }

    void Update()
    {
        // Estimate swing velocity from transform motion (works for kinematic held hammers)
        velocity = (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    void OnCollisionEnter(Collision collision)
    {
        TryHit(collision.collider, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
    }

    void TryHit(Collider other, Vector3? explicitPoint = null)
    {
        if (Time.time - lastHitTime < hitCooldown) return;

        // --- Clash / Parry check ---
        if (other.CompareTag("MonsterWeapon"))
        {
            lastHitTime = Time.time;
            WeaponClashed?.Invoke(this, ImpactSpeed());
            if (audioSource != null && clashSound != null)
            {
                // Clash clip in project is a long riser (~5s); keep it short/quiet so hits don't feel 2x frantic.
                float vol = clashSound.length > 1.5f ? 0.45f : 0.85f;
                audioSource.PlayOneShot(clashSound, vol);
            }
            
            // Optional: spawn sparks at hitPoint
            Vector3 clashPoint = explicitPoint ?? other.ClosestPoint(transform.position);
            if (hitParticles != null)
            {
                hitParticles.transform.position = clashPoint;
                hitParticles.Play();
            }
            
            return;
        }

        MonsterController monster = other.GetComponentInParent<MonsterController>();
        if (monster == null) return;
        if (!monster.isAlive) return;

        // --- Swing speed at moment of impact ---
        float speed = ImpactSpeed();

        // --- Min velocity gate: anti-spam ---
        float minVel = minSwingSpeed;
        if (PlayerCombat.Instance != null)
            minVel = Mathf.Max(minVel, PlayerCombat.Instance.minSwingVelocity);

        if (speed < minVel)
        {
            // Too slow — ignore. This is the core anti-cheese rule.
            SwingTooSlow?.Invoke(this, speed, minVel);
            return;
        }

        // --- Velocity → damage multiplier ---
        float maxVel = maxSwingSpeed;
        float maxMul = maxForceMultiplier;
        if (PlayerCombat.Instance != null)
        {
            if (PlayerCombat.Instance.maxSwingVelocity > maxVel) maxVel = PlayerCombat.Instance.maxSwingVelocity;
            if (PlayerCombat.Instance.maxVelocityDamageMultiplier > maxMul) maxMul = PlayerCombat.Instance.maxVelocityDamageMultiplier;
        }
        float t = Mathf.InverseLerp(minVel, maxVel, speed);
        float forceMul = Mathf.Lerp(1f, maxMul, t);

        // --- Angle bonus (stretch goal) ---
        float angleMul = 1f;
        if (PlayerCombat.Instance != null)
            angleMul = PlayerCombat.Instance.GetAngleBonusMultiplier(PlayerCombat.Instance.CurrentAngularSpeed);

        Vector3 hitPoint = explicitPoint ?? other.ClosestPoint(transform.position);
        float facingMul = ComputeFacingMultiplier(monster.transform, hitPoint);

        float damage = baseDamage * forceMul * angleMul * facingMul;

        // --- Stamina + fitness modifiers ---
        if (PlayerCombat.Instance != null)
        {
            if (!PlayerCombat.Instance.TrySpendAttackStamina())
                damage *= 0.5f; // exhausted swings still hit but weaker
            damage = PlayerCombat.Instance.ApplyFitnessModifiers(damage, speed);
        }

        lastHitTime = Time.time;

        var monsterAi = monster.GetComponent<MonsterAI>();
        bool monsterDefending = monsterAi != null && monsterAi.IsDefending;
        monster.OnTakeDamage(damage, hitPoint);
        PlayFeedback(hitPoint, speed);

        // --- XP: reward good form (full swing > tap) ---
        if (XPManager.Instance != null)
            XPManager.Instance.AwardHitXP(speed, damage);

        // Keep RoundManager fitness metrics in sync (same as CubeTapCounter)
        if (RoundManager.Instance != null && RoundManager.Instance.roundActive)
            RoundManager.Instance.RegisterTap();

        HitLanded?.Invoke(this, speed, monsterDefending);

        if (BattleManager.Instance != null)
            BattleManager.Instance.RegisterSuccessfulHit(damage);
    }

    /// <summary>Weapon speed right now: the faster of this collider's motion and its hand's tracked speed.</summary>
    float ImpactSpeed()
    {
        float speed = velocity.magnitude;
        if (PlayerCombat.Instance != null)
        {
            float handSpeed = (hand == Hand.Left)
                ? PlayerCombat.Instance.CurrentOffhandSwingSpeed
                : PlayerCombat.Instance.CurrentSwingSpeed;
            speed = Mathf.Max(speed, handSpeed);
        }
        return speed;
    }

    float ComputeFacingMultiplier(Transform monsterRoot, Vector3 hitPoint)
    {
        Vector3 toHit = (hitPoint - monsterRoot.position).normalized;
        float facing = Vector3.Dot(monsterRoot.forward, toHit); // front hits ~1
        if (facing > 0.35f) return facingBonusMultiplier;
        if (facing < -0.35f) return 0.85f;
        return 1f;
    }

    void PlayFeedback(Vector3 hitPoint, float swingSpeed)
    {
        if (hitParticles != null)
        {
            hitParticles.transform.position = hitPoint;
            hitParticles.Play();
        }

        if (audioSource != null)
        {
            AudioClip clipToPlay = null;
            if (swingSpeed >= heavySwingSpeedThreshold && heavyHitSound != null)
                clipToPlay = heavyHitSound;
            else
                clipToPlay = hitSound != null ? hitSound : heavyHitSound;

            if (clipToPlay != null)
                audioSource.PlayOneShot(clipToPlay);
        }
        else if (soundButtonFallback != null)
            soundButtonFallback.PlayClickSound();
    }

    public Vector3 CurrentVelocity => velocity;
}
