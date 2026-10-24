// MonsterExpertBrain.cs
// Purpose: Level 3 Expert mode. Uses ExpertMotionPredictor's forecast through MonsterAI's advisor hooks and MonsterTactics' early warning,
//          without a second state machine:
//            - Attack choice: when the player is predicted to move sideways (dodging), a narrow pick is swapped for a sweep (wide hit
//              arc) that covers where they are going.
//          It reads the HEAD prediction only, at horizons up to config.maxHorizonSeconds (0.75 s): measured, the head prediction beat
//          "stays still" up to 0.75 s and the hand predictions did not beat it at all. Hands are read only with config.useHandPrediction.
//            - Timing: when the player is predicted to lunge / close in, a due strike is held back a short capped moment (plus a little
//              random jitter, so the timing is not one fixed beat), so they walk into it.
//            - Aim: as a wind-up starts the monster turns toward the predicted position (at most maxAimDegrees); a Rush runs along that
//              lane and thrown axes fly to a lead point (at most maxLeadMetres ahead) at the same flight speed.
//            - Defence (only with useHandPrediction; off by default, then the guard is exactly Normal's): the predicted hand movement
//              toward the monster raises the swing predictor's confidence (early warning); every defence rule (reaction delay, speed
//              floor, cooldown, guard meter, 55 % block-rate cap) still applies.
//          Fairness governor (ExpertGovernor): no prediction is used while the model is unsure (window not full, player nearly still,
//          stale or missing prediction) or after the player has taken the Expert cap of hits in the last minute; an aimed attack never
//          chains, so its recovery stays punishable. Telegraphs, damage, health, projectile speed and recovery are never changed.
//          If the predictor switches itself off, the hooks are removed and Level 3 is exactly Normal ("AI offline - normal behaviour").
// Dependencies: ExpertMotionPredictor, MonsterAI (IMonsterAttackAdvisor), MonsterTactics, MonsterHealth, PlayerHealth,
//               CombatUIController (HUD tag), MonsterAIModeSetting, StrikeFit.ExpertAI
// Attach to: the Level 3 monster root (does nothing unless this fight is Level 3 in Expert mode)

using StrikeFit.ExpertAI;
using UnityEngine;

public class MonsterExpertBrain : MonoBehaviour, IMonsterAttackAdvisor
{
    public ExpertAIConfig config;

    /// <summary>End-of-fight numbers for telemetry (SessionReporter's monster_ai_summary).</summary>
    public struct Summary
    {
        public bool expert, modelLoaded, handPrediction;
        public float maxHorizonSeconds;
        public int inferences, fallbacks, decisionsChanged, hitsOnPlayer, earlyWarnings, aimedAttacks, heldStrikes, swappedAttacks, leadThrows;
        public float avgMs, maxMs, p95Ms;
        public string fallbackReason;
    }
    /// <summary>The last Expert fight's numbers (Level 3, Expert only).</summary>
    public static Summary Last;
    public static bool HasLast;
    /// <summary>The brain of the Expert fight running now (null otherwise): the diagnostics overlay reads it.</summary>
    public static MonsterExpertBrain Current { get; private set; }

    public ExpertMotionPredictor Predictor { get; private set; }
    public ExpertGovernor Governor { get; private set; }
    public int DecisionsChanged { get; private set; }
    public int HeldStrikes { get; private set; }
    public int SwappedAttacks { get; private set; }
    public int AimedAttacks { get; private set; }
    public int LeadThrows { get; private set; }
    public int HitsOnPlayer { get; private set; }
    public bool Active { get; private set; }

    MonsterAI ai;
    MonsterTactics tactics;
    MonsterController ctrl;
    MonsterHealth health;
    float holdUntil = -1f, holdDueAt = -1f;
    bool lastAttackAimed;
    float turnAngle, turnDone, turnElapsed = -1f;   // the eased wind-up turn, advanced in Update (no coroutine allocation)
    const float TurnSeconds = 0.15f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { HasLast = false; Last = default; Current = null; }

    void Start()
    {
        ai = GetComponent<MonsterAI>(); tactics = GetComponent<MonsterTactics>(); ctrl = GetComponent<MonsterController>(); health = GetComponent<MonsterHealth>();
        HasLast = false; Last = default;   // a new fight: never report the previous fight's numbers
        if (!MonsterAIModeSetting.ExpertThisFight || config == null || ai == null) { enabled = false; SetTag(""); return; }   // Normal: nothing changes
        Predictor = GetComponent<ExpertMotionPredictor>();
        if (Predictor == null) Predictor = gameObject.AddComponent<ExpertMotionPredictor>();
        Predictor.config = config;
        Predictor.Disabled += OnPredictorDisabled;
        Governor = new ExpertGovernor(config.maxAimDegrees, config.maxLeadMetres, config.HitsPerMinuteCap, config.minMotionEnergy,
                                      config.maxHorizonSeconds, config.useHandPrediction);
        ai.advisor = this;
        // The guard's early warning reads the hand predictions: without useHandPrediction it is not installed at all, so the guard
        // runs exactly Normal's code path.
        if (tactics != null) tactics.earlyWarning = Governor.UseHands ? EarlyWarning : null;
        Current = this;
        PlayerHealth.DamageTaken += OnPlayerDamaged;
        Active = true;
        SetTag("EXPERT AI");
        HasLast = true; Last = Snapshot();
    }

    void OnDestroy()
    {
        PlayerHealth.DamageTaken -= OnPlayerDamaged;
        if (Predictor != null) Predictor.Disabled -= OnPredictorDisabled;
        if (ReferenceEquals(Current, this)) Current = null;
        if (Active) Last = Snapshot();
    }

    void Update()
    {
        if (!Active) return;
        Last = Snapshot();   // cheap struct copy; the reporter reads it when the fight ends
        AdvanceTurn();
        if (ctrl != null && !ctrl.isAlive) Active = false;
    }

    void AdvanceTurn()
    {
        if (turnElapsed < 0f) return;
        turnElapsed += Time.deltaTime;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(turnElapsed / TurnSeconds));
        transform.rotation = Quaternion.AngleAxis(turnAngle * (k - turnDone), Vector3.up) * transform.rotation;
        turnDone = k;
        if (turnElapsed >= TurnSeconds) turnElapsed = -1f;
    }

    void OnPredictorDisabled(string reason)
    {
        // Back to exactly Normal for the rest of the fight.
        if (ai != null && ReferenceEquals(ai.advisor, this)) ai.advisor = null;
        if (tactics != null) tactics.earlyWarning = null;
        SetTag("AI offline - normal behaviour");
        Last = Snapshot();
    }

    void OnPlayerDamaged(float amount, bool blocked, float hpAfter)
    {
        if (amount <= 0.001f) return;
        HitsOnPlayer++;
        Governor?.RegisterHitOnPlayer(Time.time);
    }

    void SetTag(string tag)
    {
        if (CombatUIController.Instance != null) CombatUIController.Instance.SetMonsterAiTag(tag);
    }

    Summary Snapshot() => new Summary
    {
        expert = true, modelLoaded = Predictor != null && Predictor.ModelLoaded,
        handPrediction = config != null && config.useHandPrediction, maxHorizonSeconds = config != null ? config.maxHorizonSeconds : 0f,
        inferences = Predictor != null ? Predictor.Inferences : 0, fallbacks = Predictor != null ? Predictor.Fallbacks : 0,
        decisionsChanged = DecisionsChanged, hitsOnPlayer = HitsOnPlayer, earlyWarnings = tactics != null ? tactics.EarlyWarnings : 0,
        aimedAttacks = AimedAttacks, heldStrikes = HeldStrikes, swappedAttacks = SwappedAttacks, leadThrows = LeadThrows,
        avgMs = Predictor != null ? (float)Predictor.AverageMs : 0f, maxMs = Predictor != null ? (float)Predictor.MaxMs : 0f,
        p95Ms = 0f, fallbackReason = Predictor != null && !Predictor.Healthy ? Predictor.DisabledReason : ""
    };

    // ───────────────────────── is the prediction usable now? ─────────────────────────
    bool Usable()
    {
        if (Predictor == null || Governor == null) return false;
        bool fresh = Predictor.HasPrediction && Time.time - Predictor.PredictionTime < 0.35f;
        return Governor.MayUsePrediction(Time.time, Predictor.WindowFull, Predictor.MotionEnergy, Predictor.Healthy && fresh);
    }

    /// <summary>The predicted head movement (horizontal), at a horizon never beyond maxHorizonSeconds.</summary>
    Vector3 HeadOffset(int horizon)
    {
        Vector3 d = Predictor.WorldOffset(Forecast.Point.Head, Governor.ClampHorizon(horizon)); d.y = 0f;
        return d;
    }

    /// <summary>Predicted head movement split into sideways (+ = toward the monster's right) and toward the monster.</summary>
    void Split(Vector3 d, out float sideways, out float closing)
    {
        Vector3 toMonster = transform.position - (ai.playerTarget != null ? ai.playerTarget.position : transform.position); toMonster.y = 0f;
        toMonster = toMonster.sqrMagnitude > 1e-4f ? toMonster.normalized : -transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, toMonster);   // the player's right as they face the monster
        sideways = Vector3.Dot(d, right);
        closing = Vector3.Dot(d, toMonster);
    }

    // ───────────────────────── IMonsterAttackAdvisor ─────────────────────────
    public bool HoldAttack()
    {
        if (!Active) return false;
        float now = Time.time;
        if (holdUntil > now) return true;
        if (holdDueAt >= 0f && now - holdDueAt < 1f) return false;   // one hold per due attack
        if (!Usable()) return false;
        Split(HeadOffset(config.aimHorizon), out _, out float closing);
        if (closing < config.closingMetres) return false;
        // The player is predicted to close in: hold the due strike a capped moment so they walk into it.
        float delay = Mathf.Min(config.maxStrikeDelay, 0.5f * config.maxStrikeDelay + Random.Range(0f, config.delayJitter));
        holdUntil = now + delay; holdDueAt = now;
        HeldStrikes++; DecisionsChanged++;
        return true;
    }

    public MonsterAI.AttackDefinition Advise(MonsterAI.AttackDefinition picked)
    {
        holdUntil = -1f;
        if (!Active || picked == null || picked.hitArc >= config.wideArcDegrees || !Usable()) return picked;
        Split(HeadOffset(config.aimHorizon), out float sideways, out _);
        if (Mathf.Abs(sideways) < config.dodgeSideways) return picked;
        // The player is dodging sideways: a sweep that covers where they are going.
        float hp = health != null && health.maxHealth > 0f ? health.currentHealth / health.maxHealth : 1f;
        MonsterAI.AttackDefinition best = null; float total = 0f;
        foreach (var a in ai.attackTable)
        {
            if (a == null || a == picked || a.unblockable || a.rangedKind != MonsterAI.RangedKind.None || string.IsNullOrEmpty(a.animatorTrigger)) continue;
            if (a.hitArc < config.wideArcDegrees || hp > a.maxHpFraction || a.requiresIdleFor > 0f || Time.time - a.lastUsedTime < a.cooldown) continue;
            float w = ai.enraged && a.enragedWeight >= 0f ? a.enragedWeight : Mathf.Lerp(a.weightFullHp, a.weightLowHp, 1f - hp);
            if (w <= 0f) continue;
            total += w;
            if (Random.value * total < w) best = a;
        }
        if (best == null) return picked;
        SwappedAttacks++; DecisionsChanged++;
        return best;
    }

    public void OnWindUpStart(MonsterAI.AttackDefinition a)
    {
        lastAttackAimed = false;
        if (!Active || a == null || a.unblockable || ai.playerTarget == null || !Usable()) return;
        // Turn toward where the player is predicted to be when the blow lands (capped).
        float windUp = a.windUp / Mathf.Max(0.25f, ai.tempoScale);
        int h = Governor.ClampHorizon(Mathf.RoundToInt(windUp / 0.25f) - 1);   // a long wind-up still reads 0.75 s at most
        Vector3 target = ai.playerTarget.position + HeadOffset(h);
        Vector3 to = target - transform.position; to.y = 0f;
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (to.sqrMagnitude < 0.01f || fwd.sqrMagnitude < 0.01f) return;
        float want = Vector3.SignedAngle(fwd, to, Vector3.up);
        Vector3 now = ai.playerTarget.position - transform.position; now.y = 0f;
        float current = Vector3.SignedAngle(fwd, now, Vector3.up);
        float angle = Governor.ClampAim(want);
        if (Mathf.Abs(angle - current) < 2f) return;   // the prediction adds nothing over facing the player now
        lastAttackAimed = true; AimedAttacks++; DecisionsChanged++; Governor.NoteAimedAttack();
        if (a.rangedKind == MonsterAI.RangedKind.Rush) transform.rotation = Quaternion.AngleAxis(angle, Vector3.up) * transform.rotation;   // the lane is set now
        else { turnAngle = angle; turnDone = 0f; turnElapsed = 0f; }   // eased over TurnSeconds, early in the wind-up (Update)
    }

    public bool LeadTarget(MonsterAI.AttackDefinition a, out Vector3 target)
    {
        target = default;
        if (!Active || a == null || a.rangedKind != MonsterAI.RangedKind.Throw || ai.playerTarget == null || !Usable()) return false;
        Vector3 lead = Governor.ClampLead(HeadOffset(config.aimHorizon));
        if (lead.sqrMagnitude < 0.0025f) return false;
        target = ai.playerTarget.position + lead;
        LeadThrows++; DecisionsChanged++;
        return true;
    }

    public bool AllowChainAfter(MonsterAI.AttackDefinition a) => ExpertGovernor.AllowChainAfter(lastAttackAimed);

    /// <summary>MonsterTactics: extra confidence for a swing of hand h (0 right, 1 left) the forecast sees coming at the monster.</summary>
    float EarlyWarning(int h)
    {
        if (!Active || !Governor.UseHands || !Usable() || ai.playerTarget == null) return 0f;
        var pc = PlayerCombat.Instance;
        Transform hand = pc != null ? (h == 0 ? pc.weaponTransform : pc.offhandWeaponTransform) : null;
        if (hand == null) return 0f;
        Vector3 d = Predictor.WorldOffset(Predictor.PointForHand(h), 0);
        Vector3 chest = transform.position + Vector3.up * 1.3f;
        Vector3 toward = chest - hand.position; toward.y = 0f;
        if (toward.sqrMagnitude < 1e-4f) return 0f;
        float along = Vector3.Dot(d, toward.normalized);
        if (along < config.warnMinDisplacement) return 0f;
        return config.warnMaxBoost * Mathf.Clamp01((along - config.warnMinDisplacement) / Mathf.Max(0.01f, config.warnMinDisplacement));
    }
}
