// DangerZoneIndicator.cs
// Purpose: Shows where an unblockable attack will land and how big the zone is, so the player knows to step OUT of
//          it (blocking / parrying does nothing against it). The rune circle is placed at the impact point, sized to
//          the attack's dodge radius, and an inner fill disc grows to the edge over the wind-up = time left to move.
// Dependencies: GroundRuneVFX (outer ring), optional fill renderer
// Attach to: a root object (not parented to the monster, so it stays on the ground while the monster moves).

using UnityEngine;

public class DangerZoneIndicator : MonoBehaviour
{
    public GroundRuneVFX ring;
    [Tooltip("Inner disc scaled 0 -> 1 over the wind-up (time-to-impact cue).")]
    public Transform fill;
    public Renderer fillRenderer;
    public Color fillColor = new Color(1f, 0.08f, 0.04f, 0.35f);
    [Tooltip("Ring quad local size that maps to radius 1 (the quad is 2 x 2 = radius 1).")]
    public float quadRadius = 1f;
    public float groundOffset = 0.03f;

    public bool IsShowing { get; private set; }
    public float Radius { get; private set; }

    private float showStart, windUp;
    private MaterialPropertyBlock mpb;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (transform.parent != null) transform.SetParent(null, true);
        if (fillRenderer != null) fillRenderer.enabled = false;
    }

    public void Show(Vector3 impactPoint, float radius, float windUpSeconds)
    {
        Radius = radius;
        windUp = Mathf.Max(0.05f, windUpSeconds);
        showStart = Time.time;
        IsShowing = true;
        transform.position = new Vector3(impactPoint.x, impactPoint.y + groundOffset, impactPoint.z);
        transform.localScale = Vector3.one * (radius / Mathf.Max(0.01f, quadRadius));
        if (ring != null) ring.Show(Mathf.Min(0.2f, windUp * 0.3f));
        if (fillRenderer != null) fillRenderer.enabled = true;
        UpdateFill(0f);
    }

    public void Impact()
    {
        if (ring != null) { ring.Flash(); ring.Hide(0.35f); }
        if (fillRenderer != null) fillRenderer.enabled = false;
        IsShowing = false;
    }

    public void Hide()
    {
        if (ring != null) ring.Hide(0.15f);
        if (fillRenderer != null) fillRenderer.enabled = false;
        IsShowing = false;
    }

    void Update()
    {
        if (!IsShowing) return;
        UpdateFill(Mathf.Clamp01((Time.time - showStart) / windUp));
    }

    void UpdateFill(float t)
    {
        if (fill != null) fill.localScale = new Vector3(t, 1f, t);
        if (fillRenderer != null)
        {
            fillRenderer.GetPropertyBlock(mpb);
            var c = fillColor; c.a *= Mathf.Lerp(0.4f, 1f, t);
            mpb.SetColor(ShaderIds.BaseColor, c);
            fillRenderer.SetPropertyBlock(mpb);
        }
    }
}
