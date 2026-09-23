using UnityEngine;

// Attach this to the empty "LaserPointer" GameObject under RightControllerAnchor.
// That same object should also have a LineRenderer component on it.
//
// NOTE: renamed the class to ControllerLaserPointer to avoid colliding with
// Meta's own built-in "LaserPointer" class if your project has the Oculus
// Integration / Meta XR SDK sample scripts imported. If you rename the file,
// rename the class to match (Unity requires the file name and class name to be identical).
[RequireComponent(typeof(LineRenderer))]
public class ControllerLaserPointer : MonoBehaviour
{
    public enum LaserBeamBehavior
    {
        On,             // always visible
        Off,            // never visible
        OnWhenHitTarget // only visible while the ray is hitting something on hitLayers
    }

    [Tooltip("Where the ray starts and points from. Leave empty to use this object's own transform.")]
    [SerializeField] private Transform rayOrigin;

    [Tooltip("Max length of the beam when nothing is hit.")]
    [SerializeField] private float maxLength = 10f;

    [Tooltip("Which layers the beam should visually stop against (e.g. your Canvas/Button colliders).")]
    [SerializeField] private LayerMask hitLayers = ~0;

    [Tooltip("Controls when the beam is drawn: always on, always off, or only while pointing at something.")]
    [SerializeField] private LaserBeamBehavior beamBehavior = LaserBeamBehavior.OnWhenHitTarget;

    private LineRenderer lineRenderer;

    // Change this at runtime from other scripts if you want, e.g.
    // GetComponent<ControllerLaserPointer>().BeamBehavior = ControllerLaserPointer.LaserBeamBehavior.Off;
    public LaserBeamBehavior BeamBehavior
    {
        get => beamBehavior;
        set => beamBehavior = value;
    }

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;

        if (rayOrigin == null)
        {
            rayOrigin = transform;
        }
    }

    private void Update()
    {
        if (beamBehavior == LaserBeamBehavior.Off)
        {
            lineRenderer.enabled = false;
            return;
        }

        Vector3 start = rayOrigin.position;
        Vector3 direction = rayOrigin.forward;

        bool didHit = Physics.Raycast(start, direction, out RaycastHit hit, maxLength, hitLayers);

        if (beamBehavior == LaserBeamBehavior.OnWhenHitTarget && !didHit)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;

        float length = didHit ? hit.distance : maxLength;
        Vector3 end = start + direction * length;

        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }
}