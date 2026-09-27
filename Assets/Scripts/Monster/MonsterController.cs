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
    private float lastAttackTime = -999f;

    void Awake()
    {
        health = GetComponent<MonsterHealth>();
        ai = GetComponent<MonsterAI>();
        monsterAnimator = GetComponent<MonsterAnimator>();

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
            audioSource.PlayOneShot(attackSound);
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
    public bool BeginAttackTrigger(string trigger)
    {
        if (!CanAttack()) return false;

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
        if (!isAlive) return;

        float dmg = attackDamage;
        // Weapon flavor multiplier
        dmg *= weaponType switch
        {
            WeaponType.Hammer => 1.2f,
            WeaponType.Sword => 1.1f,
            WeaponType.Shield => 0.7f,
            _ => 1.0f // None / claws
        };
        dmg *= Mathf.Max(0.1f, damageMultiplier);

        OnMonsterAttackDamage?.Invoke(dmg);

        // Route through PlayerHealth — which checks PlayerCombat.IsBlocking for shield block.
        var playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (playerHealth != null)
            playerHealth.TakeDamage(dmg);
    }

    public void EndAttack()
    {
        isAttacking = false;
        if (isAlive)
            SetAnimState(MonsterAnimState.Idle);
    }

    public void OnTakeDamage(float amount, Vector3 hitPoint)
    {
        if (!isAlive || health == null) return;

        bool isDefending = false;
        if (ai != null) isDefending = ai.IsDefending;

        if (isDefending)
        {
            // Damage reduction while defending (MonsterAI.defendDamageMultiplier, default 0.3 = 70% reduction)
            amount *= ai.defendDamageMultiplier;
            ai.NotifyDefendedHit(hitPoint);
            Debug.Log($"[Monster] Blocked! Reduced damage to {amount:0.#}");
        }

        health.TakeDamage(amount, hitPoint);

        if (health.IsDead)
            OnDeath();
        // No hit-flinch while attacking: Any State -> Hit would cancel the Slash/Heavy/Special clip mid-swing
        // (damage still applies above). Flinch only from Idle.
        else if (!isDefending && !isAttacking)
        {
            SetAnimState(MonsterAnimState.Hit);
            CancelInvoke(nameof(ReturnToIdleIfAlive));
            Invoke(nameof(ReturnToIdleIfAlive), 0.35f);
        }
    }

    void ReturnToIdleIfAlive()
    {
        if (isAlive && !isAttacking)
            SetAnimState(MonsterAnimState.Idle);
    }

    public void OnDeath()
    {
        if (!isAlive) return;

        isAlive = false;
        isAttacking = false;
        SetAnimState(MonsterAnimState.Death);

        if (audioSource != null && deathSound != null)
            audioSource.PlayOneShot(deathSound);

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
