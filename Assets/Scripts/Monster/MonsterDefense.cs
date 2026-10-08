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
//          - Dodge (needs sidestep/backstep clips; off unless enabled).
// Dependencies: MonsterAI, MonsterController, MonsterAnimator, PlayerCombat, HitDetection, XPManager (tiers),
//               CombatUIController (guard meter), MonsterEntrance / BossPhaseController (optional)
// Attach to: Monster root (with MonsterAI)

using UnityEngine;

public class MonsterDefense : MonoBehaviour
{
    public enum SwingTier { Tap = 0, Light = 1, Medium = 2, Full = 3 }
    public enum GuardDirection { High, Side }

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
    public int SideGuards { get; private set; }
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
    private Vector3 centreDir;   // horizontal, from the player's head toward the monster at fight start
    /// <summary>Signed lateral offset (m) from the centre line, measured by Recentre (verification).</summary>
    public float LateralOffset { get; private set; }

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        ctrl = GetComponent<MonsterController>();
        anim = GetComponent<MonsterAnimator>();
        entrance = GetComponent<MonsterEntrance>();
        phases = GetComponent<BossPhaseController>();
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
        if (!FightActive()) { pendingGuardAt = -1f; return; }
        if (fightStart < 0f)
        {
            fightStart = Time.time; nextTimedGuard = Time.time + TimedInterval(); PushMeter();
            if (ai.playerTarget != null) { centreDir = transform.position - ai.playerTarget.position; centreDir.y = 0f; centreDir.Normalize(); }
        }

        // Uptime bookkeeping + guard end time (for the gap rule).
        bool defending = ai.IsDefending;
        if (defending) GuardTime += Time.deltaTime;
        if (wasDefending && !defending) lastGuardEnd = Time.time;
        wasDefending = defending;

        Turn();
        Recentre();
        DetectSwings();

        if (pendingGuardAt >= 0f && Time.time >= pendingGuardAt)
        {
            pendingGuardAt = -1f;
            if (CanGuardNow(true)) RaiseGuard(true, pendingDir);
        }

        if (timedGuard && Time.time >= nextTimedGuard)
        {
            // Not free right now: try again shortly instead of waiting a whole interval.
            nextTimedGuard = Time.time + (CanGuardNow(false) && RaiseGuard(false, GuardDirection.High) ? TimedInterval() : 1f);
        }
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
        float want = Mathf.Abs(LateralOffset) < recentreDeadZone ? 0f : Mathf.Clamp(-LateralOffset * recentreGain, -recentreSpeed, recentreSpeed);
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
            if (hdOwner[h] != w) { hdOwner[h] = w; hd[h] = w.GetComponentInChildren<HitDetection>(); }
            if (hd[h] == null) continue;
            Vector3 vel = hd[h].CurrentVelocity;
            float speed = Mathf.Max(vel.magnitude, h == 0 ? pc.CurrentSwingSpeed : pc.CurrentOffhandSwingSpeed);
            if (speed < gate * 0.5f) { armed[h] = true; continue; } // swing over: the next one may roll
            if (!armed[h] || speed < gate || vel.sqrMagnitude < 0.01f) continue;
            Vector3 to = target - hd[h].transform.position;
            if (to.magnitude > detectRange) continue;
            if (Vector3.Angle(vel, to) > detectConeAngle) continue;
            armed[h] = false; // one roll per swing
            IncomingSwings++;
            OnIncomingSwing(vel);
        }
    }

    void OnIncomingSwing(Vector3 velocity)
    {
        if (pendingGuardAt >= 0f || !CanGuardNow(true)) return;

        if (dodgeEnabled && Time.time >= nextDodge && Random.value < dodgeChance && TryDodge(velocity)) return;

        if (!reactiveGuard) return;
        GuardRolls++;
        float chance = ai.enraged ? guardChanceEnraged : guardChance;
        if (Random.value >= chance) return;
        pendingGuardAt = Time.time + reactionDelay;
        // Mostly downward = overhead swing -> high guard; anything else -> side guard.
        pendingDir = velocity.y < -0.6f * velocity.magnitude ? GuardDirection.High : GuardDirection.Side;
    }

    /// <summary>Fairness gate shared by the reactive and timed guards.</summary>
    public bool CanGuardNow(bool reactive)
    {
        if (!FightActive() || !ai.canDefend) return false;
        if (ctrl.isAttacking || ai.IsBusy || ai.IsDefending || ai.holdActions) return false; // wind-up / attack / guard / stagger / dodge
        if (IsStaggered || InFlinch || Time.time < guardLockUntil) return false;
        if (phases != null && phases.InPhaseShift) return false;
        if (Time.time - lastGuardEnd < minGuardGap) return false;
        float dur = guardDuration.y;
        if (GuardTime + dur > maxGuardUptime * (FightTime + dur)) return false;
        return true;
    }

    bool RaiseGuard(bool reactive, GuardDirection dir)
    {
        float dur = Random.Range(guardDuration.x, guardDuration.y);
        string trigger = dir == GuardDirection.High ? highGuardTrigger : sideGuardTrigger;
        if (!ai.BeginGuard(dur, reactive, trigger, guardFlashColor, guardFlashSeconds)) return false;
        if (reactive) ReactiveGuards++; else TimedGuards++;
        if (dir == GuardDirection.High) HighGuards++; else SideGuards++;
        LastGuardDirection = dir;
        nextTimedGuard = Time.time + TimedInterval(); // a reactive guard also resets the timed one
        return true;
    }

    // ───────────────────────── guard meter / break ─────────────────────────
    /// <summary>MonsterController: a body hit was absorbed by the guard (weapon parries never reach here).</summary>
    public void OnGuardAbsorbedHit()
    {
        if (!guardBreakEnabled || guardBreakHits <= 0) return;
        GuardMeter++;
        if (GuardMeter >= guardBreakHits) BreakGuard();
        PushMeter();
    }

    void BreakGuard()
    {
        GuardMeter = 0;
        GuardBreaks++;
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
        if (!showGuardMeter || CombatUIController.Instance == null || GuardMeter == shownMeter) return;
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

    bool HasTrigger(string trigger)
    {
        foreach (var p in anim.animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == trigger) return true;
        return false;
    }

    /// <summary>Resets the counters (test harness).</summary>
    public void ResetStats()
    {
        IncomingSwings = GuardRolls = ReactiveGuards = TimedGuards = GuardBreaks = Flinches = Dodges = HighGuards = SideGuards = 0;
        GuardTime = 0f; fightStart = FightActive() ? Time.time : -1f;
    }
}
