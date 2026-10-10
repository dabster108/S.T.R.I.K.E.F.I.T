// MonsterCosmetics.cs
// Purpose: Level 1 polish that never touches the fight's numbers (damage, telegraph, cooldown, range, AI timing are not read or
//          written here). Four independent parts, each with its own toggle, all driven through real Animator transitions:
//            idle variants  every few seconds of calm the monster plays one of two short idle variations (IdleB / IdleC)
//            taunt          after 8-10 s without being hit, while calm, a taunt (the HipHop model's own dance clip)
//            hit variants   a hit reaction chosen by the side the hit came from (left / right: the same clip mirrored, plus a lean)
//                           and a heavier stagger on a full-power swing (Great Sword Impact), 6 s cooldown
//            sway           a slow breathing sway and a lean away from a hit, applied as an additive rotation to the chest bone only
//                           (the visible body: root, colliders, AI, range checks never see it; same pattern as MonsterHitShake)
//          Every variant is entered and left through Idle -> X / X -> Idle transitions; an attack, a guard or a hit trigger always
//          wins immediately (the variant states carry the same outgoing transitions as Idle), so nothing here can delay an attack.
//          A controller without the variant triggers (Levels 2 and 3) simply gets the plain Hit: every trigger is checked first.
// Dependencies: MonsterAnimator, MonsterController, MonsterAI, MonsterDefense (optional), BattleManager, XPManager, Animator
// Attach to: Level 1 monster root (built by Level1AnimationSetup).

using Unity.Profiling;
using UnityEngine;

[DefaultExecutionOrder(210)]   // after the Animator and MonsterHitShake (200): the sway is added on top of the final pose
public class MonsterCosmetics : MonoBehaviour
{
    [Header("Idle variants")]
    public bool idleVariants = true;
    [Tooltip("Seconds of calm between two variants (random in the range).")]
    public Vector2 idleVariantInterval = new Vector2(6f, 11f);
    public string[] idleVariantTriggers = { "IdleB", "IdleC" };

    [Header("Taunt")]
    public bool taunt = true;
    [Tooltip("Seconds without the monster being hit before a taunt (random in the range).")]
    public Vector2 tauntAfterNoHit = new Vector2(8f, 10f);
    [Tooltip("Never again sooner than this after the last taunt.")]
    public float tauntCooldown = 14f;
    [Header("Spacing")]
    [Tooltip("A variant or taunt starts only after the Animator has sat in plain Idle for this long (no back-to-back flourishes).")]
    public float minPlainIdle = 0.4f;
    [Tooltip("...and only when the next attack is at least this far away (cooldown only; an attack that becomes due earlier still wins at once).")]
    public float variantMinTimeBeforeAttack = 0.7f;
    public float tauntMinTimeBeforeAttack = 1.2f;
    public string tauntTrigger = "Taunt";

    [Header("Hit variants")]
    public bool directionalHits = true;
    public bool heavyHitReaction = true;
    [Tooltip("A hit whose point is further than this to the side (fraction of the body width, 0-1) is a side hit.")]
    [Range(0.05f, 1f)] public float sideThreshold = 0.25f;
    [Tooltip("Heavy stagger only for a swing in this tier or above (tap / light / medium / full).")]
    public string heavyTier = "full";
    public float heavyCooldown = 6f;
    public string hitLeftTrigger = "HitLeft", hitRightTrigger = "HitRight", hitHeavyTrigger = "HitHeavy";

    [Header("Sway (additive, chest bone only)")]
    public bool sway = true;
    [Tooltip("Breathing sway amplitude (degrees) and period (s).")]
    public float swayDegrees = 1.4f;
    public float swayPeriod = 4.2f;
    [Tooltip("Lean away from a hit (degrees), dying out over leanSeconds.")]
    public float leanDegrees = 5f;
    public float leanSeconds = 0.28f;

    // ── runtime (verification) ──
    public int IdleVariantsPlayed { get; private set; }
    public int TauntsPlayed { get; private set; }
    public int DirectionalHits { get; private set; }
    public int HeavyHits { get; private set; }
    public float LastIdleVariantAt { get; private set; } = -99f;
    public float LastTauntAt { get; private set; } = -99f;
    public float SinceLastHit => Time.time - lastHitTime;
    public string LastHitTrigger { get; private set; } = "";
    public float AppliedSwayDegrees { get; private set; }

    static readonly ProfilerMarker Marker = new ProfilerMarker("MonsterCosmetics.Update");
    static readonly ProfilerMarker LateMarker = new ProfilerMarker("MonsterCosmetics.LateUpdate");

    private MonsterController ctrl;
    private MonsterAI ai;
    private MonsterAnimator anim;
    private MonsterDefense defense;
    private MonsterEntrance entrance;
    private MonsterHealth health;
    private Animator animator;
    private Transform chest;
    private Quaternion applied = Quaternion.identity;
    private float nextVariant, nextTaunt, lastTaunt = -99f, lastHeavy = -99f, lastHitTime, calmSince = -1f;
    private Vector3 leanAxis; private float leanStart = -99f;
    private int idleHash;
    private bool hasIdleB, hasTaunt, hasLeft, hasRight, hasHeavy;

    void Awake()
    {
        ctrl = GetComponent<MonsterController>();
        ai = GetComponent<MonsterAI>();
        anim = GetComponent<MonsterAnimator>();
        defense = GetComponent<MonsterDefense>();
        entrance = GetComponent<MonsterEntrance>();
        health = GetComponent<MonsterHealth>();
        idleHash = Animator.StringToHash("Idle");
    }

    void Start()
    {
        animator = anim != null ? anim.animator : GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        hasIdleB = HasTrigger(idleVariantTriggers != null && idleVariantTriggers.Length > 0 ? idleVariantTriggers[0] : "");
        hasTaunt = HasTrigger(tauntTrigger);
        hasLeft = HasTrigger(hitLeftTrigger); hasRight = HasTrigger(hitRightTrigger); hasHeavy = HasTrigger(hitHeavyTrigger);
        lastHitTime = Time.time;
        ScheduleVariant(); ScheduleTaunt();
        if (health != null) health.OnDamaged += OnDamaged;
    }

    void OnDestroy() { if (health != null) health.OnDamaged -= OnDamaged; }

    bool HasTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (var p in animator.parameters) if (p.type == AnimatorControllerParameterType.Trigger && p.name == name) return true;
        return false;
    }

    void OnDamaged(float amount, Vector3 point) { lastHitTime = Time.time; ScheduleTaunt(); }

    void ScheduleVariant() { nextVariant = Time.time + Random.Range(idleVariantInterval.x, idleVariantInterval.y); }
    void ScheduleTaunt() { nextTaunt = Time.time + Random.Range(tauntAfterNoHit.x, tauntAfterNoHit.y); }

    // Calm = nothing owns the monster and the Animator sits in Idle (not blending). The only state variants start from.
    bool Calm()
    {
        if (ctrl == null || !ctrl.isAlive || ctrl.isAttacking || animator == null) return false;
        if (ai != null && (ai.IsBusy || ai.holdActions || ai.IsDefending || ai.IsStaggered || ai.CurrentAttack != null)) return false;
        if (entrance != null && !entrance.Complete) return false;
        var bm = BattleManager.Instance;
        if (bm == null || !bm.IsBattleActive) return false;
        if (animator.IsInTransition(0)) return false;
        return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == idleHash;
    }

    // ── hit variant: called by MonsterController just before it plays the Hit state ──
    public void PrepareHit(Vector3 hitPoint, float swingSpeed)
    {
        if (anim == null) return;
        string trigger = null;
        Vector3 toHit = hitPoint - transform.position; toHit.y = 0f;
        float side = toHit.sqrMagnitude > 0.0001f ? Vector3.Dot(toHit.normalized, transform.right) : 0f;
        bool heavy = heavyHitReaction && hasHeavy && Time.time - lastHeavy >= heavyCooldown &&
                     XPManager.Instance != null && TierAtLeast(XPManager.Instance.GetSwingTier(swingSpeed), heavyTier);
        if (heavy) { trigger = hitHeavyTrigger; lastHeavy = Time.time; HeavyHits++; }
        else if (directionalHits && Mathf.Abs(side) >= sideThreshold)
        {
            // the monster's right (+x) is the player's left when facing them: a hit on the +x side leans the body to -x
            trigger = side > 0f ? hitRightTrigger : hitLeftTrigger;
            if ((side > 0f && !hasRight) || (side <= 0f && !hasLeft)) trigger = null; else DirectionalHits++;
        }
        anim.NextHitTrigger = trigger;
        LastHitTrigger = trigger ?? "Hit";
        if (sway && leanDegrees > 0f)
        {
            leanAxis = Vector3.Cross(Vector3.up, toHit.sqrMagnitude > 0.0001f ? toHit.normalized : transform.forward);
            leanStart = Time.time;
        }
    }

    static bool TierAtLeast(string tier, string min)
    {
        return TierRank(tier) >= TierRank(min);
    }
    static int TierRank(string t)
    {
        // XPManager.GetSwingTier returns "TAP" / "LIGHT" / "MEDIUM" / "FULL"; the first letter is enough (no allocation, any case).
        if (string.IsNullOrEmpty(t)) return 0;
        switch (char.ToUpperInvariant(t[0])) { case 'T': return 0; case 'L': return 1; case 'M': return 2; case 'F': return 3; default: return 0; }
    }

    void Update()
    {
        using (Marker.Auto())
        {
            // The additive sway is taken off first (Update runs before the Animator), so it can never accumulate.
            if (applied != Quaternion.identity && chest != null) { chest.localRotation = chest.localRotation * Quaternion.Inverse(applied); applied = Quaternion.identity; }
            if (!Calm()) { calmSince = -1f; return; }
            float now = Time.time;
            if (calmSince < 0f) calmSince = now;
            if (now - calmSince < minPlainIdle) return;
            float untilAttack = ctrl != null ? ctrl.SecondsUntilAttackReady : 99f;
            if (taunt && hasTaunt && now >= nextTaunt && now - lastTaunt >= tauntCooldown && untilAttack >= tauntMinTimeBeforeAttack)
            {
                lastTaunt = now; LastTauntAt = now; TauntsPlayed++;
                anim.PlayNamedTrigger(tauntTrigger);
                ScheduleTaunt(); ScheduleVariant();
                return;
            }
            if (idleVariants && hasIdleB && now >= nextVariant && untilAttack >= variantMinTimeBeforeAttack)
            {
                string t = idleVariantTriggers[Random.Range(0, idleVariantTriggers.Length)];
                if (HasTrigger(t)) { anim.PlayNamedTrigger(t); IdleVariantsPlayed++; LastIdleVariantAt = now; }
                ScheduleVariant();
            }
        }
    }

    void LateUpdate()
    {
        using (LateMarker.Auto()) ApplySway();
    }

    void ApplySway()
    {
        if (!sway || chest == null || ctrl == null || !ctrl.isAlive) { AppliedSwayDegrees = 0f; return; }
        if (entrance != null && !entrance.Complete) { AppliedSwayDegrees = 0f; return; }
        float yaw = Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(0.5f, swayPeriod)) * swayDegrees;
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        float since = Time.time - leanStart;
        if (since >= 0f && since < leanSeconds)
        {
            float k = 1f - since / leanSeconds;
            Vector3 local = chest.parent != null ? chest.parent.InverseTransformDirection(leanAxis) : leanAxis;
            q = Quaternion.AngleAxis(leanDegrees * k * k, local) * q;
        }
        AppliedSwayDegrees = Quaternion.Angle(Quaternion.identity, q);
        applied = q;
        chest.localRotation = chest.localRotation * q;
    }
}
