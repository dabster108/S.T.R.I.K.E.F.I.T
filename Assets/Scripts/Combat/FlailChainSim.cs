// FlailChainSim.cs
// Purpose: Gives the War Flail its swing feel: the chain and ball trail the handle as a damped two-segment pendulum
//          (Verlet, gravity, fixed chain length) instead of moving rigidly with the hand. The ball carries the
//          flail's HitDetection, so its own speed (faster than the hand on a full swing) drives the damage.
// Dependencies: none (HitDetection on the ball reads the ball's motion)
// Attach to: the flail root (on the hand anchor). anchor = top of the handle, ball + links = children.

using UnityEngine;

[DefaultExecutionOrder(200)] // after the hand anchors (OVRCameraRig, EditorHandPoseFallback) have moved
public class FlailChainSim : MonoBehaviour
{
    public Transform anchor;
    public Transform ball;
    public Transform[] links = new Transform[0];
    public float chainLength = 0.4f;
    [Range(0.8f, 1f)] public float damping = 0.985f;
    public float gravity = 9.81f;
    [Range(1, 8)] public int iterations = 4;

    private Vector3 mid, midPrev, end, endPrev;
    private bool initialised;

    void OnEnable() { initialised = false; }

    void LateUpdate()
    {
        if (anchor == null || ball == null) return;
        // While paused the chain rests straight along the handle (it follows the hand rigidly) and is re-seeded every frame, so a
        // resume starts from rest instead of from pause-time motion.
        if (GamePause.IsPaused) initialised = false;
        float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
        Vector3 a = anchor.position;
        float half = chainLength * 0.5f;

        if (!initialised)
        {
            // Rest pose: straight out along the handle (as authored).
            Vector3 dir = (ball.position - a).normalized;
            if (dir.sqrMagnitude < 0.5f) dir = anchor.forward;
            mid = midPrev = a + dir * half;
            end = endPrev = a + dir * chainLength;
            initialised = true;
        }

        Vector3 g = Vector3.down * gravity * dt * dt;
        Vector3 vm = (mid - midPrev) * damping; midPrev = mid; mid += vm + g;
        Vector3 ve = (end - endPrev) * damping; endPrev = end; end += ve + g;

        for (int i = 0; i < iterations; i++)
        {
            mid = a + (mid - a).normalized * half;
            Vector3 d = end - mid;
            float len = d.magnitude;
            if (len > 0.0001f)
            {
                Vector3 corr = d * (1f - half / len) * 0.5f;
                mid += corr; end -= corr;
            }
            mid = a + (mid - a).normalized * half;
        }

        ball.position = end;
        Vector3 along = (end - mid).sqrMagnitude > 0.0001f ? (end - mid).normalized : anchor.forward;
        ball.rotation = Quaternion.LookRotation(along, anchor.up);

        int n = links.Length;
        for (int i = 0; i < n; i++)
        {
            if (links[i] == null) continue;
            float t = (i + 0.5f) / n;
            Vector3 p, fwd;
            if (t < 0.5f) { float k = t * 2f; p = Vector3.Lerp(a, mid, k); fwd = mid - a; }
            else { float k = (t - 0.5f) * 2f; p = Vector3.Lerp(mid, end, k); fwd = end - mid; }
            if (fwd.sqrMagnitude < 0.000001f) fwd = anchor.forward;
            links[i].position = p;
            // Alternate links 90 degrees around the chain axis, like a real chain.
            links[i].rotation = Quaternion.LookRotation(fwd.normalized, anchor.up) * Quaternion.Euler(0f, 90f, (i % 2) * 90f);
        }
    }
}
