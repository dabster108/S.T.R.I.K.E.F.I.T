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
        Heavy = 1,
        Special = 2
    }

    public enum AttackCategory
    {
        Standard = 0,
        Heavy = 1,
        IdleSlam = 2,
        Special = 3,
        Combo = 4
    }

    /// <summary>
    /// One row of the Level 2+ attack table. Timings are real seconds and must match the Animator state
    /// speed/offset of <see cref="animatorState"/> so the damage frame lands on the visible impact.
    /// </summary>
    [System.Serializable]
    public class AttackDefinition
    {
        public string name = "Attack";
        public AttackCategory category = AttackCategory.Standard;
        [Tooltip("Animator trigger that starts this attack (needs a real Idle -> state transition).")]
        public string animatorTrigger;
        [Tooltip("Animator state the trigger must reach (verification/debug).")]
        public string animatorState;
        [Tooltip("Seconds from trigger to the first hit frame — the player's reaction window.")]
        public float windUp = 0.6f;
        [Tooltip("Combo follow-up hit frames, in seconds after the first hit.")]
        public float[] followUpHits = new float[0];
        [Tooltip("Seconds after the last hit before the monster is free again.")]
        public float recover = 0.45f;
        public float damageMultiplier = 1f;
        [Tooltip("Half-angle (deg) of the swing arc. A player outside it at the hit frame dodges.")]
        public float hitArc = 45f;
        [Tooltip("Selection weight at full monster HP.")]
        public float weightFullHp = 10f;
        [Tooltip("Selection weight at 0 monster HP (lerped by damage taken).")]
        public float weightLowHp = 10f;
        [Tooltip("Only selectable at or below this monster HP fraction (1 = always).")]
        [Range(0f, 1f)] public float maxHpFraction = 1f;
        [Tooltip("Only selectable after standing still in Idle for this many seconds (0 = any time).")]
        public float requiresIdleFor = 0f;
        [Tooltip("Minimum seconds between two uses of this attack.")]
        public float cooldown = 0f;
        public Color telegraphColor = new Color(1f, 0.2f, 0.2f);
        public AudioClip windUpClip;
        [Range(0f, 1f)] public float windUpVolume = 0.8f;
        [Tooltip("Whoosh played just before each hit frame.")]
        public AudioClip swingClip;
        [Range(0f, 1f)] public float swingVolume = 0.9f;
        [Tooltip("Played on each hit frame whether or not the player was hit (ground impact etc.).")]
        public AudioClip impactClip;
        [Range(0f, 1f)] public float impactVolume = 1f;
        [Tooltip("Ignite the weapon fire VFX during the wind-up and burst on impact.")]
        public bool fireVfx;

        [System.NonSerialized] public float lastUsedTime = -999f;
    }

    [Header("Target")]
    public Transform playerTarget;
    public string playerTag = "Player";
    [Tooltip("Level 2+: track the headset (CenterEyeAnchor) and measure horizontal distance, so the player " +
             "can physically step out of range / out of the swing arc. Off = legacy rig-root 3D distance.")]
    public bool useHeadTarget = false;

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
    [Tooltip("Special attack: very long wind-up.")]
    public float specialHitDelay = 1.5f;
    [Tooltip("Special attack: recovery after hit.")]
    public float specialRecoverDelay = 1.0f;
    [Tooltip("Special attack damage multiplier.")]
    public float specialDamageMultiplier = 1.5f;

    [Header("Attack Table (Level 2+; empty = legacy Slash/Heavy/Special tiers)")]
    public System.Collections.Generic.List<AttackDefinition> attackTable = new System.Collections.Generic.List<AttackDefinition>();
    [Tooltip("Force a Heavy/IdleSlam/Special if none was chosen in this many cycles.")]
    public int heavyPityCycles = 4;
    [Tooltip("The first time monster HP drops to this fraction, the Special attack is forced (desperation).")]
    [Range(0f, 1f)] public float desperationHpFraction = 0.4f;
    [Tooltip("Attack cooldown multiplier at 0 HP (lerped by damage taken). <1 = faster when hurt.")]
    [Range(0.5f, 1f)] public float lowHpCooldownScale = 0.85f;
    [Tooltip("Swing whoosh plays this many seconds before each hit frame.")]
    public float swingSfxLead = 0.12f;

    [Header("Weapon FX (attack table, optional)")]
    public ParticleSystem weaponFireVfx;
    public ParticleSystem impactBurstVfx;
    [Tooltip("Where impact bursts spawn, relative to the monster (forward/up metres).")]
    public Vector2 impactBurstOffset = new Vector2(1.2f, 0.05f);
    public Light weaponFireLight;
    public float weaponFireLightIntensity = 2.5f;

    [Header("Defense")]
    public bool canDefend = true;
    public float defendCooldown = 8.0f;
    public float defendDuration = 1.5f;
    [Tooltip("Incoming damage multiplier while defending (0.3 = 70% reduction).")]
    [Range(0f, 1f)] public float defendDamageMultiplier = 0.3f;
    [Tooltip("Tint telegraphRenderer while defending so the window is readable.")]
    public bool defendTintEnabled = false;
    public Color defendTintColor = new Color(0.45f, 0.65f, 1f);
    [Tooltip("Brace cue when the defend window opens.")]
    public AudioClip defendBraceClip;
    [Range(0f, 1f)] public float defendBraceVolume = 0.8f;
    [Tooltip("Block clang when a hit lands during the defend window.")]
    public AudioClip defendBlockClip;
    [Range(0f, 1f)] public float defendBlockVolume = 0.9f;
    private float nextDefendTime = 0f;
    public bool IsDefending { get; private set; }

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

    [Header("Debug")]
    [Tooltip("When true, logs each chosen attack tier to the Console. Keep OFF for builds.")]
    public bool verboseAttackLogs = false;

    private MonsterController controller;
    private float nextThinkTime;
    private bool busy;
    private MaterialPropertyBlock mpb;
    private Color baseColor = Color.white;
    private int attackCycleCount;

    // Attack-table runtime
    private float idleSince;
    private int cyclesSinceHeavy;
    private bool desperationUsed;
    private float baseAttackCooldown = -1f;
    private AttackDefinition lastTableAttack;

    /// <summary>Last attack chosen from the table (verification / debug).</summary>
    public AttackDefinition LastChosenAttack { get; private set; }
    /// <summary>True if the last resolved hit frame damaged the player.</summary>
    public bool LastHitLanded { get; private set; }
    public int TableAttackCount { get; private set; }
    public bool UsesAttackTable => attackTable != null && attackTable.Count > 0;

    void Awake()
    {
        controller = GetComponent<MonsterController>();
        mpb = new MaterialPropertyBlock();
        if (sfxAudioSource == null)
            sfxAudioSource = GetComponent<AudioSource>();
        idleSince = Time.time;
        if (weaponFireLight != null) weaponFireLight.intensity = 0f;
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

        if (useHeadTarget && PlayerHealth.Instance != null)
        {
            var rig = PlayerHealth.Instance.GetComponent<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                playerTarget = rig.centerEyeAnchor;
                return;
            }
        }

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

        float dist = DistanceToTarget();

        if (facePlayer)
            FaceTarget();

        if (dist <= controller.attackRange)
        {
            if (canDefend && Time.time >= nextDefendTime && !controller.isAttacking)
            {
                StartCoroutine(DefendRoutine());
            }
            else if (UsesAttackTable)
            {
                UpdateTableCooldown();
                if (controller.CanAttack())
                {
                    var chosen = ChooseFromTable();
                    if (chosen != null)
                        StartCoroutine(TableAttackRoutine(chosen));
                }
            }
            else if (controller.CanAttack())
            {
                StartCoroutine(AttackRoutine(ChooseAttackPattern()));
            }
        }
        else if (canMove)
        {
            MoveToward(playerTarget.position, dist);
        }
    }

    /// <summary>Legacy: 3D distance to rig root. Head-target mode: horizontal distance to the headset.</summary>
    public float DistanceToTarget()
    {
        if (playerTarget == null) return float.MaxValue;
        if (!useHeadTarget) return Vector3.Distance(transform.position, playerTarget.position);
        Vector3 d = playerTarget.position - transform.position;
        d.y = 0f;
        return d.magnitude;
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
        idleSince = Time.time;
    }

    void LateUpdate()
    {
        if (controller == null || !controller.isAlive || busy || !canMove) return;
        if (playerTarget == null) return;
        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive) return;

        float dist = DistanceToTarget();
        if (dist > controller.attackRange && dist > stopDistance)
        {
            Vector3 dir = playerTarget.position - transform.position;
            dir.y = 0f;
            transform.position += dir.normalized * controller.moveSpeed * aggression * Time.deltaTime;
            idleSince = Time.time; // walking is not standing still (IdleSlam requirement)
        }
    }

    /// <summary>
    /// Telegraphed attack: wind-up (telegraph) → hit frame → recovery.
    /// During wind-up the player can raise the shield to block. On the hit frame,
    /// damage is routed through PlayerHealth which checks PlayerCombat.IsBlocking.
    /// </summary>
    AttackPattern ChooseAttackPattern()
    {
        if (!randomizeAttackPattern) return AttackPattern.Slash;

        attackCycleCount++;
        float hpPercent = 1f;
        var health = GetComponent<MonsterHealth>();
        if (health != null && health.maxHealth > 0)
            hpPercent = health.currentHealth / health.maxHealth;

        AttackPattern chosen;
        // Final phase: deterministic special cadence to guarantee variety.
        if (hpPercent <= 0.25f)
        {
            if (attackCycleCount % 4 == 0) chosen = AttackPattern.Special;
            else if (attackCycleCount % 2 == 0) chosen = AttackPattern.Heavy;
            else chosen = AttackPattern.Slash;
        }
        // Mid phase: heavy every 3rd cycle.
        else if (hpPercent <= 0.5f)
        {
            if (attackCycleCount % 5 == 0) chosen = AttackPattern.Special;
            else chosen = attackCycleCount % 3 == 0 ? AttackPattern.Heavy : AttackPattern.Slash;
        }
        else
        {
            // Normal phase: Heavy every 3rd, Special every 6th so variety is obvious in short fights.
            if (attackCycleCount % 6 == 0) chosen = AttackPattern.Special;
            else if (attackCycleCount % 3 == 0) chosen = AttackPattern.Heavy;
            else chosen = AttackPattern.Slash;
        }

        if (verboseAttackLogs)
            Debug.Log($"[MonsterAI] ChooseAttackPattern cycle={attackCycleCount} hp%={hpPercent:0.00} => {chosen}");
        return chosen;
    }

    IEnumerator DefendRoutine()
    {
        busy = true;
        IsDefending = true;
        nextDefendTime = Time.time + defendCooldown;
        if (verboseAttackLogs)
            Debug.Log("[MonsterAI] Enter Defend");

        var anim = GetComponent<MonsterAnimator>();
        if (anim != null) anim.PlayDefend();

        if (defendBraceClip != null && sfxAudioSource != null)
            sfxAudioSource.PlayOneShot(defendBraceClip, defendBraceVolume);
        if (defendTintEnabled)
            SetBodyTint(defendTintColor);

        yield return new WaitForSeconds(defendDuration);

        IsDefending = false;
        busy = false;
        idleSince = Time.time;
        if (defendTintEnabled)
            ClearBodyTint();
        if (verboseAttackLogs)
            Debug.Log("[MonsterAI] Exit Defend");
        if (anim != null) anim.PlayState(MonsterAnimState.Idle);
    }

    /// <summary>Called by MonsterController when a player hit lands inside the defend window.</summary>
    public void NotifyDefendedHit(Vector3 hitPoint)
    {
        if (defendBlockClip != null && sfxAudioSource != null)
            sfxAudioSource.PlayOneShot(defendBlockClip, defendBlockVolume);
        if (defendTintEnabled)
            StartCoroutine(BlockFlash());
    }

    IEnumerator BlockFlash()
    {
        SetBodyTint(Color.white);
        yield return new WaitForSeconds(0.06f);
        if (IsDefending) SetBodyTint(defendTintColor);
    }

    void SetBodyTint(Color c)
    {
        if (telegraphRenderer == null) return;
        telegraphRenderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_Color", c);
        telegraphRenderer.SetPropertyBlock(mpb);
    }

    void ClearBodyTint()
    {
        SetBodyTint(Color.white);
    }

    // ───────────────────────── Attack table (Level 2+) ─────────────────────────

    void UpdateTableCooldown()
    {
        if (baseAttackCooldown < 0f) baseAttackCooldown = controller.attackCooldown;
        float hurt = 1f - HpFraction();
        controller.attackCooldown = baseAttackCooldown * Mathf.Lerp(1f, lowHpCooldownScale, hurt);
    }

    float HpFraction()
    {
        var health = GetComponent<MonsterHealth>();
        if (health == null || health.maxHealth <= 0f) return 1f;
        return Mathf.Clamp01(health.currentHealth / health.maxHealth);
    }

    static bool IsBigHit(AttackCategory c)
    {
        return c == AttackCategory.Heavy || c == AttackCategory.IdleSlam || c == AttackCategory.Special;
    }

    bool IsEligible(AttackDefinition a, float hp, float stillFor, bool ignoreCooldown)
    {
        if (a == null || string.IsNullOrEmpty(a.animatorTrigger)) return false;
        if (hp > a.maxHpFraction) return false;
        if (!ignoreCooldown && Time.time - a.lastUsedTime < a.cooldown) return false;
        if (a.requiresIdleFor > 0f && stillFor < a.requiresIdleFor) return false;
        return true;
    }

    /// <summary>
    /// Weighted pick. Weights lerp from weightFullHp to weightLowHp as the monster loses HP, so heavy / slam
    /// attacks escalate. Rules: no immediate repeat, heavy "pity" after heavyPityCycles, and a one-time forced
    /// Special when HP first crosses desperationHpFraction.
    /// </summary>
    AttackDefinition ChooseFromTable()
    {
        float hp = HpFraction();
        float stillFor = Time.time - idleSince;
        AttackDefinition chosen = null;
        string reason = "weighted";

        if (!desperationUsed && hp <= desperationHpFraction)
        {
            foreach (var a in attackTable)
            {
                if (a.category == AttackCategory.Special && IsEligible(a, hp, stillFor, true)) { chosen = a; break; }
            }
            if (chosen != null) { desperationUsed = true; reason = "desperation"; }
        }

        if (chosen == null)
        {
            var pool = new System.Collections.Generic.List<AttackDefinition>();
            foreach (var a in attackTable)
                if (a != lastTableAttack && IsEligible(a, hp, stillFor, false)) pool.Add(a);
            if (pool.Count == 0)
                foreach (var a in attackTable)
                    if (IsEligible(a, hp, stillFor, false)) pool.Add(a);

            if (cyclesSinceHeavy >= heavyPityCycles)
            {
                var heavies = pool.FindAll(a => IsBigHit(a.category));
                if (heavies.Count > 0) { pool = heavies; reason = "heavy-pity"; }
            }

            float hurt = 1f - hp;
            float total = 0f;
            foreach (var a in pool) total += Mathf.Max(0f, Mathf.Lerp(a.weightFullHp, a.weightLowHp, hurt));
            float r = Random.value * total;
            foreach (var a in pool)
            {
                r -= Mathf.Max(0f, Mathf.Lerp(a.weightFullHp, a.weightLowHp, hurt));
                if (r <= 0f) { chosen = a; break; }
            }
            if (chosen == null && pool.Count > 0) chosen = pool[pool.Count - 1];
        }

        if (verboseAttackLogs && chosen != null)
            Debug.Log($"[MonsterAI] Table pick '{chosen.name}' ({chosen.category}) hp={hp:0.00} still={stillFor:0.0}s reason={reason}");
        return chosen;
    }

    /// <summary>Debug / verification: run a specific table attack immediately (ignores selection rules).</summary>
    public bool DebugForceAttack(int index)
    {
        if (!UsesAttackTable || index < 0 || index >= attackTable.Count || busy || controller == null) return false;
        ResolvePlayerTarget();
        StartCoroutine(TableAttackRoutine(attackTable[index]));
        return true;
    }

    IEnumerator TableAttackRoutine(AttackDefinition a)
    {
        busy = true;
        if (!controller.BeginAttackTrigger(a.animatorTrigger))
        {
            busy = false;
            yield break;
        }

        a.lastUsedTime = Time.time;
        lastTableAttack = a;
        LastChosenAttack = a;
        TableAttackCount++;
        cyclesSinceHeavy = IsBigHit(a.category) ? 0 : cyclesSinceHeavy + 1;
        LastHitLanded = false;

        // --- Telegraph ---
        if (a.windUpClip != null && sfxAudioSource != null)
            sfxAudioSource.PlayOneShot(a.windUpClip, a.windUpVolume);
        StartCoroutine(TelegraphFlash(a.windUp, a.telegraphColor));
        if (a.fireVfx) SetWeaponFire(true);

        float start = Time.time;
        int hits = 1 + (a.followUpHits != null ? a.followUpHits.Length : 0);
        for (int i = 0; i < hits; i++)
        {
            float hitAt = a.windUp + (i == 0 ? 0f : a.followUpHits[i - 1]);

            float whooshAt = start + Mathf.Max(0f, hitAt - swingSfxLead);
            while (Time.time < whooshAt) yield return null;
            if (a.swingClip != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(a.swingClip, a.swingVolume);

            while (Time.time < start + hitAt) yield return null;
            if (controller == null || !controller.isAlive) break;

            if (a.impactClip != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(a.impactClip, a.impactVolume);
            if (a.fireVfx) PlayImpactBurst();

            if (IsTargetInSwing(a))
            {
                controller.PerformAttackHit(a.damageMultiplier); // routes through PlayerHealth
                LastHitLanded = true;
            }
        }
        if (a.fireVfx) SetWeaponFire(false);

        yield return new WaitForSeconds(a.recover);

        if (controller != null && controller.isAlive)
            controller.EndAttack();

        idleSince = Time.time;
        busy = false;
    }

    /// <summary>Hit frame check: in range (small tolerance) AND inside the attack's swing arc.</summary>
    bool IsTargetInSwing(AttackDefinition a)
    {
        if (playerTarget == null) return false;
        if (DistanceToTarget() > controller.attackRange * 1.15f) return false;
        Vector3 to = playerTarget.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(transform.forward, to) <= a.hitArc;
    }

    void SetWeaponFire(bool on)
    {
        if (weaponFireVfx != null)
        {
            if (on) weaponFireVfx.Play(true);
            else weaponFireVfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (weaponFireLight != null)
            weaponFireLight.intensity = on ? weaponFireLightIntensity : 0f;
    }

    void PlayImpactBurst()
    {
        if (impactBurstVfx == null) return;
        impactBurstVfx.transform.position = transform.position + transform.forward * impactBurstOffset.x + Vector3.up * impactBurstOffset.y;
        impactBurstVfx.Play(true);
    }

    IEnumerator AttackRoutine(AttackPattern pattern)
    {
        busy = true;
        controller.attackCooldown = Mathf.Max(controller.attackCooldown, minAttackCooldown);
        if (verboseAttackLogs)
            Debug.Log("[MonsterAI] Attack pattern: " + pattern);

        controller.BeginAttackVariant(pattern);

        float baseHitDelay = pattern == AttackPattern.Heavy ? heavyHitDelay : (pattern == AttackPattern.Special ? specialHitDelay : slashHitDelay);
        float baseRecover = pattern == AttackPattern.Heavy ? heavyRecoverDelay : (pattern == AttackPattern.Special ? specialRecoverDelay : slashRecoverDelay);
        float damageMultiplier = pattern == AttackPattern.Heavy ? heavyDamageMultiplier : (pattern == AttackPattern.Special ? specialDamageMultiplier : slashDamageMultiplier);

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
