// HitDetection.cs
// Purpose: Detect hammer collisions against monsters; compute damage from SWING VELOCITY
//          (not fixed collision). Min velocity gate prevents spam tapping. Awards XP.
//          Contacts come from two sources, resolved once per frame in LateUpdate (after the monster's animation, IK,
//          personal-space clamp and hit-shake, and after the flail chain): the swept test (WeaponSweep.cs, catches swings
//          that step over a thin collider between two 50 Hz physics samples) and the normal trigger events. Whichever
//          contact comes first along the swing wins, a weapon contact wins a tie, and one contact fires once.
//          A clash (parry) locks this weapon out of body damage for clashBodyLockSeconds.
// Dependencies: MonsterController, PlayerCombat, RoundManager, XPManager, MonsterHitShapes / SweepMath (WeaponSweep.cs),
//               CombatDebugInfo, optional VFX/SFX
// Attach to: Hammer head collider (tag this object "Hammer")

using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DefaultExecutionOrder(300)] // LateUpdate after MonsterPersonalSpace (100), MonsterHitShake / FlailChainSim (200)
[RequireComponent(typeof(Collider))]
public class HitDetection : MonoBehaviour
{
    // Telemetry hooks (SessionReporter). Raised synchronously; listeners must not throw.
    /// <summary>A swing landed and counted. Args: source, swing speed (m/s), monster was defending.</summary>
    public static event Action<HitDetection, float, bool> HitLanded;
    /// <summary>The weapon touched the monster below the minimum speed. Args: source, speed, minimum.</summary>
    public static event Action<HitDetection, float, float> SwingTooSlow;
    /// <summary>The weapon struck the monster's weapon (parry / clash). Args: source, speed.</summary>
    public static event Action<HitDetection, float> WeaponClashed;

    [Header("Damage — Velocity Based")]
    public float baseDamage = 12f;
    [Tooltip("Below this controller speed (m/s) the hit is IGNORED. Anti-spam. " +
             "Mirrors PlayerCombat.minSwingVelocity; the larger of the two is used.")]
    public float minSwingSpeed = 1.2f;
    [Tooltip("Speed at which velocity scaling caps (uses PlayerCombat.maxSwingVelocity if larger).")]
    public float maxSwingSpeed = 6f;
    [Tooltip("Max damage multiplier from velocity alone (uses PlayerCombat.maxVelocityDamageMultiplier if > 0).")]
    public float maxForceMultiplier = 2.5f;
    [Tooltip("Bonus for hits on the monster's front half (measured horizontally from its root).")]
    public float facingBonusMultiplier = 1.25f;

    [Header("Hit Rate Limit")]
    public float hitCooldown = 0.35f;

    [Header("Momentum (optional, e.g. flail)")]
    [Tooltip("Extra damage fraction for a swing that has stayed above the speed gate for momentumBuildSeconds " +
             "(0 = off). Rewards full wind-up swings over flicks.")]
    public float momentumMaxBonus = 0f;
    public float momentumBuildSeconds = 0.6f;

    [Header("Feedback (optional)")]
    public ParticleSystem hitParticles;
    public AudioSource audioSource;
    public AudioClip hitSound;
    [Tooltip("Secondary impact sound for big/full swings.")]
    public AudioClip heavyHitSound;
    [Tooltip("Swing speed threshold that switches to heavyHitSound.")]
    public float heavySwingSpeedThreshold = 4.8f;
    [Tooltip("Played when hammer hits the monster's weapon (parry/clash).")]
    public AudioClip clashSound;
    [Tooltip("Body block: played INSTEAD of hitSound when the hit lands on the body of a defending monster. " +
             "Empty = only the monster's own block clang (MonsterAI.defendBlockClip) is heard; if the monster " +
             "has none either, the normal hit sound is used.")]
    public AudioClip blockedHitSound;
    [Range(0f, 1f)] public float blockedHitVolume = 0.8f;
    public VRSoundButton soundButtonFallback;

    [Header("Hit feedback layers")]
    [Tooltip("Impact sound on a landed hit (normal, weak point, guarded). Off = silent hits; the clash sound has its own clip. " +
             "The other layers have their own toggles: blood (HitBloodFx.mode), haptics (CombatFeedback.haptics), damage numbers " +
             "(CombatUIController.builtInDamageNumbers), hit-shake (MonsterHitShake.shakeEnabled).")]
    public bool hitSounds = true;

    [Header("Sound variation")]
    [Tooltip("Pick a random variant + small random pitch per hit so repeated hits never sound identical.")]
    public bool varyHitSounds = true;
    [Tooltip("Variants for normal hits (hitSound is used when empty).")]
    public AudioClip[] hitVariants = new AudioClip[0];
    [Tooltip("Variants for heavy hits (heavyHitSound is used when empty).")]
    public AudioClip[] heavyHitVariants = new AudioClip[0];
    [Tooltip("Random pitch spread (0.06 = +/-6 %).")]
    [Range(0f, 0.2f)] public float pitchJitter = 0.06f;

    [Header("Valid contacts")]
    [Tooltip("One landed hit or clash per swing contact with a monster: after it, every collider of that monster (body parts, " +
             "blade, haft) is ignored until the weapon slows below the speed gate or has been out of the monster for " +
             "rearmOutsideSeconds. Stops a multi-collider body from sounding several times per swing.")]
    public bool oneContactPerSwing = true;
    public float rearmOutsideSeconds = 0.12f;
    [Tooltip("A clash needs a real swing: this weapon above the speed gate (a parry), the monster's weapon in an attack's " +
             "strike window (MonsterAI.InStrikeWindow, not wind-up or recovery), or a thrown axe. A monster weapon merely " +
             "brushing a held weapon (idle sway, guard pose, stepping, wind-up) plays no sound, sends no haptic and raises no " +
             "clash event.")]
    public bool clashNeedsSwingOrAttack = true;
    [Tooltip("One clash SOUND per monster strike across both player weapons: a second weapon clashing with the same monster " +
             "within this many seconds stays silent (its hand still gets the haptic). 0 = off.")]
    public float sharedClashSoundWindow = 0.15f;
    [Tooltip("Ignore the monster while its entrance (summon / rise / roar) is still playing: no damage, no sound.")]
    public bool ignoreDuringEntrance = true;

    [Header("Swept detection (fast swings)")]
    [Tooltip("Each frame, move this weapon's collider from its previous pose to its current one in small steps against the " +
             "monster's weapon and body colliders (moving too). Catches swings that pass through a thin collider between two " +
             "physics steps. Off = trigger events only.")]
    public bool sweptDetection = true;
    [Tooltip("Longest relative travel (m) between two sub-steps; a thinner weapon uses half its thickness.")]
    public float sweepStep = 0.03f;
    [Tooltip("Upper bound on sub-steps per frame (32 x 3 cm = about 1 m of travel in one frame).")]
    [Range(1, 64)] public int maxSweepSubSteps = 32;
    [Tooltip("The sweep only runs while this weapon is within this distance (m) of the monster's root.")]
    public float sweepRange = 4f;

    [Header("Parry (weapon clash)")]
    [Tooltip("After a clash this weapon deals no body damage for at least this long (s): the parried swing cannot go on to " +
             "score. Parries never fill the monster's guard meter.")]
    public float clashBodyLockSeconds = 0.3f;
    [Tooltip("The clashed flag also holds until that swing ends (the weapon slows below the speed gate), so a parried swing " +
             "that carries on into the body later still deals nothing. Never longer than this (s) after the clash.")]
    public float clashSwingLockMax = 1f;
    [Tooltip("One clash per player weapon / monster weapon pair per contact: the same pair cannot clash again for this long (s).")]
    public float clashPairCooldown = 0.25f;
    [Tooltip("Play the clash clip at one of three pitches (never the same one twice in a row).")]
    public bool clashPitchVariants = true;
    public Vector3 clashPitches = new Vector3(0.93f, 1f, 1.08f);

    [Header("Who can deal hits")]
    public string hammerTag = "Hammer";
    public string playerHandTag = "PlayerHand";

    [Header("Dual-wield")]
    [Tooltip("Which hand this hammer is in. Right = primary (uses CurrentSwingSpeed). " +
             "Left = offhand (uses CurrentOffhandSwingSpeed). Haptics use the hand anchor the weapon actually sits under " +
             "(ControllerHand), so a mismatch here cannot buzz the wrong controller.")]
    public Hand hand = Hand.Right;

    /// <summary>What the last monster contact resolved to (verification / debug).</summary>
    public enum ContactResult { None, TooSlow, Clash, Blocked, Hit }
    public ContactResult LastResult { get; private set; }
    /// <summary>True if the last Hit / Blocked contact was on a MonsterWeakPoint collider.</summary>
    public bool LastWasWeakPoint { get; private set; }
    /// <summary>Damage passed to the monster by the last Hit / Blocked contact (before the monster's own defend reduction).</summary>
    public float LastDamage { get; private set; }
    /// <summary>Monster struck by the last Hit / Blocked contact.</summary>
    public MonsterController LastMonster { get; private set; }
    public Vector3 LastHitPoint { get; private set; }
    /// <summary>Monster collider struck by the last Hit / Blocked contact (its transform follows a bone: blood drips use it).</summary>
    public Collider LastHitCollider { get; private set; }
    /// <summary>Contact point of the last weapon clash.</summary>
    public Vector3 LastClashPoint { get; private set; }
    /// <summary>Monster weapon of the last clash (blade and haft of one axe are one weapon).</summary>
    public Transform LastClashWeapon { get; private set; }
    /// <summary>True if the front-hit bonus applied to the last Hit / Blocked contact (verification).</summary>
    public bool LastWasFrontHit { get; private set; }
    /// <summary>Controller that holds this weapon: the hand anchor it sits under (falls back to <see cref="hand"/>).</summary>
    public Hand ControllerHand { get; private set; }

    // Verification counters (where each accepted contact came from, and what the new rules refused).
    public int SweepClashes { get; private set; }
    public int SweepBodyContacts { get; private set; }
    public int TriggerClashes { get; private set; }
    public int TriggerBodyContacts { get; private set; }
    public int BodyBlockedByClash { get; private set; }
    public int PairCooldownRejects { get; private set; }
    /// <summary>Clashes accepted by every HitDetection since the scene loaded.</summary>
    public static int TotalClashes { get; private set; }

    private float lastHitTime = -999f;
    private Vector3 lastPosition;
    private Vector3 velocity;
    // One landed hit per swing contact: re-armed when the swing ends (below the gate) or, with oneContactPerSwing, once the
    // weapon has left the struck monster.
    private bool stayArmed = true;
    private UnityEngine.Object contactTarget;
    private float lastTouchTime = -999f;
    private bool lastContactBody;
    private float clashLockUntil = -999f;
    private bool clashedSwing;      // set by a clash, cleared when the swing ends (below the gate) or after clashSwingLockMax
    private float clashedAt = -999f;
    /// <summary>This weapon's current swing was parried: no body damage until the swing ends.</summary>
    public bool ClashLocked => Time.time < clashLockUntil || clashedSwing;

    // Clash cooldown per monster weapon this weapon met (small fixed table, no allocation).
    private readonly Transform[] pairWeapon = new Transform[4];
    private readonly float[] pairTime = { -999f, -999f, -999f, -999f };

    // Collider -> owning monster (null = not a valid target: floor, walls, the other player weapon, the rig). Filled once per
    // collider so OnTriggerStay never walks the hierarchy every physics step.
    static readonly Dictionary<Collider, MonsterController> ownerCache = new Dictionary<Collider, MonsterController>();
    static readonly Dictionary<Collider, Transform> weaponRootCache = new Dictionary<Collider, Transform>();
    static UnityEngine.Object lastClashSoundTarget;
    static float lastClashSoundTime = -999f;
    private MonsterController entranceOwner;
    private MonsterEntrance entrance;
    private MonsterAI ownerAi;
    private float aboveGateSince = -1f;
    private int lastPitch = -1;

    // Trigger contacts of this frame, resolved in LateUpdate together with the sweep (weapons first).
    private struct Pending { public Collider col; public Vector3 point; public bool hasPoint, stay; }
    private readonly Pending[] pending = new Pending[12];
    private int pendingCount;

    // Sweep state (preallocated).
    private SweepShape self;
    private bool selfValid;
    private SweepMath.Geo[] selfAt = new SweepMath.Geo[33];
    private readonly int[] cand = new int[64];

    void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void Awake()
    {
        selfValid = SweepShape.TryMake(GetComponent<Collider>(), out self);
        ResolveControllerHand();
    }

    void OnEnable()
    {
        // Shown, equipped or swapped (Level 3 pickup): no motion carried over from wherever it was last.
        ResetSweepHistory();
        pendingCount = 0;
        lastPosition = transform.position;
        velocity = Vector3.zero;
    }

    void OnDisable() { pendingCount = 0; }

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.pitch = 1f;
            audioSource.dopplerLevel = 0f;
        }
        lastPosition = transform.position;
    }

    /// <summary>The hand anchor this weapon is parented under decides which controller buzzes.</summary>
    void ResolveControllerHand()
    {
        ControllerHand = hand;
        for (var t = transform; t != null; t = t.parent)
        {
            if (t.name.StartsWith("LeftHandAnchor") || t.name.StartsWith("LeftControllerAnchor")) { ControllerHand = Hand.Left; break; }
            if (t.name.StartsWith("RightHandAnchor") || t.name.StartsWith("RightControllerAnchor")) { ControllerHand = Hand.Right; break; }
        }
        if (ControllerHand != hand)
            Debug.LogWarning($"[HitDetection] {name}: 'hand' is {hand} but the weapon sits under the {ControllerHand} hand anchor; haptics use {ControllerHand}.");
    }

    /// <summary>Forget the weapon's previous pose (teleport, equip): the next frame does not sweep the jump.</summary>
    public void ResetSweepHistory()
    {
        self.ResetHistory();
    }

    void Update()
    {
        // Estimate swing velocity from transform motion (works for kinematic held hammers)
        velocity = (transform.position - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;

        float speedNow = ImpactSpeed();
        if (clashedSwing && Time.time >= clashLockUntil && (speedNow < MinSwingSpeed() || Time.time - clashedAt > clashSwingLockMax))
            clashedSwing = false;
        if (!stayArmed && (speedNow < MinSwingSpeed() ||
                           (oneContactPerSwing && Time.time - lastTouchTime > rearmOutsideSeconds)))
            stayArmed = true;

        if (momentumMaxBonus > 0f)
        {
            if (speedNow >= MinSwingSpeed()) { if (aboveGateSince < 0f) aboveGateSince = Time.time; }
            else aboveGateSince = -1f;
        }
    }

    static readonly ProfilerMarker SweepMarker = new ProfilerMarker("HitDetection.Sweep");

    void LateUpdate()
    {
        if (selfValid) self.Advance();
        if (sweptDetection && selfValid) { using (SweepMarker.Auto()) Sweep(); }
        ResolvePending();
    }

    float MomentumMultiplier()
    {
        if (momentumMaxBonus <= 0f || aboveGateSince < 0f) return 1f;
        return 1f + momentumMaxBonus * Mathf.Clamp01((Time.time - aboveGateSince) / Mathf.Max(0.01f, momentumBuildSeconds));
    }

    // ───────────────────────── trigger path (collected, resolved in LateUpdate) ─────────────────────────
    void OnTriggerEnter(Collider other) { Enqueue(other, Vector3.zero, false, false); }

    // A contact rejected on entry (too slow, or inside the cooldown after a clash / the other body part) used to be
    // lost for good, because Enter fires once. While the weapon is still inside the monster it is retried here.
    void OnTriggerStay(Collider other) { Enqueue(other, Vector3.zero, false, true); }

    void OnCollisionEnter(Collision collision)
    {
        Enqueue(collision.collider, collision.contactCount > 0 ? collision.GetContact(0).point : transform.position, true, false);
    }

    void Enqueue(Collider other, Vector3 point, bool hasPoint, bool stay)
    {
        for (int i = 0; i < pendingCount; i++)
            if (pending[i].col == other) { if (!stay) pending[i].stay = false; return; } // one entry per collider per frame
        if (pendingCount >= pending.Length) return;
        pending[pendingCount++] = new Pending { col = other, point = point, hasPoint = hasPoint, stay = stay };
    }

    void ResolvePending()
    {
        if (pendingCount == 0) return;
        // Monster weapons first (a weapon contact wins a tie), then body entries, then body "still inside" retries.
        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < pendingCount; i++)
            {
                var p = pending[i];
                if (p.col == null || !p.col.enabled) continue;
                bool weapon = p.col.CompareTag("MonsterWeapon");
                if (pass == 0 ? !weapon : (weapon || (pass == 1) == p.stay)) continue;
                Contact(p.col, null, p.hasPoint ? p.point : (Vector3?)null, p.stay, false);
            }
        pendingCount = 0;
    }

    // ───────────────────────── swept path ─────────────────────────
    void Sweep()
    {
        var bm = BattleManager.Instance;
        var m = bm != null ? bm.ActiveMonster : null;
        if (m == null || !m.isAlive || !self.active || !self.hasPrev) return;
        if (InEntrance(m)) return;
        if ((self.curPos - m.transform.position).sqrMagnitude > sweepRange * sweepRange) return;

        var table = MonsterHitShapes.For(m);
        table.BeginFrame();
        var shapes = table.shapes;

        // Broad phase: shapes whose bounding spheres can meet this frame (each centre moves at most its Travel).
        float selfTravel = self.Travel, selfBound = self.BoundRadius;
        Vector3 selfC = self.CenterAt(1f);
        float maxOther = 0f;
        int n = 0;
        for (int pass = 0; pass < 2; pass++) // weapon shapes first
            for (int i = 0; i < shapes.Length && n < cand.Length; i++)
            {
                if (table.isWeapon[i] != (pass == 0)) continue;
                ref var s = ref shapes[i];
                if (!s.active || !s.hasPrev) continue;
                float otherTravel = s.Travel;
                float reach = selfBound + s.BoundRadius + selfTravel + otherTravel;
                if ((selfC - s.CenterAt(1f)).sqrMagnitude > reach * reach) continue;
                cand[n++] = i;
                if (otherTravel > maxOther) maxOther = otherTravel;
            }
        if (n == 0) return;

        float step = Mathf.Clamp(0.5f * self.Thickness, 0.01f, Mathf.Max(0.01f, sweepStep));
        int maxK = Mathf.Clamp(maxSweepSubSteps, 1, 64);
        if (selfAt.Length < maxK + 1) selfAt = new SweepMath.Geo[maxK + 1];
        int K = Mathf.Clamp(Mathf.CeilToInt((selfTravel + maxOther) / step), 1, maxK);
        for (int k = 0; k <= K; k++) self.At(k / (float)K, out selfAt[k]);

        int bestK = int.MaxValue, best = -1;
        bool bestWeapon = false;
        Vector3 bestPoint = Vector3.zero;
        for (int c = 0; c < n; c++)
        {
            int i = cand[c];
            ref var s = ref shapes[i];
            bool weapon = table.isWeapon[i];
            // Cheap reject per sub-step: bounding spheres around the interpolated centres (+5 cm for the centre's own swing).
            Vector3 c0 = s.CenterAt(0f), c1 = s.CenterAt(1f);
            float rr = selfBound + s.BoundRadius + 0.05f; rr *= rr;
            // Already touching at the start of the frame: an ongoing contact, not a new one (the trigger path retries it).
            if ((selfAt[0].c - c0).sqrMagnitude <= rr)
            {
                s.At(0f, out var g0);
                if (SweepMath.Overlap(selfAt[0], g0)) { if (weapon) TouchPair(table.weaponRoot[i]); continue; }
            }
            int limit = Mathf.Min(K, bestK);
            for (int k = 1; k <= limit; k++)
            {
                float f = k / (float)K;
                if ((selfAt[k].c - Vector3.LerpUnclamped(c0, c1, f)).sqrMagnitude > rr) continue;
                s.At(f, out var g);
                if (!SweepMath.Overlap(selfAt[k], g)) continue;
                // Earlier wins; on the same sub-step a weapon wins over the body.
                if (k < bestK || (k == bestK && weapon && !bestWeapon))
                {
                    bestK = k; best = i; bestWeapon = weapon;
                    bestPoint = SweepMath.Closest(g, selfAt[k].c);
                }
                break;
            }
        }
        if (best < 0) return;

        var col = shapes[best].collider;
        if (bestWeapon) Clash(col, m, table.weaponRoot[best], m, bestPoint, false, true);
        else
        {
            if (m == contactTarget) lastTouchTime = Time.time;
            Body(col, m, m, bestPoint, false, true);
        }
    }

    // ───────────────────────── resolution (both paths) ─────────────────────────
    static MonsterController OwnerOf(Collider c)
    {
        if (!ownerCache.TryGetValue(c, out var m))
        {
            if (ownerCache.Count > 512) ownerCache.Clear(); // scene changes leave dead keys behind
            m = c.GetComponentInParent<MonsterController>();
            ownerCache[c] = m;
        }
        return m;
    }

    static Transform WeaponRootOf(Collider c)
    {
        if (!weaponRootCache.TryGetValue(c, out var t))
        {
            if (weaponRootCache.Count > 128) weaponRootCache.Clear();
            t = MonsterHitShapes.WeaponRootOf(c.transform);
            weaponRootCache[c] = t;
        }
        return t;
    }

    void CacheOwnerParts(MonsterController m)
    {
        if (m == entranceOwner) return;
        entranceOwner = m; entrance = m.GetComponent<MonsterEntrance>(); ownerAi = m.GetComponent<MonsterAI>();
    }

    bool InEntrance(MonsterController m)
    {
        if (!ignoreDuringEntrance || m == null) return false;
        CacheOwnerParts(m);
        return entrance != null && !entrance.Complete;
    }

    /// <summary>The monster's weapon is actually swinging: an attack-table strike window, or any attack for the older
    /// pattern-based AI (no strike timing available).</summary>
    bool MonsterStriking(MonsterController m)
    {
        if (!m.isAttacking) return false;
        CacheOwnerParts(m);
        return ownerAi == null || !ownerAi.UsesAttackTable || ownerAi.InStrikeWindow;
    }

    /// <summary>Trigger path: only a monster's body colliders and MonsterWeapon colliders count (also a thrown axe in flight).
    /// Everything else (floor, walls, the other player weapon, the player's own rig) is ignored before any sound, damage or haptic.</summary>
    void Contact(Collider other, MonsterController knownOwner, Vector3? explicitPoint, bool fromStay, bool fromSweep)
    {
        bool monsterWeapon = other.CompareTag("MonsterWeapon");
        MonsterController owner = knownOwner != null ? knownOwner : OwnerOf(other);
        if (owner == null && !monsterWeapon) return;
        if (owner != null && (!owner.isAlive || InEntrance(owner))) return;
        UnityEngine.Object target = owner != null ? (UnityEngine.Object)owner : other.transform.root;
        if (target == contactTarget) lastTouchTime = Time.time;

        if (monsterWeapon)
            Clash(other, owner, WeaponRootOf(other), target, explicitPoint ?? other.ClosestPoint(transform.position), fromStay, fromSweep);
        else
            Body(other, owner, target, explicitPoint, fromStay, fromSweep);
    }

    bool PairOnCooldown(Transform weapon)
    {
        for (int i = 0; i < pairWeapon.Length; i++)
            if (pairWeapon[i] == weapon && Time.time - pairTime[i] < clashPairCooldown) return true;
        return false;
    }

    /// <summary>Still touching this monster weapon (another of its boxes, or the same one): the pair's cooldown runs from the
    /// end of the contact, so one long contact is one clash.</summary>
    void TouchPair(Transform weapon)
    {
        for (int i = 0; i < pairWeapon.Length; i++)
            if (pairWeapon[i] == weapon && Time.time - pairTime[i] < clashPairCooldown) { pairTime[i] = Time.time; return; }
    }

    void MarkPair(Transform weapon)
    {
        int slot = 0;
        for (int i = 0; i < pairWeapon.Length; i++)
        {
            if (pairWeapon[i] == weapon) { slot = i; break; }
            if (pairTime[i] < pairTime[slot]) slot = i; // oldest
        }
        pairWeapon[slot] = weapon; pairTime[slot] = Time.time;
    }

    void Clash(Collider other, MonsterController owner, Transform weaponRoot, UnityEngine.Object target, Vector3 point, bool fromStay, bool fromSweep)
    {
        if (fromStay) { TouchPair(weaponRoot); return; } // a clash is the moment of contact only; resting on the weapon is not a parry
        if (PairOnCooldown(weaponRoot)) { TouchPair(weaponRoot); PairCooldownRejects++; return; }
        // This swing already landed on the body: it cannot turn into a parry on the way through.
        if (oneContactPerSwing && !stayArmed && lastContactBody && target == contactTarget) return;
        bool thrown = owner == null || !weaponRoot.IsChildOf(owner.transform);
        if (clashNeedsSwingOrAttack && !thrown && !MonsterStriking(owner) && ImpactSpeed() < MinSwingSpeed()) return;

        MarkPair(weaponRoot);
        lastHitTime = Time.time;
        stayArmed = false; // a parried swing cannot go on to score on the body it is still passing through
        contactTarget = target; lastTouchTime = Time.time; lastContactBody = false;
        clashLockUntil = Time.time + clashBodyLockSeconds;
        clashedSwing = true; clashedAt = Time.time;
        LastResult = ContactResult.Clash;
        LastClashPoint = point;
        LastClashWeapon = weaponRoot;
        if (fromSweep) SweepClashes++; else TriggerClashes++;
        TotalClashes++;
        CombatDebugInfo.NoteClash(ControllerHand);
        WeaponClashed?.Invoke(this, ImpactSpeed());

        bool otherWeaponJustClanged = sharedClashSoundWindow > 0f && target == lastClashSoundTarget &&
                                      Time.time - lastClashSoundTime < sharedClashSoundWindow;
        if (audioSource != null && clashSound != null && !otherWeaponJustClanged)
        {
            lastClashSoundTarget = target; lastClashSoundTime = Time.time;
            // Clash clip in project is a long riser (~5s); keep it short/quiet so hits don't feel 2x frantic.
            float vol = clashSound.length > 1.5f ? 0.45f : 0.85f;
            audioSource.pitch = ClashPitch();
            audioSource.PlayOneShot(clashSound, vol);
            CombatDebugInfo.NoteClashSound(clashSound, audioSource.pitch);
            SfxTrace.Note(this, clashSound, fromSweep ? "clash: swept contact with a MonsterWeapon collider" : "clash: player weapon entered a MonsterWeapon collider");
        }

        if (hitParticles != null)
        {
            hitParticles.transform.position = point;
            hitParticles.Play();
        }
    }

    float ClashPitch()
    {
        if (!clashPitchVariants) { Jitter(); return audioSource.pitch; }
        int i = UnityEngine.Random.Range(0, 3);
        if (i == lastPitch) i = (i + 1 + UnityEngine.Random.Range(0, 2)) % 3;
        lastPitch = i;
        return clashPitches[i];
    }

    void Body(Collider other, MonsterController monster, UnityEngine.Object target, Vector3? explicitPoint, bool fromStay, bool fromSweep)
    {
        // A parried swing deals no body damage for clashBodyLockSeconds.
        if (ClashLocked) { if (!fromStay) BodyBlockedByClash++; return; }
        if (Time.time - lastHitTime < hitCooldown) return;
        if (oneContactPerSwing && !stayArmed && target == contactTarget) return;
        if (fromStay && !stayArmed) return;

        // --- Swing speed at moment of impact ---
        float speed = ImpactSpeed();

        // --- Min velocity gate: anti-spam ---
        float minVel = MinSwingSpeed();

        if (speed < minVel)
        {
            // Too slow — ignore. This is the core anti-cheese rule.
            if (!fromStay)
            {
                LastResult = ContactResult.TooSlow;
                SwingTooSlow?.Invoke(this, speed, minVel);
            }
            return;
        }

        // --- Velocity → damage multiplier ---
        float maxVel = maxSwingSpeed;
        float maxMul = maxForceMultiplier;
        if (PlayerCombat.Instance != null)
        {
            if (PlayerCombat.Instance.maxSwingVelocity > maxVel) maxVel = PlayerCombat.Instance.maxSwingVelocity;
            if (PlayerCombat.Instance.maxVelocityDamageMultiplier > maxMul) maxMul = PlayerCombat.Instance.maxVelocityDamageMultiplier;
        }
        float t = Mathf.InverseLerp(minVel, maxVel, speed);
        float forceMul = Mathf.Lerp(1f, maxMul, t);

        // --- Angle bonus (stretch goal) ---
        float angleMul = 1f;
        if (PlayerCombat.Instance != null)
            angleMul = PlayerCombat.Instance.GetAngleBonusMultiplier(PlayerCombat.Instance.CurrentAngularSpeed);

        Vector3 hitPoint = explicitPoint ?? other.ClosestPoint(transform.position);
        float facingMul = ComputeFacingMultiplier(monster.transform, hitPoint);

        var weakPoint = other.GetComponent<MonsterWeakPoint>();
        float weakMul = weakPoint != null ? Mathf.Max(1f, weakPoint.damageMultiplier) : 1f;

        float damage = baseDamage * forceMul * angleMul * facingMul * MomentumMultiplier() * weakMul;

        // --- Stamina + fitness modifiers ---
        if (PlayerCombat.Instance != null)
        {
            if (!PlayerCombat.Instance.TrySpendAttackStamina())
                damage *= 0.5f; // exhausted swings still hit but weaker
            damage = PlayerCombat.Instance.ApplyFitnessModifiers(damage, speed);
        }

        lastHitTime = Time.time;
        stayArmed = false;
        contactTarget = monster; lastTouchTime = Time.time; lastContactBody = true;
        if (fromSweep) SweepBodyContacts++; else TriggerBodyContacts++;

        var monsterAi = monster.GetComponent<MonsterAI>();
        // Same decision the monster makes: guarding AND the raised guard covers where this hit landed (zone guard).
        bool monsterDefending = monster.WouldBlock(hitPoint);
        LastResult = monsterDefending ? ContactResult.Blocked : ContactResult.Hit;
        LastDamage = damage;
        LastWasWeakPoint = weakPoint != null;
        LastWasFrontHit = facingMul > 1f;
        LastMonster = monster;
        LastHitPoint = hitPoint;
        LastHitCollider = other;
        monster.OnTakeDamage(damage, hitPoint, speed, weakPoint != null);
        if (weakPoint != null) weakPoint.NotifyHit(this);
        if (monsterDefending)
        {
            if (hitSounds) PlayBlockedFeedback(hitPoint, speed, monsterAi);
        }
        else if (weakPoint != null && weakPoint.hitClip != null && audioSource != null && hitSounds)
        {
            // Weak point: its own confirmation sound instead of the normal flesh hit.
            if (hitParticles != null) { hitParticles.transform.position = hitPoint; hitParticles.Play(); }
            Jitter();
            audioSource.PlayOneShot(weakPoint.hitClip, weakPoint.hitVolume);
            SfxTrace.Note(this, weakPoint.hitClip, "hit: landed on weak point");
        }
        else if (hitSounds)
            PlayFeedback(hitPoint, speed);

        // --- XP: reward good form (full swing > tap) ---
        if (XPManager.Instance != null)
            XPManager.Instance.AwardHitXP(speed, damage);

        // Keep RoundManager fitness metrics in sync (same as CubeTapCounter)
        if (RoundManager.Instance != null && RoundManager.Instance.roundActive)
            RoundManager.Instance.RegisterTap();

        HitLanded?.Invoke(this, speed, monsterDefending);

        if (BattleManager.Instance != null)
            BattleManager.Instance.RegisterSuccessfulHit(damage);
    }

    float MinSwingSpeed()
    {
        float minVel = minSwingSpeed;
        if (PlayerCombat.Instance != null)
            minVel = Mathf.Max(minVel, PlayerCombat.Instance.minSwingVelocity);
        return minVel;
    }

    /// <summary>Weapon speed right now: the faster of this collider's motion and its hand's tracked speed.</summary>
    float ImpactSpeed()
    {
        float speed = velocity.magnitude;
        if (PlayerCombat.Instance != null)
        {
            float handSpeed = (hand == Hand.Left)
                ? PlayerCombat.Instance.CurrentOffhandSwingSpeed
                : PlayerCombat.Instance.CurrentSwingSpeed;
            speed = Mathf.Max(speed, handSpeed);
        }
        return speed;
    }

    float ComputeFacingMultiplier(Transform monsterRoot, Vector3 hitPoint)
    {
        // Horizontal: the root is at the feet, so a 3-D direction to a chest-height hit point pointed mostly up and the
        // front bonus almost never fired (dot ~0.2 for a hit dead centre on the chest).
        Vector3 toHit = hitPoint - monsterRoot.position;
        toHit.y = 0f;
        if (toHit.sqrMagnitude < 1e-6f) return 1f;
        toHit.Normalize();
        Vector3 fwd = monsterRoot.forward; fwd.y = 0f; fwd.Normalize();
        float facing = Vector3.Dot(fwd, toHit); // front hits ~1
        if (facing > 0.35f) return facingBonusMultiplier;
        if (facing < -0.35f) return 0.85f;
        return 1f;
    }

    void PlayFeedback(Vector3 hitPoint, float swingSpeed)
    {
        if (hitParticles != null)
        {
            hitParticles.transform.position = hitPoint;
            hitParticles.Play();
        }

        if (audioSource != null)
        {
            AudioClip clipToPlay = null;
            bool heavy = swingSpeed >= heavySwingSpeedThreshold && (heavyHitSound != null || (varyHitSounds && heavyHitVariants.Length > 0));
            if (heavy)
                clipToPlay = Pick(heavyHitVariants, heavyHitSound);
            else
                clipToPlay = Pick(hitVariants, hitSound != null ? hitSound : heavyHitSound);

            if (clipToPlay != null)
            {
                Jitter();
                audioSource.PlayOneShot(clipToPlay);
                SfxTrace.Note(this, clipToPlay, heavy ? "hit: landed (heavy)" : "hit: landed");
            }
        }
        else if (soundButtonFallback != null)
            soundButtonFallback.PlayClickSound();
    }

    /// <summary>
    /// Body block (monster defending): no flesh-hit sound. Either this weapon's blockedHitSound or the monster's
    /// own block clang is heard, so a block sounds different from a normal hit and from a weapon clash.
    /// </summary>
    void PlayBlockedFeedback(Vector3 hitPoint, float swingSpeed, MonsterAI monsterAi)
    {
        if (audioSource != null && blockedHitSound != null)
        {
            Jitter();
            audioSource.PlayOneShot(blockedHitSound, blockedHitVolume);
            SfxTrace.Note(this, blockedHitSound, "block: landed on a guarding monster");
            return;
        }

        bool monsterPlaysBlockCue = monsterAi != null && monsterAi.defendBlockClip != null && monsterAi.sfxAudioSource != null;
        if (!monsterPlaysBlockCue)
            PlayFeedback(hitPoint, swingSpeed); // no block cue anywhere: keep the hit audible
    }

    AudioClip Pick(AudioClip[] variants, AudioClip fallback)
    {
        if (!varyHitSounds || variants == null || variants.Length == 0) return fallback;
        var c = variants[UnityEngine.Random.Range(0, variants.Length)];
        return c != null ? c : fallback;
    }

    void Jitter()
    {
        if (audioSource != null)
            audioSource.pitch = varyHitSounds ? 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter) : 1f;
    }

    public Vector3 CurrentVelocity => velocity;
}
