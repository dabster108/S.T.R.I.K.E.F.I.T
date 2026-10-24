// WeaponSweep.cs
// Purpose: Swept weapon contact for fast swings (HitDetection.sweptDetection). Physics triggers only see a weapon where it is
//          at each physics step (50 Hz in this project), so a swing faster than (thickness x 50) m/s can step over the
//          monster's weapon between two samples and land on the body behind it. Every frame the player weapon's collider is
//          moved from its previous pose to its current one in small sub-steps while each monster collider is moved from its
//          previous pose to its current one at the same time (relative motion: a monster swing meeting a held weapon is
//          caught too). The first sub-step that overlaps gives the order of contact, so a weapon met before the body is a
//          parry. Analytic shapes (box / capsule / sphere) taken from the real colliders: no physics queries, no
//          Physics.SyncTransforms, no allocation after a monster's shape table is built (once, at its first frame).
// Dependencies: MonsterController (owns the shapes; colliders tagged MonsterWeapon are weapon shapes, every other collider
//               under it is body), HitDetection (calls MonsterHitShapes.For / Sweep each LateUpdate)
// Attach to: nothing (plain classes)

using System.Collections.Generic;
using UnityEngine;

/// <summary>Pose of one collider at the previous and the current frame, plus its shape in local space.</summary>
public struct SweepShape
{
    public enum Kind { Box, Capsule, Sphere }
    public Kind kind;
    public Collider collider;
    public Transform t;
    public Vector3 center;      // local
    public Vector3 size;        // box: local size; capsule: (radius, height, direction); sphere: (radius, 0, 0)
    public Vector3 prevPos, curPos, prevScale, curScale;
    public Quaternion prevRot, curRot;
    public bool hasCur, hasPrev, active;

    public static bool TryMake(Collider c, out SweepShape s)
    {
        s = default;
        s.collider = c; s.t = c.transform;
        switch (c)
        {
            case BoxCollider b: s.kind = Kind.Box; s.center = b.center; s.size = b.size; return true;
            case CapsuleCollider cc: s.kind = Kind.Capsule; s.center = cc.center; s.size = new Vector3(cc.radius, cc.height, cc.direction); return true;
            case SphereCollider sc: s.kind = Kind.Sphere; s.center = sc.center; s.size = new Vector3(sc.radius, 0f, 0f); return true;
        }
        return false; // mesh colliders: left to the trigger path
    }

    /// <summary>Reads the transform: the old current pose becomes the previous one.</summary>
    public void Advance()
    {
        active = collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        if (!active) { hasCur = hasPrev = false; return; }
        if (hasCur) { prevPos = curPos; prevRot = curRot; prevScale = curScale; hasPrev = true; }
        curPos = t.position; curRot = t.rotation; curScale = t.lossyScale;
        if (!hasCur) { prevPos = curPos; prevRot = curRot; prevScale = curScale; hasPrev = true; }
        hasCur = true;
    }

    /// <summary>Forget the motion so far (teleport, equip): the next frame starts from where the collider is then.</summary>
    public void ResetHistory() { hasCur = hasPrev = false; }

    public Vector3 CenterAt(float k)
    {
        Vector3 p = Vector3.LerpUnclamped(prevPos, curPos, k);
        Quaternion r = k >= 1f ? curRot : (k <= 0f ? prevRot : Quaternion.Slerp(prevRot, curRot, k));
        Vector3 sc = Vector3.LerpUnclamped(prevScale, curScale, k);
        return p + r * Vector3.Scale(center, sc);
    }

    /// <summary>Distance the shape's centre moved this frame plus the arc its far end swept by rotating (m).</summary>
    public float Travel => (CenterAt(1f) - CenterAt(0f)).magnitude + Quaternion.Angle(prevRot, curRot) * Mathf.Deg2Rad * BoundRadius;

    public float BoundRadius
    {
        get
        {
            float s = Mathf.Max(Mathf.Abs(curScale.x), Mathf.Max(Mathf.Abs(curScale.y), Mathf.Abs(curScale.z))); // 2-arg Max: the 3-arg overload allocates a params array every call
            switch (kind)
            {
                case Kind.Box: return Vector3.Scale(size * 0.5f, Abs(curScale)).magnitude;
                case Kind.Capsule: return Mathf.Max(size.x * s, size.y * 0.5f * s);
                default: return size.x * s;
            }
        }
    }

    /// <summary>Thinnest dimension (m): sets the sub-step length.</summary>
    public float Thickness
    {
        get
        {
            Vector3 a = Abs(curScale);
            switch (kind)
            {
                case Kind.Box: { Vector3 w = Vector3.Scale(size, a); return Mathf.Min(w.x, Mathf.Min(w.y, w.z)); }
                default: return 2f * size.x * Mathf.Max(a.x, Mathf.Max(a.y, a.z));
            }
        }
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>World geometry at fraction k of the frame (0 = previous pose, 1 = current).</summary>
    public void At(float k, out SweepMath.Geo g)
    {
        Vector3 p = Vector3.LerpUnclamped(prevPos, curPos, k);
        Quaternion r = k >= 1f ? curRot : (k <= 0f ? prevRot : Quaternion.Slerp(prevRot, curRot, k));
        Vector3 sc = Abs(Vector3.LerpUnclamped(prevScale, curScale, k));
        g = default;
        g.kind = kind;
        g.c = p + r * Vector3.Scale(center, sc);
        switch (kind)
        {
            case Kind.Box:
                g.a0 = r * Vector3.right; g.a1 = r * Vector3.up; g.a2 = r * Vector3.forward;
                g.h = Vector3.Scale(size * 0.5f, sc);
                break;
            case Kind.Capsule:
            {
                int dir = (int)size.z;
                Vector3 axisLocal = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
                float axisScale = sc[dir];
                float radScale = dir == 0 ? Mathf.Max(sc.y, sc.z) : dir == 1 ? Mathf.Max(sc.x, sc.z) : Mathf.Max(sc.x, sc.y);
                g.r = size.x * radScale;
                float halfSeg = Mathf.Max(0f, size.y * 0.5f * axisScale - g.r);
                Vector3 axis = r * axisLocal;
                g.p0 = g.c - axis * halfSeg; g.p1 = g.c + axis * halfSeg;
                break;
            }
            default:
                g.r = size.x * Mathf.Max(sc.x, Mathf.Max(sc.y, sc.z));
                g.p0 = g.p1 = g.c;
                break;
        }
    }
}

/// <summary>Every collider of one monster, split into weapon shapes (tag MonsterWeapon) and body shapes, advanced once per frame.</summary>
public sealed class MonsterHitShapes
{
    public readonly MonsterController owner;
    public readonly SweepShape[] shapes;
    public readonly bool[] isWeapon;
    public readonly Transform[] weaponRoot;   // weapon shapes: the weapon they belong to (blade + haft = one weapon)
    int frame = -1;

    static readonly Dictionary<MonsterController, MonsterHitShapes> cache = new Dictionary<MonsterController, MonsterHitShapes>();

    MonsterHitShapes(MonsterController m)
    {
        owner = m;
        var cols = m.GetComponentsInChildren<Collider>(true);
        var list = new List<SweepShape>(cols.Length);
        var w = new List<bool>(cols.Length);
        var roots = new List<Transform>(cols.Length);
        foreach (var c in cols)
        {
            if (!SweepShape.TryMake(c, out var s)) continue;
            bool weapon = c.CompareTag("MonsterWeapon");
            if (!weapon && c.isTrigger) continue; // body = solid hit volumes (and the root capsule); triggers are not flesh
            list.Add(s); w.Add(weapon); roots.Add(weapon ? WeaponRootOf(c.transform) : null);
        }
        shapes = list.ToArray(); isWeapon = w.ToArray(); weaponRoot = roots.ToArray();
    }

    /// <summary>The weapon a MonsterWeapon collider belongs to: its highest MonsterWeapon-tagged ancestor.</summary>
    public static Transform WeaponRootOf(Transform t)
    {
        while (t.parent != null && t.parent.CompareTag("MonsterWeapon")) t = t.parent;
        return t;
    }

    /// <summary>Shape table of a monster (built on first use; null for null).</summary>
    public static MonsterHitShapes For(MonsterController m)
    {
        if (m == null) return null;
        if (!cache.TryGetValue(m, out var s))
        {
            if (cache.Count > 8) cache.Clear(); // monsters from earlier battles / scenes
            s = new MonsterHitShapes(m);
            cache[m] = s;
        }
        return s;
    }

    /// <summary>Reads every pose once per frame (the first caller this frame does it).</summary>
    public void BeginFrame()
    {
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        for (int i = 0; i < shapes.Length; i++) shapes[i].Advance();
    }
}

/// <summary>Overlap tests between the analytic shapes (world space).</summary>
public static class SweepMath
{
    public struct Geo
    {
        public SweepShape.Kind kind;
        public Vector3 c, a0, a1, a2, h;  // box
        public Vector3 p0, p1; public float r; // capsule / sphere (sphere: p0 == p1)
    }

    public static bool Overlap(in Geo a, in Geo b)
    {
        if (a.kind == SweepShape.Kind.Box)
        {
            if (b.kind == SweepShape.Kind.Box) return BoxBox(a, b);
            return SegBoxDistSq(b.p0, b.p1, a) <= b.r * b.r;
        }
        if (b.kind == SweepShape.Kind.Box) return SegBoxDistSq(a.p0, a.p1, b) <= a.r * a.r;
        float rr = a.r + b.r;
        return SegSegDistSq(a.p0, a.p1, b.p0, b.p1) <= rr * rr;
    }

    /// <summary>Point on b closest to p (contact point for sparks / blood).</summary>
    public static Vector3 Closest(in Geo b, Vector3 p)
    {
        if (b.kind == SweepShape.Kind.Box)
        {
            Vector3 d = p - b.c;
            float x = Mathf.Clamp(Vector3.Dot(d, b.a0), -b.h.x, b.h.x);
            float y = Mathf.Clamp(Vector3.Dot(d, b.a1), -b.h.y, b.h.y);
            float z = Mathf.Clamp(Vector3.Dot(d, b.a2), -b.h.z, b.h.z);
            return b.c + b.a0 * x + b.a1 * y + b.a2 * z;
        }
        Vector3 q = ClosestOnSeg(b.p0, b.p1, p);
        Vector3 to = p - q;
        float len = to.magnitude;
        return len > b.r && len > 1e-6f ? q + to * (b.r / len) : p;
    }

    // ── box vs box: separating axis test (15 axes) ──
    static bool BoxBox(in Geo A, in Geo B)
    {
        const float eps = 1e-6f;
        Vector3 t0 = B.c - A.c;
        Vector3 t = new Vector3(Vector3.Dot(t0, A.a0), Vector3.Dot(t0, A.a1), Vector3.Dot(t0, A.a2));
        float r00 = Vector3.Dot(A.a0, B.a0), r01 = Vector3.Dot(A.a0, B.a1), r02 = Vector3.Dot(A.a0, B.a2);
        float r10 = Vector3.Dot(A.a1, B.a0), r11 = Vector3.Dot(A.a1, B.a1), r12 = Vector3.Dot(A.a1, B.a2);
        float r20 = Vector3.Dot(A.a2, B.a0), r21 = Vector3.Dot(A.a2, B.a1), r22 = Vector3.Dot(A.a2, B.a2);
        float q00 = Mathf.Abs(r00) + eps, q01 = Mathf.Abs(r01) + eps, q02 = Mathf.Abs(r02) + eps;
        float q10 = Mathf.Abs(r10) + eps, q11 = Mathf.Abs(r11) + eps, q12 = Mathf.Abs(r12) + eps;
        float q20 = Mathf.Abs(r20) + eps, q21 = Mathf.Abs(r21) + eps, q22 = Mathf.Abs(r22) + eps;
        Vector3 a = A.h, b = B.h;
        // A's axes
        if (Mathf.Abs(t.x) > a.x + b.x * q00 + b.y * q01 + b.z * q02) return false;
        if (Mathf.Abs(t.y) > a.y + b.x * q10 + b.y * q11 + b.z * q12) return false;
        if (Mathf.Abs(t.z) > a.z + b.x * q20 + b.y * q21 + b.z * q22) return false;
        // B's axes
        if (Mathf.Abs(t.x * r00 + t.y * r10 + t.z * r20) > a.x * q00 + a.y * q10 + a.z * q20 + b.x) return false;
        if (Mathf.Abs(t.x * r01 + t.y * r11 + t.z * r21) > a.x * q01 + a.y * q11 + a.z * q21 + b.y) return false;
        if (Mathf.Abs(t.x * r02 + t.y * r12 + t.z * r22) > a.x * q02 + a.y * q12 + a.z * q22 + b.z) return false;
        // cross products
        if (Mathf.Abs(t.z * r10 - t.y * r20) > a.y * q20 + a.z * q10 + b.y * q02 + b.z * q01) return false;
        if (Mathf.Abs(t.z * r11 - t.y * r21) > a.y * q21 + a.z * q11 + b.x * q02 + b.z * q00) return false;
        if (Mathf.Abs(t.z * r12 - t.y * r22) > a.y * q22 + a.z * q12 + b.x * q01 + b.y * q00) return false;
        if (Mathf.Abs(t.x * r20 - t.z * r00) > a.x * q20 + a.z * q00 + b.y * q12 + b.z * q11) return false;
        if (Mathf.Abs(t.x * r21 - t.z * r01) > a.x * q21 + a.z * q01 + b.x * q12 + b.z * q10) return false;
        if (Mathf.Abs(t.x * r22 - t.z * r02) > a.x * q22 + a.z * q02 + b.x * q11 + b.y * q10) return false;
        if (Mathf.Abs(t.y * r00 - t.x * r10) > a.x * q10 + a.y * q00 + b.y * q22 + b.z * q21) return false;
        if (Mathf.Abs(t.y * r01 - t.x * r11) > a.x * q11 + a.y * q01 + b.x * q22 + b.z * q20) return false;
        if (Mathf.Abs(t.y * r02 - t.x * r12) > a.x * q12 + a.y * q02 + b.x * q21 + b.y * q20) return false;
        return true;
    }

    // ── segment / point vs box: the distance from a point moving along the segment to a convex box is convex in the
    //    segment parameter, so a golden-section search finds the minimum (to 0.12 % of the length). ──
    static float PointBoxDistSq(Vector3 p, in Geo b)
    {
        Vector3 d = p - b.c;
        float x = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, b.a0)) - b.h.x);
        float y = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, b.a1)) - b.h.y);
        float z = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, b.a2)) - b.h.z);
        return x * x + y * y + z * z;
    }

    static float SegBoxDistSq(Vector3 p0, Vector3 p1, in Geo b)
    {
        if ((p1 - p0).sqrMagnitude < 1e-10f) return PointBoxDistSq(p0, b);
        const float g = 0.618034f;
        float lo = 0f, hi = 1f;
        float x1 = hi - g * (hi - lo), x2 = lo + g * (hi - lo);
        float f1 = PointBoxDistSq(Vector3.LerpUnclamped(p0, p1, x1), b), f2 = PointBoxDistSq(Vector3.LerpUnclamped(p0, p1, x2), b);
        for (int i = 0; i < 14; i++) // interval shrinks to 0.618^14 = 0.12 % of the segment
        {
            if (f1 == 0f || f2 == 0f) return 0f;
            if (f1 < f2) { hi = x2; x2 = x1; f2 = f1; x1 = hi - g * (hi - lo); f1 = PointBoxDistSq(Vector3.LerpUnclamped(p0, p1, x1), b); }
            else { lo = x1; x1 = x2; f1 = f2; x2 = lo + g * (hi - lo); f2 = PointBoxDistSq(Vector3.LerpUnclamped(p0, p1, x2), b); }
        }
        float fe = Mathf.Min(PointBoxDistSq(p0, b), PointBoxDistSq(p1, b));
        return Mathf.Min(fe, Mathf.Min(f1, f2));
    }

    static Vector3 ClosestOnSeg(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float l = ab.sqrMagnitude;
        if (l < 1e-10f) return a;
        return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / l);
    }

    // Closest points between two segments (Ericson, Real-Time Collision Detection 5.1.9).
    static float SegSegDistSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-10f && e <= 1e-10f) return r.sqrMagnitude;
        if (a <= 1e-10f) { s = 0f; t = Mathf.Clamp01(f / e); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-10f) { t = 0f; s = Mathf.Clamp01(-c / a); }
            else
            {
                float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                s = denom > 1e-10f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
            }
        }
        Vector3 c1 = p1 + d1 * s, c2 = p2 + d2 * t;
        return (c1 - c2).sqrMagnitude;
    }
}
