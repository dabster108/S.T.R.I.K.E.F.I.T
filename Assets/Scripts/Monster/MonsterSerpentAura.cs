// MonsterSerpentAura.cs
// Purpose: Level 3 "serpent of light": a few orbs (default 3) with short blue additive trails coil continuously up the
//          boss from feet to head, one after another on the same helix, so together they read as one snake of
//          electricity winding up the body. The helix axis follows the body itself (feet -> hips -> chest -> neck -> head
//          bones), and the radius at each height comes from the live skeleton (feet spread, hip width, shoulder width)
//          plus a clearance margin, so the coil hugs the silhouette and stays wrapped around it while the boss crouches,
//          leans, leaps and spins in its attacks. Each orb climbs, thins out at the head, and restarts at the feet.
//          Appears only after the entrance (the summoning circle / rise / roar stay their own moment), fades with death.
//          Enraged (MonsterAI.enraged, the one enrage flag): faster climb, more turns, tighter coil, brighter, wider trail.
// Dependencies: MonsterAI (enraged), MonsterController, MonsterEntrance (optional)
// Attach to: monster root. orbs = children with a TrailRenderer (world space); bones assigned by the builder.

using Unity.Profiling;
using UnityEngine;

public class MonsterSerpentAura : MonoBehaviour
{
    [Header("Orbs (each carries a TrailRenderer)")]
    public TrailRenderer[] orbs = new TrailRenderer[3];

    [Header("Skeleton the coil follows")]
    public Transform leftFoot, rightFoot, hips, leftUpLeg, rightUpLeg, chest, leftArm, rightArm, neck, head;
    [Tooltip("Clearance (m) added to the skeleton width at each height.")]
    public float feetMargin = 0.14f, hipMargin = 0.22f, chestMargin = 0.18f, neckMargin = 0.2f, headMargin = 0.22f;
    [Tooltip("Height of the coil's top above the head bone (m).")]
    public float headTop = 0.22f;

    [Header("Motion")]
    public float climbSeconds = 3.2f;
    public float enragedClimbSeconds = 1.9f;
    public float turnsPerClimb = 2.5f;
    public float enragedTurnsPerClimb = 3f;
    [Tooltip("Slow spin of the whole coil (deg/s), so the helix itself turns.")]
    public float coilSpin = 25f;
    [Tooltip("Radius multiplier while enraged (<1 = coiled tighter, ready to strike).")]
    public float enragedRadiusScale = 0.88f;

    [Header("Look")]
    public float trailWidth = 0.05f;
    public float enragedTrailWidth = 0.065f;
    public float brightness = 1f;
    public float enragedBrightness = 1.7f;
    public float fadeSeconds = 0.8f;
    [Tooltip("Seconds to blend calm -> enraged.")]
    public float enrageBlendSeconds = 1f;

    public float Visibility { get; private set; }
    public float EnrageBlend { get; private set; }
    public float CurrentClimbSeconds { get; private set; }
    public float CurrentRadiusAt(float u) { Sample(u, out _, out float r); return r; }

    static readonly ProfilerMarker Marker = new ProfilerMarker("MonsterSerpentAura.LateUpdate");

    private MonsterAI ai;
    private MonsterController controller;
    private MonsterEntrance entrance;
    private MaterialPropertyBlock mpb;
    private float progress;
    private float spin;
    private float[] lastU;

    // Polyline of the body axis: positions and radii at feet, hips, chest, neck, head top (rebuilt every frame).
    private readonly Vector3[] pts = new Vector3[5];
    private readonly float[] rad = new float[5];
    private readonly float[] cum = new float[5];

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        controller = GetComponent<MonsterController>();
        entrance = GetComponent<MonsterEntrance>();
        mpb = new MaterialPropertyBlock();
        lastU = new float[orbs.Length];
        for (int i = 0; i < orbs.Length; i++)
        {
            if (orbs[i] == null) continue;
            orbs[i].emitting = false;
            orbs[i].enabled = false;
        }
    }

    void LateUpdate() // after the Animator: the bones are this frame's pose
    {
        using (Marker.Auto())
        {
            bool alive = controller == null || controller.isAlive;
            bool ready = entrance == null || entrance.Complete;
            float targetVis = alive && ready ? 1f : 0f;
            Visibility = Mathf.MoveTowards(Visibility, targetVis, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));
            bool show = Visibility > 0.001f;
            if (!show)
            {
                foreach (var o in orbs) if (o != null && o.enabled) { o.emitting = false; o.Clear(); o.enabled = false; }
                return;
            }

            bool enraged = ai != null && ai.enraged;
            EnrageBlend = Mathf.MoveTowards(EnrageBlend, enraged ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, enrageBlendSeconds));
            float e = EnrageBlend;
            CurrentClimbSeconds = Mathf.Lerp(climbSeconds, enragedClimbSeconds, e);
            float turns = Mathf.Lerp(turnsPerClimb, enragedTurnsPerClimb, e);
            float rScale = Mathf.Lerp(1f, enragedRadiusScale, e);
            float width = Mathf.Lerp(trailWidth, enragedTrailWidth, e);
            float bright = Mathf.Lerp(brightness, enragedBrightness, e) * Visibility;

            progress += Time.deltaTime / Mathf.Max(0.2f, CurrentClimbSeconds);
            spin += coilSpin * Time.deltaTime;
            BuildAxis();

            Vector3 right = transform.right, fwd = transform.forward;
            int n = orbs.Length;
            for (int i = 0; i < n; i++)
            {
                var o = orbs[i];
                if (o == null) continue;
                // Orbs share one helix, evenly spaced in height: together they read as one long coil.
                float u = Mathf.Repeat(progress + (float)i / n, 1f);
                bool wrapped = u < lastU[i];
                lastU[i] = u;

                Sample(u, out Vector3 c, out float r);
                float ang = (u * turns * 360f + spin) * Mathf.Deg2Rad;
                Vector3 p = c + (Mathf.Cos(ang) * right + Mathf.Sin(ang) * fwd) * (r * rScale);

                if (!o.enabled) { o.enabled = true; o.transform.position = p; o.Clear(); }
                if (wrapped) { o.emitting = false; o.transform.position = p; o.Clear(); } // jump head -> feet unseen
                else o.transform.position = p;
                o.emitting = true;
                // Thin at both ends (born at the feet, dissolving at the head).
                o.widthMultiplier = width * Mathf.Sqrt(Mathf.Sin(Mathf.PI * u));
                o.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", new Color(bright, bright, bright, Visibility));
                o.SetPropertyBlock(mpb);
            }
        }
    }

    void BuildAxis()
    {
        Vector3 lf = leftFoot.position, rf = rightFoot.position;
        float ground = Mathf.Min(transform.position.y, Mathf.Min(lf.y, rf.y));
        Vector3 feet = (lf + rf) * 0.5f; feet.y = ground + 0.05f;
        pts[0] = feet;
        rad[0] = Horizontal(lf - rf) * 0.5f + feetMargin;
        pts[1] = hips.position;
        rad[1] = Horizontal(leftUpLeg.position - rightUpLeg.position) * 0.5f + hipMargin;
        pts[2] = chest.position;
        rad[2] = Horizontal(leftArm.position - rightArm.position) * 0.5f + chestMargin;
        pts[3] = neck.position;
        rad[3] = neckMargin + 0.05f;
        pts[4] = head.position + Vector3.up * headTop;
        rad[4] = headMargin;
        cum[0] = 0f;
        for (int i = 1; i < 5; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
    }

    static float Horizontal(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// <summary>Point on the body axis and coil radius at climb fraction u (0 = feet, 1 = above the head).</summary>
    void Sample(float u, out Vector3 center, out float radius)
    {
        float d = Mathf.Clamp01(u) * cum[4];
        for (int i = 1; i < 5; i++)
        {
            if (d <= cum[i] || i == 4)
            {
                float t = Mathf.InverseLerp(cum[i - 1], cum[i], d);
                center = Vector3.Lerp(pts[i - 1], pts[i], t);
                radius = Mathf.Lerp(rad[i - 1], rad[i], Mathf.SmoothStep(0f, 1f, t));
                return;
            }
        }
        center = pts[4]; radius = rad[4];
    }
}
