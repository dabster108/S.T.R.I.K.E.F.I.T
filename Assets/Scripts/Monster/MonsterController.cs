// MonsterController.cs
// Purpose: Monster state, difficulty, weapon type, attack cooldown, damage/death routing.
// Dependencies: MonsterHealth, MonsterAI, MonsterAnimator, LevelManager, WeaponType
// Attach to: Monster root (Hip Hop Mixamo model, Warrok, or Cube placeholder)

using System;
using UnityEngine;

public enum MonsterAnimState
{
    Idle,
    Attack,
    Hit,
    Death
}

[RequireComponent(typeof(MonsterHealth))]
public class MonsterController : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Display name used by UI.")]
    public string monsterName = "Fitness Monster";

    [Header("Difficulty (usually set by LevelManager for this scene)")]
    public DifficultyLevel difficulty = DifficultyLevel.Easy;

    [Header("Weapon (data-driven — set per level in Inspector / LevelManager)")]
    [Tooltip("None = bare hands/claws. Sword/Hammer for armed monsters.")]
    public WeaponType weaponType = WeaponType.None;
    [Tooltip("Optional visual child to enable for this weapon.")]
    public GameObject weaponObject;
    public GameObject hammerObject;
    public GameObject swordObject;
    public GameObject shieldObject;

    [Header("Combat Stats (overridden by LevelManager when spawned)")]
    public float attackDamage = 10f;
    public float attackCooldown = 2.5f;
    public float attackRange = 2.5f;
    public float moveSpeed = 0.5f;

    [Header("Damage cap (Level 3)")]
    [Tooltip("Largest share of max HP one player hit can take after every multiplier (0.07 = 7 %). 0 = no cap.")]
    [Range(0f, 1f)] public float maxSingleHitFraction = 0f;

    [Header("Burst limit (Level 3)")]
    [Tooltip("Most damage per second the monster takes, as a fraction of max HP, averaged over burstWindow. Damage past it is " +
             "taken at burstExcessMultiplier and shown as RESISTED. This sets the fastest possible kill (about 1 / fraction " +
             "seconds); ordinary swinging never reaches it. 0 = off.")]
    [Range(0f, 0.1f)] public float maxDamagePerSecondFraction = 0f;
    public float burstWindow = 4f;
    [Range(0f, 1f)] public float burstExcessMultiplier = 0.2f;
    /// <summary>Hits cut by the burst limit (verification).</summary>
    public int BurstLimitedHits { get; private set; }

    [Header("Runtime (read-only)")]
    public MonsterAnimState currentAnimState = MonsterAnimState.Idle;
    public bool isAlive = true;
    public bool isAttacking;

    [Header("Optional Refs")]
    public Transform attackPoint;
    public AudioSource audioSource;
    public AudioClip attackSound;
    public AudioClip deathSound;

    public event Action OnMonsterDeath;
    public event Action<float> OnMonsterAttackDamage;

    private MonsterHealth health;
    private MonsterAI ai;
    private MonsterAnimator monsterAnimator;
    private MonsterDefense defense;
    private BossPhaseController phases;
    private MonsterCosmetics cosmetics;
    private float lastAttackTime = -999f;

    /// <summary>Damage the last player hit dealt after every reduction / bonus / cap (verification).</summary>
    public float LastDamageTaken { get; private set; }
    /// <summary>True if the last hit was cut down by maxSingleHitFraction.</summary>
    public bool LastHitCapped { get; private set; }
    public int CappedHits { get; private set; }

    void Awake()
    {
        health = GetComponent<MonsterHealth>();
        ai = GetComponent<MonsterAI>();
        colors = GetComponent<MonsterColorCompositor>();
        monsterAnimator = GetComponent<MonsterAnimator>();
        defense = GetComponent<MonsterDefense>();
        phases = GetComponent<BossPhaseController>();
        cosmetics = GetComponent<MonsterCosmetics>();

        if (attackPoint == null)
            attackPoint = transform;
    }

    void Start()
    {
        ApplyDifficultyDefaultsIfNeeded();
        ApplyWeaponVisuals();
        SetAnimState(MonsterAnimState.Idle);
    }

    public void ApplyDifficultyDefaultsIfNeeded()
    {
        if (difficulty == DifficultyLevel.Easy && attackDamage == 10f && attackCooldown == 2.5f)
            Configure(LevelManager.GetParams(difficulty));
    }

    /// <summary>
    /// Called by LevelManager / BattleManager after spawn.
    /// </summary>
    public void Configure(DifficultyParams p)
    {
        difficulty = p.level;
        attackDamage = p.monsterDamage;
        attackCooldown = p.monsterAttackCooldown;
        attackRange = p.monsterAttackRange;
        moveSpeed = p.monsterMoveSpeed;
        weaponType = p.monsterWeapon;

        if (health != null)
            health.SetMaxHealth(p.monsterMaxHealth, true);

        if (ai != null)
            ai.ApplyDifficulty(p);

        ApplyWeaponVisuals();
    }

    /// <summary>
    /// Inspector / LevelManager can call this to swap monster weapon without code changes.
    /// </summary>
    public void SetWeapon(WeaponType type)
    {
        weaponType = type;
        ApplyWeaponVisuals();
    }

    public void ApplyWeaponVisuals()
    {
        if (hammerObject != null)
            hammerObject.SetActive(weaponType == WeaponType.Hammer);
        if (swordObject != null)
            swordObject.SetActive(weaponType == WeaponType.Sword);
        if (shieldObject != null)
            shieldObject.SetActive(weaponType == WeaponType.Shield);

        if (weaponObject != null)
            weaponObject.SetActive(weaponType != WeaponType.None);

        // Mild damage flavor by weapon (stats still come from DifficultyParams)
        // Unarmed Mixamo Hip Hop = None (claws/fists flavor)
    }

    /// <summary>Seconds until CanAttack() would be true on cooldown alone (read-only; MonsterCosmetics uses it to keep flourishes out of a due attack).</summary>
    public float SecondsUntilAttackReady => Mathf.Max(0f, lastAttackTime + attackCooldown - Time.time);

    /// <summary>MonsterTactics: an attack started <paramref name="seconds"/> before the cooldown was over; the next one waits that much
    /// longer, so counters and punishes do not raise the attack frequency.</summary>
    public void DeferNextAttack(float seconds)
    {
        if (seconds > 0f) lastAttackTime += seconds;
    }

    public bool CanAttack()
    {
        return isAlive && !isAttacking && Time.time >= lastAttackTime + attackCooldown;
    }

    public void BeginAttack()
    {
        if (!CanAttack()) return;

        isAttacking = true;
        lastAttackTime = Time.time;
        SetAnimState(MonsterAnimState.Attack);

        if (audioSource != null && attackSound != null)
        {
            audioSource.PlayOneShot(attackSound);
            SfxTrace.Note(this, attackSound, "monster attack (BeginAttack)");
        }
    }

    /// <summary>
    /// Same as BeginAttack, but allows selecting an attack animation variant trigger.
    /// Falls back to default Attack trigger if the variant trigger is not present.
    /// </summary>
    public void BeginAttackVariant(MonsterAI.AttackPattern pattern)
    {
        if (!CanAttack()) return;

        isAttacking = true;
        lastAttackTime = Time.time;
        currentAnimState = MonsterAnimState.Attack;

        if (monsterAnimator != null)
            monsterAnimator.PlayAttackVariant(pattern);
        else
            SetAnimState(MonsterAnimState.Attack);
    }

    /// <summary>
    /// Attack-table path (Level 2+): start an attack by explicit Animator trigger name.
    /// Returns false (and does nothing) if the monster cannot attack right now.
    /// </summary>
    public bool BeginAttackTrigger(string trigger) => BeginAttackTrigger(trigger, false);

    /// <param name="ignoreCooldown">Counter-attack after a guard: allowed even inside the attack cooldown.</param>
    public bool BeginAttackTrigger(string trigger, bool ignoreCooldown)
    {
        if (ignoreCooldown ? (!isAlive || isAttacking) : !CanAttack()) return false;

        isAttacking = true;
        lastAttackTime = Time.time;
        currentAnimState = MonsterAnimState.Attack;

        if (monsterAnimator != null)
            monsterAnimator.PlayAttackTrigger(trigger);
        else
            SetAnimState(MonsterAnimState.Attack);
        return true;
    }

    public void PerformAttackHit()
    {
        PerformAttackHit(1f);
    }

    public void PerformAttackHit(float damageMultiplier)
    {
        PerformAttackHit(damageMultiplier, false);
    }

    /// <param name="unblockable">Dodge-only attack: PlayerHealth skips every block reduction.</param>
    public void PerformAttackHit(float damageMultiplier, bool unblockable)
    {
        if (!isAlive) return;

        float dmg = attackDamage;
        // Weapon flavor multiplier
        dmg *= weaponType switch
        {
            WeaponType.Hammer => 1.2f,
            WeaponType.Sword => 1.1f,
            WeaponType.Shield => 0.7f,
            WeaponType.Flail => 1.25f,
            WeaponType.DualAxe => 1.25f, // Level 3 boss: two axes
            _ => 1.0f // None / claws
        };
        dmg *= Mathf.Max(0.1f, damageMultiplier);

        OnMonsterAttackDamage?.Invoke(dmg);
        if (colors != null) colors.Strike(ai != null && ai.CurrentAttack != null ? ai.CurrentAttack.telegraphColor : (ai != null ? ai.telegraphColor : Color.red));

        // Route through PlayerHealth — which checks PlayerCombat.IsBlocking for shield block.
        var playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (playerHealth != null)
            playerHealth.TakeDamage(dmg, unblockable);
    }

    public void EndAttack()
    {
        isAttacking = false;
        if (isAlive)
            SetAnimState(MonsterAnimState.Idle);
    }

    // Colour writer: the hit flash (tier of the swing) and the weapon strike flash go through it.
    private MonsterColorCompositor colors;

    public void OnTakeDamage(float amount, Vector3 hitPoint)
    {
        OnTakeDamage(amount, hitPoint, float.PositiveInfinity, false); // unknown swing (debug key): treated as full
    }

    /// <param name="swingSpeed">Player swing speed (m/s): picks the poise tier and the damage-number style.</param>
    /// <param name="weakPoint">The hit landed on a MonsterWeakPoint.</param>
    public void OnTakeDamage(float amount, Vector3 hitPoint, float swingSpeed, bool weakPoint)
    {
        if (!isAlive || health == null) return;

        var style = weakPoint ? CombatUIController.DamageStyle.Weak : CombatUIController.StyleForSpeed(swingSpeed);
        bool guarding = ai != null && ai.IsDefending;
        bool isDefending = WouldBlock(hitPoint);

        if (isDefending)
        {
            // Damage reduction while defending (MonsterAI.defendDamageMultiplier, default 0.3 = 70% reduction)
            float unguarded = amount;
            amount *= ai.defendDamageMultiplier;
            bool zoned = defense != null && defense.zoneGuard;
            ai.NotifyDefendedHit(hitPoint, !zoned);
            if (zoned) defense.PlayBlockClang(ai.defendBlockClip, ai.defendBlockVolume);
            if (defense != null) defense.OnGuardAbsorbedHit(amount, unguarded - amount); // fills the guard meter (weapon parries never get here)
            style = CombatUIController.DamageStyle.Blocked;
            if (ai.verboseAttackLogs)
                DevLog.Log($"[Monster] Blocked! Reduced damage to {amount:0.#}");
        }
        else if (guarding && defense != null)
        {
            // Guard up, but the hit went where it does not cover: full damage plus the opening bonus.
            float bonus = amount * defense.openingBonus;
            amount += bonus;
            defense.OnOpeningHit(amount, bonus, hitPoint);
            if (CombatUIController.Instance != null)
                CombatUIController.Instance.ShowCallout(hitPoint + Vector3.up * 0.25f, "OPENING", CombatUIController.DamageStyle.Opening);
        }
        else
        {
            if (defense != null) defense.OnUnblockedHit();   // ends a run of blocks
            if (defense != null && defense.IsStaggered)
                amount *= defense.staggerDamageMultiplier; // guard broken: the opening pays more
        }

        if (phases != null && phases.InPhaseShift)
        {
            // Phase-shift roar (Level 3): hits are resisted, so a phase cannot be burst through.
            amount *= phases.phaseShiftDamageMultiplier;
            phases.NotifyResistedHit();
            style = CombatUIController.DamageStyle.Resisted;
        }

        if (maxDamagePerSecondFraction > 0f && health.maxHealth > 0f)
        {
            float allowed = maxDamagePerSecondFraction * health.maxHealth * burstWindow - RecentDamage(burstWindow);
            if (amount > allowed)
            {
                amount = Mathf.Max(0f, allowed) + (amount - Mathf.Max(0f, allowed)) * burstExcessMultiplier;
                style = CombatUIController.DamageStyle.Resisted;
                BurstLimitedHits++;
            }
        }

        LastHitCapped = false;
        if (maxSingleHitFraction > 0f)
        {
            float cap = health.maxHealth * maxSingleHitFraction;
            if (amount > cap) { amount = cap; LastHitCapped = true; CappedHits++; }
        }
        LastDamageTaken = amount;
        RecordDamage(amount);

        if (colors != null) colors.NotifyDamage(style);   // hit flash in the colour of the swing tier (visual only)
        health.TakeDamage(amount, hitPoint, style);

        if (health.IsDead)
            OnDeath();
        // No hit-flinch while attacking: Any State -> Hit would cancel the Slash/Heavy/Special clip mid-swing
        // (damage still applies above). Flinch only from Idle, never during a guard / stagger / dodge, and only for
        // swings strong enough for this monster's poise (MonsterDefense.flinchMinTier).
        else if (!isDefending && !isAttacking && !(ai != null && ai.IsBusy) && (defense == null || defense.TryFlinch(swingSpeed)))
        {
            if (cosmetics != null) cosmetics.PrepareHit(hitPoint, swingSpeed);
            SetAnimState(MonsterAnimState.Hit);
            CancelInvoke(nameof(ReturnToIdleIfAlive));
            Invoke(nameof(ReturnToIdleIfAlive), 0.35f);
        }
    }

    /// <summary>The one block-or-not decision for a body hit: the monster is guarding (MonsterAI.IsDefending) and, with the zone
    /// guard, the raised guard covers the height the hit landed at. HitDetection asks the same question for its feedback.</summary>
    public bool WouldBlock(Vector3 hitPoint)
    {
        if (ai == null || !ai.IsDefending) return false;
        return defense == null || defense.Covers(hitPoint);
    }

    // Damage history for the burst limit: a small ring buffer, no allocation.
    private readonly float[] dmgTime = new float[64];
    private readonly float[] dmgAmount = new float[64];
    private int dmgHead;

    void RecordDamage(float amount)
    {
        dmgTime[dmgHead] = Time.time; dmgAmount[dmgHead] = amount;
        dmgHead = (dmgHead + 1) % dmgTime.Length;
    }

    float RecentDamage(float window)
    {
        float sum = 0f, since = Time.time - window;
        for (int i = 0; i < dmgTime.Length; i++) if (dmgAmount[i] > 0f && dmgTime[i] >= since) sum += dmgAmount[i];
        return sum;
    }

    void ReturnToIdleIfAlive()
    {
        // A defend that began during the flinch owns the Animator: sending Idle now would drop the guard pose
        // while the damage reduction is still active.
        if (isAlive && !isAttacking && !(ai != null && ai.IsBusy))
            SetAnimState(MonsterAnimState.Idle);
    }

    public void OnDeath()
    {
        if (!isAlive) return;

        isAlive = false;
        isAttacking = false;
        SetAnimState(MonsterAnimState.Death);

        if (audioSource != null && deathSound != null)
        {
            audioSource.PlayOneShot(deathSound);
            SfxTrace.Note(this, deathSound, "monster death");
        }

        if (ai != null)
            ai.enabled = false;

        foreach (var col in GetComponentsInChildren<Collider>())
            col.enabled = false;

        OnMonsterDeath?.Invoke();

        if (BattleManager.Instance != null)
            BattleManager.Instance.NotifyMonsterDefeated(this);
    }

    public void SetAnimState(MonsterAnimState state)
    {
        currentAnimState = state;
        if (monsterAnimator != null)
            monsterAnimator.PlayState(state);
    }

#if UNITY_EDITOR
    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current == null) return;
        if (UnityEngine.InputSystem.Keyboard.current.kKey.wasPressedThisFrame)
            OnTakeDamage(25f, transform.position + Vector3.up);
    }
#endif
}
