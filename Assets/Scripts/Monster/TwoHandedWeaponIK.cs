// TwoHandedWeaponIK.cs
// Purpose: Keep the left hand locked on a two-handed weapon's haft (humanoid IK) so one-handed
//          Mixamo clips (Standing Melee *) still read as a two-handed axe grip.
// Dependencies: humanoid Animator with "IK Pass" enabled on the base layer, MonsterController (optional)
// Attach to: Monster root (same GameObject as the Animator)

using UnityEngine;

[RequireComponent(typeof(Animator))]
public class TwoHandedWeaponIK : MonoBehaviour
{
    [Tooltip("Grip point on the weapon haft (child of the weapon) where the left hand should sit.")]
    public Transform leftHandTarget;
    [Range(0f, 1f)] public float positionWeight = 1f;
    [Range(0f, 1f)] public float rotationWeight = 0.85f;
    [Tooltip("How fast the IK weight fades in/out (per second). Fades out on death.")]
    public float blendSpeed = 6f;
    [Tooltip("Full grip while the grip is within this × arm length of the left shoulder.")]
    public float reachFullGrip = 0.95f;
    [Tooltip("Hand releases the haft beyond this × arm length (wide one-handed swings), regrips on the way back.")]
    public float reachRelease = 1.2f;

    private Animator animator;
    private MonsterController controller;
    private MonsterGuardIK guardIK;   // optional: moves the right hand while guarding
    private float currentWeight;

    // leftHandTarget expressed in the right-hand IK goal frame. The weapon is rigidly parented to the
    // right hand, so this offset is constant; it is refreshed every LateUpdate (after the pose is written)
    // and applied in the NEXT OnAnimatorIK against that frame's goal — no one-frame lag during fast swings.
    private Vector3 offsetPos;
    private Quaternion offsetRot = Quaternion.identity;
    private bool hasOffset;
    private Vector3 goalPos;
    private Quaternion goalRot = Quaternion.identity;
    private bool hasGoal;

    private Transform shoulder;
    private float armLength;

    void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<MonsterController>();
        guardIK = GetComponent<MonsterGuardIK>();
        if (animator != null && animator.isHuman)
        {
            shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var elbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var wrist = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (shoulder != null && elbow != null && wrist != null)
                armLength = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || leftHandTarget == null) return;

        // The right hand's final goal (MonsterGuardIK may move it while guarding), so the grip follows the haft.
        goalPos = guardIK != null && guardIK.isActiveAndEnabled ? guardIK.RightGoal() : animator.GetIKPosition(AvatarIKGoal.RightHand);
        goalRot = animator.GetIKRotation(AvatarIKGoal.RightHand);
        hasGoal = true;

        float target = (hasOffset && (controller == null || controller.isAlive)) ? 1f : 0f;
        if (hasOffset && shoulder != null && armLength > 0f)
        {
            // One-handed Mixamo swings throw the right hand ~1.3 m from the left shoulder (arm ≈ 0.75 m):
            // the grip is physically unreachable there, so release instead of locking a straight arm.
            float reach = Vector3.Distance(shoulder.position, goalPos + goalRot * offsetPos) / armLength;
            target *= 1f - Mathf.InverseLerp(reachFullGrip, reachRelease, reach);
        }
        currentWeight = Mathf.MoveTowards(currentWeight, target, blendSpeed * Time.deltaTime);

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight * currentWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight * currentWeight);
        if (!hasOffset) return;

        LastCommandedPosition = goalPos + goalRot * offsetPos;
        animator.SetIKPosition(AvatarIKGoal.LeftHand, LastCommandedPosition);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, goalRot * offsetRot);
    }

    /// <summary>World position the left-hand IK goal was set to this frame (verification).</summary>
    public Vector3 LastCommandedPosition { get; private set; }
    /// <summary>Right-hand IK goal read this frame (verification).</summary>
    public Vector3 LastRightGoal => goalPos;

    void LateUpdate()
    {
        if (!hasGoal || leftHandTarget == null) return;
        Quaternion inv = Quaternion.Inverse(goalRot);
        offsetPos = inv * (leftHandTarget.position - goalPos);
        offsetRot = inv * leftHandTarget.rotation;
        hasOffset = true;
    }

    /// <summary>Current IK weight (0..1) — for runtime verification.</summary>
    public float CurrentWeight => currentWeight;
}
