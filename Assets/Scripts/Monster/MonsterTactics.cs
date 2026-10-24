// MonsterTactics.cs
// Purpose: The Level 2 / Level 3 fight brain. One decision order for both levels (DefensePolicy):
//            1. never interrupt an attack the monster is committed to (telegraph, strike, recovery = the player's punish window);
//            2. defend a predicted swing (SwingPredictor: ring buffer of controller samples projected 0.15-0.35 s ahead) on the
//               predicted side / zone when the confidence, the time to impact and the guard meter allow it;
//            3. riposte inside the counter window after a block or parry, with a fast attack from the side it blocked;
//            4. punish a whiffed swing (quick attack) or a retreat (gap-closer);
//            5. otherwise MonsterAI's weighted table, with spacing and strafing while the attack cooldown runs.
//          Fairness: a random reaction delay (shorter in later phases and on the side the player has used most in the last
//          biasWindow seconds), a speed floor that fast swings beat, one predicted block per blockCooldown, a hard maximum block
//          rate per phase (token bucket, never above blockRateCap), a guard meter every block drains (the same side drains more),
//          and a guard-break stagger when it empties. Counters and punishes that start before the attack cooldown is over push
//          the next attack back by the same time, so the attack frequency stays the level's own.
//          Every tunable is in the level's DefenseProfile. Without a profile (Level 1) this component does nothing.
//          Telemetry: predicted_block, parry, counter and guard_break go out through Reported (SessionReporter's event path).
// Dependencies: MonsterAI, MonsterController, MonsterDefense, MonsterHealth, PlayerCombat, HitDetection, BattleManager,
//               BossPhaseController / MonsterEntrance (optional), StrikeFit.Defense (DefenseProfile, SwingPredictor, DefensePolicy)
// Attach to: Monster root (Levels 2 and 3), with a DefenseProfile

using StrikeFit.Defense;
using UnityEngine;

[DefaultExecutionOrder(-20)]   // decides before MonsterDefense and MonsterAI read the frame
public class MonsterTactics : MonoBehaviour
{
    public DefenseProfile profile;
    [Tooltip("Keep the last 256 decisions in a fixed ring (verification; no allocation while playing).")]
    public bool logDecisions = true;

    public enum EventKind { PredictedBlock = 0, Parry = 1, Counter = 2, GuardBreak = 3 }

    /// <summary>One telemetry report (predicted_block, parry, counter, guard_break).</summary>
    public struct Report
    {
        public EventKind kind;
        public SwingZone zone;
        public int phase;
        public float confidence, timeToImpact, reactionDelay, swingSpeed, guardMeter;
        public string attack;   // counter: the riposte row
    }
    /// <summary>Static, so SessionReporter needs no reference to the monster.</summary>
    public static event System.Action<MonsterTactics, Report> Reported;

    // ── runtime (read-only, verification) ──
    public const int Phases = 3;
    public readonly int[] IncomingByPhase = new int[Phases], DefendByPhase = new int[Phases], RaisedByPhase = new int[Phases],
        BlockedByPhase = new int[Phases], ParriedByPhase = new int[Phases];
    public readonly int[] RefusalCounts = new int[10];
    public int Counters { get; private set; }
    public int CounterRolls { get; private set; }
    public int Punishes { get; private set; }
    public int Whiffs { get; private set; }
    public int Retreats { get; private set; }
    public int GuardBreaks { get; private set; }
    public int CommittedSwings { get; private set; }
    /// <summary>Hits blocked while an attack was committed (must stay 0).</summary>
    public int CommittedBlocks { get; private set; }
    public int Detected { get; private set; }
    public float GuardMeterValue => meter.Value;
    public int Phase => phaseNow < 0 ? 0 : phaseNow;
    public float StrafeOffset { get; private set; }
    public SwingZone LastPredictedZone { get; private set; }
    public SwingPrediction LastPrediction { get; private set; }
    public SwingZone GuardZone { get; private set; }
    public string LastCounterAttack { get; private set; } = "";
    public string LastPunishAttack { get; private set; } = "";
    public bool Active => profile != null && isActiveAndEnabled;
    /// <summary>Level 3 Expert (MonsterExpertBrain): extra confidence for a swing of hand h (0 right, 1 left). Null elsewhere.</summary>
    [System.NonSerialized] public System.Func<int, float> earlyWarning;
    public int EarlyWarnings { get; private set; }

    private MonsterAI ai;
    private MonsterController ctrl;
    private MonsterDefense def;
    private MonsterHealth health;
    private BossPhaseController phases;
    private MonsterEntrance entrance;
    private readonly SwingPredictor[] pred = { new SwingPredictor(), new SwingPredictor() };
    private readonly bool[] armed = { true, true };
    private readonly HitDetection[] hd = new HitDetection[2];
    private readonly Collider[] hdCol = new Collider[2];
    private readonly Transform[] hdOwner = new Transform[2];
    private readonly Vector3[] strikeLocal = new Vector3[2];
    private readonly BlockRateLimiter limiter = new BlockRateLimiter();
    private readonly GuardMeter meter = new GuardMeter();
    private readonly SideBias bias = new SideBias();
    // swing episodes (whiff detection)
    private readonly bool[] inSwing = new bool[2], swingHit = new bool[2], whiffed = new bool[2];
    private readonly float[] swingPeak = new float[2], swingMinDist = new float[2];
    // pending predicted guard
    private float pendingAt = -1f, pendingImpactAt, pendingConf, pendingDelay, pendingSpeed;
    private SwingZone pendingZone;
    private Vector3 pendingContact;
    // guard raised by this component
    private bool ourGuard;
    private float lastRaise = -999f;
    // counter
    private float counterAt = -1f, counterUntil = -1f;
    private SwingZone counterZone;
    // punish
    private float punishUntil = -1f;
    private bool punishRetreat;
    private float retreatFor, lastHeadDist = -1f;
    private Vector3 lastHeadPos;
    private float punishHeldLogged = -1f;
    private bool retreatFlagged;
    // guard break
    private float brokenUntil = -1f, lastBlockAt = -999f;
    // strafe
    private float strafeTarget, nextStrafeSwitch;
    private int phaseNow = -1;
    private bool fightStarted;

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        ctrl = GetComponent<MonsterController>();
        def = GetComponent<MonsterDefense>();
        health = GetComponent<MonsterHealth>();
        phases = GetComponent<BossPhaseController>();
        entrance = GetComponent<MonsterEntrance>();
    }

    void OnEnable()
    {
        HitDetection.HitLanded += OnHitLanded;
        HitDetection.WeaponClashed += OnClash;
        if (ai != null) ai.TacticalAttackStarted += OnTacticalStarted;
    }

    void OnDisable()
    {
        HitDetection.HitLanded -= OnHitLanded;
        HitDetection.WeaponClashed -= OnClash;
        if (ai != null) ai.TacticalAttackStarted -= OnTacticalStarted;
    }

    // The riposte / punish asked of MonsterAI (it starts once the guard is back in Idle): 1 = counter, 2 = punish.
    private int tactical;
    private SwingZone tacticalZone;
    private bool tacticalRetreat, tacticalCloseGap;

    void OnTacticalStarted(MonsterAI.AttackDefinition a, bool started)
    {
        int kind = tactical; tactical = 0;
        if (!started) { Log(LogKind.Refuse, tacticalZone, Phase, 0f, 0f, 0f, (kind == 1 ? "counter " : "punish ") + "could not start"); return; }
        if (kind == 1)
        {
            Counters++; LastCounterAttack = a.name;
            Log(LogKind.Counter, tacticalZone, Phase, 0f, 0f, 0f, a.name);
            Send(new Report { kind = EventKind.Counter, zone = tacticalZone, phase = Phase, attack = a.name, guardMeter = meter.Value });
        }
        else if (kind == 2)
        {
            Punishes++; LastPunishAttack = a.name;
            Log(LogKind.Punish, SwingZone.None, Phase, tacticalRetreat ? 1f : 0f, tacticalCloseGap ? 1f : 0f, 0f, a.name);
        }
    }

    void Start()
    {
        if (profile == null || def == null) return;
        // The legacy roll-based guard, timed guard, blocks-in-a-row rule, hit-count guard meter and counter are replaced by this
        // component's rules on this monster; the stagger and lockout come from the profile. The prefab values stay as they are.
        def.staggerSeconds = profile.guardBreakStagger;
        def.guardLockoutAfterBreak = profile.guardLockoutAfterBreak;
        def.detectSpeed = profile.predictor.detectSpeed;
    }

    bool FightActive()
    {
        if (ctrl == null || !ctrl.isAlive || ai == null || !ai.enabled) return false;
        if (entrance != null && !entrance.Complete) return false;
        return BattleManager.Instance == null || BattleManager.Instance.IsBattleActive;
    }

    int CurrentPhase()
    {
        if (phases != null) return Mathf.Clamp(phases.Phase, 0, Phases - 1);
        float hp = health != null && health.maxHealth > 0f ? health.currentHealth / health.maxHealth : 1f;
        return hp > profile.hpPhaseThresholds.x ? 0 : hp > profile.hpPhaseThresholds.y ? 1 : 2;
    }

    /// <summary>The monster has started an attack: telegraph, strike, recovery, or the gap of a chain.</summary>
    public bool Committed => ctrl != null && (ctrl.isAttacking || ai.CurrentAttack != null || (ai.IsBusy && !ai.IsDefending && !ai.IsStaggered));

    bool BusyOther => (def != null && (def.IsStaggered || def.InFlinch)) || ai.IsStaggered || ai.holdActions || (phases != null && phases.InPhaseShift)
                      || Time.time < brokenUntil || ai.IsDefending;

    void Update()
    {
        if (profile == null || GamePause.IsPaused) return;
        if (!FightActive()) { pendingAt = -1f; return; }
        float now = Time.time, dt = Time.deltaTime;
        if (!fightStarted) { fightStarted = true; nextStrafeSwitch = now + Random.Range(profile.strafeInterval.x, profile.strafeInterval.y); PushMeter(); }

        int p = CurrentPhase();
        if (p != phaseNow) { if (phaseNow >= 0) Log(LogKind.Phase, SwingZone.None, p, 0f, 0f, 0f, null); phaseNow = p; limiter.Reset(); }

        meter.Tick(now, dt, profile.regenDelay, profile.regenPerSecond);
        if (meter.Value <= 0f && now >= brokenUntil && brokenUntil > 0f) { meter.Refill(); brokenUntil = -1f; PushMeter(); }
        if (ourGuard && !ai.IsDefending) ourGuard = false;

        var body = Body();
        var pc = PlayerCombat.Instance;
        bool committed = Committed;
        if (pc != null)
            for (int h = 0; h < 2; h++) TrackHand(h, h == 0 ? pc.weaponTransform : pc.offhandWeaponTransform, in body, committed, now);

        // The predicted guard rises after the reaction delay, unless the monster got busy meanwhile.
        if (pendingAt >= 0f && now >= pendingAt) { pendingAt = -1f; RaisePending(now); }

        TrackRetreat(now, dt);

        // Rules 3-5 (rule 2 runs per predicted swing in TrackHand).
        var input = new DecisionInput
        {
            committed = committed,
            busyOther = (def != null && (def.IsStaggered || def.InFlinch)) || ai.IsStaggered || ai.holdActions || (phases != null && phases.InPhaseShift) || now < brokenUntil,
            counterWindowOpen = counterAt >= 0f && now >= counterAt && now <= counterUntil,
            punishOpportunity = now <= punishUntil,
            tableReady = ctrl.CanAttack()
        };
        AttackDefinitionRef counterRow = default, punishRow = default;
        if (input.counterWindowOpen) { counterRow = Row(profile.CounterFor(counterZone)); input.counterReady = counterRow.a != null && !ctrl.isAttacking; }
        if (input.punishOpportunity && !ai.IsDefending && !ai.IsBusy)
        {
            punishRow = punishRetreat ? GapCloser() : QuickAttack();
            input.punishReady = punishRow.a != null && ctrl.SecondsUntilAttackReady <= profile.punishMaxEarly && !ctrl.isAttacking;
            if (!input.punishReady && punishHeldLogged < punishUntil - profile.punishWindow + 1e-4f)
            {
                punishHeldLogged = now;   // once per opportunity: why it waits (or never comes)
                Log(LogKind.Refuse, SwingZone.None, Phase, ctrl.SecondsUntilAttackReady, 0f, 0f, punishRow.a == null ? "punish: no fitting attack" : "punish waits for the cooldown");
            }
        }
        var d = DefensePolicy.Decide(input, out _);
        if (d == Decision.Counter)
        {
            counterAt = counterUntil = -1f;
            if (ai.BeginTacticalAttack(counterRow.a, false, true)) { tactical = 1; tacticalZone = counterZone; tacticalCloseGap = false; }
        }
        else if (d == Decision.Punish)
        {
            punishUntil = -1f;
            bool closeGap = punishRetreat && punishRow.a.rangedKind == MonsterAI.RangedKind.None;
            if (ai.BeginTacticalAttack(punishRow.a, closeGap, true)) { tactical = 2; tacticalRetreat = punishRetreat; tacticalCloseGap = closeGap; }
        }
        if (counterAt >= 0f && now > counterUntil) counterAt = counterUntil = -1f;

        Strafe(now, dt, committed);
    }

    // ───────────────────────── rule 2: predicted swings ─────────────────────────
    BodyFrame Body()
    {
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        return new BodyFrame { feet = transform.position, right = Vector3.Cross(Vector3.up, fwd), forward = fwd, height = def != null ? def.Height : 1.8f, radius = profile.bodyRadius };
    }

    void TrackHand(int h, Transform w, in BodyFrame body, bool committed, float now)
    {
        if (w == null || !w.gameObject.activeInHierarchy) { pred[h].Clear(); armed[h] = true; inSwing[h] = false; return; }
        if (hdOwner[h] != w)
        {
            hdOwner[h] = w; hd[h] = w.GetComponentInChildren<HitDetection>(); hdCol[h] = hd[h] != null ? hd[h].GetComponent<Collider>() : null; pred[h].Clear();
            // The striking part in the weapon's own space. Sampled through the transform every frame: Collider.bounds follows the
            // physics step (50 Hz here, transforms not auto-synced), so at 72 / 90 Hz it moves in stair steps that read as speed spikes.
            strikeLocal[h] = hdCol[h] != null ? w.InverseTransformPoint(hdCol[h].bounds.center) : Vector3.zero;
        }
        Vector3 strike = w.TransformPoint(strikeLocal[h]);
        pred[h].Push(strike, now);
        var s = profile.predictor;
        var p = pred[h].Predict(in body, in s);

        // Swing episode: start at the detection speed, end below half of it. A swing that came near and hit nothing is a whiff.
        Vector3 l = body.ToLocal(strike);
        float dist = Mathf.Sqrt(l.x * l.x + l.z * l.z);
        if (p.swinging)
        {
            if (!inSwing[h]) { inSwing[h] = true; swingHit[h] = false; whiffed[h] = false; swingPeak[h] = 0f; swingMinDist[h] = float.MaxValue; }
            swingPeak[h] = Mathf.Max(swingPeak[h], p.speed);
            swingMinDist[h] = Mathf.Min(swingMinDist[h], dist);
            // A whiff is known as soon as the weapon has passed its closest point to the body (25 cm back out) without a hit,
            // not when the arm stops: by then the monster is often already in its next attack.
            if (!whiffed[h] && !swingHit[h] && swingMinDist[h] <= profile.whiffRange && dist > swingMinDist[h] + 0.25f) Whiff(h, now);
        }
        else if (p.speed < 0.5f * s.detectSpeed)
        {
            if (inSwing[h])
            {
                inSwing[h] = false;
                if (!whiffed[h] && !swingHit[h] && swingMinDist[h] <= profile.whiffRange) Whiff(h, now);
            }
            armed[h] = true;   // the next swing may be predicted
        }
        if (!armed[h] || !p.willHit) return;

        // One decision per swing, taken the first frame its path is projected into the body.
        armed[h] = false;
        Detected++;
        LastPrediction = p; LastPredictedZone = p.zone;
        int ph = Phase;
        SwingZone most = bias.Most(now, profile.biasWindow, out float share);
        bias.Add(p.zone, now);
        IncomingByPhase[ph]++;
        limiter.OnIncomingSwing(profile.RateFor(ph));
        bool onBias = most != SwingZone.None && most == p.zone;
        // Level 3 Expert: the motion forecaster's predicted hand movement toward the monster adds confidence (early warning). The
        // reaction delay, speed floor, cooldown, guard meter and block-rate cap below are not touched by it.
        float warn = earlyWarning != null ? Mathf.Max(0f, earlyWarning(h)) : 0f;
        if (warn > 0f) EarlyWarnings++;
        float conf = Mathf.Clamp01(p.confidence + (onBias ? profile.biasConfidence * share : 0f) + warn);
        float delay = Random.Range(profile.reactionDelay.x, profile.reactionDelay.y) * profile.ReactionScale(ph) * (onBias ? 1f - profile.biasReactionCut * share : 1f);
        var input = new DecisionInput
        {
            committed = committed, busyOther = BusyOther, predicted = true,
            confidence = conf, minConfidence = profile.minConfidence,
            timeToImpact = p.timeToImpact, reactionDelay = delay, raiseTime = profile.raiseTime,
            swingSpeed = p.speed, maxBlockableSpeed = profile.maxBlockableSpeed,
            guardMeter = meter.Value, blockCooldownReady = now - lastRaise >= profile.blockCooldown && pendingAt < 0f,
            rateCreditAvailable = limiter.CanBlock
        };
        var d = DefensePolicy.Decide(input, out DefendRefusal why);
        if (committed) CommittedSwings++;
        if (d == Decision.Defend)
        {
            DefendByPhase[ph]++;
            pendingAt = now + delay; pendingImpactAt = now + p.timeToImpact; pendingZone = p.zone; pendingContact = p.contact;
            pendingConf = conf; pendingDelay = delay; pendingSpeed = p.speed;
            Log(LogKind.Defend, p.zone, ph, conf, p.timeToImpact, delay, null);
        }
        else
        {
            RefusalCounts[(int)why]++;
            Log(LogKind.Refuse, p.zone, ph, conf, p.timeToImpact, p.speed, why == DefendRefusal.Busy ? BusyReason() : DefensePolicyNames.Refusal[(int)why]);
        }
    }

    /// <summary>Which rule keeps the monster from guarding right now (log).</summary>
    string BusyReason()
    {
        if (def != null && def.IsStaggered) return "busy: guard-break stagger";
        if (def != null && def.InFlinch) return "busy: flinch";
        if (ai.IsStaggered) return "busy: stagger";
        if (ai.holdActions) return "busy: hold (phase roar)";
        if (phases != null && phases.InPhaseShift) return "busy: phase shift";
        if (Time.time < brokenUntil) return "busy: guard lockout";
        if (ai.IsDefending) return "busy: already guarding";
        return "busy";
    }

    void Whiff(int h, float now)
    {
        whiffed[h] = true; Whiffs++;
        punishRetreat = false; punishUntil = now + profile.punishWindow;   // a committed monster punishes once it is free, inside the window
        Log(LogKind.Whiff, SwingZone.None, Phase, swingPeak[h], swingMinDist[h], 0f, null);
    }

    void RaisePending(float now)
    {
        if (Committed || BusyOther) { RefusalCounts[(int)DefendRefusal.Busy]++; Log(LogKind.Refuse, pendingZone, Phase, pendingConf, 0f, pendingSpeed, !logDecisions ? null : Committed ? "committed at raise" : BusyReason() + " at raise"); return; }
        if (!limiter.TryBlock()) { RefusalCounts[(int)DefendRefusal.RateCap]++; Log(LogKind.Refuse, pendingZone, Phase, pendingConf, 0f, pendingSpeed, "rate cap at raise"); return; }
        float dur = Mathf.Max(profile.minGuardSeconds, pendingImpactAt - now + profile.holdAfterImpact);
        MonsterDefense.SwingClass c; MonsterDefense.GuardPose pose;
        ToClassAndPose(pendingZone, out c, out pose);
        if (!def.RaisePlannedGuard(c, pose, pendingContact, dur))
        {
            RefusalCounts[(int)DefendRefusal.Busy]++;
            Log(LogKind.Refuse, pendingZone, Phase, pendingConf, 0f, pendingSpeed, logDecisions ? "guard refused (" + def.LastRefusal + ")" : null);   // the text is built only when the log keeps it
            return;
        }
        ourGuard = true; GuardZone = pendingZone; lastRaise = now;
        RaisedByPhase[Phase]++;
        Log(LogKind.Raise, pendingZone, Phase, pendingConf, pendingImpactAt - now, dur, MonsterDefense.PoseNames[(int)pose]);
        Send(new Report { kind = EventKind.PredictedBlock, zone = pendingZone, phase = Phase, confidence = pendingConf, timeToImpact = pendingImpactAt - (now - pendingDelay), reactionDelay = pendingDelay, swingSpeed = pendingSpeed, guardMeter = meter.Value });
    }

    public static void ToClassAndPose(SwingZone z, out MonsterDefense.SwingClass c, out MonsterDefense.GuardPose pose)
    {
        switch (z)
        {
            case SwingZone.Left: c = MonsterDefense.SwingClass.HorizontalLeft; pose = MonsterDefense.GuardPose.SideLeft; break;
            case SwingZone.Right: c = MonsterDefense.SwingClass.HorizontalRight; pose = MonsterDefense.GuardPose.SideRight; break;
            case SwingZone.High: c = MonsterDefense.SwingClass.Overhead; pose = MonsterDefense.GuardPose.High; break;
            case SwingZone.Low: c = MonsterDefense.SwingClass.LowSweep; pose = MonsterDefense.GuardPose.Low; break;
            default: c = MonsterDefense.SwingClass.Thrust; pose = MonsterDefense.GuardPose.Low; break;   // the low / thrust guard
        }
    }

    // ───────────────────────── blocks, parries, guard break ─────────────────────────
    void OnHitLanded(HitDetection source, float speed, bool defended)
    {
        for (int h = 0; h < 2; h++) if (hd[h] == source) swingHit[h] = true;
    }

    void OnClash(HitDetection source, float speed)
    {
        for (int h = 0; h < 2; h++) if (hd[h] == source) swingHit[h] = true;
        if (profile == null || !FightActive() || !ai.IsDefending) return;
        // A player weapon met the raised guard weapon: a parry.
        ParriedByPhase[Phase]++;
        Log(LogKind.Parry, GuardZone, Phase, speed, meter.Value, 0f, null);
        Send(new Report { kind = EventKind.Parry, zone = GuardZone, phase = Phase, swingSpeed = speed, guardMeter = meter.Value });
        Blocked(true);
    }

    /// <summary>MonsterDefense: a body hit was absorbed by the raised guard.</summary>
    public void OnGuardAbsorbed()
    {
        if (Committed) CommittedBlocks++;
        BlockedByPhase[Phase]++;
        Log(LogKind.Block, GuardZone, Phase, meter.Value, 0f, 0f, null);
        Blocked(false);
    }

    void Blocked(bool parry)
    {
        float now = Time.time;
        // One swing can both clash on the guard weapon and be absorbed by the body: drain once.
        if (now - lastBlockAt < 0.15f) return;
        lastBlockAt = now;
        bool broke = meter.Drain(GuardZone, now, profile.drainPerBlock, profile.sameSideDrainMultiplier, profile.sameSideWindow);
        PushMeter();
        if (broke)
        {
            GuardBreaks++;
            brokenUntil = now + profile.guardBreakStagger + profile.guardLockoutAfterBreak;
            counterAt = counterUntil = -1f; pendingAt = -1f;
            Log(LogKind.Break, GuardZone, Phase, 0f, 0f, 0f, null);
            Send(new Report { kind = EventKind.GuardBreak, zone = GuardZone, phase = Phase, guardMeter = 0f });
            def.BreakGuardNow();
            return;
        }
        // Counter window: may riposte from the side it just blocked.
        if (counterAt < 0f)
        {
            CounterRolls++;
            if (Random.value < profile.counterChance)
            {
                counterZone = GuardZone;
                counterAt = now + Random.Range(0.1f, Mathf.Max(0.12f, profile.counterWindow * 0.75f));
                counterUntil = now + profile.counterWindow;
            }
        }
    }

    void PushMeter()
    {
        if (def == null || !def.showGuardMeter || CombatUIController.Instance == null) return;
        // The bar fills toward the break, like the old hit-count meter.
        CombatUIController.Instance.SetGuardMeter(Mathf.RoundToInt((1f - meter.Value) * 10f), 10);
    }

    // ───────────────────────── rule 4: punish ─────────────────────────
    void TrackRetreat(float now, float dt)
    {
        if (ai.playerTarget == null || dt <= 0f) return;
        float dist = ai.DistanceToTarget();
        Vector3 headPos = ai.playerTarget.position;
        if (lastHeadDist >= 0f)
        {
            // The player's own speed away from the monster (not the change of distance: a monster that follows would hide it).
            Vector3 dir = headPos - transform.position; dir.y = 0f;
            Vector3 vel = (headPos - lastHeadPos) / dt; vel.y = 0f;
            float away = dir.sqrMagnitude > 1e-4f ? Vector3.Dot(vel, dir.normalized) : 0f;
            retreatFor = away >= profile.retreatSpeed ? retreatFor + dt : 0f;
            if (retreatFor <= 0f) retreatFlagged = false;
            if (!retreatFlagged && retreatFor >= profile.retreatSeconds && dist > ctrl.attackRange * 0.8f && !Committed)
            {
                retreatFlagged = true; Retreats++;
                punishRetreat = true; punishUntil = now + profile.punishWindow;
                Log(LogKind.Retreat, SwingZone.None, Phase, dist, away, 0f, null);
            }
        }
        lastHeadDist = dist; lastHeadPos = headPos;
    }

    struct AttackDefinitionRef { public MonsterAI.AttackDefinition a; }

    AttackDefinitionRef Row(string name)
    {
        if (!string.IsNullOrEmpty(name))
            foreach (var a in ai.attackTable)
                if (a != null && a.name == name && !a.unblockable && Time.time - a.lastUsedTime >= a.cooldown) return new AttackDefinitionRef { a = a };
        return default;
    }

    /// <summary>Whiff: the fastest blockable melee row whose wind-up is short enough.</summary>
    AttackDefinitionRef QuickAttack()
    {
        MonsterAI.AttackDefinition best = null;
        foreach (var a in ai.attackTable)
        {
            if (a == null || a.unblockable || a.rangedKind != MonsterAI.RangedKind.None || string.IsNullOrEmpty(a.animatorTrigger)) continue;
            if (a.windUp > profile.quickAttackMaxWindUp || Time.time - a.lastUsedTime < a.cooldown) continue;
            if (best == null || a.windUp < best.windUp) best = a;
        }
        if (ai.DistanceToTarget() > ctrl.attackRange) return default;   // a whiff from out of reach is not punished with a melee row
        return new AttackDefinitionRef { a = best };
    }

    /// <summary>Retreat: a Rush row whose band holds the distance, else the profile's gap-closer rows.</summary>
    AttackDefinitionRef GapCloser()
    {
        float dist = ai.DistanceToTarget();
        foreach (var a in ai.attackTable)
            if (a != null && a.rangedKind == MonsterAI.RangedKind.Rush && dist >= a.minRange && dist <= a.maxRange && Time.time - a.lastUsedTime >= a.cooldown)
                return new AttackDefinitionRef { a = a };
        if (profile.gapClosers != null)
            foreach (var n in profile.gapClosers) { var r = Row(n); if (r.a != null && r.a.rangedKind == MonsterAI.RangedKind.None && dist <= ctrl.attackRange + 1.4f) return r; }
        return default;
    }

    // ───────────────────────── rule 5: spacing and strafing ─────────────────────────
    void Strafe(float now, float dt, bool committed)
    {
        bool free = !committed && !ai.IsBusy && !ai.holdActions && !ctrl.isAttacking;
        if (!profile.strafe) { StrafeOffset = 0f; return; }
        if (free && now >= nextStrafeSwitch)
        {
            strafeTarget = strafeTarget > 0f ? -profile.strafeWidth : profile.strafeWidth;
            if (Random.value < 0.25f) strafeTarget = 0f;
            nextStrafeSwitch = now + Random.Range(profile.strafeInterval.x, profile.strafeInterval.y);
            if (logDecisions) Log(LogKind.Strafe, SwingZone.None, Phase, strafeTarget, 0f, 0f, null);
        }
        // While an attack is due, come back to the centre line so the swing is aimed straight.
        float want = free && ctrl.SecondsUntilAttackReady > 0.4f ? strafeTarget : 0f;
        StrafeOffset = Mathf.MoveTowards(StrafeOffset, want, 0.5f * dt);
        // Spacing: too close while waiting -> step back a little.
        if (free && ai.playerTarget != null && ai.canMove)
        {
            Vector3 away = transform.position - ai.playerTarget.position; away.y = 0f;
            float dist = away.magnitude;
            if (dist > 0.01f && dist < profile.spacingMin && ctrl.SecondsUntilAttackReady > 0.4f)
                transform.position += away / dist * Mathf.Min(profile.spacingSpeed * dt, profile.spacingMin - dist);
        }
    }

    void Send(Report r) => Reported?.Invoke(this, r);

    // ───────────────────────── decision log (fixed ring, no allocation) ─────────────────────────
    public enum LogKind : byte { Defend, Refuse, Raise, Block, Parry, Counter, Punish, Whiff, Retreat, Break, Phase, Strafe }
    struct Entry { public float t; public LogKind kind; public SwingZone zone; public int phase; public float a, b, c; public string text; }
    const int LogSize = 256;
    private readonly Entry[] ring = new Entry[LogSize];
    private int ringHead, ringCount;
    public int LogTotal { get; private set; }

    void Log(LogKind k, SwingZone z, int ph, float a, float b, float c, string text)
    {
        if (!logDecisions) return;
        ring[ringHead] = new Entry { t = Time.time, kind = k, zone = z, phase = ph, a = a, b = b, c = c, text = text };
        ringHead = (ringHead + 1) % LogSize;
        if (ringCount < LogSize) ringCount++;
        LogTotal++;
    }

    /// <summary>The decision log as text (verification only: allocates).</summary>
    public string DumpLog(float since = -1f)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ringCount; i++)
        {
            var e = ring[(ringHead - ringCount + i + LogSize) % LogSize];
            if (e.t < since) continue;
            sb.Append(e.t.ToString("0.00").PadLeft(8)).Append("s  P").Append(e.phase + 1).Append("  ");
            switch (e.kind)
            {
                case LogKind.Defend: sb.Append("DEFEND   ").Append(e.zone).Append(" conf ").Append(e.a.ToString("0.00")).Append(" tti ").Append(e.b.ToString("0.00")).Append(" s, guard in ").Append(e.c.ToString("0.00")).Append(" s"); break;
                case LogKind.Refuse when e.zone == SwingZone.None: sb.Append("WAIT     ").Append(e.text).Append(" (next attack in ").Append(e.a.ToString("0.00")).Append(" s)"); break;
                case LogKind.Refuse: sb.Append("NO GUARD ").Append(e.zone).Append(" (").Append(e.text).Append(") conf ").Append(e.a.ToString("0.00")).Append(" tti ").Append(e.b.ToString("0.00")).Append(" speed ").Append(e.c.ToString("0.0")); break;
                case LogKind.Raise: sb.Append("GUARD UP ").Append(e.text).Append(" for ").Append(e.zone).Append(", impact in ").Append(e.b.ToString("0.00")).Append(" s, held ").Append(e.c.ToString("0.00")).Append(" s"); break;
                case LogKind.Block: sb.Append("BLOCK    ").Append(e.zone).Append(" meter ").Append(e.a.ToString("0.00")); break;
                case LogKind.Parry: sb.Append("PARRY    ").Append(e.zone).Append(" ").Append(e.a.ToString("0.0")).Append(" m/s, meter ").Append(e.b.ToString("0.00")); break;
                case LogKind.Counter: sb.Append("COUNTER  ").Append(e.text).Append(" after a ").Append(e.zone).Append(" block"); break;
                case LogKind.Punish: sb.Append("PUNISH   ").Append(e.text).Append(e.a > 0.5f ? " (retreat" : " (whiff").Append(e.b > 0.5f ? ", closes the gap)" : ")"); break;
                case LogKind.Whiff: sb.Append("WHIFF    peak ").Append(e.a.ToString("0.0")).Append(" m/s, nearest ").Append(e.b.ToString("0.00")).Append(" m"); break;
                case LogKind.Retreat: sb.Append("RETREAT  ").Append(e.a.ToString("0.00")).Append(" m away at ").Append(e.b.ToString("0.0")).Append(" m/s"); break;
                case LogKind.Break: sb.Append("GUARD BREAK (").Append(e.zone).Append(" blocks emptied the meter): stagger"); break;
                case LogKind.Phase: sb.Append("PHASE -> ").Append(e.phase + 1).Append(" (block-rate bank reset)"); break;
                case LogKind.Strafe: sb.Append("STRAFE   target ").Append(e.a.ToString("+0.00;-0.00;0.00")).Append(" m"); break;
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Clears the per-swing state of a hand (harness: after teleporting the weapon).</summary>
    public void ResetHand(int h) { pred[h].Clear(); armed[h] = true; inSwing[h] = false; }

    /// <summary>Resets every counter and the fairness state (harness).</summary>
    public void ResetStats()
    {
        for (int i = 0; i < Phases; i++) IncomingByPhase[i] = DefendByPhase[i] = RaisedByPhase[i] = BlockedByPhase[i] = ParriedByPhase[i] = 0;
        for (int i = 0; i < RefusalCounts.Length; i++) RefusalCounts[i] = 0;
        Counters = CounterRolls = Punishes = Whiffs = Retreats = GuardBreaks = CommittedSwings = CommittedBlocks = Detected = 0;
        limiter.Reset(); meter.Reset(); bias.Clear(); lastRaise = -999f; pendingAt = -1f; counterAt = counterUntil = -1f; punishUntil = -1f;
        brokenUntil = -1f; ringCount = 0; ringHead = 0; LogTotal = 0; PushMeter();
    }
}

/// <summary>Readable names for DefendRefusal (log, harness).</summary>
public static class DefensePolicyNames
{
    public static readonly string[] Refusal = { "none", "no prediction", "committed to an attack", "low confidence", "too late (reaction)", "too fast (speed floor)", "guard meter empty", "block cooldown", "block-rate cap", "busy" };
}
