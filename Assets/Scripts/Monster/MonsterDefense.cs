// MonsterDefense.cs
// Purpose: The monster's combat sense, one component shared by all three monsters and parameterised per level:
//          - Awareness: turns to face the player between attacks at a capped turn speed. Once an attack's wind-up
//            starts it stops turning (MonsterAI locks the aim), so physically stepping aside beats the attack.
//          - Swing detection: a player weapon faster than detectSpeed, within detectRange of the guard target and moving
//            at it (cone check, not speed alone) is an incoming swing. One roll per swing.
//          - Reactive guard: on a successful roll the guard goes up after reactionDelay, so a very fast swing can still
//            beat it. Fairness limits: only while the monster is free (never during the entrance, a wind-up, an
//            attack, a flinch, a phase shift or a stagger), a minimum gap between guards, a guard length window and
//            an uptime budget. The timed fallback guard obeys the same rules. MonsterAI.IsDefending stays the single
//            "is defending" flag: this component only decides when MonsterAI.BeginGuard runs.
//          - Guard meter: body blocks absorbed while guarding fill it (weapon parries never do). At guardBreakHits the
//            guard breaks: stagger, extra damage taken, then a guard lockout.
//          - Poise: which swing tiers make it flinch (a flinch never cancels an attack; MonsterController checks that).
//          - Zone guard: every incoming swing is classed HIGH (above chest height, downward), MID (horizontal / lateral) or LOW
//            (aimed below the waist) in the monster's own space, and the guard raised for it covers zones of the body by contact
//            height (fraction of the monster's height): high guard = head + upper torso, mid = torso, low = legs + lower torso.
//            A hit on a covered zone is blocked (reduced, fills the guard meter, block clang pitched per guard); a hit on an
//            uncovered zone lands in full plus openingBonus with an "OPENING" cue. One guard clip per monster exists, so all
//            three guards play it; the zone logic lives here. MonsterController.WouldBlock is the one block-or-not decision.
//          - Counter: after a guard that absorbed counterMinAbsorbed hits the monster may answer with its fastest attack
//            within counterWithin seconds (never with less than counterMinTelegraph of telegraph).
//          - Swing classes (swingClasses, on by default): every incoming swing is one of OVERHEAD, DIAGONAL, HORIZONTAL_LEFT,
//            HORIZONTAL_RIGHT, LOW_SWEEP, UPPERCUT, THRUST, from the weapon's velocity in the monster's own space (left / right
//            as the PLAYER sees it: HORIZONTAL_LEFT comes in from the player's left). The predicted contact point is the
//            weapon's striking part plus velocity x reactionDelay, clamped to guardReach around the chest. Each class gets a
//            guard pose: HIGH (overhead, diagonal), SIDE_LEFT / SIDE_RIGHT (horizontals), LOW (low sweep, uppercut), and the
//            pose nearest the predicted contact for a thrust. A pose that has no way to be shown (sideGuards / lowGuard off)
//            falls back to the high guard, which is what the one guard clip shows. MonsterGuardIK can move the weapon hand
//            toward the predicted contact while a guard is up.
//          - Adaptive guard (rule-based, not learned): the last memoryLength swing classes are remembered with their time.
//            repeatThreshold swings of one class (or one side) inside repeatWindow seconds raise that class's guard chance by
//            adaptiveStep (capped at adaptiveMaxBoost); varied swings and time decay it, so varied swings keep the base chance.
//          - Fairness extras: after maxBlocksInARow blocks in a row the next swing cannot be guarded (it lands as an opening).
//          - Opening cue: a hit on a zone the raised guard leaves open plays openingClip and a brief white flash.
//          - Telemetry: GuardEnded (one report per guard) and OpeningLanded (one per opening hit) static events.
//          - Dodge (needs sidestep/backstep clips; off unless enabled).
//          - Levels 2 and 3 (a MonsterTactics with a DefenseProfile on the same object): MonsterTactics predicts the swings and decides
//            every guard (RaisePlannedGuard), its guard meter breaks the guard (BreakGuardNow) and it strafes through Recentre. The
//            detection roll, timed guard, blocks-in-a-row rule, hit-count meter and counter here then stand down. Level 1 is unchanged.
// Dependencies: MonsterAI, MonsterController, MonsterAnimator, PlayerCombat, HitDetection, XPManager (tiers),
//               CombatUIController (guard meter), MonsterEntrance / BossPhaseController (optional)
// Attach to: Monster root (with MonsterAI)

using UnityEngine;

public class MonsterDefense : MonoBehaviour
{
    public enum SwingTier { Tap = 0, Light = 1, Medium = 2, Full = 3 }
    public enum GuardDirection { High, Side }
    /// <summary>Swing class and the guard raised against it.</summary>
    public enum GuardZone { High = 0, Mid = 1, Low = 2 }
    /// <summary>Seven swing classes (left / right as the player sees it).</summary>
    public enum SwingClass { Overhead = 0, Diagonal = 1, HorizontalLeft = 2, HorizontalRight = 3, LowSweep = 4, Uppercut = 5, Thrust = 6 }
    /// <summary>Guard pose raised for a swing class. SideLeft covers the side on the player's left (the monster's right).</summary>
    public enum GuardPose { High = 0, SideLeft = 1, SideRight = 2, Low = 3 }
    public const int ClassCount = 7, PoseCount = 4;
    public static readonly string[] ClassNames = { "OVERHEAD", "DIAGONAL", "HORIZONTAL_LEFT", "HORIZONTAL_RIGHT", "LOW_SWEEP", "UPPERCUT", "THRUST" };
    public static readonly string[] PoseNames = { "HIGH", "SIDE_LEFT", "SIDE_RIGHT", "LOW" };

    /// <summary>One report per guard, sent when it drops (telemetry, verification).</summary>
    public struct GuardReport
    {
        public SwingClass swingClass;
        public GuardPose pose;
        public bool reactive;
        public bool broken;
        public int blocked;
        public int openings;
        public float damagePrevented;
        public float seconds;
        public float chance;
    }
    /// <summary>A guard dropped (or broke). Static, so telemetry needs no reference to the monster.</summary>
    public static event System.Action<MonsterDefense, GuardReport> GuardEnded;
    /// <summary>A hit landed on a zone the raised guard leaves open: swing class, bonus damage.</summary>
    public static event System.Action<MonsterDefense, SwingClass, float> OpeningLanded;

    [Header("Awareness")]
    [Tooltip("Turn to face the player between attacks. Off = MonsterAI's legacy facing.")]
    public bool turnToFacePlayer = true;
    [Tooltip("Turn speed cap (degrees/second) while free. Never turns during an attack.")]
    public float turnSpeed = 120f;
    [Tooltip("Damped turn: no turning while the aim error is under turnDeadZone, so head sway never swings the monster. " +
             "Off = the old constant-speed turn every frame.")]
    public bool dampedTurn = true;
    [Tooltip("Aim error (deg) that starts a turn.")]
    public float turnDeadZone = 4f;
    [Tooltip("A started turn continues until the aim error is under this (deg), so it settles instead of stopping at the edge.")]
    public float turnSettleAngle = 1f;
    [Tooltip("Turn rate per degree of aim error (1/s): the turn slows as it lines up and never overshoots.")]
    public float turnGain = 4f;
    [Tooltip("Max change of the turn rate (deg/s per second): turns ease in.")]
    public float turnAcceleration = 360f;

    [Header("Centre line")]
    [Tooltip("While free, ease back onto the centre line (the spawn direction, through the player's head) after an attack, " +
             "the personal-space clamp or a rush left the monster off to one side. Never during an attack, guard, flinch, " +
             "stagger or the entrance.")]
    public bool recentre = true;
    [Tooltip("Lateral offset (m) tolerated before easing back. Wider than a standing player's head sway (about 4 cm), so the monster never chases the sway.")]
    public float recentreDeadZone = 0.08f;
    [Tooltip("Max sideways speed (m/s) while easing back.")]
    public float recentreSpeed = 0.35f;
    [Tooltip("Sideways speed per metre of offset (1/s).")]
    public float recentreGain = 2f;

    [Header("Swing detection")]
    [Tooltip("Weapon speed (m/s) that counts as an incoming swing (the weapon's hit gate is used if higher).")]
    public float detectSpeed = 2.5f;
    [Tooltip("The weapon must be within this distance (m) of the guard target.")]
    public float detectRange = 1.5f;
    [Tooltip("Half-angle (deg) of the cone between the weapon's velocity and the direction to the guard target.")]
    [Range(5f, 90f)] public float detectConeAngle = 40f;
    [Tooltip("What the swing has to be aimed at (the chest). Root + 1.3 m if empty.")]
    public Transform guardTarget;

    [Header("Reactive guard")]
    public bool reactiveGuard = true;
    [Tooltip("Seconds from detection to the guard being raised. A swing faster than this beats the guard.")]
    public float reactionDelay = 0.25f;
    [Tooltip("Chance to guard one incoming swing.")]
    [Range(0f, 1f)] public float guardChance = 0.3f;
    [Tooltip("Chance while enraged (MonsterAI.enraged).")]
    [Range(0f, 1f)] public float guardChanceEnraged = 0.3f;
    [Tooltip("Minimum seconds from the end of one guard to the start of the next.")]
    public float minGuardGap = 4.5f;
    [Tooltip("Each guard lasts a random time in this window (s).")]
    public Vector2 guardDuration = new Vector2(0.8f, 1.2f);
    [Tooltip("Guards are refused while the guarded share of the fight would go above this.")]
    [Range(0f, 1f)] public float maxGuardUptime = 0.18f;

    [Header("Timed fallback guard (same rules)")]
    public bool timedGuard = true;
    public float timedGuardInterval = 9f;
    [Range(0f, 0.9f)] public float timedGuardJitter = 0.3f;

    [Header("Guard directions")]
    [Tooltip("Trigger for a guard against an overhead swing.")]
    public string highGuardTrigger = "Defend";
    [Tooltip("Trigger for a guard against a horizontal swing. Same as high when only one guard clip exists.")]
    public string sideGuardTrigger = "Defend";

    [Header("Zone guard (block by where the swing goes)")]
    [Tooltip("Class each incoming swing HIGH / MID / LOW and raise the guard for it; hits on a zone that guard does not cover land " +
             "with an opening bonus. Off = every guard covers the whole body (the old behaviour).")]
    public bool zoneGuard = true;
    [Tooltip("Contact height (fraction of the monster's height) under which a hit is on the legs (below the waist).")]
    [Range(0f, 1f)] public float legsTop = 0.45f;
    [Tooltip("Contact height above which a hit is on the head.")]
    [Range(0f, 1f)] public float headBottom = 0.8f;
    [Tooltip("Lower / upper torso split: the high guard covers above it, the low guard below it.")]
    [Range(0f, 1f)] public float torsoSplit = 0.62f;
    [Tooltip("A swing counts as HIGH when it is aimed at or above this height and moving down.")]
    [Range(0f, 1f)] public float highSwingFrom = 0.62f;
    [Tooltip("Damage bonus for a hit on a zone the raised guard does not cover (0.1 = +10 %).")]
    [Range(0f, 1f)] public float openingBonus = 0.1f;
    public string midGuardTrigger = "Defend";
    public string lowGuardTrigger = "Defend";
    [Tooltip("Block clang pitch for the high, mid and low guard (the project has one block clip, so the guards differ by pitch).")]
    public Vector3 blockPitch = new Vector3(1.18f, 1f, 0.82f);
    [Tooltip("Level 3: guard chance added for each boss phase after the first (0.05 = +5 % in phase 2, +10 % enraged).")]
    [Range(0f, 0.2f)] public float guardChancePerPhase = 0f;

    [Header("Swing classes + guard poses (left / right as the player sees it)")]
    [Tooltip("Class swings into the seven classes and raise the matching guard pose. Off = the HIGH / MID / LOW zone guard above.")]
    public bool swingClasses = true;
    [Tooltip("Swing classes: a swing is incoming when its path passes within this horizontal distance (m) of the body axis...")]
    public float pathRadius = 0.45f;
    [Tooltip("...inside this many seconds.")]
    public float pathLookAhead = 0.5f;
    [Tooltip("A swing whose speed across the monster's face is under this share of its speed is a THRUST.")]
    [Range(0.1f, 0.9f)] public float thrustAcrossRatio = 0.5f;
    [Tooltip("Downward share (of the speed across the face) from which a swing is OVERHEAD.")]
    [Range(0f, 1f)] public float overheadVertical = 0.8f;
    [Tooltip("Downward share from which a swing is DIAGONAL (below it: horizontal).")]
    [Range(0f, 1f)] public float diagonalVertical = 0.35f;
    [Tooltip("Upward share from which a swing is an UPPERCUT.")]
    [Range(0f, 1f)] public float uppercutVertical = 0.6f;
    [Tooltip("The predicted contact point is clamped inside this radius (m) around the guard target: where the monster can reach.")]
    public float guardReach = 0.9f;
    [Tooltip("A side guard also covers this far (m) past the body's centre line.")]
    public float sideOverlap = 0.1f;
    [Tooltip("Side guards can be shown (by a clip or MonsterGuardIK). Off: horizontals get the high guard.")]
    public bool sideGuards = false;
    [Tooltip("The low guard can be shown (by a clip or MonsterGuardIK). Off: low sweeps and uppercuts get the high guard.")]
    public bool lowGuard = false;
    public string sideLeftGuardTrigger = "Defend";
    public string sideRightGuardTrigger = "Defend";
    public string lowPoseGuardTrigger = "Defend";
    [Tooltip("Block clang pitch for the HIGH, SIDE_LEFT, SIDE_RIGHT and LOW guard.")]
    public Vector4 posePitch = new Vector4(1.18f, 1.06f, 0.95f, 0.82f);
    [Tooltip("Random pitch variation (+-) on every block clang.")]
    [Range(0f, 0.2f)] public float blockPitchJitter = 0.04f;

    [Header("Adaptive guard (rule-based)")]
    [Tooltip("Remember the player's last swings; a repeated class (or side) raises the guard chance for it.")]
    public bool adaptiveGuard = true;
    [Range(1, 16)] public int memoryLength = 6;
    [Tooltip("Swings of one class (or one side) inside repeatWindow that count as a pattern.")]
    public int repeatThreshold = 3;
    public float repeatWindow = 8f;
    [Tooltip("Guard chance added to a repeated class (0.15 = +15 points).")]
    [Range(0f, 0.5f)] public float adaptiveStep = 0.15f;
    [Range(0f, 0.5f)] public float adaptiveMaxBoost = 0.15f;
    [Tooltip("Boost lost by every other class each time the player swings a different class.")]
    [Range(0f, 0.5f)] public float decayPerVariedSwing = 0.05f;
    [Tooltip("Boost lost per second.")]
    [Range(0f, 0.5f)] public float decayPerSecond = 0.02f;
    [Tooltip("The guard chance never goes above this, boost included.")]
    [Range(0f, 1f)] public float maxGuardChance = 0.8f;

    [Header("Fairness extras")]
    [Tooltip("After this many blocks in a row the next swing cannot be guarded (0 = off).")]
    public int maxBlocksInARow = 3;
    [Tooltip("How long (s) that next swing has to land as an opening once it is detected.")]
    public float forcedOpeningWindow = 1.0f;

    [Header("Opening cue (a hit on a zone the raised guard leaves open)")]
    public AudioClip openingClip;
    [Range(0f, 1f)] public float openingVolume = 0.7f;
    public float openingPitch = 1.15f;
    public Color openingFlashColor = Color.white;
    public float openingFlashSeconds = 0.08f;

    [Header("Counter (after a guard that absorbed hits)")]
    public bool counterEnabled = false;
    [Range(0f, 1f)] public float counterChance = 0f;
    [Tooltip("Hits the guard must have absorbed before a counter can follow it.")]
    public int counterMinAbsorbed = 2;
    [Tooltip("The counter starts within this many seconds of the guard dropping.")]
    public float counterWithin = 0.5f;
    [Tooltip("Shortest telegraph (wind-up, s) a counter may have.")]
    public float counterMinTelegraph = 0.45f;

    [Header("Guard cue (distinct from the red attack telegraph)")]
    public Color guardFlashColor = new Color(0.78f, 0.9f, 1f);
    public float guardFlashSeconds = 0.12f;

    [Header("Guard meter / guard break")]
    public bool guardBreakEnabled = true;
    [Tooltip("Body hits absorbed while guarding that break the guard.")]
    public int guardBreakHits = 4;
    [Tooltip("Stagger length after a guard break (s). No attacks, no guard.")]
    public float staggerSeconds = 2f;
    [Tooltip("Hit reaction playback speed during the stagger, so the clip fills it.")]
    public float staggerAnimSpeed = 0.5f;
    [Tooltip("Damage taken while staggered.")]
    public float staggerDamageMultiplier = 1.25f;
    [Tooltip("No guard for this long after the stagger ends (s).")]
    public float guardLockoutAfterBreak = 4f;
    public AudioClip guardBreakClip;
    [Range(0f, 1f)] public float guardBreakVolume = 1f;
    public Color guardBreakFlashColor = new Color(1f, 1f, 1f);
    public float guardBreakFlashSeconds = 0.2f;
    [Tooltip("Thin guard bar under the monster HP bar.")]
    public bool showGuardMeter = true;

    [Header("Poise")]
    [Tooltip("Weakest swing tier that makes the monster flinch.")]
    public SwingTier flinchMinTier = SwingTier.Medium;
    public float flinchCooldown = 0.6f;
    [Tooltip("Seconds a flinch keeps the monster from guarding.")]
    public float flinchWindow = 0.6f;

    [Header("Dodge (needs sidestep / backstep clips; ships off when they are missing)")]
    public bool dodgeEnabled = false;
    public string sidestepTrigger = "";
    public string backstepTrigger = "";
    [Range(0f, 1f)] public float dodgeChance = 0.12f;
    public float dodgeCooldown = 8f;
    public float dodgeDistance = 0.7f;
    public float dodgeSeconds = 0.45f;
    [Tooltip("The dodge never leaves this radius around where the monster spawned (m).")]
    public float dodgeArenaRadius = 3.5f;

    // ── runtime (read-only, verification) ──
    public int GuardMeter { get; private set; }
    public int IncomingSwings { get; private set; }
    public int GuardRolls { get; private set; }
    public int ReactiveGuards { get; private set; }
    public int TimedGuards { get; private set; }
    public int GuardBreaks { get; private set; }
    public int Flinches { get; private set; }
    public int Dodges { get; private set; }
    public int HighGuards { get; private set; }
    public int SideGuards { get; private set; }   // mid + low
    public GuardZone ActiveZone { get; private set; } = GuardZone.Mid;
    /// <summary>Per class (High, Mid, Low): incoming swings, guards raised, blocked hits, opening hits.</summary>
    public readonly int[] SwingsByClass = new int[3], GuardsByZone = new int[3], BlockedByZone = new int[3], OpeningsByZone = new int[3];
    public readonly float[] BlockedDamageByZone = new float[3], OpeningDamageByZone = new float[3];
    public int Counters { get; private set; }
    public int CounterRolls { get; private set; }
    /// <summary>Per swing class: incoming swings, guards raised for it, guards raised with an adaptive boost, rolls, sum of
    /// rolled chances, hits blocked / damage prevented, opening hits / bonus damage (attributed to the class of the swing that landed).</summary>
    public readonly int[] ClassSwings = new int[ClassCount], ClassGuards = new int[ClassCount], ClassBoostedGuards = new int[ClassCount],
        ClassRolls = new int[ClassCount], ClassBlocked = new int[ClassCount], ClassOpenings = new int[ClassCount];
    public readonly float[] ClassChanceSum = new float[ClassCount], ClassPrevented = new float[ClassCount], ClassOpeningBonus = new float[ClassCount];
    public readonly int[] PoseGuards = new int[PoseCount];
    public int ForcedOpenings { get; private set; }
    /// <summary>Successful rolls whose guard was refused when it was due (the monster became busy, flinched...). Verification.</summary>
    public int PendingRefused { get; private set; }
    public int BlocksInARow { get; private set; }
    public int MaxBlocksInARowSeen { get; private set; }
    public SwingClass LastClass { get; private set; } = SwingClass.HorizontalLeft;
    public SwingClass ActiveClass { get; private set; } = SwingClass.HorizontalLeft;
    public GuardPose ActivePose { get; private set; } = GuardPose.High;
    /// <summary>Predicted contact point (world) of the swing the current / pending guard answers.</summary>
    public Vector3 PredictedContact { get; private set; }
    /// <summary>Guard chance added to a class by the adaptive memory right now.</summary>
    public float Boost(SwingClass c) => classBoost[(int)c];
    public float SideBoost(int side) => side < 0 ? sideBoost[0] : side > 0 ? sideBoost[1] : 0f;
    public GuardZone LastSwingClass { get; private set; } = GuardZone.Mid;
    public GuardDirection LastGuardDirection { get; private set; }
    public float GuardTime { get; private set; }
    public float FightTime => fightStart >= 0f ? Time.time - fightStart : 0f;
    public float Uptime => FightTime > 0f ? GuardTime / FightTime : 0f;
    public bool IsStaggered => Time.time < staggerUntil;
    public bool InFlinch => Time.time < flinchUntil;
    public bool GuardPending => pendingGuardAt >= 0f;

    private MonsterAI ai;
    private MonsterController ctrl;
    private MonsterAnimator anim;
    private MonsterEntrance entrance;
    private BossPhaseController phases;
    private MonsterTactics tactics;
    /// <summary>Levels 2 and 3: MonsterTactics (with a DefenseProfile) predicts swings and decides every guard; this component's own
    /// detection roll, timed guard, blocks-in-a-row rule, hit-count meter and counter stand down. Level 1 has no tactics.</summary>
    public bool TacticsActive => tactics != null && tactics.Active;
    private float fightStart = -1f;
    private float pendingGuardAt = -1f;
    private GuardDirection pendingDir;
    private float lastGuardEnd = -999f, nextTimedGuard, guardLockUntil, staggerUntil = -1f, flinchUntil = -1f, lastFlinch = -999f, nextDodge;
    private bool wasDefending;
    private readonly bool[] armed = { true, true };
    private readonly HitDetection[] hd = new HitDetection[2];
    private readonly Transform[] hdOwner = new Transform[2];
    private int shownMeter = -1;
    private Vector3 home;
    private bool dodgeWarned;
    private bool turning;
    private float turnRate, recentreVel;
    private readonly Collider[] hdCol = new Collider[2];
    private GuardZone pendingZone = GuardZone.Mid;
    private int absorbedThisGuard;
    private float counterAt = -1f;
    private float height = -1f;
    private AudioSource blockSource;
    private Vector3 centreDir;   // horizontal, from the player's head toward the monster at fight start
    // Swing classes / adaptive memory (fixed arrays, no allocation).
    private SwingClass pendingClass;
    private GuardPose pendingPose;
    private Vector3 pendingContact;
    private float pendingChance, activeChance;
    private readonly SwingClass[] lastClassByHand = { SwingClass.HorizontalLeft, SwingClass.HorizontalLeft };
    private readonly SwingClass[] memClass = new SwingClass[16];
    private readonly float[] memTime = new float[16];
    private readonly sbyte[] memSide = new sbyte[16];
    private int memHead, memCount;
    private readonly float[] classBoost = new float[ClassCount];
    private readonly float[] sideBoost = new float[2];   // [0] = player's left, [1] = player's right
    private bool forceOpenNext;          // armed by maxBlocksInARow, consumed by the next detected swing
    private float forcedUntil = -1f;     // that swing's window: it cannot be blocked or guarded
    private int openingsThisGuard;
    private float preventedThisGuard, guardStartedAt;
    private bool guardBroke, activeReactive;
    /// <summary>Signed lateral offset (m) from the centre line, measured by Recentre (verification).</summary>
    public float LateralOffset { get; private set; }

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        ctrl = GetComponent<MonsterController>();
        anim = GetComponent<MonsterAnimator>();
        entrance = GetComponent<MonsterEntrance>();
        phases = GetComponent<BossPhaseController>();
        tactics = GetComponent<MonsterTactics>();
    }

    void Start()
    {
        home = transform.position;
    }

    void OnDisable()
    {
        if (showGuardMeter && CombatUIController.Instance != null) CombatUIController.Instance.SetGuardMeter(0, 0);
    }

    bool FightActive()
    {
        if (ctrl == null || !ctrl.isAlive || ai == null || !ai.enabled) return false;
        if (entrance != null && !entrance.Complete) return false;
        return BattleManager.Instance == null || BattleManager.Instance.IsBattleActive;
    }

    void Update()
    {
        if (GamePause.IsPaused) return;   // no guard reaction to the player's hands while paused
        if (!FightActive()) { pendingGuardAt = -1f; return; }
        if (fightStart < 0f)
        {
            fightStart = Time.time; nextTimedGuard = Time.time + TimedInterval(); PushMeter();
            if (ai.playerTarget != null) { centreDir = transform.position - ai.playerTarget.position; centreDir.y = 0f; centreDir.Normalize(); }
        }

        // Uptime bookkeeping + guard end time (for the gap rule).
        bool defending = ai.IsDefending;
        if (defending) GuardTime += Time.deltaTime;
        if (wasDefending && !defending)
        {
            lastGuardEnd = Time.time;
            ReportGuardEnd();
            // Counter: the guard soaked up enough hits; answer with a fast attack shortly after it drops.
            if (counterEnabled && !TacticsActive && absorbedThisGuard >= counterMinAbsorbed && !IsStaggered)
            {
                CounterRolls++;
                if (Random.value < counterChance) counterAt = Time.time + Random.Range(0.05f, Mathf.Max(0.06f, counterWithin));
            }
        }
        wasDefending = defending;
        if (counterAt >= 0f && Time.time >= counterAt)
        {
            counterAt = -1f;
            if (!IsStaggered && !(phases != null && phases.InPhaseShift) && ai.BeginCounter(counterMinTelegraph)) Counters++;
        }

        Turn();
        Recentre();
        if (TacticsActive) return;   // MonsterTactics predicts the swings and raises every guard
        DecayBoosts(Time.deltaTime);
        DetectSwings();

        if (pendingGuardAt >= 0f && Time.time >= pendingGuardAt)
        {
            pendingGuardAt = -1f;
            if (!CanGuardNow(true)) PendingRefused++;
            else
            {
                if (swingClasses) { if (!RaiseGuard(true, pendingClass, pendingPose, pendingContact, pendingChance)) PendingRefused++; }
                else RaiseGuard(true, zoneGuard ? pendingZone : (pendingDir == GuardDirection.High ? GuardZone.High : GuardZone.Mid));
            }
        }

        if (timedGuard && Time.time >= nextTimedGuard)
        {
            // Not free right now: try again shortly instead of waiting a whole interval. The timed guard covers where the
            // player's last swing went.
            bool raised = CanGuardNow(false) && (swingClasses
                ? RaiseGuard(false, LastClass, PoseFor(LastClass, PredictedContact), PredictedContact, 0f)
                : RaiseGuard(false, LastSwingClass));
            nextTimedGuard = Time.time + (raised ? TimedInterval() : 1f);
        }
    }

    void ReportGuardEnd()
    {
        if (!swingClasses) return;
        var r = new GuardReport
        {
            swingClass = ActiveClass, pose = ActivePose, reactive = activeReactive, broken = guardBroke,
            blocked = absorbedThisGuard, openings = openingsThisGuard, damagePrevented = preventedThisGuard,
            seconds = Time.time - guardStartedAt, chance = activeChance
        };
        guardBroke = false;
        GuardEnded?.Invoke(this, r);
    }

    float TimedInterval() => timedGuardInterval * (1f + Random.Range(-timedGuardJitter, timedGuardJitter));

    // ───────────────────────── awareness ─────────────────────────
    void Turn()
    {
        // Aim locked from the start of a wind-up through the attack; no turning in a stagger or a flinch either.
        if (!turnToFacePlayer || ctrl.isAttacking || IsStaggered || (dampedTurn && InFlinch) || ai.playerTarget == null)
        {
            turning = false; turnRate = 0f;
            return;
        }
        Vector3 dir = ai.playerTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        if (!dampedTurn)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
            return;
        }
        Vector3 fwd = transform.forward; fwd.y = 0f;
        float err = Vector3.SignedAngle(fwd, dir, Vector3.up);
        float abs = Mathf.Abs(err);
        if (!turning && abs > turnDeadZone) turning = true;
        else if (turning && abs < turnSettleAngle) turning = false;
        float want = turning ? Mathf.Clamp(err * turnGain, -turnSpeed, turnSpeed) : 0f;
        if (turnRate != 0f && Mathf.Sign(turnRate) != Mathf.Sign(err)) turnRate = 0f; // never swing back past the target
        turnRate = Mathf.MoveTowards(turnRate, want, turnAcceleration * Time.deltaTime);
        float step = turnRate * Time.deltaTime;
        if (Mathf.Abs(step) > abs) step = err;
        if (step != 0f) transform.rotation = Quaternion.AngleAxis(step, Vector3.up) * transform.rotation;
    }

    // ───────────────────────── centre line ─────────────────────────
    void Recentre()
    {
        if (ai.playerTarget == null || centreDir.sqrMagnitude < 0.5f) return;
        Vector3 right = Vector3.Cross(Vector3.up, centreDir);
        Vector3 rel = transform.position - ai.playerTarget.position; rel.y = 0f;
        LateralOffset = Vector3.Dot(rel, right);
        bool free = !ctrl.isAttacking && !ai.IsBusy && !ai.holdActions && !IsStaggered && !InFlinch;
        if (!recentre || !free) { recentreVel = 0f; return; }
        // MonsterTactics strafes by moving the line it eases back to (0 = the centre line).
        float off = LateralOffset - (TacticsActive ? tactics.StrafeOffset : 0f);
        float want = Mathf.Abs(off) < recentreDeadZone ? 0f : Mathf.Clamp(-off * recentreGain, -recentreSpeed, recentreSpeed);
        recentreVel = Mathf.MoveTowards(recentreVel, want, 1.5f * Time.deltaTime);   // eases in and out, no snap
        if (recentreVel != 0f) transform.position += right * (recentreVel * Time.deltaTime);
    }

    // ───────────────────────── swing detection ─────────────────────────
    void DetectSwings()
    {
        var pc = PlayerCombat.Instance;
        if (pc == null) return;
        Vector3 target = guardTarget != null ? guardTarget.position : transform.position + Vector3.up * 1.3f;
        float gate = Mathf.Max(detectSpeed, pc.minSwingVelocity);
        for (int h = 0; h < 2; h++)
        {
            Transform w = h == 0 ? pc.weaponTransform : pc.offhandWeaponTransform;
            if (w == null || !w.gameObject.activeInHierarchy) { armed[h] = true; continue; }
            if (hdOwner[h] != w) { hdOwner[h] = w; hd[h] = w.GetComponentInChildren<HitDetection>(); hdCol[h] = hd[h] != null ? hd[h].GetComponent<Collider>() : null; }
            if (hd[h] == null) continue;
            Vector3 vel = hd[h].CurrentVelocity;
            float speed = Mathf.Max(vel.magnitude, h == 0 ? pc.CurrentSwingSpeed : pc.CurrentOffhandSwingSpeed);
            if (speed < gate * 0.5f) { armed[h] = true; continue; } // swing over: the next one may roll
            if (!armed[h] || speed < gate || vel.sqrMagnitude < 0.01f) continue;
            Vector3 to = target - hd[h].transform.position;
            if (to.magnitude > detectRange) continue;
            // Zone guard: the swing may be aimed at any height (legs, torso, head), so the cone points at the monster's body
            // axis at the height of the weapon's striking part instead of always at the chest.
            Vector3 aim = to;
            if (zoneGuard)
            {
                Vector3 strike = hdCol[h] != null ? hdCol[h].bounds.center : hd[h].transform.position;
                float y = Mathf.Clamp(strike.y - transform.position.y, 0.15f * Height, 0.95f * Height);
                aim = transform.position + Vector3.up * y - strike;
            }
            // Swing classes: the cone at the weapon's own height rejects overheads and uppercuts (their velocity is mostly
            // vertical), so the test is whether the swing's path passes within pathRadius of the body axis soon.
            if (swingClasses ? !PathMeetsBody(hdCol[h] != null ? hdCol[h].bounds.center : hd[h].transform.position, vel)
                             : Vector3.Angle(vel, aim) > detectConeAngle) continue;
            armed[h] = false; // one roll per swing
            IncomingSwings++;
            // Class from the striking part (the weapon's collider), not the grip.
            Vector3 head = hdCol[h] != null ? hdCol[h].bounds.center : hd[h].transform.position;
            GuardZone zone = ClassifySwing(head, vel, (target - head).magnitude);
            if (swingClasses)
            {
                Vector3 contact = PredictContact(head, vel, target);
                int side;
                SwingClass c = ClassifySwing7(head, vel, contact, out side);
                lastClassByHand[h] = c;
                OnIncomingSwing(vel, zone, c, side, contact);
            }
            else OnIncomingSwing(vel, zone);
        }
    }

    void OnIncomingSwing(Vector3 velocity, GuardZone swingClass)
    {
        SwingsByClass[(int)swingClass]++;
        LastSwingClass = swingClass;
        if (pendingGuardAt >= 0f || !CanGuardNow(true)) return;

        if (dodgeEnabled && Time.time >= nextDodge && Random.value < dodgeChance && TryDodge(velocity)) return;

        if (!reactiveGuard) return;
        GuardRolls++;
        float chance = (ai.enraged ? guardChanceEnraged : guardChance) + guardChancePerPhase * (phases != null ? phases.Phase : 0);
        if (Random.value >= chance) return;
        pendingGuardAt = Time.time + reactionDelay;
        pendingZone = swingClass;
        pendingDir = swingClass == GuardZone.High ? GuardDirection.High : GuardDirection.Side;
    }

    void OnIncomingSwing(Vector3 velocity, GuardZone zone, SwingClass c, int side, Vector3 contact)
    {
        SwingsByClass[(int)zone]++;
        LastSwingClass = zone;
        ClassSwings[(int)c]++;
        LastClass = c;
        PredictedContact = contact;
        Remember(c, side);   // the memory sees every swing, guarded or not
        if (forceOpenNext)
        {
            // Blocks in a row: this is the swing that cannot be guarded. It gets a short window to land; later swings roll again.
            forceOpenNext = false;
            forcedUntil = Time.time + forcedOpeningWindow;
            RefusedAtDetection[6]++;
            return;
        }
        if (pendingGuardAt >= 0f) { RefusedAtDetection[2]++; return; }   // a guard is already on its way
        if (!CanGuardNow(true)) { RefusedAtDetection[lastRefusalCode]++; return; }

        if (dodgeEnabled && Time.time >= nextDodge && Random.value < dodgeChance && TryDodge(velocity)) return;

        if (!reactiveGuard) return;
        GuardRolls++;
        float chance = ChanceFor(c, side);
        ClassRolls[(int)c]++;
        ClassChanceSum[(int)c] += chance;
        if (Random.value >= chance) return;
        pendingGuardAt = Time.time + reactionDelay;
        pendingZone = zone;
        pendingDir = zone == GuardZone.High ? GuardDirection.High : GuardDirection.Side;
        pendingClass = c;
        pendingPose = PoseFor(c, contact);
        pendingContact = contact;
        pendingChance = chance;
    }

    /// <summary>Base chance (per level, + per boss phase) plus the adaptive boost for this class or side, capped.</summary>
    public float ChanceFor(SwingClass c, int side)
    {
        float baseChance = (ai.enraged ? guardChanceEnraged : guardChance) + guardChancePerPhase * (phases != null ? phases.Phase : 0);
        float boost = adaptiveGuard ? Mathf.Max(classBoost[(int)c], SideBoost(side)) : 0f;
        return Mathf.Min(maxGuardChance, baseChance + boost);
    }

    // ───────────────────────── swing classes ─────────────────────────
    /// <summary>Does the swing's straight path come within pathRadius (horizontal) of the body axis, between the shins and the
    /// top of the head, inside pathLookAhead seconds? Ten steps, no allocation.</summary>
    bool PathMeetsBody(Vector3 pos, Vector3 vel)
    {
        Vector3 axis = transform.position;
        float h = Height, r2 = pathRadius * pathRadius;
        for (int i = 0; i <= 10; i++)
        {
            Vector3 p = pos + vel * (pathLookAhead * i * 0.1f);
            float y = p.y - axis.y;
            if (y < 0.1f * h || y > 1.08f * h) continue;
            float dx = p.x - axis.x, dz = p.z - axis.z;
            if (dx * dx + dz * dz <= r2) return true;
        }
        return false;
    }

    /// <summary>Weapon striking part + velocity x reactionDelay (never past the swing's closest approach to the body axis, so a
    /// fast swing is not predicted out through the monster's back), clamped to guardReach around the guard target.</summary>
    Vector3 PredictContact(Vector3 weaponPos, Vector3 velocity, Vector3 target)
    {
        Vector3 axis = transform.position; axis.y = weaponPos.y;
        Vector3 flat = velocity; flat.y = 0f;
        float tArrive = flat.sqrMagnitude > 0.0001f ? Mathf.Max(0f, Vector3.Dot(axis - weaponPos, flat) / flat.sqrMagnitude) : reactionDelay;
        Vector3 p = weaponPos + velocity * Mathf.Min(reactionDelay, tArrive);
        Vector3 d = p - target;
        if (d.sqrMagnitude > guardReach * guardReach) p = target + d.normalized * guardReach;
        return p;
    }

    /// <summary>Seven-way class from the velocity in the monster's space. side: -1 from the player's left, +1 from the right, 0 none.</summary>
    SwingClass ClassifySwing7(Vector3 weaponPos, Vector3 velocity, Vector3 contact, out int side)
    {
        Vector3 v = transform.InverseTransformDirection(velocity);
        float speed = v.magnitude;
        float across = Mathf.Sqrt(v.x * v.x + v.y * v.y);   // motion across the monster's face
        // The monster's +x is the player's left: a swing moving toward -x came in from the player's left.
        side = Mathf.Abs(v.x) < 0.3f * Mathf.Max(0.01f, across) ? 0 : (v.x < 0f ? -1 : 1);
        if (speed < 0.01f || across < thrustAcrossRatio * speed) { side = 0; return SwingClass.Thrust; }
        // Height where the swing meets the body axis (not the reaction-delay point, which overshoots a downward swing).
        float f = HeightFraction(ArrivalPoint(weaponPos, velocity));
        if (v.y > uppercutVertical * across) { side = 0; return SwingClass.Uppercut; }
        if (v.y < -overheadVertical * across) { side = 0; return SwingClass.Overhead; }
        if (v.y < -diagonalVertical * across && f >= legsTop) return SwingClass.Diagonal;
        if (f < legsTop) return SwingClass.LowSweep;
        return side < 0 ? SwingClass.HorizontalLeft : side > 0 ? SwingClass.HorizontalRight
            : (Vector3.Dot(contact - transform.position, transform.right) >= 0f ? SwingClass.HorizontalLeft : SwingClass.HorizontalRight);
    }

    /// <summary>Point on the swing line nearest the monster's vertical body axis (at most 0.4 s ahead).</summary>
    Vector3 ArrivalPoint(Vector3 pos, Vector3 vel)
    {
        Vector3 axis = transform.position; axis.y = pos.y;
        Vector3 flat = vel; flat.y = 0f;
        float t = flat.sqrMagnitude > 0.0001f ? Vector3.Dot(axis - pos, flat) / flat.sqrMagnitude : 0f;
        return pos + vel * Mathf.Clamp(t, 0f, 0.4f);
    }

    /// <summary>Guard pose for a class. THRUST takes the pose nearest the predicted contact. Poses that cannot be shown fall back to HIGH.</summary>
    public GuardPose PoseFor(SwingClass c, Vector3 contact)
    {
        GuardPose p;
        switch (c)
        {
            case SwingClass.Overhead:
            case SwingClass.Diagonal: p = GuardPose.High; break;
            case SwingClass.HorizontalLeft: p = GuardPose.SideLeft; break;
            case SwingClass.HorizontalRight: p = GuardPose.SideRight; break;
            case SwingClass.LowSweep:
            case SwingClass.Uppercut: p = GuardPose.Low; break;
            default:
                float f = HeightFraction(contact);
                float x = Vector3.Dot(contact - transform.position, transform.right);
                if (f < legsTop) p = GuardPose.Low;
                else if (f >= torsoSplit && Mathf.Abs(x) < 0.2f) p = GuardPose.High;
                else if (Mathf.Abs(x) >= 0.2f) p = x > 0f ? GuardPose.SideLeft : GuardPose.SideRight;
                else p = GuardPose.Low;
                break;
        }
        if ((p == GuardPose.SideLeft || p == GuardPose.SideRight) && !sideGuards) p = GuardPose.High;
        if (p == GuardPose.Low && !lowGuard) p = GuardPose.High;
        return p;
    }

    // ───────────────────────── adaptive memory ─────────────────────────
    void Remember(SwingClass c, int side)
    {
        if (!adaptiveGuard) return;
        int len = Mathf.Clamp(memoryLength, 1, memClass.Length);
        memClass[memHead] = c; memTime[memHead] = Time.time; memSide[memHead] = (sbyte)side;
        memHead = (memHead + 1) % len;
        if (memCount < len) memCount++;
        int sameClass = 0, sameSide = 0;
        for (int i = 0; i < memCount; i++)
        {
            if (Time.time - memTime[i] > repeatWindow) continue;
            if (memClass[i] == c) sameClass++;
            if (side != 0 && memSide[i] == side) sameSide++;
        }
        // Varied swings: every other class decays.
        for (int k = 0; k < ClassCount; k++)
            if (k != (int)c) classBoost[k] = Mathf.Max(0f, classBoost[k] - decayPerVariedSwing);
        for (int s = 0; s < 2; s++)
            if ((s == 0 ? -1 : 1) != side) sideBoost[s] = Mathf.Max(0f, sideBoost[s] - decayPerVariedSwing);
        if (sameClass >= repeatThreshold) classBoost[(int)c] = Mathf.Min(adaptiveMaxBoost, classBoost[(int)c] + adaptiveStep);
        if (side != 0 && sameSide >= repeatThreshold)
        {
            int s = side < 0 ? 0 : 1;
            sideBoost[s] = Mathf.Min(adaptiveMaxBoost, sideBoost[s] + adaptiveStep);
        }
    }

    void DecayBoosts(float dt)
    {
        if (decayPerSecond <= 0f) return;
        float d = decayPerSecond * dt;
        for (int k = 0; k < ClassCount; k++) if (classBoost[k] > 0f) classBoost[k] = Mathf.Max(0f, classBoost[k] - d);
        if (sideBoost[0] > 0f) sideBoost[0] = Mathf.Max(0f, sideBoost[0] - d);
        if (sideBoost[1] > 0f) sideBoost[1] = Mathf.Max(0f, sideBoost[1] - d);
    }

    // ───────────────────────── zones ─────────────────────────
    /// <summary>Monster height (m) in its idle pose: the top of its highest body collider above the root.</summary>
    public float Height
    {
        get
        {
            if (height > 0f) return height;
            float top = 0f;
            foreach (var c in GetComponentsInChildren<Collider>())
            {
                if (c.CompareTag("MonsterWeapon") || c.transform == transform) continue;
                top = Mathf.Max(top, c.bounds.max.y - transform.position.y);
            }
            height = top > 0.5f ? top : 1.8f;
            return height;
        }
    }

    /// <summary>Contact height as a fraction of the monster's height (0 = feet, 1 = top of the head).</summary>
    public float HeightFraction(Vector3 point) => Mathf.Clamp01((point.y - transform.position.y) / Mathf.Max(0.5f, Height));

    /// <summary>HIGH: aimed at or above chest height and moving down. LOW: aimed below the waist. MID: everything else.</summary>
    GuardZone ClassifySwing(Vector3 weaponPos, Vector3 velocity, float distance)
    {
        float speed = velocity.magnitude;
        // Where the weapon will be when it arrives (capped look-ahead).
        float reach = speed > 0.01f ? Mathf.Min(0.25f, distance / speed) : 0f;
        float f = HeightFraction(weaponPos + velocity * reach);
        if (f < legsTop) return GuardZone.Low;
        if (f >= highSwingFrom && velocity.y < -0.35f * speed) return GuardZone.High;
        return GuardZone.Mid;
    }

    /// <summary>Does the active guard cover a hit at this point? (high: head + upper torso, mid: torso, low: legs + lower torso)</summary>
    public bool Covers(Vector3 hitPoint)
    {
        if (forceOpenNext || Time.time < forcedUntil) return false;   // blocks in a row: this swing cannot be guarded
        if (!zoneGuard) return true;
        float f = HeightFraction(hitPoint);
        if (swingClasses)
        {
            float x = Vector3.Dot(hitPoint - transform.position, transform.right);   // + = the player's left
            switch (ActivePose)
            {
                // The guard clips (both monsters' HIGH guard) hold the weapon across the front of the upper body: above the waist.
                case GuardPose.High: return f >= legsTop;
                // With MonsterTactics the low guard is the low / thrust guard: the weapon slants across the front of the body, so a
                // thrust at the belly or chest centre is covered too.
                case GuardPose.Low: return f < torsoSplit || (TacticsActive && f < headBottom && Mathf.Abs(x) <= 0.25f);
                case GuardPose.SideLeft: return f >= legsTop && f < headBottom && x >= -sideOverlap;
                default: return f >= legsTop && f < headBottom && x <= sideOverlap;
            }
        }
        switch (ActiveZone)
        {
            case GuardZone.High: return f >= torsoSplit;
            case GuardZone.Low: return f < torsoSplit;
            default: return f >= legsTop && f < headBottom;
        }
    }

    /// <summary>MonsterController: a hit landed on a zone the raised guard leaves open.</summary>
    public void OnOpeningHit(float damage) => OnOpeningHit(damage, 0f, transform.position);

    /// <param name="bonus">The opening bonus part of <paramref name="damage"/>.</param>
    public void OnOpeningHit(float damage, float bonus, Vector3 hitPoint)
    {
        OpeningsByZone[(int)ActiveZone]++;
        OpeningDamageByZone[(int)ActiveZone] += damage;
        if (forceOpenNext || Time.time < forcedUntil) ForcedOpenings++;
        forceOpenNext = false; forcedUntil = -1f;
        BlocksInARow = 0;
        if (!swingClasses) return;
        openingsThisGuard++;
        var c = ClassOfHitAt(hitPoint);
        ClassOpenings[(int)c]++;
        ClassOpeningBonus[(int)c] += bonus;
        // Opening cue: a brief white flash and its own sound (not the guard's white-blue, not the block clang).
        if (openingFlashSeconds > 0f) ai.FlashTint(openingFlashColor, openingFlashSeconds);
        if (openingClip != null)
        {
            var src = BlockSource();
            src.pitch = openingPitch * (1f + Random.Range(-blockPitchJitter, blockPitchJitter));
            src.PlayOneShot(openingClip, openingVolume);
            SfxTrace.Note(this, openingClip, "opening: hit on a zone the raised guard leaves open");
        }
        OpeningLanded?.Invoke(this, c, bonus);
    }

    /// <summary>MonsterController: a hit landed and was not blocked (no guard up). Ends a run of blocks.</summary>
    public void OnUnblockedHit()
    {
        BlocksInARow = 0;
        forceOpenNext = false; forcedUntil = -1f;
    }

    /// <summary>Class of the swing that landed at hitPoint: the class last read for the hand whose weapon is nearer.</summary>
    SwingClass ClassOfHitAt(Vector3 hitPoint)
    {
        float d0 = hdCol[0] != null ? (hdCol[0].bounds.center - hitPoint).sqrMagnitude : float.MaxValue;
        float d1 = hdCol[1] != null && hdCol[1].gameObject.activeInHierarchy ? (hdCol[1].bounds.center - hitPoint).sqrMagnitude : float.MaxValue;
        return d1 < d0 ? lastClassByHand[1] : lastClassByHand[0];
    }

    AudioSource BlockSource()
    {
        if (blockSource != null) return blockSource;
        var go = new GameObject("GuardBlockAudio");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.3f;
        blockSource = go.AddComponent<AudioSource>();
        var src = ai.sfxAudioSource;
        blockSource.playOnAwake = false;
        blockSource.spatialBlend = src != null ? src.spatialBlend : 1f;
        blockSource.minDistance = src != null ? src.minDistance : 1.5f;
        blockSource.maxDistance = src != null ? src.maxDistance : 20f;
        blockSource.rolloffMode = src != null ? src.rolloffMode : AudioRolloffMode.Logarithmic;
        blockSource.outputAudioMixerGroup = src != null ? src.outputAudioMixerGroup : null;
        return blockSource;
    }

    /// <summary>Block clang for the raised guard (its own pitch), on a dedicated source so other monster sounds keep their pitch.</summary>
    public void PlayBlockClang(AudioClip clip, float volume)
    {
        if (clip == null) return;
        var src = BlockSource();
        float pitch = swingClasses ? posePitch[(int)ActivePose] : ActiveZone == GuardZone.High ? blockPitch.x : ActiveZone == GuardZone.Low ? blockPitch.z : blockPitch.y;
        src.pitch = pitch * (1f + Random.Range(-blockPitchJitter, blockPitchJitter));
        src.PlayOneShot(clip, volume);
        SfxTrace.Note(this, clip, swingClasses ? "monster guard absorbed a landed hit (pose guard)" : "monster guard absorbed a landed hit (zone guard)");
    }

    /// <summary>Fairness gate shared by the reactive and timed guards.</summary>
    public bool CanGuardNow(bool reactive)
    {
        int code = RefusalCode();
        lastRefusalCode = code;
        return code < 0;
    }

    /// <summary>-1 = free to guard; otherwise the index into RefusalNames of the first rule that refuses.</summary>
    int RefusalCode()
    {
        if (!FightActive() || !ai.canDefend) return 0;
        if (ctrl.isAttacking) return 1;                       // wind-up / attack
        if (ai.IsDefending) return 2;
        if (ai.IsBusy || ai.holdActions) return 3;            // stagger / dodge / counter / hold
        if (IsStaggered || Time.time < guardLockUntil) return 4;
        if (InFlinch) return 5;
        if (forceOpenNext || Time.time < forcedUntil) return 6;   // blocks in a row: the next swing is not guarded
        if (phases != null && phases.InPhaseShift) return 7;
        if (Time.time - lastGuardEnd < minGuardGap) return 8;
        float dur = guardDuration.y;
        if (GuardTime + dur > maxGuardUptime * (FightTime + dur)) return 9;
        return -1;
    }

    public static readonly string[] RefusalNames = { "fight/canDefend", "attacking", "already guarding", "busy", "stagger/lockout", "flinch", "blocks in a row", "phase shift", "gap", "uptime" };
    /// <summary>Why a detected swing got no roll (verification): counts per RefusalNames entry.</summary>
    public readonly int[] RefusedAtDetection = new int[10];
    private int lastRefusalCode = -1;
    public string LastRefusal => lastRefusalCode >= 0 ? RefusalNames[lastRefusalCode] : "";

    bool RaiseGuard(bool reactive, GuardDirection dir) => RaiseGuard(reactive, dir == GuardDirection.High ? GuardZone.High : GuardZone.Mid);

    bool RaiseGuard(bool reactive, GuardZone zone)
    {
        float dur = Random.Range(guardDuration.x, guardDuration.y);
        string trigger = zone == GuardZone.High ? highGuardTrigger : zone == GuardZone.Low ? lowGuardTrigger : midGuardTrigger;
        if (!ai.BeginGuard(dur, reactive, trigger, guardFlashColor, guardFlashSeconds)) return false;
        if (reactive) ReactiveGuards++; else TimedGuards++;
        if (zone == GuardZone.High) HighGuards++; else SideGuards++;
        GuardsByZone[(int)zone]++;
        ActiveZone = zone;
        LastGuardDirection = zone == GuardZone.High ? GuardDirection.High : GuardDirection.Side;
        absorbedThisGuard = 0;
        nextTimedGuard = Time.time + TimedInterval(); // a reactive guard also resets the timed one
        return true;
    }

    bool RaiseGuard(bool reactive, SwingClass c, GuardPose pose, Vector3 contact, float chance)
    {
        float dur = Random.Range(guardDuration.x, guardDuration.y);
        string trigger = pose == GuardPose.High ? highGuardTrigger : pose == GuardPose.Low ? lowPoseGuardTrigger
            : pose == GuardPose.SideLeft ? sideLeftGuardTrigger : sideRightGuardTrigger;
        if (!ai.BeginGuard(dur, reactive, trigger, guardFlashColor, guardFlashSeconds)) return false;
        if (reactive) ReactiveGuards++; else TimedGuards++;
        GuardZone zone = pose == GuardPose.High ? GuardZone.High : pose == GuardPose.Low ? GuardZone.Low : GuardZone.Mid;
        if (zone == GuardZone.High) HighGuards++; else SideGuards++;
        GuardsByZone[(int)zone]++;
        ActiveZone = zone;
        LastGuardDirection = zone == GuardZone.High ? GuardDirection.High : GuardDirection.Side;
        ActiveClass = c; ActivePose = pose; PredictedContact = contact;
        ClassGuards[(int)c]++;
        PoseGuards[(int)pose]++;
        if (reactive && adaptiveGuard && Mathf.Max(classBoost[(int)c], Mathf.Max(sideBoost[0], sideBoost[1])) > 0f && chance > ChanceBase() + 0.001f) ClassBoostedGuards[(int)c]++;
        activeReactive = reactive; activeChance = chance; guardStartedAt = Time.time;
        absorbedThisGuard = 0; openingsThisGuard = 0; preventedThisGuard = 0f; guardBroke = false;
        nextTimedGuard = Time.time + TimedInterval(); // a reactive guard also resets the timed one
        return true;
    }

    float ChanceBase() => (ai.enraged ? guardChanceEnraged : guardChance) + guardChancePerPhase * (phases != null ? phases.Phase : 0);

    /// <summary>MonsterTactics: raise the guard for a predicted swing now, for <paramref name="duration"/> seconds. Only the rules that
    /// keep a committed or helpless monster from guarding apply here (fight, attacking, guarding, busy, stagger / lockout, flinch,
    /// phase shift); the gap, uptime and blocks-in-a-row rules are replaced by MonsterTactics' cooldown, rate cap and guard meter.</summary>
    public bool RaisePlannedGuard(SwingClass c, GuardPose pose, Vector3 contact, float duration)
    {
        int code = RefusalCode();
        lastRefusalCode = code == 6 || code == 8 || code == 9 ? -1 : code;
        if (lastRefusalCode >= 0) return false;
        string trigger = pose == GuardPose.High ? highGuardTrigger : pose == GuardPose.Low ? lowPoseGuardTrigger
            : pose == GuardPose.SideLeft ? sideLeftGuardTrigger : sideRightGuardTrigger;
        if (!ai.BeginGuard(duration, true, trigger, guardFlashColor, guardFlashSeconds)) { lastRefusalCode = 3; return false; }
        ReactiveGuards++;
        GuardZone zone = pose == GuardPose.High ? GuardZone.High : pose == GuardPose.Low ? GuardZone.Low : GuardZone.Mid;
        if (zone == GuardZone.High) HighGuards++; else SideGuards++;
        GuardsByZone[(int)zone]++;
        ActiveZone = zone;
        LastGuardDirection = zone == GuardZone.High ? GuardDirection.High : GuardDirection.Side;
        ActiveClass = c; ActivePose = pose; PredictedContact = contact; LastClass = c;
        ClassGuards[(int)c]++;
        PoseGuards[(int)pose]++;
        activeReactive = true; activeChance = 1f; guardStartedAt = Time.time;
        absorbedThisGuard = 0; openingsThisGuard = 0; preventedThisGuard = 0f; guardBroke = false;
        return true;
    }

    /// <summary>MonsterTactics: its guard meter is empty.</summary>
    public void BreakGuardNow() => BreakGuard();

    // ───────────────────────── guard meter / break ─────────────────────────
    /// <summary>MonsterController: a body hit was absorbed by the guard (weapon parries never reach here).</summary>
    public void OnGuardAbsorbedHit() => OnGuardAbsorbedHit(0f);

    public void OnGuardAbsorbedHit(float damage) => OnGuardAbsorbedHit(damage, 0f);

    /// <param name="prevented">Damage the guard took off this hit.</param>
    public void OnGuardAbsorbedHit(float damage, float prevented)
    {
        absorbedThisGuard++;
        BlockedByZone[(int)ActiveZone]++;
        BlockedDamageByZone[(int)ActiveZone] += damage;
        preventedThisGuard += prevented;
        ClassBlocked[(int)ActiveClass]++;
        ClassPrevented[(int)ActiveClass] += prevented;
        BlocksInARow++;
        if (BlocksInARow > MaxBlocksInARowSeen) MaxBlocksInARowSeen = BlocksInARow;
        if (TacticsActive) { tactics.OnGuardAbsorbed(); return; }   // its guard meter drains per block and breaks the guard
        if (maxBlocksInARow > 0 && BlocksInARow >= maxBlocksInARow) forceOpenNext = true;
        if (!guardBreakEnabled || guardBreakHits <= 0) return;
        GuardMeter++;
        if (GuardMeter >= guardBreakHits) BreakGuard();
        PushMeter();
    }

    void BreakGuard()
    {
        GuardMeter = 0;
        GuardBreaks++;
        guardBroke = true;
        BlocksInARow = 0; forceOpenNext = false; forcedUntil = -1f;   // the break is the opening
        pendingGuardAt = -1f;
        staggerUntil = Time.time + staggerSeconds;
        guardLockUntil = staggerUntil + guardLockoutAfterBreak;
        ai.BeginStagger(staggerSeconds, staggerAnimSpeed, guardBreakFlashColor, guardBreakFlashSeconds);
        if (guardBreakClip != null && ai.sfxAudioSource != null)
        {
            ai.sfxAudioSource.PlayOneShot(guardBreakClip, guardBreakVolume);
            SfxTrace.Note(this, guardBreakClip, "monster guard broken");
        }
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.ShowCallout(transform.position + Vector3.up * 2.1f, "GUARD BREAK!", CombatUIController.DamageStyle.Break);
    }

    void PushMeter()
    {
        if (TacticsActive || !showGuardMeter || CombatUIController.Instance == null || GuardMeter == shownMeter) return;   // MonsterTactics drives the bar
        shownMeter = GuardMeter;
        CombatUIController.Instance.SetGuardMeter(GuardMeter, guardBreakEnabled ? guardBreakHits : 0);
    }

    // ───────────────────────── poise ─────────────────────────
    public static SwingTier TierOf(float speed)
    {
        var xp = XPManager.Instance;
        float light = xp != null ? xp.weakSwingSpeed : 1.5f;
        float medium = xp != null ? xp.mediumSwingSpeed : 3f;
        float full = xp != null ? xp.fullSwingSpeed : 5f;
        if (speed >= full) return SwingTier.Full;
        if (speed >= medium) return SwingTier.Medium;
        if (speed >= light) return SwingTier.Light;
        return SwingTier.Tap;
    }

    /// <summary>MonsterController asks before playing a flinch; true also starts the flinch window.</summary>
    public bool TryFlinch(float swingSpeed)
    {
        if (IsStaggered || TierOf(swingSpeed) < flinchMinTier || Time.time - lastFlinch < flinchCooldown) return false;
        lastFlinch = Time.time;
        flinchUntil = Time.time + flinchWindow;
        pendingGuardAt = -1f; // a flinch interrupts the reaction
        Flinches++;
        return true;
    }

    // ───────────────────────── dodge ─────────────────────────
    bool TryDodge(Vector3 swingVelocity)
    {
        bool side = Mathf.Abs(Vector3.Dot(swingVelocity.normalized, transform.right)) < 0.6f; // horizontal swing: step back
        string trigger = side ? sidestepTrigger : backstepTrigger;
        if (string.IsNullOrEmpty(trigger) || anim == null || anim.animator == null || !HasTrigger(trigger))
        {
            if (!dodgeWarned) { dodgeWarned = true; Debug.LogWarning($"[MonsterDefense] Dodge enabled on {name} but trigger '{trigger}' is missing; dodge skipped."); }
            return false;
        }
        Vector3 dir = side ? (Random.value < 0.5f ? transform.right : -transform.right) : -transform.forward;
        dir.y = 0f;
        Vector3 dest = transform.position + dir.normalized * dodgeDistance;
        Vector3 fromHome = dest - home; fromHome.y = 0f;
        if (fromHome.magnitude > dodgeArenaRadius) return false; // would leave the arena
        if (!ai.BeginDodge(trigger, dir.normalized * dodgeDistance, dodgeSeconds)) return false;
        nextDodge = Time.time + dodgeCooldown;
        Dodges++;
        return true;
    }

    bool HasTrigger(string trigger) => anim.HasTrigger(trigger);   // cached per controller in MonsterAnimator (no allocation)

    /// <summary>Resets the counters (test harness).</summary>
    public void ResetStats()
    {
        IncomingSwings = GuardRolls = ReactiveGuards = TimedGuards = GuardBreaks = Flinches = Dodges = HighGuards = SideGuards = 0;
        Counters = CounterRolls = 0;
        for (int i = 0; i < 3; i++) { SwingsByClass[i] = GuardsByZone[i] = BlockedByZone[i] = OpeningsByZone[i] = 0; BlockedDamageByZone[i] = OpeningDamageByZone[i] = 0f; }
        for (int i = 0; i < ClassCount; i++)
        {
            ClassSwings[i] = ClassGuards[i] = ClassBoostedGuards[i] = ClassRolls[i] = ClassBlocked[i] = ClassOpenings[i] = 0;
            ClassChanceSum[i] = ClassPrevented[i] = ClassOpeningBonus[i] = classBoost[i] = 0f;
        }
        for (int i = 0; i < PoseCount; i++) PoseGuards[i] = 0;
        for (int i = 0; i < RefusedAtDetection.Length; i++) RefusedAtDetection[i] = 0;
        PendingRefused = 0;
        sideBoost[0] = sideBoost[1] = 0f;
        memCount = memHead = 0;
        ForcedOpenings = BlocksInARow = MaxBlocksInARowSeen = 0;
        forceOpenNext = false; forcedUntil = -1f;
        GuardTime = 0f; fightStart = FightActive() ? Time.time : -1f;
    }
}
