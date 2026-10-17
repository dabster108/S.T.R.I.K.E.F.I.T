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

    /// <summary>Attack-table rows used when the player is OUT of melee reach (Level 3).</summary>
    public enum RangedKind
    {
        None = 0,   // melee row: chosen inside attackRange
        Rush = 1,   // runs at the player during travelWindow, strikes on arrival
        Throw = 2   // each hit frame releases an axe (MonsterAxeThrow); the axes fly back to the hands
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
        [Tooltip("Optional half-angle per follow-up hit (same order as followUpHits). Missing entries use hitArc.")]
        public float[] followUpArcs = new float[0];
        [Tooltip("Selection weight at full monster HP.")]
        public float weightFullHp = 10f;
        [Tooltip("Selection weight at 0 monster HP (lerped by damage taken).")]
        public float weightLowHp = 10f;
        [Tooltip("Selection weight while enraged (BossPhaseController). Negative = keep the HP-lerped weight.")]
        public float enragedWeight = -1f;
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

        [Header("Unblockable (dodge-only) attack")]
        [Tooltip("Cannot be blocked or parried: PlayerHealth takes the full damage. Hits if the player's head is inside " +
                 "dodgeRadius of the impact point at the hit frame, whatever the arc; the only defence is to move out.")]
        public bool unblockable;
        [Tooltip("Impact centre, metres in front of the monster.")]
        public float impactForward = 1.3f;
        [Tooltip("Radius (m, horizontal) of the impact zone the player has to leave.")]
        public float dodgeRadius = 1.6f;
        [Tooltip("Distinct warning sound at the start of the wind-up, so the player hears THIS one must be dodged.")]
        public AudioClip warningClip;
        [Range(0f, 1f)] public float warningVolume = 1f;

        [Header("Ranged / gap-closer (optional)")]
        [Tooltip("None = melee row. Rush / Throw rows are chosen only while the player is between minRange and maxRange " +
                 "(horizontal metres), i.e. out of melee reach, and never from inside it.")]
        public RangedKind rangedKind = RangedKind.None;
        public float minRange = 0f;
        public float maxRange = 0f;
        [Tooltip("Rush: seconds after the trigger (authored, divided by tempo) during which the monster runs at the " +
                 "player; it arrives at stopDistance as the window closes.")]
        public Vector2 travelWindow;
        [Tooltip("Throw: axe released on each hit frame (0 = right hand, 1 = left hand).")]
        public int[] throwAxes = new int[0];
        [Tooltip("Throw: seconds after the trigger (authored) by which the thrown axes are caught again.")]
        public float catchTime = 2f;

        [System.NonSerialized] public float lastUsedTime = -999f;
    }

    // Telemetry hooks (SessionReporter). Raised synchronously; listeners must not throw.
    /// <summary>Telegraph began. Args: monster, attack name, tier (Slash/Heavy/Special or table category).</summary>
    public static event System.Action<MonsterAI, string, string> AttackStarted;
    /// <summary>Hit frame resolved. Args: monster, attack name, whether it reached the player.</summary>
    public static event System.Action<MonsterAI, string, bool> AttackResolved;
    /// <summary>Defend window opened.</summary>
    public static event System.Action<MonsterAI> DefendStarted;

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
    [Tooltip("Attack tempo (1 = authored). >1 shortens wind-ups, combo gaps and recovery; the Animator speed should be " +
             "set to the same value so hit frames stay on the visible impact (BossPhaseController does both).")]
    public float tempoScale = 1f;

    [Header("Chains and telegraph variance (Level 3)")]
    [Tooltip("Chance (boss phase 1 / 2 / 3; x only without a BossPhaseController) that the next attack follows the last one " +
             "after chainGap instead of after the cooldown. Never the must-dodge attack, never a ranged row.")]
    public Vector3 chainChanceByPhase = Vector3.zero;
    [Tooltip("Linked attacks in one chain at most.")]
    [Range(1, 4)] public int chainMaxAttacks = 3;
    [Tooltip("Pause between two linked attacks (s, min / max).")]
    public Vector2 chainGap = new Vector2(0.35f, 0.6f);
    [Tooltip("Random change to each telegraph (first wind-up), seconds +/-. The wind-up animation is time-scaled to match, so " +
             "the visible blow still lands on the damage frame. 0 = off.")]
    public float telegraphJitter = 0f;
    [Tooltip("No damaging table attack is ever telegraphed for less than this (s), whatever the tempo, phase or jitter. 0 = off.")]
    public float minTelegraph = 0f;
    [Tooltip("Weight multiplier for unblockable (must-dodge) rows per boss phase 1 / 2 / 3.")]
    public Vector3 unblockableWeightByPhase = Vector3.one;
    /// <summary>Attack cooldown multiplier set by the boss phase (1 = none).</summary>
    [System.NonSerialized] public float phaseCooldownScale = 1f;
    public int ChainedAttacks { get; private set; }
    public int LongestChain { get; private set; }
    /// <summary>Shortest telegraph actually used since the scene started (verification).</summary>
    public float ShortestTelegraph { get; private set; } = 99f;
    public float LongestTelegraph { get; private set; }

    [Header("Parry (optional, Level 3)")]
    [Tooltip("A weapon clash (player weapon hits the monster weapon) during the wind-up of a normal attack parries it: " +
             "its hits deal parryDamageMultiplier. Unblockable attacks ignore parries.")]
    public bool allowParry = false;
    [Range(0f, 1f)] public float parryDamageMultiplier = 0.35f;

    [Header("Ranged attacks (optional, Level 3)")]
    [Tooltip("Flies the thrown axes of Throw rows.")]
    public MonsterAxeThrow axeThrow;
    [Tooltip("Rush rows never move the monster faster than this (m/s).")]
    public float maxRushSpeed = 6f;

    [Header("Enrage (set by BossPhaseController)")]
    [Tooltip("While true, attack-table rows with enragedWeight >= 0 use it instead of the HP-lerped weight.")]
    public bool enraged = false;
    [Tooltip("Body colour the defend / telegraph tints return to (white = untinted; enrage sets a red tint).")]
    public Color restingTint = Color.white;
    [Tooltip("Optional extra renderers that receive the same body tint as telegraphRenderer (split meshes).")]
    public Renderer[] extraTintRenderers = new Renderer[0];

    [Header("Danger zone (optional, unblockable attacks)")]
    [Tooltip("Ground marker shown at the impact point during an unblockable wind-up (size = dodgeRadius).")]
    public DangerZoneIndicator dangerZone;

    [Header("Weapon FX (attack table, optional)")]
    public ParticleSystem weaponFireVfx;
    [Tooltip("More weapon fire emitters that ignite with weaponFireVfx (Level 3: the second axe).")]
    public ParticleSystem[] extraWeaponFireVfx = new ParticleSystem[0];
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
    [Tooltip("Also play the brace cue when the guard is a REACTION to a player swing. Off: a reactive guard is raised against " +
             "a swing that may never land, so the brace sounded on air swings; the block clang on a landed hit stays.")]
    public bool braceSoundOnReactiveGuard = false;
    [Tooltip("Block clang when a hit lands during the defend window.")]
    public AudioClip defendBlockClip;
    [Range(0f, 1f)] public float defendBlockVolume = 0.9f;
    [Tooltip("Random spread on defendCooldown (0.3 = 70-130 %), so the timed guard cannot be learned. 0 = fixed (Levels 1-2).")]
    [Range(0f, 0.9f)] public float defendCooldownJitter = 0f;

    [Header("Reactive defend (optional, Level 3)")]
    [Tooltip("Chance to raise the guard when a player weapon comes in fast and close while the monster is free (rolled " +
             "once per swing). 0 = off (Levels 1-2: timed guard only).")]
    [Range(0f, 1f)] public float reactiveDefendChance = 0f;
    [Tooltip("Chance in phase 2 (HP at or below reactivePhase2HpFraction).")]
    [Range(0f, 1f)] public float reactiveDefendChancePhase2 = 0f;
    [Tooltip("Chance while enraged.")]
    [Range(0f, 1f)] public float reactiveDefendChanceEnraged = 0f;
    [Range(0f, 1f)] public float reactivePhase2HpFraction = 0.66f;
    [Tooltip("Weapon speed (m/s) that counts as an incoming swing (the weapon's hit gate is used if higher).")]
    public float reactiveSwingSpeed = 2.5f;
    [Tooltip("The swing must be this close (m) to the guard target and moving toward it: the guard starts ~0.15-0.25 s before contact at real swing speeds.")]
    public float reactiveRange = 1.25f;
    [Tooltip("Guard target (the chest); monster root + 1.3 m if empty.")]
    public Transform reactiveTarget;
    [Tooltip("Shortest time between two reactive guards.")]
    public float reactiveCooldown = 3f;
    public float reactiveDefendDuration = 0.8f;

    private float nextDefendTime = 0f;
    private float nextReactiveTime = 0f;
    private readonly bool[] reactiveArmed = { true, true };
    private readonly HitDetection[] reactiveHd = new HitDetection[2];
    private readonly Transform[] reactiveHdOwner = new Transform[2];
    public bool IsDefending { get; private set; }
    /// <summary>True while an attack, guard, stagger or dodge owns the monster (nothing new may start).</summary>
    public bool IsBusy => busy;
    /// <summary>True during a guard-break stagger (MonsterDefense).</summary>
    public bool IsStaggered { get; private set; }
    /// <summary>Set by BossPhaseController for a phase-shift roar: no new attack or guard starts while it is on.</summary>
    [System.NonSerialized] public bool holdActions;
    /// <summary>True while the current guard was raised in reaction to a swing (not the timer).</summary>
    public bool DefendIsReactive { get; private set; }
    /// <summary>Swings that were close and fast enough to roll for a reactive guard (verification).</summary>
    public int ReactiveRolls { get; private set; }
    public int ReactiveDefends { get; private set; }

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
    private MonsterDefense defense;             // shared combat sense (facing, guards, poise); null = legacy behaviour
    private MonsterPersonalSpace personalSpace; // personal-space clamp; its push-back is added back to range checks
    private BossPhaseController bossPhases;      // Level 3 only
    private float attackTimeScale = 1f;         // wind-up time-scale for the telegraph jitter / floor (1 = authored)
    private int chainLink;                      // attacks already linked in the running chain
    private Coroutine guardRoutine;
    private float animSpeedScale = 1f;          // stagger slows the hit reaction; tempoScale * this = Animator speed
    private Vector3 lockedImpact;               // unblockable impact centre, fixed when the wind-up starts
    private bool hasLockedImpact;
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
    /// <summary>Attack currently between trigger and recovery end (null otherwise).</summary>
    public AttackDefinition CurrentAttack { get; private set; }
    /// <summary>True if the current / last attack was parried by a weapon clash during its wind-up.</summary>
    public bool CurrentAttackParried { get; private set; }
    /// <summary>Damage multiplier passed to the player by the last resolved hit (after a parry, before block).</summary>
    public float LastHitDamageMultiplier { get; private set; }
    private bool inWindUp;
    private bool? hasRangedRows;
    // Attack-table strike window on the AI clock: from the swing whoosh to strikeWindowAfterHit after each hit frame.
    private float strikeFrom = -1f, strikeUntil = -1f;
    /// <summary>The weapon is in an attack's actual swing (not wind-up or recovery). HitDetection uses it to tell a monster
    /// strike meeting the player's weapon (a clash) from the axe drifting through a held weapon.</summary>
    public bool InStrikeWindow => AiTime >= strikeFrom && AiTime <= strikeUntil;
    [Tooltip("Seconds after each hit frame that still count as the strike (InStrikeWindow).")]
    public float strikeWindowAfterHit = 0.2f;

    // Hit-stop: the AI clock (every attack timing) stops together with the Animator, so damage frames stay on the
    // visible impact after a freeze. Without a hit-stop AiTime == Time.time (Levels 1-2 unchanged).
    private float freezeStart = -1f;
    private float frozenTotal;
    /// <summary>Attack clock: Time.time minus every hit-stop so far.</summary>
    public float AiTime => Time.time - frozenTotal - (freezeStart >= 0f ? Time.time - freezeStart : 0f);
    public bool InHitStop => freezeStart >= 0f;
    public int HitStopCount { get; private set; }
    /// <summary>Metres covered by the last Rush (verification).</summary>
    public float LastRushDistance { get; private set; }

    void OnEnable()
    {
        HitDetection.WeaponClashed += HandleWeaponClashed;
    }

    void OnDisable()
    {
        HitDetection.WeaponClashed -= HandleWeaponClashed;
        if (dangerZone != null && dangerZone.IsShowing) dangerZone.Hide();
    }

    void HandleWeaponClashed(HitDetection source, float speed)
    {
        if (!allowParry || !inWindUp || CurrentAttack == null || CurrentAttack.unblockable) return;
        // A parry is an active swing into the weapon, not a weapon merely held in the swing's path.
        float gate = PlayerCombat.Instance != null ? PlayerCombat.Instance.minSwingVelocity : 1.2f;
        if (speed < gate) return;
        CurrentAttackParried = true;
        if (verboseAttackLogs) Debug.Log($"[MonsterAI] '{CurrentAttack.name}' parried");
    }

    void Awake()
    {
        controller = GetComponent<MonsterController>();
        defense = GetComponent<MonsterDefense>();
        personalSpace = GetComponent<MonsterPersonalSpace>();
        bossPhases = GetComponent<BossPhaseController>();
        if (axeThrow == null) axeThrow = GetComponent<MonsterAxeThrow>();
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
        if (controller == null || !controller.isAlive) return;
        if (GamePause.IsPaused) return;   // no thinking, no new attack while paused
        if (defense == null) CheckReactiveDefend(); // MonsterDefense owns guards when present (one source of truth)
        if (busy || holdActions) return;
        if (Time.time < nextThinkTime) return;
        nextThinkTime = Time.time + thinkInterval;

        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive)
            return;

        ResolvePlayerTarget();
        if (playerTarget == null) return;

        float dist = DistanceToTarget();

        if (facePlayer && (defense == null || !defense.turnToFacePlayer))
            FaceTarget();

        if (dist > controller.attackRange && UsesAttackTable && HasRangedRows && controller.CanAttack())
        {
            UpdateTableCooldown();
            var ranged = ChooseRanged(dist);
            if (ranged != null)
            {
                StartCoroutine(TableAttackRoutine(ranged));
                return;
            }
        }

        if (dist <= controller.attackRange)
        {
            if (defense == null && canDefend && Time.time >= nextDefendTime && !controller.isAttacking)
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
        if (GamePause.IsPaused) return;
        if (controller == null || !controller.isAlive || busy || holdActions || !canMove) return;
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

    float CurrentReactiveChance()
    {
        if (enraged) return reactiveDefendChanceEnraged;
        return HpFraction() <= reactivePhase2HpFraction ? reactiveDefendChancePhase2 : reactiveDefendChance;
    }

    /// <summary>
    /// Reactive guard: a player weapon moving fast toward the chest from close range gets one roll per swing; on success
    /// the monster guards right before the blow lands. Only while it is free (not attacking / guarding), so its own
    /// attacks stay the safe punish window, and never faster than reactiveCooldown.
    /// </summary>
    void CheckReactiveDefend()
    {
        if (!canDefend || (reactiveDefendChance <= 0f && reactiveDefendChancePhase2 <= 0f && reactiveDefendChanceEnraged <= 0f)) return;
        var pc = PlayerCombat.Instance;
        if (pc == null || (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive)) return;
        Vector3 target = reactiveTarget != null ? reactiveTarget.position : transform.position + Vector3.up * 1.3f;
        for (int h = 0; h < 2; h++)
        {
            Transform w = h == 0 ? pc.weaponTransform : pc.offhandWeaponTransform;
            if (w == null || !w.gameObject.activeInHierarchy) { reactiveArmed[h] = true; continue; }
            if (reactiveHdOwner[h] != w) { reactiveHdOwner[h] = w; reactiveHd[h] = w.GetComponentInChildren<HitDetection>(); }
            Vector3 vel = reactiveHd[h] != null ? reactiveHd[h].CurrentVelocity : Vector3.zero;
            float speed = Mathf.Max(vel.magnitude, h == 0 ? pc.CurrentSwingSpeed : pc.CurrentOffhandSwingSpeed);
            float gate = Mathf.Max(reactiveSwingSpeed, pc.minSwingVelocity);
            if (speed < gate * 0.5f) { reactiveArmed[h] = true; continue; } // swing over: the next one may roll
            if (!reactiveArmed[h] || speed < gate) continue;
            Vector3 to = target - w.position;
            if (to.magnitude > reactiveRange || Vector3.Dot(vel, to) <= 0f) continue;
            reactiveArmed[h] = false; // one roll per swing
            if (busy || IsDefending || controller.isAttacking || Time.time < nextReactiveTime) continue;
            ReactiveRolls++;
            if (Random.value < CurrentReactiveChance())
            {
                ReactiveDefends++;
                nextReactiveTime = Time.time + reactiveCooldown;
                StartCoroutine(DefendRoutine(true));
                return;
            }
        }
    }

    IEnumerator DefendRoutine()
    {
        return DefendRoutine(false);
    }

    IEnumerator DefendRoutine(bool reactive)
    {
        return DefendRoutine(reactive, reactive ? reactiveDefendDuration : defendDuration, null, Color.clear, 0f);
    }

    /// <summary>
    /// MonsterDefense: raise the guard now for <paramref name="duration"/> seconds with <paramref name="trigger"/>
    /// (empty = MonsterAnimator.defendTrigger). A brief flash in <paramref name="flashColor"/> marks the raise. Refused
    /// while anything else owns the monster.
    /// </summary>
    public bool BeginGuard(float duration, bool reactive, string trigger, Color flashColor, float flashSeconds)
    {
        if (busy || holdActions || controller == null || !controller.isAlive || controller.isAttacking) return false;
        guardRoutine = StartCoroutine(DefendRoutine(reactive, duration, trigger, flashColor, flashSeconds));
        return true;
    }

    IEnumerator DefendRoutine(bool reactive, float duration, string trigger, Color flashColor, float flashSeconds)
    {
        busy = true;
        IsDefending = true;
        DefendIsReactive = reactive;
        // A reactive guard also resets the timed one, so the two never stack into a wall of blocks.
        nextDefendTime = Time.time + defendCooldown * (1f + Random.Range(-defendCooldownJitter, defendCooldownJitter));
        DefendStarted?.Invoke(this);
        if (verboseAttackLogs)
            Debug.Log(reactive ? "[MonsterAI] Enter Defend (reactive)" : "[MonsterAI] Enter Defend");

        var anim = GetComponent<MonsterAnimator>();
        if (anim != null) anim.PlayDefend(trigger);

        if (defendBraceClip != null && sfxAudioSource != null && (!reactive || braceSoundOnReactiveGuard))
        {
            sfxAudioSource.PlayOneShot(defendBraceClip, defendBraceVolume);
            SfxTrace.Note(this, defendBraceClip, "monster guard raised");
        }
        if (flashSeconds > 0f)
        {
            // Raise cue: a short cool flash (never the red attack telegraph), then the guard tint if this monster has one.
            SetBodyTint(flashColor);
            yield return new WaitForSeconds(flashSeconds);
            duration -= flashSeconds;
            if (defendTintEnabled) SetBodyTint(defendTintColor); else ClearBodyTint();
        }
        else if (defendTintEnabled)
            SetBodyTint(defendTintColor);

        yield return new WaitForSeconds(Mathf.Max(0f, duration));

        guardRoutine = null;
        IsDefending = false;
        DefendIsReactive = false;
        busy = false;
        idleSince = Time.time;
        if (defendTintEnabled || flashSeconds > 0f)
            ClearBodyTint();
        if (verboseAttackLogs)
            Debug.Log("[MonsterAI] Exit Defend");
        if (anim != null) anim.PlayState(MonsterAnimState.Idle);
    }

    /// <summary>
    /// MonsterDefense guard break: drop the guard and stagger for <paramref name="seconds"/> (no attacks, no guard). The
    /// hit reaction is reached through its real transitions (Defend -> Idle -> Hit) and played slower so it fills the stagger.
    /// </summary>
    public void BeginStagger(float seconds, float animScale, Color flashColor, float flashSeconds)
    {
        if (controller == null || !controller.isAlive) return;
        if (guardRoutine != null) { StopCoroutine(guardRoutine); guardRoutine = null; }
        IsDefending = false;
        DefendIsReactive = false;
        StartCoroutine(StaggerRoutine(seconds, animScale, flashColor, flashSeconds));
    }

    IEnumerator StaggerRoutine(float seconds, float animScale, Color flashColor, float flashSeconds)
    {
        busy = true;
        IsStaggered = true;
        float end = Time.time + seconds;
        var anim = GetComponent<MonsterAnimator>();
        SetBodyTint(flashColor);
        if (anim != null)
        {
            anim.PlayState(MonsterAnimState.Idle); // Defend -> Idle (trigger), then Idle -> Hit
            float giveUp = Time.time + 0.3f;
            while (Time.time < giveUp && anim.animator != null && !InIdleState(anim.animator)) yield return null;
            anim.PlayState(MonsterAnimState.Hit);
        }
        animSpeedScale = Mathf.Clamp(animScale, 0.1f, 1f);
        ApplyAnimSpeed();
        float flashEnd = Time.time + flashSeconds;
        while (Time.time < end)
        {
            if (controller == null || !controller.isAlive) break;
            if (flashEnd > 0f && Time.time >= flashEnd) { flashEnd = -1f; ClearBodyTint(); }
            yield return null;
        }
        if (flashEnd > 0f) ClearBodyTint();
        animSpeedScale = 1f;
        ApplyAnimSpeed();
        IsStaggered = false;
        busy = false;
        idleSince = Time.time;
        if (controller != null && controller.isAlive && anim != null) anim.PlayState(MonsterAnimState.Idle);
    }

    static bool InIdleState(Animator a)
    {
        var st = a.GetCurrentAnimatorStateInfo(0);
        return !a.IsInTransition(0) && (st.IsName("Idle") || st.IsName("IdleEnraged"));
    }

    /// <summary>Animator speed = attack tempo x stagger slow-down (left alone while a hit-stop holds it at 0).</summary>
    void ApplyAnimSpeed()
    {
        if (InHitStop) return;
        var anim = GetComponent<MonsterAnimator>();
        if (anim != null && anim.animator != null) anim.animator.speed = Mathf.Max(0.25f, tempoScale) * animSpeedScale * attackTimeScale;
    }

    /// <summary>MonsterDefense dodge: fire the dodge clip's trigger and slide the root by <paramref name="offset"/>.</summary>
    public bool BeginDodge(string trigger, Vector3 offset, float seconds)
    {
        if (busy || holdActions || controller == null || !controller.isAlive || controller.isAttacking) return false;
        var anim = GetComponent<MonsterAnimator>();
        if (anim == null || !anim.PlayNamedTrigger(trigger)) return false;
        StartCoroutine(DodgeRoutine(offset, seconds));
        return true;
    }

    IEnumerator DodgeRoutine(Vector3 offset, float seconds)
    {
        busy = true;
        float t = 0f, done = 0f;
        while (t < seconds && controller != null && controller.isAlive)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, seconds)));
            transform.position += offset * (k - done); // MonsterPersonalSpace still clamps after this
            done = k;
            yield return null;
        }
        busy = false;
        idleSince = Time.time;
        var anim = GetComponent<MonsterAnimator>();
        if (anim != null && controller != null && controller.isAlive) anim.PlayState(MonsterAnimState.Idle);
    }

    /// <summary>Called by MonsterController when a player hit lands inside the defend window.</summary>
    public void NotifyDefendedHit(Vector3 hitPoint) => NotifyDefendedHit(hitPoint, true);

    /// <param name="playClip">False when MonsterDefense plays its own (per-guard) block clang.</param>
    public void NotifyDefendedHit(Vector3 hitPoint, bool playClip)
    {
        if (playClip && defendBlockClip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(defendBlockClip, defendBlockVolume);
            SfxTrace.Note(this, defendBlockClip, "monster guard absorbed a landed hit");
        }
        if (defendTintEnabled)
            StartCoroutine(BlockFlash());
    }

    IEnumerator BlockFlash()
    {
        SetBodyTint(Color.white);
        yield return new WaitForSeconds(0.06f);
        if (IsDefending) SetBodyTint(defendTintColor);
    }

    /// <summary>MonsterDefense opening cue: a brief body flash, then back to the guard tint (or the resting tint).</summary>
    public void FlashTint(Color c, float seconds)
    {
        if (controller == null || !controller.isAlive) return;
        StartCoroutine(FlashTintRoutine(c, seconds));
    }

    IEnumerator FlashTintRoutine(Color c, float seconds)
    {
        SetBodyTint(c);
        yield return new WaitForSeconds(seconds);
        if (IsDefending && defendTintEnabled) SetBodyTint(defendTintColor);
        else if (!IsStaggered && controller != null && !controller.isAttacking) ClearBodyTint();   // never wipe a telegraph
    }

    void SetBodyTint(Color c)
    {
        TintRenderer(telegraphRenderer, c);
        if (extraTintRenderers != null)
            foreach (var r in extraTintRenderers) TintRenderer(r, c);
    }

    void TintRenderer(Renderer r, Color c)
    {
        if (r == null) return;
        bool perSlot = extraTintRenderers != null && extraTintRenderers.Length > 0;
        if (!perSlot)
        {
            // Levels 1-2: renderer-wide block, exactly as before (TelegraphFlash uses the same block).
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
            return;
        }
        // Split-mesh monsters: every material slot (the weak-point slot's emission is driven by MonsterWeakPoint).
        for (int i = 0; i < r.sharedMaterials.Length; i++)
        {
            r.GetPropertyBlock(mpb, i);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb, i);
        }
    }

    void ClearBodyTint()
    {
        SetBodyTint(restingTint);
    }

    /// <summary>Apply the resting tint now (enrage on/off).</summary>
    public void ApplyRestingTint()
    {
        if (!IsDefending) SetBodyTint(restingTint);
    }

    int Phase => bossPhases != null ? bossPhases.Phase : 0;

    static float ByPhase(Vector3 v, int phase) => phase <= 0 ? v.x : phase == 1 ? v.y : v.z;

    float SelectionWeight(AttackDefinition a, float hurt)
    {
        float w = enraged && a.enragedWeight >= 0f ? a.enragedWeight : Mathf.Max(0f, Mathf.Lerp(a.weightFullHp, a.weightLowHp, hurt));
        if (a.unblockable) w *= ByPhase(unblockableWeightByPhase, Phase);
        return w;
    }

    /// <summary>Boss phase: attack tempo (every wind-up, swing and recovery scale with it; the Animator follows).</summary>
    public void SetTempo(float tempo)
    {
        tempoScale = Mathf.Max(0.25f, tempo);
        ApplyAnimSpeed();
    }

    // ───────────────────────── Attack table (Level 2+) ─────────────────────────

    void UpdateTableCooldown()
    {
        if (baseAttackCooldown < 0f) baseAttackCooldown = controller.attackCooldown;
        float hurt = 1f - HpFraction();
        controller.attackCooldown = baseAttackCooldown * Mathf.Lerp(1f, lowHpCooldownScale, hurt) * phaseCooldownScale;
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

    /// <summary>Optional gate (DragonHazard): while it returns true the must-dodge (unblockable) rows cannot be chosen, so a dragon's
    /// fire strip and the boss's own dodge-only attack never overlap.</summary>
    [System.NonSerialized] public System.Func<bool> mustDodgeGate;

    bool IsEligible(AttackDefinition a, float hp, float stillFor, bool ignoreCooldown)
    {
        if (a == null || string.IsNullOrEmpty(a.animatorTrigger)) return false;
        if (a.unblockable && mustDodgeGate != null && mustDodgeGate()) return false;
        if (a.rangedKind != RangedKind.None) return false; // ranged rows: ChooseRanged only
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
            foreach (var a in pool) total += SelectionWeight(a, hurt);
            float r = Random.value * total;
            foreach (var a in pool)
            {
                r -= SelectionWeight(a, hurt);
                if (r <= 0f) { chosen = a; break; }
            }
            if (chosen == null && pool.Count > 0) chosen = pool[pool.Count - 1];
        }

        if (verboseAttackLogs && chosen != null)
            Debug.Log($"[MonsterAI] Table pick '{chosen.name}' ({chosen.category}) hp={hp:0.00} still={stillFor:0.0}s reason={reason}");
        return chosen;
    }

    bool HasRangedRows
    {
        get
        {
            if (!hasRangedRows.HasValue)
                hasRangedRows = attackTable != null && attackTable.Exists(a => a != null && a.rangedKind != RangedKind.None);
            return hasRangedRows.Value;
        }
    }

    /// <summary>Weighted pick among the ranged rows whose range band holds the player (same HP / enrage weights).</summary>
    public AttackDefinition ChooseRanged(float dist)
    {
        float hp = HpFraction();
        var pool = new System.Collections.Generic.List<AttackDefinition>();
        foreach (var a in attackTable)
        {
            if (a == null || a.rangedKind == RangedKind.None || string.IsNullOrEmpty(a.animatorTrigger)) continue;
            if (dist < a.minRange || dist > a.maxRange) continue;
            if (hp > a.maxHpFraction || Time.time - a.lastUsedTime < a.cooldown) continue;
            pool.Add(a);
        }
        if (pool.Count > 1) pool.Remove(lastTableAttack);
        float hurt = 1f - hp, total = 0f;
        foreach (var a in pool) total += SelectionWeight(a, hurt);
        if (total <= 0f) return null;
        float r = Random.value * total;
        foreach (var a in pool)
        {
            r -= SelectionWeight(a, hurt);
            if (r <= 0f)
            {
                if (verboseAttackLogs) Debug.Log($"[MonsterAI] Ranged pick '{a.name}' at {dist:0.00} m hp={hp:0.00}");
                return a;
            }
        }
        return pool[pool.Count - 1];
    }

    /// <summary>
    /// Freeze the monster (Animator speed 0 and the AI attack clock) for a few frames: the hit-stop that confirms a
    /// weak-point hit. Real time; nothing else in the scene is slowed (the headset and the player's hands never are).
    /// </summary>
    public void HitStop(float seconds)
    {
        if (seconds <= 0f || freezeStart >= 0f || controller == null || !controller.isAlive) return;
        StartCoroutine(HitStopRoutine(seconds));
    }

    IEnumerator HitStopRoutine(float seconds)
    {
        freezeStart = Time.time;
        HitStopCount++;
        var anim = GetComponent<MonsterAnimator>();
        Animator a = anim != null ? anim.animator : null;
        if (a != null) a.speed = 0f;
        yield return new WaitForSecondsRealtime(seconds);
        frozenTotal += Time.time - freezeStart;
        freezeStart = -1f;
        // Restore what the monster should run at now (tempo x stagger), not what it ran at before: an enrage switch or
        // the end of a stagger during the freeze keeps its own speed.
        if (a != null && a.speed == 0f) a.speed = Mathf.Max(0.25f, tempoScale) * animSpeedScale * attackTimeScale;
    }

    /// <summary>MonsterAxeThrow: a thrown axe reached the player.</summary>
    public void NotifyThrowHit(float multiplier)
    {
        LastHitLanded = true;
        LastHitDamageMultiplier = multiplier;
    }

    IEnumerator RushTravel(AttackDefinition a, float start, float tempo)
    {
        float t0 = start + a.travelWindow.x / tempo, t1 = start + a.travelWindow.y / tempo;
        LastRushDistance = 0f;
        // Aim lock (MonsterDefense): the charge line is fixed when the wind-up starts, so a sidestep beats it.
        bool aimLocked = defense != null && defense.turnToFacePlayer;
        Vector3 lane = transform.forward; lane.y = 0f; lane.Normalize();
        // Anticipation: square up to the player (legacy only).
        while (AiTime < t0)
        {
            if (controller == null || !controller.isAlive || CurrentAttack != a) yield break;
            if (!aimLocked) TurnToTarget(12f);
            yield return null;
        }
        float last = AiTime;
        while (AiTime < t1)
        {
            if (controller == null || !controller.isAlive || CurrentAttack != a || playerTarget == null) yield break;
            float now = AiTime, dt = now - last;
            last = now;
            if (dt > 0f)
            {
                Vector3 d = playerTarget.position - transform.position; d.y = 0f;
                Vector3 dir;
                float remaining;
                if (aimLocked)
                {
                    // Along the locked lane, up to where the player stood in it.
                    dir = lane;
                    remaining = Vector3.Dot(d, lane) - stopDistance;
                }
                else
                {
                    TurnToTarget(10f);
                    dir = d.normalized;
                    remaining = d.magnitude - stopDistance;
                }
                if (remaining > 0.005f)
                {
                    // Cover what is left by the end of the window.
                    float step = Mathf.Min(remaining * Mathf.Clamp01(dt / Mathf.Max(dt, t1 - now + dt)), maxRushSpeed * dt);
                    transform.position += dir * step;
                    LastRushDistance += step;
                }
            }
            yield return null;
        }
    }

    void TurnToTarget(float rate)
    {
        if (playerTarget == null) return;
        Vector3 dir = playerTarget.position - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    /// <summary>MonsterDefense counter: the fastest melee row whose telegraph is at least minTelegraph, started now even inside
    /// the attack cooldown. Never while anything else owns the monster. Returns false if nothing fits.</summary>
    public bool BeginCounter(float minTelegraph)
    {
        if (!UsesAttackTable || busy || holdActions || controller == null || !controller.isAlive || controller.isAttacking) return false;
        float tempo = Mathf.Max(0.25f, tempoScale);
        AttackDefinition best = null;
        foreach (var a in attackTable)
        {
            if (a == null || string.IsNullOrEmpty(a.animatorTrigger) || a.unblockable || a.rangedKind != RangedKind.None) continue;
            if (a.windUp / tempo < minTelegraph) continue;
            if (best == null || a.windUp < best.windUp) best = a;
        }
        if (best == null) return false;
        StartCoroutine(TableAttackRoutine(best, true));
        return true;
    }

    /// <summary>Debug / verification: run a specific table attack immediately (ignores selection rules).</summary>
    public bool DebugForceAttack(int index)
    {
        if (!UsesAttackTable || index < 0 || index >= attackTable.Count || busy || controller == null) return false;
        ResolvePlayerTarget();
        StartCoroutine(TableAttackRoutine(attackTable[index]));
        return true;
    }

    IEnumerator TableAttackRoutine(AttackDefinition a) => TableAttackRoutine(a, false);

    IEnumerator TableAttackRoutine(AttackDefinition a, bool counter)
    {
        busy = true;
        if (!controller.BeginAttackTrigger(a.animatorTrigger, counter))
        {
            busy = false;
            yield break;
        }

        a.lastUsedTime = Time.time;
        lastTableAttack = a;
        LastChosenAttack = a;
        CurrentAttack = a;
        CurrentAttackParried = false;
        TableAttackCount++;
        cyclesSinceHeavy = IsBigHit(a.category) ? 0 : cyclesSinceHeavy + 1;
        LastHitLanded = false;
        AttackStarted?.Invoke(this, a.name, a.category.ToString());

        // Tempo (enrage): every authored time is divided by it; the Animator speed is scaled to match.
        float tempo = Mathf.Max(0.25f, tempoScale);
        float windUp = a.windUp / tempo;
        // Telegraph variance and the fairness floor: the first wind-up may move by +/- telegraphJitter but never under
        // minTelegraph; the wind-up animation is time-scaled by the same ratio so the blow still lands on the damage frame.
        float authoredWindUp = windUp;
        if (telegraphJitter > 0f) windUp += Random.Range(-telegraphJitter, telegraphJitter);
        if (minTelegraph > 0f) windUp = Mathf.Max(windUp, minTelegraph);
        windUp = Mathf.Max(0.05f, windUp);
        attackTimeScale = authoredWindUp / windUp;
        ApplyAnimSpeed();
        ShortestTelegraph = Mathf.Min(ShortestTelegraph, windUp);
        LongestTelegraph = Mathf.Max(LongestTelegraph, windUp);

        // --- Telegraph ---
        if (a.unblockable && a.warningClip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(a.warningClip, a.warningVolume);
            SfxTrace.Note(this, a.warningClip, "monster attack: unblockable warning");
        }
        if (a.windUpClip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(a.windUpClip, a.windUpVolume);
            SfxTrace.Note(this, a.windUpClip, "monster attack: wind-up telegraph");
        }
        StartCoroutine(TelegraphFlash(windUp, a.telegraphColor));
        if (a.fireVfx) SetWeaponFire(true);
        // The impact zone is fixed where it was shown (the clamp may slide the root back during the slam).
        lockedImpact = ImpactPoint(a);
        hasLockedImpact = true;
        if (a.unblockable && dangerZone != null)
            dangerZone.Show(lockedImpact, a.dodgeRadius, windUp);
        inWindUp = true;

        float start = AiTime;
        if (a.rangedKind == RangedKind.Rush) StartCoroutine(RushTravel(a, start, tempo));
        bool throws = a.rangedKind == RangedKind.Throw && axeThrow != null;
        int hits = 1 + (a.followUpHits != null ? a.followUpHits.Length : 0);
        for (int i = 0; i < hits; i++)
        {
            float hitAt = windUp + (i == 0 ? 0f : a.followUpHits[i - 1] / tempo);

            float whooshAt = start + Mathf.Max(0f, hitAt - swingSfxLead);
            strikeFrom = whooshAt; strikeUntil = start + hitAt + strikeWindowAfterHit / tempo;
            while (AiTime < whooshAt) yield return null;
            if (a.swingClip != null && sfxAudioSource != null)
            {
                sfxAudioSource.PlayOneShot(a.swingClip, a.swingVolume);
                SfxTrace.Note(this, a.swingClip, "monster attack: swing whoosh");
            }

            while (AiTime < start + hitAt) yield return null;
            inWindUp = false;
            if (i == 0 && attackTimeScale != 1f) { attackTimeScale = 1f; ApplyAnimSpeed(); } // follow-ups at the authored timing
            if (controller == null || !controller.isAlive) break;

            if (throws)
            {
                // Release frame: the axe flies; MonsterAxeThrow resolves hit / dodge / deflect and brings it back.
                int axe = a.throwAxes != null && i < a.throwAxes.Length ? a.throwAxes[i] : 0;
                float tmul = a.damageMultiplier * (CurrentAttackParried ? parryDamageMultiplier : 1f);
                axeThrow.Launch(axe, this, tmul, start + a.catchTime / tempo);
                continue;
            }

            if (a.impactClip != null && sfxAudioSource != null)
            {
                sfxAudioSource.PlayOneShot(a.impactClip, a.impactVolume);
                SfxTrace.Note(this, a.impactClip, "monster attack: impact frame");
            }
            if (a.fireVfx || a.unblockable) PlayImpactBurst();
            if (a.unblockable && dangerZone != null) dangerZone.Impact();

            bool reached;
            if (a.unblockable)
                reached = IsTargetInImpactZone(a); // arc and attack range do not matter: only leaving the zone does
            else
            {
                float arc = (i > 0 && a.followUpArcs != null && i - 1 < a.followUpArcs.Length) ? a.followUpArcs[i - 1] : a.hitArc;
                reached = IsTargetInSwing(arc);
            }

            if (reached)
            {
                // Unblockable: full multiplier, block bypassed in PlayerHealth. Normal: a parry during the wind-up reduces it.
                float mul = a.damageMultiplier * (!a.unblockable && CurrentAttackParried ? parryDamageMultiplier : 1f);
                LastHitDamageMultiplier = mul;
                controller.PerformAttackHit(mul, a.unblockable); // routes through PlayerHealth
                LastHitLanded = true;
            }
        }
        inWindUp = false;
        if (throws)
        {
            // Hold the catch pose until both axes are back (they are timed to land in the hand at catchTime).
            float giveUp = start + a.catchTime / tempo + 1.5f;
            while ((axeThrow.AnyInFlight || AiTime < start + a.catchTime / tempo) && AiTime < giveUp && controller != null && controller.isAlive)
                yield return null;
            axeThrow.RecallAll();
        }
        if (a.fireVfx) SetWeaponFire(false);
        if (controller != null && controller.isAlive)
            AttackResolved?.Invoke(this, a.name, LastHitLanded);

        if (attackTimeScale != 1f) { attackTimeScale = 1f; ApplyAnimSpeed(); }
        hasLockedImpact = false;
        float recoverEnd = AiTime + a.recover / tempo;
        while (AiTime < recoverEnd) yield return null;
        CurrentAttack = null;

        if (controller != null && controller.isAlive)
            controller.EndAttack();
        idleSince = Time.time;

        // Chain: another attack after a short gap instead of the cooldown (each keeps its own telegraph >= minTelegraph).
        float chainChance = ByPhase(chainChanceByPhase, Phase);
        if (chainChance > 0f && chainLink + 1 < chainMaxAttacks && controller != null && controller.isAlive && !holdActions &&
            a.rangedKind == RangedKind.None && DistanceToTarget() <= controller.attackRange * 1.15f && Random.value < chainChance)
        {
            var next = ChooseChainFollowUp(a);
            if (next != null)
            {
                float gapEnd = AiTime + Random.Range(chainGap.x, chainGap.y);
                while (AiTime < gapEnd && controller != null && controller.isAlive && !holdActions) yield return null;
                if (controller != null && controller.isAlive && !holdActions)
                {
                    chainLink++;
                    ChainedAttacks++;
                    LongestChain = Mathf.Max(LongestChain, chainLink + 1);
                    StartCoroutine(TableAttackRoutine(next, true)); // busy stays set: no guard, no new pick in between
                    yield break;
                }
            }
        }
        chainLink = 0;
        busy = false;
    }

    /// <summary>A blockable melee row to link after <paramref name="previous"/> (weighted, never the same row twice in a row).</summary>
    AttackDefinition ChooseChainFollowUp(AttackDefinition previous)
    {
        float hp = HpFraction(), hurt = 1f - hp, total = 0f;
        AttackDefinition pick = null;
        foreach (var a in attackTable)
        {
            if (a == previous || a.unblockable || a.rangedKind != RangedKind.None || !IsEligible(a, hp, 0f, true)) continue;
            float w = SelectionWeight(a, hurt);
            if (w <= 0f) continue;
            total += w;
            if (Random.value * total < w) pick = a; // reservoir pick, no allocation
        }
        return pick;
    }

    public Vector3 ImpactPoint(AttackDefinition a)
    {
        Vector3 fwd = transform.forward; fwd.y = 0f;
        return transform.position + fwd.normalized * a.impactForward;
    }

    /// <summary>Unblockable hit test: player's head (horizontal) inside dodgeRadius of the impact point.</summary>
    public bool IsTargetInImpactZone(AttackDefinition a)
    {
        if (playerTarget == null) return false;
        Vector3 d = playerTarget.position - (hasLockedImpact ? lockedImpact : ImpactPoint(a));
        d.y = 0f;
        return d.magnitude <= a.dodgeRadius;
    }

    /// <summary>Hit frame check: in range (small tolerance) AND inside the attack's swing arc.</summary>
    bool IsTargetInSwing(float hitArc)
    {
        if (playerTarget == null) return false;
        float pushback = personalSpace != null ? personalSpace.AttackPushback : 0f;
        if (DistanceToTarget() - pushback > controller.attackRange * 1.15f) return false;
        Vector3 to = playerTarget.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(transform.forward, to) <= hitArc;
    }

    void SetWeaponFire(bool on)
    {
        if (weaponFireVfx != null)
        {
            if (on) weaponFireVfx.Play(true);
            else weaponFireVfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (extraWeaponFireVfx != null)
            foreach (var ps in extraWeaponFireVfx)
            {
                if (ps == null) continue;
                if (on) ps.Play(true);
                else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
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
        AttackStarted?.Invoke(this, pattern.ToString(), pattern.ToString());

        float baseHitDelay = pattern == AttackPattern.Heavy ? heavyHitDelay : (pattern == AttackPattern.Special ? specialHitDelay : slashHitDelay);
        float baseRecover = pattern == AttackPattern.Heavy ? heavyRecoverDelay : (pattern == AttackPattern.Special ? specialRecoverDelay : slashRecoverDelay);
        float damageMultiplier = pattern == AttackPattern.Heavy ? heavyDamageMultiplier : (pattern == AttackPattern.Special ? specialDamageMultiplier : slashDamageMultiplier);

        // --- Telegraph: visual + audio cue at wind-up start ---
        if (telegraphCueClip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(telegraphCueClip, telegraphCueVolume);
            SfxTrace.Note(this, telegraphCueClip, "monster attack (pattern): telegraph");
        }
        float windUp = baseHitDelay / Mathf.Max(0.5f, aggression);
        Color cueColor = pattern == AttackPattern.Heavy ? heavyTelegraphColor : telegraphColor;
        StartCoroutine(TelegraphFlash(windUp, cueColor));

        yield return new WaitForSeconds(windUp);

        // Attack swing SFX at the hit window.
        if (attackSwingClip != null && sfxAudioSource != null)
        {
            sfxAudioSource.PlayOneShot(attackSwingClip, attackSwingVolume);
            SfxTrace.Note(this, attackSwingClip, "monster attack (pattern): swing");
        }

        // --- Hit frame: apply damage if player still in range and didn't move away ---
        if (controller != null && controller.isAlive && playerTarget != null)
        {
            float dist = Vector3.Distance(transform.position, playerTarget.position);
            bool inRange = dist <= controller.attackRange * 1.15f;
            if (inRange)
            {
                controller.PerformAttackHit(damageMultiplier); // routes through PlayerHealth
            }
            AttackResolved?.Invoke(this, pattern.ToString(), inRange);
        }

        yield return new WaitForSeconds(baseRecover / Mathf.Max(0.5f, aggression));

        if (controller != null && controller.isAlive)
            controller.EndAttack();

        busy = false;
    }

    IEnumerator TelegraphFlash(float duration, Color cueColor)
    {
        if (telegraphRenderer == null) yield break;
        if (extraTintRenderers != null && extraTintRenderers.Length > 0)
        {
            // Split-mesh monsters: tint every part, then return to the resting tint (keeps the enrage colour).
            SetBodyTint(cueColor);
            yield return new WaitForSeconds(duration);
            if (!IsDefending) SetBodyTint(restingTint);
            yield break;
        }
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
