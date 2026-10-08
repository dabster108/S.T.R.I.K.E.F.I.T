// MonsterAxeThrow.cs
// Purpose: Level 3 ranged attack. On a Throw row's release frame (MonsterAI) one axe leaves its hand, spins end over
//          end toward where the player's upper chest was at release (no lead, so a sidestep or a duck dodges it),
//          burning and trailing so it reads in the dark, with its own 3D whirr so it can be heard coming. It hits if it
//          passes within hitRadius of the player's head-to-torso line; a swing into it (weapon clash at swing speed)
//          bats it away. Hit, miss or deflected, it then flies back and lands in the same hand on the clip's catch
//          frame. While an axe is away the boss holds its catch pose, so it never attacks one-handed.
// Dependencies: MonsterAI (AiTime, hit-stop, NotifyThrowHit), MonsterController (PerformAttackHit), HitDetection
//               (WeaponClashed), PlayerCombat (swing gate)
// Attach to: monster root (Level 3 Twin-Axe Brute). axes[0] = MonsterAxe_R, axes[1] = MonsterAxe_L.

using UnityEngine;

public class MonsterAxeThrow : MonoBehaviour
{
    [Header("Axes (0 = right hand, 1 = left hand)")]
    public Transform[] axes = new Transform[2];
    public ParticleSystem[] axeFire = new ParticleSystem[2];
    public TrailRenderer[] trails = new TrailRenderer[2];
    [Tooltip("3D looping whirr on each axe (played only in flight).")]
    public AudioSource[] spinSources = new AudioSource[2];
    public AudioClip catchClip;
    [Range(0f, 1f)] public float catchVolume = 0.9f;
    public AudioSource catchSource;

    [Header("Flight")]
    public float flightSpeed = 10f;
    public float spinRevsPerSecond = 2.6f;
    [Tooltip("Axe-local balance point the spin turns around (the head-heavy end of the haft).")]
    public Vector3 spinPivotLocal = new Vector3(0f, 0.38f, 0.1f);
    [Tooltip("Aim this far below the eyes (upper chest).")]
    public float aimDrop = 0.3f;
    [Tooltip("Hit if the spinning head passes this close to the line from the eyes down bodyLength.")]
    public float hitRadius = 0.32f;
    public float bodyLength = 0.9f;
    [Tooltip("Metres past the aim point before a miss turns back.")]
    public float overshoot = 1.2f;
    [Tooltip("A clash this close to a flying axe (at swing speed) deflects it.")]
    public float deflectRadius = 0.75f;
    public float maxReturnSpeed = 22f;

    public enum Result { None, Hit, Missed, Deflected }

    class Flight
    {
        public Transform axe, hand;
        public Vector3 homePos; public Quaternion homeRot; public Vector3 homeScale;
        public Vector3 pivot, velocity, spinAxis;
        public Quaternion baseRot;
        public float spin, travelled, outLength, catchAt, multiplier, launchedAt;
        public bool back;
        public Result result;
    }

    private readonly Flight[] flights = new Flight[2];
    private MonsterAI ai;
    private MonsterController controller;
    private Collider[][] axeColliders = new Collider[2][];

    public bool AnyInFlight => flights[0] != null || flights[1] != null;
    public bool InFlight(int i) => flights[i] != null;
    public Result LastResult { get; private set; }
    public int Throws { get; private set; }
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Deflects { get; private set; }
    public int Catches { get; private set; }
    /// <summary>Seconds from release to the hit / miss / deflect of the last throw (verification).</summary>
    public float LastOutboundSeconds { get; private set; }
    /// <summary>Distance between the axe and its grip when the last catch snapped it home (verification).</summary>
    public float LastCatchGap { get; private set; }

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        controller = GetComponent<MonsterController>();
        for (int i = 0; i < 2; i++)
        {
            if (axes[i] != null) axeColliders[i] = axes[i].GetComponentsInChildren<Collider>(true);
            if (trails[i] != null) trails[i].emitting = false;
        }
    }

    void OnEnable() { HitDetection.WeaponClashed += HandleClash; }
    void OnDisable() { HitDetection.WeaponClashed -= HandleClash; RecallAll(); }

    /// <summary>Release axe i toward the player; it must be back in the hand at catchAt (MonsterAI.AiTime).</summary>
    public bool Launch(int i, MonsterAI owner, float multiplier, float catchAt)
    {
        if (i < 0 || i > 1 || axes[i] == null || flights[i] != null || owner == null || owner.playerTarget == null) return false;
        var axe = axes[i];
        var f = new Flight
        {
            axe = axe, hand = axe.parent,
            homePos = axe.localPosition, homeRot = axe.localRotation, homeScale = axe.localScale,
            multiplier = multiplier, catchAt = catchAt, launchedAt = owner.AiTime,
        };
        f.pivot = axe.TransformPoint(spinPivotLocal);
        Vector3 aim = owner.playerTarget.position + Vector3.down * aimDrop;
        Vector3 dir = aim - f.pivot;
        f.outLength = dir.magnitude + overshoot;
        f.velocity = dir.normalized * flightSpeed;
        f.spinAxis = Vector3.Cross(Vector3.up, dir).normalized; // end over end, top of the axe toward the target
        if (f.spinAxis.sqrMagnitude < 0.01f) f.spinAxis = transform.right;
        axe.SetParent(null, true);
        f.baseRot = axe.rotation;
        flights[i] = f;
        Throws++;
        SetFlightFx(i, true);
        return true;
    }

    /// <summary>Snap every axe still away back into its hand (end of the attack, death, disable).</summary>
    public void RecallAll()
    {
        for (int i = 0; i < 2; i++) if (flights[i] != null) Catch(i, false);
    }

    void HandleClash(HitDetection source, float speed)
    {
        float gate = PlayerCombat.Instance != null ? PlayerCombat.Instance.minSwingVelocity : 1.2f;
        if (source == null || speed < gate) return;
        for (int i = 0; i < 2; i++)
        {
            var f = flights[i];
            if (f == null || f.back || f.result != Result.None) continue;
            if (Vector3.Distance(source.transform.position, f.pivot) > deflectRadius) continue;
            f.result = Result.Deflected; LastResult = Result.Deflected; Deflects++;
            LastOutboundSeconds = ai != null ? ai.AiTime - f.launchedAt : 0f;
            f.back = true;
        }
    }

    void LateUpdate()
    {
        if (!AnyInFlight) return;
        if (controller != null && !controller.isAlive) { RecallAll(); return; }
        float dt = ai != null && ai.InHitStop ? 0f : Time.deltaTime;
        for (int i = 0; i < 2; i++)
        {
            var f = flights[i];
            if (f == null) continue;
            if (f.axe == null || f.hand == null) { flights[i] = null; continue; }
            f.spin += spinRevsPerSecond * 360f * dt;
            Quaternion spinRot = Quaternion.AngleAxis(f.spin, f.spinAxis) * f.baseRot;

            if (!f.back)
            {
                f.pivot += f.velocity * dt;
                f.travelled += flightSpeed * dt;
                if (TouchesPlayer(f.pivot))
                {
                    f.result = Result.Hit; LastResult = Result.Hit; Hits++;
                    LastOutboundSeconds = ai.AiTime - f.launchedAt;
                    if (controller != null) controller.PerformAttackHit(f.multiplier, false);
                    if (ai != null) ai.NotifyThrowHit(f.multiplier);
                    f.back = true;
                }
                else if (f.travelled >= f.outLength)
                {
                    f.result = Result.Missed; LastResult = Result.Missed; Misses++;
                    LastOutboundSeconds = ai.AiTime - f.launchedAt;
                    f.back = true;
                }
                Place(f, spinRot);
            }
            else
            {
                // Home: the grip pose in the (moving) hand, timed to arrive on the catch frame.
                Vector3 homeWorld = f.hand.TransformPoint(f.homePos);
                Quaternion homeWorldRot = f.hand.rotation * f.homeRot;
                Vector3 homePivot = homeWorld + homeWorldRot * Vector3.Scale(spinPivotLocal, f.axe.lossyScale);
                Vector3 to = homePivot - f.pivot;
                float dist = to.magnitude;
                float timeLeft = ai != null ? f.catchAt - ai.AiTime : 0f;
                float speed = Mathf.Clamp(dist / Mathf.Max(0.05f, timeLeft), flightSpeed * 0.6f, maxReturnSpeed);
                float step = speed * dt;
                if (dist <= step + 0.02f || (timeLeft <= 0f && dist < 0.25f)) { Catch(i, true); continue; }
                f.pivot += to / dist * step;
                // Last 0.6 m: turn from the spin into the grip orientation so it lands haft-first in the fist.
                float settle = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(dist / 0.6f));
                Place(f, Quaternion.Slerp(spinRot, homeWorldRot, settle));
            }
        }
    }

    void Place(Flight f, Quaternion rot)
    {
        f.axe.rotation = rot;
        f.axe.position += f.pivot - f.axe.TransformPoint(spinPivotLocal);
    }

    bool TouchesPlayer(Vector3 p)
    {
        if (ai == null || ai.playerTarget == null) return false;
        Vector3 a = ai.playerTarget.position, b = a + Vector3.down * bodyLength;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t) <= hitRadius;
    }

    void Catch(int i, bool natural)
    {
        var f = flights[i];
        flights[i] = null;
        if (f == null || f.axe == null) return;
        Vector3 homeWorld = f.hand != null ? f.hand.TransformPoint(f.homePos) : f.axe.position;
        LastCatchGap = Vector3.Distance(f.axe.position, homeWorld);
        if (f.hand != null)
        {
            f.axe.SetParent(f.hand, false);
            f.axe.localPosition = f.homePos; f.axe.localRotation = f.homeRot; f.axe.localScale = f.homeScale;
        }
        SetFlightFx(i, false);
        if (natural)
        {
            Catches++;
            if (catchClip != null && catchSource != null) { catchSource.PlayOneShot(catchClip, catchVolume); SfxTrace.Note(this, catchClip, "thrown axe caught"); }
        }
        // The monster's death switches every child collider off; an axe that was away missed that.
        if (controller != null && !controller.isAlive && axeColliders[i] != null)
            foreach (var c in axeColliders[i]) if (c != null) c.enabled = false;
    }

    void SetFlightFx(int i, bool on)
    {
        if (axeFire[i] != null) { if (on) axeFire[i].Play(true); else axeFire[i].Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        if (trails[i] != null) { trails[i].emitting = on; if (on) trails[i].Clear(); }
        if (spinSources[i] != null) { if (on) spinSources[i].Play(); else spinSources[i].Stop(); }
    }
}
