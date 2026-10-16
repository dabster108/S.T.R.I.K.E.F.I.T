// UiLaser.cs
// Purpose: The laser line and reticle for UI panels. It is the OVRInputModule's cursor, so the line ends exactly where the UI
//          ray hits a canvas (weapons and other colliders never stop it, because it is never a physics raycast) and the reticle
//          sits on that point. ControllerMapping decides when it is visible; this class only draws. Drawn with the material the
//          scene's old laser already used, so no shader is looked up at runtime and nothing new has to be included in the build.
// Dependencies: OVRCursor (Meta XR SDK), ControlSettings, ControllerMapping
// Attach to: created by ControllerMapping under the pointing hand's anchor

using UnityEngine;

public class UiLaser : OVRCursor
{
    public LineRenderer line;
    public Transform reticle;

    Transform ray;
    bool hit;
    Vector3 hitPoint, hitNormal;
    bool shown;

    public bool Shown => shown;

    public override void SetCursorRay(Transform r) { ray = r; }

    public override void SetCursorStartDest(Vector3 start, Vector3 dest, Vector3 normal)
    {
        hit = true; hitPoint = dest; hitNormal = normal;
    }

    void LateUpdate()
    {
        bool want = ControllerMapping.LaserWanted && line != null;
        var origin = ray != null ? ray : transform;
        if (want)
        {
            var s = ControlSettings.Current;
            Vector3 start = origin.position;
            Vector3 end = hit ? hitPoint : start + origin.forward * s.laserLength;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            if (reticle != null)
            {
                bool showReticle = hit;
                if (reticle.gameObject.activeSelf != showReticle) reticle.gameObject.SetActive(showReticle);
                if (showReticle)
                {
                    float dist = Vector3.Distance(start, hitPoint);
                    reticle.position = hitPoint + hitNormal * 0.003f;
                    if (hitNormal.sqrMagnitude > 0.01f) reticle.rotation = Quaternion.LookRotation(-hitNormal);
                    reticle.localScale = Vector3.one * (s.reticleSize * Mathf.Max(0.3f, dist));
                }
            }
        }
        else if (reticle != null && reticle.gameObject.activeSelf) reticle.gameObject.SetActive(false);
        if (line != null && line.enabled != want) line.enabled = want;
        shown = want;
        hit = false;   // the module sets it again next frame while the ray is on a canvas
    }

    /// <summary>A flat 24-sided disc facing -Z (looks along its own forward when rotated onto a surface normal).</summary>
    public static Mesh BuildDisc()
    {
        const int n = 24;
        var verts = new Vector3[n + 1];
        var tris = new int[n * 3];
        verts[0] = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            verts[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f);
            tris[i * 3] = 0; tris[i * 3 + 1] = 1 + (i + 1) % n; tris[i * 3 + 2] = 1 + i;
        }
        var m = new Mesh { name = "UiLaserReticle", vertices = verts, triangles = tris };
        m.RecalculateBounds();
        return m;
    }
}
