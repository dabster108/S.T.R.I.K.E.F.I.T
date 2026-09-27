// EditorHandPoseFallback.cs
// Purpose: Editor-only preview fix. Without a headset/controllers (Game view with no Link), OVRCameraRig leaves both
//          hand anchors on the eye, so held weapons render inside the camera. This puts any untracked hand in a
//          resting pose in front of the body. Tracked controllers are never touched, and it compiles out of builds.
// Dependencies: OVRCameraRig
// Attach to: OVRCameraRig

using UnityEngine;

[RequireComponent(typeof(OVRCameraRig))]
public class EditorHandPoseFallback : MonoBehaviour
{
    [Tooltip("Right hand resting position relative to the head (metres, head-yaw space). Left hand mirrors it on X.")]
    public Vector3 rightHandOffset = new Vector3(0.22f, -0.42f, 0.38f);
    [Tooltip("Right hand resting rotation (pitch down tilts the weapon toward the monster). Left hand mirrors yaw.")]
    public Vector3 rightHandEuler = new Vector3(15f, 8f, 0f);

#if UNITY_EDITOR
    private OVRCameraRig rig;

    void Awake()
    {
        rig = GetComponent<OVRCameraRig>();
    }

    // OVRCameraRig writes the anchors in Update; LateUpdate runs after it every frame.
    void LateUpdate()
    {
        if (rig == null || rig.centerEyeAnchor == null) return;
        Place(rig.rightHandAnchor, OVRInput.Controller.RTouch, 1f);
        Place(rig.leftHandAnchor, OVRInput.Controller.LTouch, -1f);
    }

    void Place(Transform hand, OVRInput.Controller controller, float side)
    {
        if (hand == null || OVRInput.GetControllerPositionTracked(controller)) return;

        Transform head = rig.centerEyeAnchor;
        Quaternion yaw = Quaternion.Euler(0f, head.localEulerAngles.y, 0f);
        Vector3 offset = new Vector3(rightHandOffset.x * side, rightHandOffset.y, rightHandOffset.z);
        hand.localPosition = head.localPosition + yaw * offset;
        hand.localRotation = yaw * Quaternion.Euler(rightHandEuler.x, rightHandEuler.y * side, rightHandEuler.z * side);
    }
#endif
}
