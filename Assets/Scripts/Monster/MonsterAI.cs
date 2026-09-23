// MonsterAI.cs
// Purpose: Attack patterns with TELEGRAPH wind-up + cooldown, player targeting, distance checks.
//          Monster attacking back is what turns this from a "hit a target" drill into
//          fitness gamification — player must react (dodge/block/reposition), adding real movement.
// Dependencies: MonsterController, PlayerHealth / Camera / XR rig transform, PlayerCombat (block check)
// Attach to: Monster root (with MonsterController)

using System.Collections;
using UnityEngine;

public class MonsterAI : MonoBehaviour
{
    public enum AttackPattern
    {
        Slash = 0,
        Heavy = 1
    }

    [Header("Target")]
    public Transform playerTarget;
    public string playerTag = "Player";

    [Header("Behavior")]
    public float thinkInterval = 0.2f;
    public float stopDistance = 1.6f;
    public bool facePlayer = true;
    public bool canMove = true;

    [Header("Attack Timing — Telegraph Window")]
    [Tooltip("Delay after BeginAttack (wind-up anim) BEFORE damage lands. " +
             "This is the player's reaction/block window. Level 1: long & telegraphed.")]
    public float attackHitDelay = 0.9f;
    [Tooltip("Recovery delay after the hit frame before monster can act again.")]
    public float attackRecoverDelay = 0.6f;
    [Tooltip("Cooldown between attacks (Level 1: ~3-4 sec, slow & telegraphed). " +
             "MonsterController.attackCooldown is the authoritative value; this is a floor.")]
    public float minAttackCooldown = 3.0f;

    [Header("Attack Variety")]
    [Tooltip("If true, randomly picks Slash or Heavy each cycle (avoids repeating the same type twice).")]
    public bool randomizeAttackPattern = true;
    [Tooltip("Slash attack: time between telegraph start and hit frame.")]
    public float slashHitDelay = 0.9f;
    [Tooltip("Slash attack: recovery after hit.")]
    public float slashRecoverDelay = 0.6f;
    [Tooltip("Slash attack damage multiplier.")]
    public float slashDamageMultiplier = 1.0f;
    [Tooltip("Heavy attack: longer wind-up before hit frame.")]
    public float heavyHitDelay = 1.25f;
    [Tooltip("Heavy attack: longer recovery after hit.")]
    public float heavyRecoverDelay = 0.85f;
    [Tooltip("Heavy attack damage multiplier.")]
    public float heavyDamageMultiplier = 1.2f;

    [Header("Telegraph VFX (optional)")]
    [Tooltip("Optional renderer that flashes red during wind-up so the player sees the attack coming.")]
    public Renderer telegraphRenderer;
    public Color telegraphColor = new Color(1f, 0.2f, 0.2f);
    public Color heavyTelegraphColor = new Color(1f, 0.1f, 0.1f);

    [Header("Attack Audio (optional)")]
    public AudioSource sfxAudioSource;
    [Tooltip("Plays when telegraph starts (wind-up cue).")]
    public AudioClip telegraphCueClip;
    [Tooltip("Plays near the hit frame (sword swing/slice).")]
    public AudioClip attackSwingClip;
    [Range(0f, 1f)] public float telegraphCueVolume = 0.9f;
    [Range(0f, 1f)] public float attackSwingVolume = 1f;

    [Header("Aggression (set via ApplyDifficulty)")]
    [Range(0.5f, 3f)] public float aggression = 1f;

    private MonsterController controller;
    private float nextThinkTime;
    private bool busy;
    private MaterialPropertyBlock mpb;
    private Color baseColor = Color.white;
    private AttackPattern lastPattern = AttackPattern.Heavy;

    void Awake()
    {
        controller = GetComponent<MonsterController>();
        mpb = new MaterialPropertyBlock();
        if (sfxAudioSource == null)
            sfxAudioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        ResolvePlayerTarget();
    }

    public void ApplyDifficulty(DifficultyParams p)
    {
        aggression = p.aggression;
        canMove = p.monsterMoveSpeed > 0.05f;
        // Level 1: keep wind-up long & telegraphed (don't shrink much with aggression on Easy)
        attackHitDelay = Mathf.Max(0.5f, 0.9f - (aggression - 1f) * 0.08f);
    }

    void ResolvePlayerTarget()
    {
        if (playerTarget != null) return;

        if (PlayerHealth.Instance != null)
        {
            playerTarget = PlayerHealth.Instance.transform;
            return;
        }

        var tagged = GameObject.FindGameObjectWithTag(playerTag);
        if (tagged != null)
        {
            playerTarget = tagged.transform;
            return;
        }

        if (Camera.main != null)
            playerTarget = Camera.main.transform;
    }

    void Update()
    {
        if (controller == null || !controller.isAlive || busy) return;
        if (Time.time < nextThinkTime) return;
        nextThinkTime = Time.time + thinkInterval;

        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive)
            return;

        ResolvePlayerTarget();
        if (playerTarget == null) return;

        float dist = Vector3.Distance(transform.position, playerTarget.position);

        if (facePlayer)
            FaceTarget();

        if (dist <= controller.attackRange)
        {
            if (controller.CanAttack())
                StartCoroutine(AttackRoutine(ChooseAttackPattern()));
        }
        else if (canMove)
        {
            MoveToward(playerTarget.position, dist);
        }
    }

    void FaceTarget()
    {
        Vector3 dir = playerTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        Quaternion look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 5f * aggression);
    }

    void MoveToward(Vector3 destination, float dist)
    {
        if (dist <= stopDistance) return;
        Vector3 dir = (destination - transform.position);
        dir.y = 0f;
        transform.position += dir.normalized * controller.moveSpeed * aggression * Time.deltaTime;
    }

    void LateUpdate()
    {
        if (controller == null || !controller.isAlive || busy || !canMove) return;
        if (playerTarget == null) return;
        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive) return;

        float dist = Vector3.Distance(transform.position, playerTarget.position);
        if (dist > controller.attackRange && dist > stopDistance)
        {
            Vector3 dir = playerTarget.position - transform.position;
            dir.y = 0f;
            transform.position += dir.normalized * controller.moveSpeed * aggression * Time.deltaTime;
        }
    }

    /// <summary>
    /// Telegraphed attack: wind-up (telegraph) → hit frame → recovery.
    /// During wind-up the player can raise the shield to block. On the hit frame,
    /// damage is routed through PlayerHealth which checks PlayerCombat.IsBlocking.
    /// </summary>
    AttackPattern ChooseAttackPattern()
    {
        if (!randomizeAttackPattern)
        {
            lastPattern = lastPattern == AttackPattern.Slash ? AttackPattern.Heavy : AttackPattern.Slash;
            return lastPattern;
        }

        // Random with no immediate repeat for better variation feel
        AttackPattern next = Random.value < 0.5f ? AttackPattern.Slash : AttackPattern.Heavy;
        if (next == lastPattern)
            next = next == AttackPattern.Slash ? AttackPattern.Heavy : AttackPattern.Slash;
        lastPattern = next;
        return next;
    }

    IEnumerator AttackRoutine(AttackPattern pattern)
    {
        busy = true;
        controller.attackCooldown = Mathf.Max(controller.attackCooldown, minAttackCooldown);

        // Variant index maps to MonsterAnimator attackTrigger / attackAltTrigger.
        controller.BeginAttackVariant(pattern == AttackPattern.Heavy ? 1 : 0);

        float baseHitDelay = pattern == AttackPattern.Heavy ? heavyHitDelay : slashHitDelay;
        float baseRecover = pattern == AttackPattern.Heavy ? heavyRecoverDelay : slashRecoverDelay;
        float damageMultiplier = pattern == AttackPattern.Heavy ? heavyDamageMultiplier : slashDamageMultiplier;

        // --- Telegraph: visual + audio cue at wind-up start ---
        if (telegraphCueClip != null && sfxAudioSource != null)
            sfxAudioSource.PlayOneShot(telegraphCueClip, telegraphCueVolume);
        float windUp = baseHitDelay / Mathf.Max(0.5f, aggression);
        Color cueColor = pattern == AttackPattern.Heavy ? heavyTelegraphColor : telegraphColor;
        StartCoroutine(TelegraphFlash(windUp, cueColor));

        yield return new WaitForSeconds(windUp);

        // Attack swing SFX at the hit window.
        if (attackSwingClip != null && sfxAudioSource != null)
            sfxAudioSource.PlayOneShot(attackSwingClip, attackSwingVolume);

        // --- Hit frame: apply damage if player still in range and didn't move away ---
        if (controller != null && controller.isAlive && playerTarget != null)
        {
            float dist = Vector3.Distance(transform.position, playerTarget.position);
            if (dist <= controller.attackRange * 1.15f)
            {
                controller.PerformAttackHit(damageMultiplier); // routes through PlayerHealth
            }
        }

        yield return new WaitForSeconds(baseRecover / Mathf.Max(0.5f, aggression));

        if (controller != null && controller.isAlive)
            controller.EndAttack();

        busy = false;
    }

    IEnumerator TelegraphFlash(float duration, Color cueColor)
    {
        if (telegraphRenderer == null) yield break;
        telegraphRenderer.GetPropertyBlock(mpb);
        baseColor = mpb.GetColor("_BaseColor");
        if (baseColor == Color.clear) baseColor = Color.white;
        mpb.SetColor("_BaseColor", cueColor);
        mpb.SetColor("_Color", cueColor);
        telegraphRenderer.SetPropertyBlock(mpb);
        yield return new WaitForSeconds(duration);
        mpb.SetColor("_BaseColor", baseColor);
        mpb.SetColor("_Color", baseColor);
        telegraphRenderer.SetPropertyBlock(mpb);
    }
}
