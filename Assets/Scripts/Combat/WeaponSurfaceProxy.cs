// WeaponSurfaceProxy.cs
// Purpose: Keeps the VISIBLE player weapon from burying itself in the monster. A controller cannot be stopped, so the logical
//          weapon (the object with the collider, Rigidbody and HitDetection, exactly where the controller is) is never moved:
//          swing speed, damage, hit and clash events all keep using the true pose. Only the drawn mesh is moved, out along the
//          contact normal, so it rests on the surface of the monster's body colliders (and on the monster's weapon colliders)
//          with at most ~3 cm of visible sink.
//          - Every LateUpdate, when the monster is within engageDistance: find the deepest overlap of the weapon's collider shape
//            with the monster's colliders (Physics.ComputePenetration at explicit poses, no allocation), push the drawn pose out
//            along the contact normal. The push direction is "sticky": a weapon that has passed deep into the body is not thrown
//            out of the far side; it keeps resting on the surface it came in through (line search along the approach side).
//          - A new push is applied at once (a hard constraint, so the weapon visibly stops on the surface); the release back to the
//            true pose is a critically damped spring (smoothTime ~35 ms). The offset is capped at maxOffset (0.35 m).
//          - Above ghostThreshold (6 cm) a faint, unlit copy of the weapon is drawn at the true controller pose.
//          - A light haptic tick on the striking hand (CombatFeedback.PenetrationTick), scaled by how far the controller is
//            inside, rate limited.
//          Flail: its ball is placed by FlailChainSim each LateUpdate (a Verlet pendulum, not a joint or a rigidbody), so the
//          proxy offsets the drawn ball and bends the chain links toward it (offset x t^2 along the chain).
// Dependencies: HitDetection (same object), BattleManager (ActiveMonster), CombatFeedback (optional), FlailChainSim (optional)
// Attach to: every player weapon object with a HitDetection (added by PlayerCombat.EnsureWeaponProxies at equip / battle start)

using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DefaultExecutionOrder(1000)]   // after the animator, FlailChainSim (200), the personal-space clamp and the hit shake
[DisallowMultipleComponent]
public class WeaponSurfaceProxy : MonoBehaviour
{
    [Header("Proxy (visual only)")]
    [Tooltip("Master toggle. Off = the weapon is drawn exactly at the controller (the old behaviour).")]
    public bool proxyEnabled = true;
    [Tooltip("Only work when a monster collider is within this distance (m) of the weapon.")]
    public float engageDistance = 0.6f;
    [Tooltip("Hard limit (m) on how far the drawn weapon may sink into a monster collider.")]
    public float maxSink = 0.03f;
    [Tooltip("Where the drawn weapon rests (m of sink): a little inside the surface, so no gap shows.")]
    public float restSink = 0.01f;
    [Tooltip("Largest distance (m) the drawn weapon may be moved away from the controller.")]
    public float maxOffset = 0.35f;
    [Tooltip("Critically damped release time (s) back to the controller pose.")]
    public float smoothTime = 0.035f;
    [Range(1, 4)] public int iterations = 3;
    [Tooltip("Include the monster's weapon colliders (the visible weapon stops on the monster's weapon).")]
    public bool includeMonsterWeapons = true;

    [Header("Ghost of the real pose")]
    public bool ghostEnabled = true;
    [Tooltip("Show the ghost when the drawn weapon is this far (m) from the controller pose.")]
    public float ghostThreshold = 0.06f;
    [Tooltip("Faint unlit transparent material (M_WeaponGhost).")]
    public Material ghostMaterial;

    [Header("Haptic tick")]
    public bool hapticEnabled = true;
    [Range(0f, 1f)] public float hapticMin = 0.1f, hapticMax = 0.35f;
    [Tooltip("Offset (m) at which the tick reaches hapticMax.")]
    public float hapticFullAt = 0.2f;
    public float hapticInterval = 0.12f;

    // ── runtime (read-only, verification) ──
    /// <summary>World-space vector from the controller pose to the drawn pose.</summary>
    public Vector3 VisualOffsetWorld { get; private set; }
    public float Offset => VisualOffsetWorld.magnitude;
    public bool GhostShown { get; private set; }
    public int Ticks { get; private set; }
    public float PeakOffset { get; private set; }
    /// <summary>Deepest sink (m) of the DRAWN pose into any monster collider this frame (after the proxy).</summary>
    public float DrawnSink { get; private set; }
    /// <summary>Deepest sink (m) of the controller pose this frame.</summary>
    public float TrueSink { get; private set; }
    /// <summary>The approach direction the last push used (verification).</summary>
    public Vector3 LastApproachUsed { get; private set; }

    // pieces that are moved
    class Piece { public Transform t; public Vector3 baseLocal; public float weight = 1f; public Renderer ghost; public MeshFilter ghostFilter; public Material[] ghostMats; }
    readonly List<Piece> pieces = new List<Piece>();
    readonly List<Piece> chain = new List<Piece>();   // flail links: partial offset
    Collider weaponCol;
    HitDetection hd;
    MonsterController monster;
    readonly List<Collider> targets = new List<Collider>(32);
    readonly Collider[] near = new Collider[32];
    int nearCount;
    Vector3 vel;                    // spring velocity (world)
    Vector3 lastApproach;           // the way the weapon came in (world), kept for half a second after it stops
    float lastApproachTime = -99f;
    float lastTick = -99f;
    Vector3 lastOffsetForTick;
    CombatFeedback feedback;
    bool built;

    static readonly ProfilerMarker Marker = new ProfilerMarker("WeaponSurfaceProxy.Update");

    void Awake()
    {
        hd = GetComponent<HitDetection>();
        weaponCol = GetComponent<Collider>();
    }

    void OnEnable()
    {
        Build();
        ClearOffset();
    }

    void OnDisable()
    {
        ClearOffset();
    }

    /// <summary>Back to the controller pose, ghost off.</summary>
    void ClearOffset()
    {
        VisualOffsetWorld = Vector3.zero; vel = Vector3.zero; lastApproachTime = -99f;
        foreach (var p in pieces) if (p.t != null) p.t.localPosition = p.baseLocal;
        foreach (var p in chain) if (p.t != null) p.t.localPosition = p.baseLocal;
        SetGhost(false);
    }

    // ───────────────────────── build: which meshes move ─────────────────────────
    void Build()
    {
        if (built) return;
        built = true;
        // 1. A renderer on this object itself (Level 1 hammers): the drawn mesh moves to a child, the collider stays here.
        var own = GetComponent<MeshRenderer>();
        if (own != null)
        {
            var mf = GetComponent<MeshFilter>();
            var go = new GameObject("Visual");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            var nf = go.AddComponent<MeshFilter>(); nf.sharedMesh = mf != null ? mf.sharedMesh : null;
            var nr = go.AddComponent<MeshRenderer>();
            nr.sharedMaterials = own.sharedMaterials;
            nr.shadowCastingMode = own.shadowCastingMode; nr.receiveShadows = own.receiveShadows;
            nr.lightProbeUsage = own.lightProbeUsage; nr.reflectionProbeUsage = own.reflectionProbeUsage;
            own.enabled = false;
            var glow = GetComponentInParent<PlayerWeaponGlow>();
            if (glow != null) glow.ReplaceRenderer(own, nr);   // the colour writer follows the renderer that is actually drawn
            AddPiece(go.transform, nr, nf, 1f, pieces);
        }
        // 2. Renderers on children (swords, mace, katana, the flail's ball): the top-level child holding each one moves.
        foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.transform == transform || r.gameObject.name.StartsWith("WeaponGhost")) continue;
            if (own != null && r.transform.parent == transform && r.gameObject.name == "Visual") continue;   // the one just built
            AddPiece(r.transform, r, r.GetComponent<MeshFilter>(), 1f, pieces);
        }
        // 3. The flail: links bend toward the drawn ball.
        var sim = GetComponentInParent<FlailChainSim>();
        if (sim != null && sim.links != null)
        {
            int n = sim.links.Length;
            for (int i = 0; i < n; i++)
            {
                if (sim.links[i] == null) continue;
                float t = (i + 0.5f) / n;
                chain.Add(new Piece { t = sim.links[i], baseLocal = Vector3.zero, weight = t * t });
            }
        }
    }

    void AddPiece(Transform t, Renderer r, MeshFilter mf, float weight, List<Piece> list)
    {
        var p = new Piece { t = t, baseLocal = t.localPosition, weight = weight };
        if (r != null && mf != null && mf.sharedMesh != null)
        {
            var g = new GameObject("WeaponGhost");
            g.transform.SetParent(t.parent, false);
            g.layer = gameObject.layer;
            g.transform.localPosition = t.localPosition; g.transform.localRotation = t.localRotation; g.transform.localScale = t.localScale;
            p.ghostFilter = g.AddComponent<MeshFilter>(); p.ghostFilter.sharedMesh = mf.sharedMesh;
            var gr = g.AddComponent<MeshRenderer>();
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; gr.receiveShadows = false;
            gr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; gr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            p.ghost = gr;
            g.SetActive(false);
        }
        list.Add(p);
    }

    // ───────────────────────── targets ─────────────────────────
    void RefreshTargets()
    {
        var m = BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
        if (m == monster) return;
        monster = m;
        targets.Clear();
        if (monster == null) return;
        foreach (var c in monster.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.transform == monster.transform) continue;      // the root capsule is not a body surface
            if (!(c is BoxCollider || c is SphereCollider || c is CapsuleCollider)) continue;
            bool weapon = c.CompareTag("MonsterWeapon");
            if (weapon && !includeMonsterWeapons) continue;
            targets.Add(c);
        }
        feedback = FindAnyObjectByType<CombatFeedback>();
    }

    // ───────────────────────── per frame ─────────────────────────
    void LateUpdate()
    {
        if (GamePause.IsPaused) return;   // the drawn mesh holds its last offset; no sink ticks while paused
        if (weaponCol == null || !weaponCol.enabled) return;
        using (Marker.Auto())
        {
            RefreshTargets();
            Vector3 P = transform.position;
            Quaternion R = transform.rotation;
            Vector3 target = Vector3.zero;
            float trueSink = 0f;
            nearCount = 0;
            if (proxyEnabled && targets.Count > 0)
            {
                CollectNear(P, R);
                if (nearCount > 0) target = Solve(P, R, out trueSink);
            }
            TrueSink = trueSink;

            // Release is a critically damped spring toward the solved offset. If the spring's pose is not valid (still sunk more than
            // maxSink: a push is needed, or the old offset swings the drawn weapon into another collider), the solved offset is
            // applied at once. The solve always starts from the controller pose, so the offset never feeds on itself.
            Vector3 cur = VisualOffsetWorld;
            if (target == Vector3.zero && cur.sqrMagnitude < 1e-8f) { cur = Vector3.zero; vel = Vector3.zero; }
            else
            {
                cur = Vector3.SmoothDamp(cur, target, ref vel, Mathf.Max(0.001f, smoothTime), Mathf.Infinity, Time.deltaTime);
                if (nearCount > 0 && DeepestSink(P + cur, R) > maxSink) { cur = target; vel = Vector3.zero; }
            }
            if (cur.sqrMagnitude > maxOffset * maxOffset) cur = cur.normalized * maxOffset;
            if (!proxyEnabled) { cur = Vector3.zero; vel = Vector3.zero; }
            VisualOffsetWorld = cur;
            float mag = cur.magnitude;
            if (mag > PeakOffset) PeakOffset = mag;
            DrawnSink = nearCount == 0 ? 0f : (cur == Vector3.zero ? trueSink : DeepestSink(P + cur, R));

            Apply(cur);
            UpdateGhost(mag);
            UpdateHaptic(mag);
        }
    }

    // World centre of the weapon's collider shape at pose (P, R), without relying on physics bounds (which lag the pose).
    Vector3 ShapeCentre(Vector3 P, Quaternion R)
    {
        Vector3 c = Vector3.zero;
        if (weaponCol is BoxCollider b) c = b.center;
        else if (weaponCol is SphereCollider sp) c = sp.center;
        else if (weaponCol is CapsuleCollider cp) c = cp.center;
        return P + R * Vector3.Scale(c, transform.lossyScale);
    }

    void CollectNear(Vector3 P, Quaternion R)
    {
        nearCount = 0;
        Vector3 wc = ShapeCentre(P, R);
        float wr = weaponCol.bounds.extents.magnitude;
        if (wr < 0.02f) wr = 0.4f;
        float limit = wr + engageDistance;
        for (int i = 0; i < targets.Count && nearCount < near.Length; i++)
        {
            var c = targets[i];
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            // surface distance from the weapon's centre (the bone-driven collider pose is at most one physics step old)
            if ((c.ClosestPoint(wc) - wc).sqrMagnitude <= limit * limit) near[nearCount++] = c;
        }
    }

    // Deepest overlap (m) of the weapon shape at pose (pos, rot) with the near monster colliders; dir = push-out direction.
    float Deepest(Vector3 pos, Quaternion rot, out Vector3 dir)
    {
        float best = 0f; dir = Vector3.zero;
        for (int i = 0; i < nearCount; i++)
        {
            var c = near[i];
            if (c == null) continue;
            if (Physics.ComputePenetration(weaponCol, pos, rot, c, c.transform.position, c.transform.rotation, out Vector3 d, out float dist) && dist > best)
            { best = dist; dir = d; }
        }
        return best;
    }

    float DeepestSink(Vector3 pos, Quaternion rot) => Deepest(pos, rot, out _);

    // Where the drawn weapon should be for the true pose P: the offset (world) that rests it on the surface.
    Vector3 Solve(Vector3 P, Quaternion R, out float trueSink)
    {
        trueSink = Deepest(P, R, out _);
        if (trueSink <= restSink) return Vector3.zero;
        Vector3 approach = Approach();
        LastApproachUsed = approach;
        Vector3 off = Vector3.zero;
        for (int it = 0; it < iterations + 1; it++)
        {
            float depth = Deepest(P + off, R, out Vector3 mtv);
            if (depth <= restSink) break;
            off = PushOnce(P, R, off, depth, mtv, approach);
            if (off.sqrMagnitude >= maxOffset * maxOffset) break;
        }
        return off;
    }

    // One push out of the deepest overlap. While the weapon has a way it came in on (it is moving, or it was a moment ago), it is walked
    // back out along that path to where it first touched: that clears every overlapping collider at once (the monster's weapon in
    // front of its body, a second limb) and rests the drawn weapon exactly on the first surface it met. Otherwise, or when the
    // shortest way out already points that way, the minimum translation vector is used.
    Vector3 PushOnce(Vector3 P, Quaternion R, Vector3 off, float depth, Vector3 mtv, Vector3 approach)
    {
        bool done = false;
        if (approach != Vector3.zero && Vector3.Dot(mtv, approach) < 0.9f)
        {
            // only when walking back along the approach really gets clear (a swinging flail ball can move across the body)
            if (ExitAlong(P, R, off, approach, out Vector3 step)) { off += step; done = true; }
        }
        if (!done) off += mtv * (depth - restSink);
        if (off.sqrMagnitude > maxOffset * maxOffset) off = off.normalized * maxOffset;
        return off;
    }

    // The way the weapon came in: against its velocity while it moves; kept while it stays in contact (it may stop inside).
    Vector3 Approach()
    {
        Vector3 v = hd != null ? hd.CurrentVelocity : Vector3.zero;
        if (v.sqrMagnitude > 1f) { lastApproach = -v.normalized; lastApproachTime = Time.time; return lastApproach; }
        return Time.time - lastApproachTime < 1.0f ? lastApproach : Vector3.zero;
    }

    // Distance to step out along dir (from the current offset) until the weapon shape is within restSink: coarse steps (at most 7),
    // then three bisections so it does not float above the surface.
    bool ExitAlong(Vector3 P, Quaternion R, Vector3 off, Vector3 dir, out Vector3 result)
    {
        const float step = 0.05f;
        float lo = 0f, hi = -1f;
        float room = Mathf.Max(0.05f, maxOffset - off.magnitude);
        for (int i = 1; i <= 7; i++)
        {
            float d = i * step;
            if (d > room + step) break;
            if (Deepest(P + off + dir * d, R, out _) <= restSink) { hi = d; lo = (i - 1) * step; break; }
        }
        result = Vector3.zero;
        if (hi < 0f) return false;
        for (int b = 0; b < 3; b++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Deepest(P + off + dir * mid, R, out _) <= restSink) hi = mid; else lo = mid;
        }
        result = dir * hi;
        return true;
    }

    // ───────────────────────── apply ─────────────────────────
    void Apply(Vector3 offsetWorld)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (p.t == null) continue;
            // the piece's own parent may be this transform or a child of it: convert from world
            Vector3 parentLocal = p.t.parent != null ? p.t.parent.InverseTransformVector(offsetWorld) : offsetWorld;
            p.t.localPosition = p.baseLocal + parentLocal * p.weight;
        }
        if (chain.Count > 0)
        {
            // The links were placed this frame by FlailChainSim (LateUpdate 200): move them toward the drawn ball.
            for (int i = 0; i < chain.Count; i++)
            {
                var p = chain[i];
                if (p.t == null) continue;
                p.t.position += offsetWorld * p.weight;
            }
        }
    }

    void UpdateGhost(float mag)
    {
        if (!ghostEnabled || ghostMaterial == null) { SetGhost(false); return; }
        bool want = GhostShown ? mag > ghostThreshold * 0.7f : mag > ghostThreshold;   // hysteresis
        SetGhost(want);
    }

    void SetGhost(bool on)
    {
        GhostShown = on;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (p.ghost == null) continue;
            if (on && ghostMaterial != null)
            {
                if (!p.ghost.gameObject.activeSelf)
                {
                    if (p.ghostMats == null)
                    {
                        int subs = p.ghostFilter.sharedMesh.subMeshCount;
                        p.ghostMats = new Material[subs];
                        for (int k = 0; k < subs; k++) p.ghostMats[k] = ghostMaterial;
                        ((MeshRenderer)p.ghost).sharedMaterials = p.ghostMats;
                    }
                    p.ghost.gameObject.SetActive(true);
                }
            }
            else if (p.ghost.gameObject.activeSelf) p.ghost.gameObject.SetActive(false);
        }
    }

    void UpdateHaptic(float mag)
    {
        if (!hapticEnabled || feedback == null || hd == null) { lastOffsetForTick = VisualOffsetWorld; return; }
        // a tick when the offset is growing (the controller is moving further into the monster), not while it sits or releases
        bool growing = mag > lastOffsetForTick.magnitude + 0.004f;
        lastOffsetForTick = VisualOffsetWorld;
        if (mag < 0.01f || !growing || Time.time - lastTick < hapticInterval) return;
        lastTick = Time.time;
        float amp = Mathf.Lerp(hapticMin, hapticMax, Mathf.Clamp01(mag / Mathf.Max(0.01f, hapticFullAt)));
        feedback.PenetrationTick(hd.ControllerHand, amp);
        Ticks++;
    }

    void OnValidate()
    {
        maxSink = Mathf.Max(0.005f, maxSink);
        restSink = Mathf.Clamp(restSink, 0f, maxSink);
        maxOffset = Mathf.Max(0.05f, maxOffset);
    }
}
