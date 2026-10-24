// MonsterPersonalSpace.cs
// Purpose: Personal-space clamp. Every frame, after animation, keeps every part of the monster's body (its hit-volume
//          capsules) at least headClearance from the player's head, and stops the monster from bringing any part of its
//          body closer than handClearance to either hand. Covers walking in, rush travel and clip-baked lunges: when a
//          limit would be breached the root slides straight back by the overlap (the attack still plays, it just travels
//          less). Pure transform maths on the cached capsules: no physics queries, no allocation.
//          Head: enforced whoever moved (the player cannot walk their face into the monster either).
//          Hands: only the monster's own motion is stopped. A player pushing a hand in keeps their swing; the monster
//          does not back away from it.
// Dependencies: MonsterController, OVRCameraRig (found through PlayerHealth), CapsuleCollider hit volumes
// Attach to: Monster root (Levels 1-3)

using UnityEngine;

[DefaultExecutionOrder(100)] // after MonsterAI.LateUpdate (walk-in / rush), so the clamp has the last word each frame
public class MonsterPersonalSpace : MonoBehaviour
{
    [Tooltip("Master toggle.")]
    public bool clampEnabled = true;
    [Tooltip("No part of the body may be closer than this to the player's head (m).")]
    public float headClearance = 0.6f;
    [Tooltip("The monster may not bring any part of its body closer than this to either hand (m).")]
    public float handClearance = 0.35f;
    [Tooltip("OFF (0): tested at 0.02 and it shoved the monster out of its own attack range and still left breaches. The monster may not bring any part of its body closer than this to a player weapon (m, from the weapon's " +
             "collider surface). Only the monster's own motion is stopped; a player swinging into the monster still hits it. 0 = off.")]
    public float weaponClearance = 0f;
    [Tooltip("EXPERIMENTAL, off: the monster's weapon (MonsterWeapon colliders) may not come closer than this to the head (m). A swing path crosses the head zone by design, so a horizontal push cannot clear it and it pushed the body into a hand in testing. Attacks still " +
             "reach: the push-back is added to MonsterAI's range check. 0 = off.")]
    public float weaponHeadClearance = 0f;
    [Tooltip("Fastest the root may be pushed away (m/s). 0 = no limit (the old behaviour: the full correction in one frame, which showed as a " +
             "0.3-0.4 m pop when an arm swung close to the head). > 0: the push is spread over a few frames, so the monster is eased back, not snapped.")]
    public float maxPushSpeed = 0f;
    [Tooltip("Push passes per frame (a curved body part can need a second pass).")]
    [Range(1, 6)] public int iterations = 4;

    /// <summary>Metres the root was pushed back this frame.</summary>
    public float LastPush { get; private set; }
    /// <summary>Metres pushed back since the current attack started; MonsterAI adds it back to its range checks so the
    /// clamp never turns a hit into a miss (only the player moving dodges).</summary>
    public float AttackPushback { get; private set; }
    public float MaxPush { get; private set; }
    /// <summary>Closest the body got to the head / to a hand after the clamp (verification).</summary>
    public float MinHeadDistance { get; private set; } = 99f;
    public float MinHandDistanceMonsterCaused { get; private set; } = 99f;
    /// <summary>Frames that still ended inside a limit after every pass (should stay 0).</summary>
    public int HeadBreaches { get; private set; }
    public int HandBreaches { get; private set; }

    private MonsterController controller;
    private Transform head, leftHand, rightHand;
    private CapsuleCollider[] capsules;
    private Collider[] weaponCols;
    // Player weapon sample points (surface points of each hit collider) + their radius.
    private readonly Vector3[][] pw = { new Vector3[27], new Vector3[27] };
    private readonly int[] pwCount = new int[2];
    private readonly float[] pwRadius = new float[2];
    private readonly HitDetection[] pwHd = new HitDetection[2];
    private readonly Transform[] pwOwner = new Transform[2];
    private readonly Collider[] pwCol = new Collider[2];
    /// <summary>Closest the monster's own motion brought a body part to a player weapon, and frames still inside the limit.</summary>
    public float MinWeaponDistanceMonsterCaused { get; private set; } = 99f;
    public int WeaponBreaches { get; private set; }
    private Vector3[] localA, localB;   // segment ends in each capsule's local space
    private float[] radius;
    private Vector3[] prevA, prevB;     // last frame's segment ends in world space (after the clamp)
    private bool hasPrev;
    private bool wasAttacking;

    void Awake()
    {
        controller = GetComponent<MonsterController>();
        CacheCapsules();
    }

    /// <summary>Re-reads the hit volumes (call if colliders are added at runtime).</summary>
    public void CacheCapsules()
    {
        var all = GetComponentsInChildren<CapsuleCollider>(true);
        int n = 0;
        foreach (var c in all) if (IsBody(c)) n++;
        capsules = new CapsuleCollider[n];
        localA = new Vector3[n]; localB = new Vector3[n]; radius = new float[n];
        prevA = new Vector3[n]; prevB = new Vector3[n];
        int i = 0;
        foreach (var c in all)
        {
            if (!IsBody(c)) continue;
            capsules[i] = c;
            Vector3 ls = c.transform.lossyScale;
            int dir = c.direction;
            Vector3 axis = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
            float axisScale = Mathf.Abs(dir == 0 ? ls.x : dir == 1 ? ls.y : ls.z);
            float radScale = dir == 0 ? Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z))
                           : dir == 1 ? Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z))
                           : Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
            radius[i] = c.radius * radScale;
            float halfLocal = Mathf.Max(0f, c.height * 0.5f - c.radius * radScale / Mathf.Max(1e-4f, axisScale));
            localA[i] = c.center - axis * halfLocal;
            localB[i] = c.center + axis * halfLocal;
            i++;
        }
        hasPrev = false;
        var w = new System.Collections.Generic.List<Collider>();
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (c.CompareTag("MonsterWeapon") && (c is BoxCollider || c is CapsuleCollider)) w.Add(c);
        weaponCols = w.ToArray();
    }

    // Nearest weapon sample point (box corners + centre / capsule axis points) to p, minus nothing: a conservative reach.
    float WeaponNearest(Vector3 p, out Vector3 point)
    {
        float best = float.MaxValue; point = p;
        foreach (var c in weaponCols)
        {
            if (c == null) continue;
            var t = c.transform;
            if (c is BoxCollider b)
            {
                Vector3 h = b.size * 0.5f;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    Vector3 q = t.TransformPoint(b.center + new Vector3(h.x * x, h.y * y, h.z * z));
                    float d = Vector3.Distance(q, p);
                    if (d < best) { best = d; point = q; }
                }
            }
            else if (c is CapsuleCollider cc)
            {
                Vector3 axis = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                for (int k = 0; k <= 4; k++)
                {
                    Vector3 q = t.TransformPoint(cc.center + axis * (cc.height * (k / 4f - 0.5f)));
                    float d = Vector3.Distance(q, p);
                    if (d < best) { best = d; point = q; }
                }
            }
        }
        return best;
    }

    /// <summary>Closest the monster's weapon got to the head after the clamp (verification).</summary>
    public float MinWeaponHeadDistance { get; private set; } = 99f;

    static bool IsBody(CapsuleCollider c) => c != null && !c.isTrigger && !c.CompareTag("MonsterWeapon");

    void ResolvePlayer()
    {
        if (head != null) return;
        var rig = PlayerHealth.Instance != null ? PlayerHealth.Instance.GetComponent<OVRCameraRig>() : null;
        if (rig == null) return;
        head = rig.centerEyeAnchor; leftHand = rig.leftHandAnchor; rightHand = rig.rightHandAnchor;
    }

    void LateUpdate()
    {
        if (GamePause.IsPaused) return;   // the monster is not pushed around by the player's head while paused
        LastPush = 0f;
        if (!clampEnabled || capsules == null || capsules.Length == 0) return;
        ResolvePlayer();
        if (head == null) return;

        RefreshPlayerWeapons();
        bool attacking = controller != null && controller.isAttacking;
        if (attacking && !wasAttacking) AttackPushback = 0f;
        wasAttacking = attacking;

        for (int pass = 0; pass < iterations; pass++)
        {
            float need = 0f, fh = 1f;
            Vector3 dir = Vector3.zero;

            // Head: always.
            float dh = Nearest(head.position, out Vector3 nh);
            if (dh < headClearance) { need = headClearance - dh; dir = Away(head.position, nh); fh = HFrac(head.position, nh); }

            // Weapon vs head (cosmetic personal space: the blade must not pass through the face).
            if (weaponHeadClearance > 0f && weaponCols != null && weaponCols.Length > 0)
            {
                float dw = WeaponNearest(head.position, out Vector3 nw);
                if (dw < weaponHeadClearance && weaponHeadClearance - dw > need) { need = weaponHeadClearance - dw; dir = Away(head.position, nw); }
            }

            // Player weapons: same rule as the hands (only what the monster's own motion caused).
            if (weaponClearance > 0f)
            {
                for (int w = 0; w < 2; w++)
                {
                    if (pwCount[w] == 0) continue;
                    float clear = weaponClearance + pwRadius[w];
                    for (int k = 0; k < pwCount[w]; k++)
                    {
                        Vector3 wp = pw[w][k];
                        float d = Nearest(wp, out Vector3 np);
                        if (d >= clear) continue;
                        float before = hasPrev ? NearestPrev(wp) : float.MaxValue;
                        if (before <= d + 1e-4f) continue;
                        float want = Mathf.Min(clear, before) - d;
                        if (want > need) { need = want; dir = Away(wp, np); fh = HFrac(wp, np); }
                    }
                }
            }

            // Hands: only what the monster's own motion caused this frame.
            for (int h = 0; h < 2; h++)
            {
                Transform hand = h == 0 ? leftHand : rightHand;
                if (hand == null) continue;
                float d = Nearest(hand.position, out Vector3 np);
                if (d >= handClearance) continue;
                float before = hasPrev ? NearestPrev(hand.position) : float.MaxValue;
                if (before <= d + 1e-4f) continue;                       // the hand came in, not the monster
                float want = Mathf.Min(handClearance, before) - d;
                if (want > need) { need = want; dir = Away(hand.position, np); fh = HFrac(hand.position, np); }
            }

            if (need <= 1e-4f) break;
            // A point above / below the body part cannot be cleared by sliding the root sideways one-for-one: scale the
            // slide by the horizontal share of the separation (capped so a point directly overhead cannot fling it).
            Vector3 step = dir * Mathf.Min(need / Mathf.Max(0.3f, fh), 0.4f);
            if (maxPushSpeed > 0f)
            {
                float room = maxPushSpeed * Time.deltaTime - LastPush;   // what this frame may still move the root
                if (room <= 1e-5f) break;
                if (step.magnitude > room) step = step.normalized * room;
            }
            transform.position += step;
            LastPush += step.magnitude;
            if (attacking) AttackPushback += Mathf.Max(0f, -Vector3.Dot(step, Flat(transform.forward)));
        }
        if (LastPush > MaxPush) MaxPush = LastPush;

        // Verification counters + the pose the next frame compares against.
        if (weaponCols != null && weaponCols.Length > 0) MinWeaponHeadDistance = Mathf.Min(MinWeaponHeadDistance, WeaponNearest(head.position, out _));
        float headD = Nearest(head.position, out _);
        MinHeadDistance = Mathf.Min(MinHeadDistance, headD);
        if (headD < headClearance - 0.005f) HeadBreaches++;
        for (int h = 0; h < 2; h++)
        {
            Transform hand = h == 0 ? leftHand : rightHand;
            if (hand == null) continue;
            float d = Nearest(hand.position, out _);
            float before = hasPrev ? NearestPrev(hand.position) : float.MaxValue;
            if (d < before - 1e-4f)
            {
                MinHandDistanceMonsterCaused = Mathf.Min(MinHandDistanceMonsterCaused, d);
                if (d < handClearance - 0.005f) HandBreaches++;
            }
        }
        if (weaponClearance > 0f)
            for (int w = 0; w < 2; w++)
            {
                float clear = weaponClearance + pwRadius[w];
                for (int k = 0; k < pwCount[w]; k++)
                {
                    float d = Nearest(pw[w][k], out _);
                    float before = hasPrev ? NearestPrev(pw[w][k]) : float.MaxValue;
                    if (d < before - 1e-4f)
                    {
                        MinWeaponDistanceMonsterCaused = Mathf.Min(MinWeaponDistanceMonsterCaused, d - pwRadius[w]);
                        if (d < clear - 0.005f) { WeaponBreaches++; break; }
                    }
                }
            }
        for (int i = 0; i < capsules.Length; i++)
        {
            var t = capsules[i].transform;
            prevA[i] = t.TransformPoint(localA[i]);
            prevB[i] = t.TransformPoint(localB[i]);
        }
        hasPrev = true;
    }

    void RefreshPlayerWeapons()
    {
        var pc = PlayerCombat.Instance;
        for (int w = 0; w < 2; w++)
        {
            pwCount[w] = 0;
            if (pc == null || weaponClearance <= 0f) continue;
            Transform root = w == 0 ? pc.weaponTransform : pc.offhandWeaponTransform;
            if (root == null || !root.gameObject.activeInHierarchy) continue;
            if (pwOwner[w] != root) { pwOwner[w] = root; pwHd[w] = root.GetComponentInChildren<HitDetection>(); pwCol[w] = pwHd[w] != null ? pwHd[w].GetComponent<Collider>() : null; }
            var col = pwCol[w];
            if (col == null) continue;
            var t = col.transform;
            if (col is BoxCollider b)
            {
                Vector3 h = b.size * 0.5f; int n = 0; pwRadius[w] = 0f;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    pw[w][n++] = t.TransformPoint(b.center + new Vector3(h.x * x, h.y * y, h.z * z));
                pwCount[w] = n;
            }
            else if (col is SphereCollider sp)
            {
                Vector3 ls = t.lossyScale;
                pw[w][0] = t.TransformPoint(sp.center); pwCount[w] = 1;
                pwRadius[w] = sp.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            }
            else if (col is CapsuleCollider cc)
            {
                Vector3 axis = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 ls = t.lossyScale;
                for (int k = 0; k < 5; k++) pw[w][k] = t.TransformPoint(cc.center + axis * (cc.height * (k / 4f - 0.5f)));
                pwCount[w] = 5; pwRadius[w] = cc.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            }
        }
    }

    /// <summary>Resets the verification counters (test harness).</summary>
    public void ResetStats()
    {
        MinWeaponDistanceMonsterCaused = 99f; WeaponBreaches = 0;
        MaxPush = 0f; MinWeaponHeadDistance = 99f; MinHeadDistance = 99f; MinHandDistanceMonsterCaused = 99f; HeadBreaches = 0; HandBreaches = 0;
    }

    /// <summary>Horizontal fraction (0-1) of the vector from a player point to the nearest body point.</summary>
    static float HFrac(Vector3 playerPoint, Vector3 bodyPoint)
    {
        Vector3 v = bodyPoint - playerPoint;
        float m = v.magnitude;
        if (m < 1e-4f) return 1f;
        v.y = 0f;
        return v.magnitude / m;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero; }

    /// <summary>Horizontal direction that moves the body away from the player point.</summary>
    Vector3 Away(Vector3 playerPoint, Vector3 bodyPoint)
    {
        Vector3 d = Flat(bodyPoint - playerPoint);
        if (d == Vector3.zero) d = Flat(transform.position - playerPoint);
        if (d == Vector3.zero) d = -Flat(transform.forward);
        return d;
    }

    /// <summary>Distance from p to the nearest body surface (0 inside) and that surface point.</summary>
    public float Nearest(Vector3 p, out Vector3 point)
    {
        float best = float.MaxValue; point = p;
        for (int i = 0; i < capsules.Length; i++)
        {
            var t = capsules[i].transform;
            float d = SegmentSurface(t.TransformPoint(localA[i]), t.TransformPoint(localB[i]), radius[i], p, out Vector3 q);
            if (d < best) { best = d; point = q; }
        }
        return best;
    }

    float NearestPrev(Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 0; i < capsules.Length; i++)
            best = Mathf.Min(best, SegmentSurface(prevA[i], prevB[i], radius[i], p, out _));
        return best;
    }

    static float SegmentSurface(Vector3 a, Vector3 b, float r, Vector3 p, out Vector3 surface)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float k = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
        Vector3 q = a + ab * k;
        Vector3 d = p - q;
        float dm = d.magnitude;
        if (dm <= r) { surface = p; return 0f; }
        surface = q + d * (r / dm);
        return dm - r;
    }
}
