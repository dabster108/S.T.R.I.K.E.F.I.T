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
    [Tooltip("Bonus for hits on the monster's front half (measured horizontally from its root).")]
    public float facingBonusMultiplier = 1.25f;

    [Header("Hit Rate Limit")]
    public float hitCooldown = 0.35f;

    [Header("Momentum (optional, e.g. flail)")]
    [Tooltip("Extra damage fraction for a swing that has stayed above the speed gate for momentumBuildSeconds " +
             "(0 = off). Rewards full wind-up swings over flicks.")]
    public float momentumMaxBonus = 0f;
    public float momentumBuildSeconds = 0.6f;

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
    [Tooltip("Body block: played INSTEAD of hitSound when the hit lands on the body of a defending monster. " +
             "Empty = only the monster's own block clang (MonsterAI.defendBlockClip) is heard; if the monster " +
             "has none either, the normal hit sound is used.")]
    public AudioClip blockedHitSound;
    [Range(0f, 1f)] public float blockedHitVolume = 0.8f;
    public VRSoundButton soundButtonFallback;

    [Header("Sound variation")]
    [Tooltip("Pick a random variant + small random pitch per hit so repeated hits never sound identical.")]
    public bool varyHitSounds = true;
    [Tooltip("Variants for normal hits (hitSound is used when empty).")]
    public AudioClip[] hitVariants = new AudioClip[0];
    [Tooltip("Variants for heavy hits (heavyHitSound is used when empty).")]
    public AudioClip[] heavyHitVariants = new AudioClip[0];
    [Tooltip("Random pitch spread (0.06 = +/-6 %).")]
    [Range(0f, 0.2f)] public float pitchJitter = 0.06f;

    [Header("Who can deal hits")]
    public string hammerTag = "Hammer";
    public string playerHandTag = "PlayerHand";

    [Header("Dual-wield")]
    [Tooltip("Which hand this hammer is in. Right = primary (uses CurrentSwingSpeed). " +
             "Left = offhand (uses CurrentOffhandSwingSpeed).")]
    public Hand hand = Hand.Right;

    /// <summary>What the last monster contact resolved to (verification / debug).</summary>
    public enum ContactResult { None, TooSlow, Clash, Blocked, Hit }
    public ContactResult LastResult { get; private set; }
    /// <summary>True if the last Hit / Blocked contact was on a MonsterWeakPoint collider.</summary>
    public bool LastWasWeakPoint { get; private set; }
    /// <summary>Damage passed to the monster by the last Hit / Blocked contact (before the monster's own defend reduction).</summary>
    public float LastDamage { get; private set; }
    /// <summary>Monster struck by the last Hit / Blocked contact.</summary>
    public MonsterController LastMonster { get; private set; }
    public Vector3 LastHitPoint { get; private set; }
    /// <summary>True if the front-hit bonus applied to the last Hit / Blocked contact (verification).</summary>
    public bool LastWasFrontHit { get; private set; }

    private float lastHitTime = -999f;
    private Vector3 lastPosition;
    private Vector3 velocity;
    // One landed hit per swing while the weapon stays inside the monster: re-armed when the swing ends.
    private bool stayArmed = true;
    private float aboveGateSince = -1f;

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

        float speedNow = ImpactSpeed();
        if (!stayArmed && speedNow < MinSwingSpeed())
            stayArmed = true;

        if (momentumMaxBonus > 0f)
        {
            if (speedNow >= MinSwingSpeed()) { if (aboveGateSince < 0f) aboveGateSince = Time.time; }
            else aboveGateSince = -1f;
        }
    }

    float MomentumMultiplier()
    {
        if (momentumMaxBonus <= 0f || aboveGateSince < 0f) return 1f;
        return 1f + momentumMaxBonus * Mathf.Clamp01((Time.time - aboveGateSince) / Mathf.Max(0.01f, momentumBuildSeconds));
    }

    void OnTriggerEnter(Collider other)
    {
        TryHit(other);
    }

    // A contact rejected on entry (too slow, or inside the cooldown after a clash / the other body part) used to be
    // lost for good, because Enter fires once. While the weapon is still inside the monster it is retried here.
    void OnTriggerStay(Collider other)
    {
        TryHit(other, null, true);
    }

    void OnCollisionEnter(Collision collision)
    {
        TryHit(collision.collider, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);
    }

    void TryHit(Collider other, Vector3? explicitPoint = null, bool fromStay = false)
    {
        if (Time.time - lastHitTime < hitCooldown) return;

        // --- Clash / Parry check ---
        if (other.CompareTag("MonsterWeapon"))
        {
            if (fromStay) return; // a clash is the moment of contact only; resting on the weapon is not a parry
            lastHitTime = Time.time;
            stayArmed = false; // a parried swing cannot go on to score on the body it is still passing through
            LastResult = ContactResult.Clash;
            WeaponClashed?.Invoke(this, ImpactSpeed());
            if (audioSource != null && clashSound != null)
            {
                // Clash clip in project is a long riser (~5s); keep it short/quiet so hits don't feel 2x frantic.
                float vol = clashSound.length > 1.5f ? 0.45f : 0.85f;
                Jitter();
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
        if (fromStay && !stayArmed) return;

        // --- Swing speed at moment of impact ---
        float speed = ImpactSpeed();

        // --- Min velocity gate: anti-spam ---
        float minVel = MinSwingSpeed();

        if (speed < minVel)
        {
            // Too slow — ignore. This is the core anti-cheese rule.
            if (!fromStay)
            {
                LastResult = ContactResult.TooSlow;
                SwingTooSlow?.Invoke(this, speed, minVel);
            }
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

        var weakPoint = other.GetComponent<MonsterWeakPoint>();
        float weakMul = weakPoint != null ? Mathf.Max(1f, weakPoint.damageMultiplier) : 1f;

        float damage = baseDamage * forceMul * angleMul * facingMul * MomentumMultiplier() * weakMul;

        // --- Stamina + fitness modifiers ---
        if (PlayerCombat.Instance != null)
        {
            if (!PlayerCombat.Instance.TrySpendAttackStamina())
                damage *= 0.5f; // exhausted swings still hit but weaker
            damage = PlayerCombat.Instance.ApplyFitnessModifiers(damage, speed);
        }

        lastHitTime = Time.time;
        stayArmed = false;

        var monsterAi = monster.GetComponent<MonsterAI>();
        bool monsterDefending = monsterAi != null && monsterAi.IsDefending;
        LastResult = monsterDefending ? ContactResult.Blocked : ContactResult.Hit;
        LastDamage = damage;
        LastWasWeakPoint = weakPoint != null;
        LastWasFrontHit = facingMul > 1f;
        LastMonster = monster;
        LastHitPoint = hitPoint;
        monster.OnTakeDamage(damage, hitPoint, speed, weakPoint != null);
        if (weakPoint != null) weakPoint.NotifyHit(this);
        if (monsterDefending)
            PlayBlockedFeedback(hitPoint, speed, monsterAi);
        else if (weakPoint != null && weakPoint.hitClip != null && audioSource != null)
        {
            // Weak point: its own confirmation sound instead of the normal flesh hit.
            if (hitParticles != null) { hitParticles.transform.position = hitPoint; hitParticles.Play(); }
            Jitter();
            audioSource.PlayOneShot(weakPoint.hitClip, weakPoint.hitVolume);
        }
        else
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

    float MinSwingSpeed()
    {
        float minVel = minSwingSpeed;
        if (PlayerCombat.Instance != null)
            minVel = Mathf.Max(minVel, PlayerCombat.Instance.minSwingVelocity);
        return minVel;
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
        // Horizontal: the root is at the feet, so a 3-D direction to a chest-height hit point pointed mostly up and the
        // front bonus almost never fired (dot ~0.2 for a hit dead centre on the chest).
        Vector3 toHit = hitPoint - monsterRoot.position;
        toHit.y = 0f;
        if (toHit.sqrMagnitude < 1e-6f) return 1f;
        toHit.Normalize();
        Vector3 fwd = monsterRoot.forward; fwd.y = 0f; fwd.Normalize();
        float facing = Vector3.Dot(fwd, toHit); // front hits ~1
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
            bool heavy = swingSpeed >= heavySwingSpeedThreshold && (heavyHitSound != null || (varyHitSounds && heavyHitVariants.Length > 0));
            if (heavy)
                clipToPlay = Pick(heavyHitVariants, heavyHitSound);
            else
                clipToPlay = Pick(hitVariants, hitSound != null ? hitSound : heavyHitSound);

            if (clipToPlay != null)
            {
                Jitter();
                audioSource.PlayOneShot(clipToPlay);
            }
        }
        else if (soundButtonFallback != null)
            soundButtonFallback.PlayClickSound();
    }

    /// <summary>
    /// Body block (monster defending): no flesh-hit sound. Either this weapon's blockedHitSound or the monster's
    /// own block clang is heard, so a block sounds different from a normal hit and from a weapon clash.
    /// </summary>
    void PlayBlockedFeedback(Vector3 hitPoint, float swingSpeed, MonsterAI monsterAi)
    {
        if (audioSource != null && blockedHitSound != null)
        {
            Jitter();
            audioSource.PlayOneShot(blockedHitSound, blockedHitVolume);
            return;
        }

        bool monsterPlaysBlockCue = monsterAi != null && monsterAi.defendBlockClip != null && monsterAi.sfxAudioSource != null;
        if (!monsterPlaysBlockCue)
            PlayFeedback(hitPoint, swingSpeed); // no block cue anywhere: keep the hit audible
    }

    AudioClip Pick(AudioClip[] variants, AudioClip fallback)
    {
        if (!varyHitSounds || variants == null || variants.Length == 0) return fallback;
        var c = variants[UnityEngine.Random.Range(0, variants.Length)];
        return c != null ? c : fallback;
    }

    void Jitter()
    {
        if (audioSource != null)
            audioSource.pitch = varyHitSounds ? 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter) : 1f;
    }

    public Vector3 CurrentVelocity => velocity;
}
