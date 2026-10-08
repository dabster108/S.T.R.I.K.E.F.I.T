// MonsterHitShake.cs
// Purpose: When the player lands a hit the monster shudders: a short damped shake (60 to 120 ms) along the swing direction, a few
//          cm at most for a full swing and a hair for a tap. Applied as an additive offset to the model's hips bone only (the
//          visible body); the monster ROOT, its AI, range checks, attack timing and hit logic never see it. It is applied in
//          LateUpdate after the Animator and after MonsterPersonalSpace (execution order 200), so the clamp measures the unshaken
//          pose; the offset is taken back off at the start of the next frame (Update runs before the Animator), so it can never
//          accumulate, whether or not the clip animates that bone.
//          Mostly skipped while the monster winds up or attacks (attackScale), never during the entrance, never on a guarded hit
//          (the guard has its own cue) and not on the killing blow (the death animation owns that frame).
// Cost: no allocation (a few floats and one Vector3 per frame while shaking, nothing at all when idle).
// Dependencies: HitDetection (HitLanded, CurrentVelocity), CombatUIController.StyleForSpeed (tier), MonsterController, MonsterEntrance
// Attach to: monster root (Levels 1-3).

using UnityEngine;

[DefaultExecutionOrder(200)] // after MonsterPersonalSpace (100)
public class MonsterHitShake : MonoBehaviour
{
    [Tooltip("Master toggle.")]
    public bool shakeEnabled = true;

    [Header("Strength by swing tier (metres, peak offset)")]
    public float tapAmplitude = 0.004f;
    public float lightAmplitude = 0.010f;
    public float mediumAmplitude = 0.020f;
    public float fullAmplitude = 0.035f;
    [Tooltip("Hard cap, whatever the tier and bonuses.")]
    public float maxAmplitude = 0.04f;
    [Tooltip("Weak-point hits shake this much more.")]
    public float weakPointScale = 1.25f;

    [Header("Timing")]
    [Tooltip("Shake length for a tap / a full swing (s); in between is interpolated by amplitude.")]
    public Vector2 duration = new Vector2(0.06f, 0.12f);
    [Tooltip("Oscillations over the shake (the swing direction, then back, a little forward again). Kept low so a 72 Hz " +
             "headset gets several frames per swing of the shake instead of aliasing into a flicker.")]
    public float cycles = 1.5f;
    [Tooltip("How fast the shake dies out (higher = more damped).")]
    public float damping = 4f;

    [Header("When")]
    [Tooltip("Strength multiplier while the monster winds up or attacks (0 = never then). Kept small so the animation reads clean.")]
    [Range(0f, 1f)] public float attackScale = 0.25f;

    /// <summary>Hits that started a shake / were skipped (verification).</summary>
    public int Shakes { get; private set; }
    public int Skipped { get; private set; }
    /// <summary>Peak offset of the last shake in metres (verification).</summary>
    public float LastAmplitude { get; private set; }
    public float LastDuration { get; private set; }
    /// <summary>Current offset magnitude (verification).</summary>
    public float CurrentOffset { get; private set; }
    /// <summary>Largest offset seen since the last shake started (verification).</summary>
    public float PeakOffset { get; private set; }

    private MonsterController ctrl;
    private MonsterEntrance entrance;
    private Transform bone;
    private Vector3 applied;          // offset currently on the bone (local space of the bone's parent)
    private Vector3 dirLocal;         // swing direction in the bone parent's space
    private float amp, dur, startTime = -1f;
    private bool pending;             // started by a hit, begins on the next LateUpdate (the hit arrives in the physics step)

    void Awake()
    {
        ctrl = GetComponent<MonsterController>();
        entrance = GetComponent<MonsterEntrance>();
        var an = GetComponentInChildren<Animator>();
        if (an != null && an.isHuman) bone = an.GetBoneTransform(HumanBodyBones.Hips);
        if (bone == null) { foreach (var t in GetComponentsInChildren<Transform>()) if (t.name.EndsWith("Hips")) { bone = t; break; } }
    }

    void OnEnable() { HitDetection.HitLanded += OnHit; }

    void OnDisable()
    {
        HitDetection.HitLanded -= OnHit;
        Restore();
        startTime = -1f; pending = false;
    }

    void OnHit(HitDetection source, float swingSpeed, bool defended)
    {
        if (!shakeEnabled || bone == null || source == null || source.LastMonster != ctrl) return;
        if (defended || !ctrl.isAlive) { Skipped++; return; }                 // guarded hit / killing blow
        if (entrance != null && !entrance.Complete) { Skipped++; return; }    // never during the entrance

        var style = CombatUIController.StyleForSpeed(swingSpeed);
        float a = style == CombatUIController.DamageStyle.Full ? fullAmplitude
                : style == CombatUIController.DamageStyle.Medium ? mediumAmplitude
                : style == CombatUIController.DamageStyle.Light ? lightAmplitude : tapAmplitude;
        if (source.LastWasWeakPoint) a *= weakPointScale;
        if (ctrl.isAttacking) a *= attackScale;
        a = Mathf.Min(a, maxAmplitude);
        if (a < 0.0005f) { Skipped++; return; }
        if (!pending && startTime >= 0f && Time.time - startTime < dur && a <= amp * Mathf.Exp(-damping * (Time.time - startTime) / dur)) return; // a stronger shake is still running
        if (pending && a <= amp) return;

        Vector3 swing = source.CurrentVelocity;
        if (swing.sqrMagnitude < 1e-4f) swing = ctrl.transform.position - source.transform.position;
        swing.Normalize();
        // Hit-stop / entrance aside, restore first so the direction is computed against the clean pose.
        Restore();
        dirLocal = bone.parent != null ? bone.parent.InverseTransformDirection(swing) : swing;
        amp = a;
        dur = Mathf.Lerp(duration.x, duration.y, Mathf.InverseLerp(tapAmplitude, fullAmplitude, a));
        startTime = -1f; pending = true;
        Shakes++; LastAmplitude = a; LastDuration = dur; PeakOffset = 0f;
    }

    // Update runs before the Animator: whatever the shake added last frame comes off first, so the Animator (which may or may
    // not overwrite this bone) always starts from the clean pose and the offset can never accumulate.
    void Update() { Restore(); }

    void Restore()
    {
        if (applied != Vector3.zero && bone != null) bone.localPosition -= applied;
        applied = Vector3.zero;
    }

    void LateUpdate()
    {
        CurrentOffset = 0f;
        if (bone == null) return;
        if (pending) { startTime = Time.time; pending = false; }
        if (startTime < 0f) return;
        float t = Time.time - startTime;
        if (t >= dur) { startTime = -1f; return; }
        float k = t / dur;
        float s = amp * Mathf.Exp(-damping * k) * Mathf.Cos(2f * Mathf.PI * cycles * k);
        applied = dirLocal * s;
        bone.localPosition += applied;
        CurrentOffset = Mathf.Abs(s);
        if (CurrentOffset > PeakOffset) PeakOffset = CurrentOffset;
    }
}
