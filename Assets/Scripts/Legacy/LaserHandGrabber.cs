using System.Collections;
using UnityEngine;

/// <summary>
/// Point the laser at an object tagged "Grabbable" and pull the index trigger:
/// the object flies to your hand and attaches to it. Pull the trigger again
/// while holding something to let go (physics takes back over).
///
/// Setup:
/// - Put this on the controller/hand anchor (same object your laser pointer
///   script is on, or a sibling of it).
/// - rayOrigin: the transform the laser is cast from (usually itself).
/// - handAnchor: the transform the object should snap/parent to once grabbed
///   (can be the same as rayOrigin, or an empty child positioned in the palm).
/// - Tag any grabbable model "Grabbable" and make sure it has a Rigidbody
///   and a Collider.
/// </summary>
public class LaserHandGrabber : MonoBehaviour
{
    [Header("Ray")]
    public Transform rayOrigin;
    public float maxGrabDistance = 10f;
    public LayerMask grabbableMask = ~0; // everything by default
    public string grabbableTag = "Grabbable";

    [Header("Hand")]
    public Transform handAnchor;
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Header("Pull")]
    public bool smoothPull = true;
    public float pullSpeed = 10f;       // units/sec, only used if smoothPull
    public float snapDistance = 0.03f;  // how close counts as "arrived"

    private GameObject heldObject;
    private Rigidbody heldRb;
    private bool isPulling;

    void Reset()
    {
        rayOrigin = transform;
        handAnchor = transform;
    }

    void Update()
    {
        bool triggerDown = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller);
        if (!triggerDown) return;

        if (heldObject != null)
        {
            Release();
            return;
        }

        if (isPulling) return; // already mid-pull from a previous press

        TryGrab();
    }

    void TryGrab()
    {
        if (rayOrigin == null) return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance, grabbableMask))
            return;

        if (!hit.collider.CompareTag(grabbableTag)) return;

        Rigidbody rb = hit.collider.attachedRigidbody;
        if (rb == null) return;

        heldObject = rb.gameObject;
        heldRb = rb;

        // stop it being pushed by physics while it travels / while held
        heldRb.isKinematic = true;
        heldRb.useGravity = false;

        if (smoothPull)
            StartCoroutine(PullToHand());
        else
            AttachToHand();
    }

    IEnumerator PullToHand()
    {
        isPulling = true;

        while (heldObject != null &&
               Vector3.Distance(heldObject.transform.position, handAnchor.position) > snapDistance)
        {
            heldObject.transform.position = Vector3.MoveTowards(
                heldObject.transform.position,
                handAnchor.position,
                pullSpeed * Time.deltaTime);

            heldObject.transform.rotation = Quaternion.RotateTowards(
                heldObject.transform.rotation,
                handAnchor.rotation,
                pullSpeed * 90f * Time.deltaTime);

            yield return null;
        }

        isPulling = false;

        if (heldObject != null)
            AttachToHand();
    }

    void AttachToHand()
    {
        if (heldObject == null) return;

        heldObject.transform.position = handAnchor.position;
        heldObject.transform.rotation = handAnchor.rotation;
        heldObject.transform.SetParent(handAnchor);
    }

    void Release()
    {
        if (heldObject == null) return;

        heldObject.transform.SetParent(null);

        heldRb.isKinematic = false;
        heldRb.useGravity = true;

        heldObject = null;
        heldRb = null;
    }
}