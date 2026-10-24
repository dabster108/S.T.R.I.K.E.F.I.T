// MonsterGuardIK.cs
// Purpose: Optional second guard layer. While MonsterDefense has a guard up, hand IK (OnAnimatorIK, position only, the clip's
//          hand rotation is kept) moves the weapon hand so the middle of the weapon heads for the predicted contact point,
//          at a capped hand speed and inside a capped reach, and keeps the hand in front of the chest so the weapon does not
//          sink into the body. When the guard drops it blends back to the clip. One guard clip then reads as a high, side or
//          low guard. Two weapons (Level 3): each hand covers its own side. The weapon colliders ride on the hand, so the
//          weapon-collider parry logic is unchanged.
// Dependencies: humanoid Animator with IK Pass on the base layer; MonsterDefense, MonsterAI, MonsterController on this object or
//               a parent; TwoHandedWeaponIK (optional, Level 2) reads RightGoal so the off hand stays on the haft.
// Attach to: the GameObject with the monster's Animator

using UnityEngine;

[RequireComponent(typeof(Animator))]
public class MonsterGuardIK : MonoBehaviour
{
    [Tooltip("Move the weapon hand toward the predicted contact point while a guard is up.")]
    public bool ikIntercept = true;
    [Tooltip("Level 3: a weapon in each hand; the hand on the contact's side intercepts.")]
    public bool twoWeapons = false;
    [Tooltip("Weapon root in the right hand (its MonsterWeapon colliders give the weapon's middle).")]
    public Transform rightWeapon;
    [Tooltip("Weapon root in the left hand (two weapons only).")]
    public Transform leftWeapon;
    [Tooltip("Fastest the IK goal moves (m/s).")]
    public float maxHandSpeed = 2.5f;
    [Tooltip("The hand stays within this share of the arm's length from its shoulder.")]
    [Range(0.5f, 1f)] public float reachFraction = 0.9f;
    [Tooltip("The hand stays at least this far (m) in front of the chest, so the weapon does not pass through the body.")]
    public float minForward = 0.3f;
    [Tooltip("The hand stays at most this far (m) to the far side of the body's centre line.")]
    public float maxCross = 0.15f;
    [Tooltip("Lowest hand height as a share of the monster's height.")]
    [Range(0f, 1f)] public float minHeight = 0.35f;
    [Tooltip("IK weight change per second while a guard rises / drops.")]
    public float blendIn = 8f, blendOut = 5f;
    [Range(0f, 1f)] public float positionWeight = 1f;
    [Tooltip("Keep the clip's world hand rotation while the hand moves, so the weapon keeps the guard clip's angle instead of tipping.")]
    public bool keepClipHandRotation = true;
    [Header("Pose anchors (weapon middle, monster space: x + = the player's left, y share of height, z m in front of the chest)")]
    public Vector3 highAnchor = new Vector3(0f, 0.95f, 0.35f);
    public Vector3 sideAnchor = new Vector3(0.4f, 0.7f, 0.45f);
    public Vector3 lowAnchor = new Vector3(0f, 0.4f, 0.4f);
    [Tooltip("Move the hand for the HIGH guard too. Off: the guard clip (already a high guard) shows it; the IK put the blade into the head.")]
    public bool ikOnHighPose = false;
    [Tooltip("How far from the pose anchor toward the predicted contact the weapon goes (0 = anchor only, 1 = the contact).")]
    [Range(0f, 1f)] public float followContact = 0.5f;

    [Header("Distinct pose shapes (Levels 2 and 3, with MonsterTactics)")]
    [Tooltip("Turn the weapon per pose as well as moving it: SIDE = upright on that side, HIGH = level above the head, LOW = slanted " +
             "down across the hips (also the thrust guard). Off: only the hand position moves (the clip's hand rotation is kept).")]
    public bool orientWeapon = false;
    [Tooltip("Weapon axis (hand -> weapon middle) per pose, monster space (x + = the player's left, y up, z toward the player).")]
    public Vector3 highAxis = new Vector3(1f, 0.08f, 0.1f);
    public Vector3 sideAxis = new Vector3(0.12f, 1f, 0.3f);
    public Vector3 lowAxis = new Vector3(0.75f, -0.55f, 0.35f);
    [Tooltip("SIDE guard on the far side of the weapon hand (one weapon: the right hand guarding the player's right): the hand cannot " +
             "cross the body far, so the weapon leans over to that side instead (given for the right hand; mirrored for the left).")]
    public Vector3 crossSideAxis = new Vector3(-0.7f, 0.7f, 0.25f);
    [Tooltip("Two weapons: HIGH and LOW guards move both weapons (side guards move the one on that side).")]
    public bool twinHighLow = false;
    [Tooltip("Two weapons: for HIGH and LOW both weapons guard, this far (m) either side of the anchor.")]
    public float twinSpread = 0.16f;
    [Tooltip("Fastest the hand turns into a pose (deg/s).")]
    public float maxHandTurn = 900f;

    private Animator animator;
    private MonsterDefense defense;
    private MonsterAI ai;
    private MonsterController ctrl;
    private Transform root, chest;
    private readonly Transform[] shoulder = new Transform[2];
    private readonly float[] arm = new float[2];
    private readonly Vector3[] weaponLocal = new Vector3[2];   // weapon middle in the hand bone's space
    private readonly bool[] hasWeapon = new bool[2];
    private readonly float[] weight = new float[2];
    private readonly Vector3[] goal = new Vector3[2];
    private readonly Vector3[] animGoal = new Vector3[2];
    private readonly Quaternion[] animRot = new Quaternion[2];
    private readonly Transform[] handBone = new Transform[2];
    private readonly Quaternion[] rotGoal = { Quaternion.identity, Quaternion.identity };
    // Humanoid IK goal rotation -> hand bone rotation (a fixed avatar offset), measured while the IK is idle.
    private readonly Quaternion[] goalToBone = { Quaternion.identity, Quaternion.identity };
    private readonly bool[] hasGoalToBone = new bool[2];
    private int computedFrame = -1;
    private static readonly AvatarIKGoal[] Goals = { AvatarIKGoal.RightHand, AvatarIKGoal.LeftHand };

    /// <summary>Largest IK weight of the two hands (verification).</summary>
    public float CurrentWeight => Mathf.Max(weight[0], weight[1]);
    /// <summary>Distance (m) from the intercepting weapon's middle to the predicted contact last frame (verification).</summary>
    public float LastMissDistance { get; private set; }
    /// <summary>Hand moved by the IK this frame (m, verification).</summary>
    public float LastShift { get; private set; }

    void Awake()
    {
        animator = GetComponent<Animator>();
        defense = GetComponentInParent<MonsterDefense>();
        ai = GetComponentInParent<MonsterAI>();
        ctrl = GetComponentInParent<MonsterController>();
        root = defense != null ? defense.transform : transform;
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Setup(0, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rightWeapon);
        Setup(1, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, twoWeapons ? leftWeapon : null);
    }

    void Setup(int h, HumanBodyBones up, HumanBodyBones low, HumanBodyBones hand, Transform weapon)
    {
        shoulder[h] = animator.GetBoneTransform(up);
        var elbow = animator.GetBoneTransform(low);
        handBone[h] = animator.GetBoneTransform(hand);
        if (shoulder[h] != null && elbow != null && handBone[h] != null)
            arm[h] = Vector3.Distance(shoulder[h].position, elbow.position) + Vector3.Distance(elbow.position, handBone[h].position);
        if (weapon == null || handBone[h] == null) return;
        // The weapon's middle: centre of its clash colliders (enabled or not), else the weapon root.
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var c in weapon.GetComponentsInChildren<Collider>(true))
        {
            if (!c.CompareTag("MonsterWeapon")) continue;
            Vector3 centre = c is BoxCollider b ? b.center : c is CapsuleCollider cc ? cc.center : c is SphereCollider s ? s.center : Vector3.zero;
            sum += c.transform.TransformPoint(centre); n++;
        }
        Vector3 mid = n > 0 ? sum / n : weapon.position;
        weaponLocal[h] = handBone[h].InverseTransformPoint(mid);
        hasWeapon[h] = true;
    }

    /// <summary>Final right-hand IK goal this frame (the clip's goal when the IK is idle). TwoHandedWeaponIK grips relative to it.</summary>
    public Vector3 RightGoal()
    {
        Compute();
        return Vector3.Lerp(animGoal[0], goal[0], weight[0] * positionWeight);
    }

    /// <summary>Final right-hand IK goal rotation this frame (the clip's when the pose does not turn the weapon).</summary>
    public Quaternion RightGoalRotation()
    {
        Compute();
        return orientWeapon && weight[0] > 0f ? Quaternion.Slerp(animRot[0], rotGoal[0], weight[0]) : animRot[0];
    }

    /// <summary>Weapon middle of hand h in monster space (verification: which side / height the guard weapon is on).</summary>
    public Vector3 WeaponMiddleLocal(int h)
    {
        if (h < 0 || h > 1 || !hasWeapon[h]) return Vector3.zero;
        return root.InverseTransformPoint(handBone[h].TransformPoint(weaponLocal[h]));
    }

    /// <summary>Weapon axis (hand -> middle) of hand h in monster space, normalised (verification).</summary>
    public Vector3 WeaponAxisLocal(int h)
    {
        if (h < 0 || h > 1 || !hasWeapon[h]) return Vector3.zero;
        return root.InverseTransformDirection(handBone[h].TransformPoint(weaponLocal[h]) - handBone[h].position).normalized;
    }

    public bool HasWeapon(int h) => h >= 0 && h < 2 && hasWeapon[h];

    // Once per frame, before any SetIKPosition from this component, whichever OnAnimatorIK runs first.
    void Compute()
    {
        if (computedFrame == Time.frameCount) return;
        computedFrame = Time.frameCount;
        float dt = Time.deltaTime;
        bool active = ikIntercept && defense != null && ai != null && ctrl != null && ctrl.isAlive && ai.IsDefending && defense.swingClasses
                      && (ikOnHighPose || defense.ActivePose != MonsterDefense.GuardPose.High);
        var pose = defense != null ? defense.ActivePose : MonsterDefense.GuardPose.High;
        Vector3 contact = active ? InterceptPoint() : Vector3.zero;
        int pick = 0;
        if (twoWeapons && hasWeapon[1]) pick = Vector3.Dot(contact - root.position, root.right) >= 0f ? 0 : 1;   // each hand its own side
        // Two weapons and a HIGH or LOW guard: both weapons guard, side by side (a crossed block).
        bool both = twoWeapons && hasWeapon[1] && twinHighLow && (pose == MonsterDefense.GuardPose.High || pose == MonsterDefense.GuardPose.Low);
        for (int h = 0; h < 2; h++)
        {
            animGoal[h] = animator.GetIKPosition(Goals[h]);
            animRot[h] = animator.GetIKRotation(Goals[h]);
            bool mine = active && hasWeapon[h] && (both || h == pick);
            if (weight[h] <= 0f) { goal[h] = animGoal[h]; rotGoal[h] = animRot[h]; }
            if (mine)
            {
                Vector3 target = both ? contact + root.right * (h == 0 ? twinSpread : -twinSpread) : contact;
                Vector3 worldOff = handBone[h].TransformPoint(weaponLocal[h]) - handBone[h].position;   // hand -> weapon middle now
                if (orientWeapon && hasGoalToBone[h])
                {
                    // Turn the hand so the weapon lies along the pose's axis, then place the hand for that rotation.
                    Quaternion boneNow = handBone[h].rotation;
                    Vector3 clipAxis = (animRot[h] * goalToBone[h] * Quaternion.Inverse(boneNow)) * worldOff;
                    Vector3 axis = root.TransformDirection(AxisFor(pose, h).normalized);
                    // A level guard can lie either way: keep the direction nearest the clip's.
                    if (pose == MonsterDefense.GuardPose.High && Vector3.Dot(axis, clipAxis) < 0f) axis = -axis;
                    Quaternion want = Quaternion.FromToRotation(clipAxis, axis) * animRot[h];
                    rotGoal[h] = Quaternion.RotateTowards(rotGoal[h], want, maxHandTurn * dt);
                    // The hand goes where the weapon middle lies along the wanted axis (not where the rotation was predicted to put it).
                    worldOff = axis * worldOff.magnitude;
                }
                Vector3 wantPos = Clamp(h, target - worldOff);
                goal[h] = Vector3.MoveTowards(goal[h], wantPos, maxHandSpeed * dt);
                LastMissDistance = Vector3.Distance(goal[h] + worldOff, target);
            }
            else if (weight[h] > 0f)
            {
                goal[h] = Vector3.MoveTowards(goal[h], animGoal[h], maxHandSpeed * dt);
                rotGoal[h] = Quaternion.RotateTowards(rotGoal[h], animRot[h], maxHandTurn * dt);
            }
            weight[h] = Mathf.MoveTowards(weight[h], mine ? 1f : 0f, (mine ? blendIn : blendOut) * dt);
        }
        LastShift = Vector3.Distance(Vector3.Lerp(animGoal[pick], goal[pick], weight[pick] * positionWeight), animGoal[pick]);
    }

    /// <summary>Weapon axis for a pose in monster space; the left hand's slant is mirrored.</summary>
    Vector3 AxisFor(MonsterDefense.GuardPose pose, int h)
    {
        switch (pose)
        {
            case MonsterDefense.GuardPose.High: return highAxis;
            // The right hand (h 0) lives on +x (SIDE_LEFT); guarding -x (SIDE_RIGHT) it leans across. The left hand mirrors it.
            case MonsterDefense.GuardPose.SideLeft: return h == 1 ? new Vector3(-crossSideAxis.x, crossSideAxis.y, crossSideAxis.z) : sideAxis;
            case MonsterDefense.GuardPose.SideRight: return h == 0 ? crossSideAxis : new Vector3(-sideAxis.x, sideAxis.y, sideAxis.z);
            default: return h == 0 ? lowAxis : new Vector3(-lowAxis.x, lowAxis.y, lowAxis.z);
        }
    }

    void LateUpdate()
    {
        // While the IK is idle the bone holds the clip's pose: measure the fixed goal -> bone rotation offset.
        for (int h = 0; h < 2; h++)
        {
            if (!hasWeapon[h] || handBone[h] == null || weight[h] > 0f || computedFrame != Time.frameCount) continue;
            goalToBone[h] = Quaternion.Inverse(animRot[h]) * handBone[h].rotation;
            hasGoalToBone[h] = true;
        }
    }

    /// <summary>Where the weapon's middle goes: the raised pose's anchor, pulled followContact of the way to the predicted contact.</summary>
    Vector3 InterceptPoint()
    {
        float height = defense.Height;
        float chestZ = chest != null ? root.InverseTransformPoint(chest.position).z : 0f;
        Vector3 a;
        switch (defense.ActivePose)
        {
            case MonsterDefense.GuardPose.SideLeft: a = sideAnchor; break;
            case MonsterDefense.GuardPose.SideRight: a = new Vector3(-sideAnchor.x, sideAnchor.y, sideAnchor.z); break;
            case MonsterDefense.GuardPose.Low: a = lowAnchor; break;
            default: a = highAnchor; break;
        }
        Vector3 anchor = root.TransformPoint(new Vector3(a.x, a.y * height, chestZ + a.z));
        return Vector3.Lerp(anchor, defense.PredictedContact, followContact);
    }

    Vector3 Clamp(int h, Vector3 p)
    {
        // Monster space: in front of the chest, not far across the body's centre line, not below minHeight.
        Vector3 local = root.InverseTransformPoint(p);
        float chestZ = chest != null ? root.InverseTransformPoint(chest.position).z : 0f;
        local.z = Mathf.Max(local.z, chestZ + minForward);
        float ownSide = h == 0 ? 1f : -1f;   // right hand lives on +x
        if (local.x * ownSide < -maxCross) local.x = -maxCross * ownSide;
        if (defense != null) local.y = Mathf.Max(local.y, minHeight * defense.Height);
        p = root.TransformPoint(local);
        // Inside the arm's reach from the shoulder.
        if (shoulder[h] != null && arm[h] > 0f)
        {
            Vector3 d = p - shoulder[h].position;
            float max = arm[h] * reachFraction;
            if (d.sqrMagnitude > max * max) p = shoulder[h].position + d.normalized * max;
        }
        return p;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || animator == null) return;
        Compute();
        for (int h = 0; h < 2; h++)
        {
            if (!hasWeapon[h]) continue;
            animator.SetIKPositionWeight(Goals[h], weight[h] * positionWeight);
            if (orientWeapon)
            {
                // Full rotation weight whenever the hand IK is on, blended inside the goal: with a partial rotation weight the
                // un-driven part of the hand rotation follows the forearm as the position IK swings the arm, which twisted the weapon
                // far past the pose on Level 3's rig (measured 95 deg from the clip for a 62 deg goal).
                animator.SetIKRotationWeight(Goals[h], weight[h] > 0f ? 1f : 0f);
                if (weight[h] > 0f) animator.SetIKRotation(Goals[h], Quaternion.Slerp(animRot[h], rotGoal[h], weight[h]));
            }
            else if (keepClipHandRotation)
            {
                animator.SetIKRotationWeight(Goals[h], weight[h]);
                if (weight[h] > 0f) animator.SetIKRotation(Goals[h], animRot[h]);
            }
            if (weight[h] > 0f) animator.SetIKPosition(Goals[h], goal[h]);
        }
    }
}
