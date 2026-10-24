// MenuAmbience.cs
// Purpose: The moving parts of the main menu's dungeon (the player and camera never move):
//          - Torch flames and embers: ONE pooled flame particle system and one ember system for every torch. Each frame the
//            torch anchors emit into them (Emit with reused EmitParams), so 10 torches cost two draw calls and a fixed particle
//            budget (the systems' maxParticles caps them) instead of 10 particle systems.
//          - A slowly swinging hanging cage and a cell door that drifts ajar and back.
//          - Now and then a hooded figure's shadow glides along the right wall (a dark soft card that fades in, moves, fades out).
//          Dust motes and floor mist are ordinary particle systems (rate based) and need no code. Everything runs on unscaled
//          time; no allocations per frame.
// Dependencies: none (wired by Editor/MenuDungeonBuilder)
// Attach to: MenuDungeon/Ambience in MainMenu.unity

using UnityEngine;

public class MenuAmbience : MonoBehaviour
{
    [Header("Torch flames (pooled: one system for every torch)")]
    public ParticleSystem flames;
    public ParticleSystem embers;
    public Transform[] flameAnchors;
    public float flamesPerSecond = 10f;
    public float embersPerSecond = 0.5f;
    public float flameRiseSpeed = 0.18f;
    [Tooltip("Flames start this far from the anchor (the anchor is the old particle origin, just above the torch basket).")]
    public Vector3 flameOffset = new Vector3(0f, -0.07f, 0f);

    [Header("Swinging cage")]
    public Transform swing;
    public float swingDegrees = 5f;
    public float swingPeriod = 7.5f;

    [Header("Drifting door")]
    public Transform door;
    public float doorDegrees = 14f;
    public float doorPeriod = 26f;

    [Header("Passing shadow")]
    public Renderer shadow;
    public Vector3 shadowFrom, shadowTo;
    public float shadowSeconds = 7f;
    public Vector2 shadowGap = new Vector2(14f, 32f);
    [Range(0f, 1f)] public float shadowAlpha = 0.6f;

    float[] flameAcc, emberAcc;
    Quaternion swingRest, doorRest;
    float nextShadow, shadowStart = -1f;
    MaterialPropertyBlock block;
    ParticleSystem.EmitParams ep;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        int n = flameAnchors != null ? flameAnchors.Length : 0;
        flameAcc = new float[n];
        emberAcc = new float[n];
        for (int i = 0; i < n; i++) { flameAcc[i] = Random.value; emberAcc[i] = Random.value; }
        if (swing != null) swingRest = swing.localRotation;
        if (door != null) doorRest = door.localRotation;
        block = new MaterialPropertyBlock();
        if (shadow != null) { SetShadowAlpha(0f); shadow.enabled = false; }
        nextShadow = Time.unscaledTime + Random.Range(6f, 12f);   // the first pass comes soon after the menu opens
        ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
        if (flameAnchors != null)
            for (int i = 0; i < flameAnchors.Length; i++)
            {
                var a = flameAnchors[i];
                if (a == null) continue;
                flameAcc[i] += dt * flamesPerSecond;
                while (flameAcc[i] >= 1f && flames != null)
                {
                    flameAcc[i] -= 1f;
                    ep.position = a.position + flameOffset + new Vector3(Random.Range(-0.025f, 0.025f), 0f, Random.Range(-0.025f, 0.025f));
                    ep.velocity = new Vector3(Random.Range(-0.03f, 0.03f), flameRiseSpeed * Random.Range(0.8f, 1.2f), Random.Range(-0.03f, 0.03f));
                    ep.rotation = Random.Range(-12f, 12f);
                    flames.Emit(ep, 1);
                }
                emberAcc[i] += dt * embersPerSecond;
                while (emberAcc[i] >= 1f && embers != null)
                {
                    emberAcc[i] -= 1f;
                    ep.position = a.position + Vector3.up * 0.12f;
                    ep.velocity = new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(0.35f, 0.7f), Random.Range(-0.12f, 0.12f));
                    ep.rotation = 0f;
                    embers.Emit(ep, 1);
                }
            }

        if (swing != null) swing.localRotation = swingRest * Quaternion.Euler(swingDegrees * Mathf.Sin(t * 2f * Mathf.PI / swingPeriod), 0f, 0f);
        if (door != null)
        {
            // Mostly still, then a slow creak open and back (smoothstep of a slow sine).
            float s = Mathf.Sin(t * 2f * Mathf.PI / doorPeriod);
            s = s > 0f ? s * s * (3f - 2f * s) : 0f;
            door.localRotation = doorRest * Quaternion.Euler(0f, doorDegrees * s, 0f);
        }

        if (shadow != null)
        {
            if (shadowStart < 0f && t >= nextShadow) { shadowStart = t; shadow.enabled = true; }
            if (shadowStart >= 0f)
            {
                float p = (t - shadowStart) / shadowSeconds;
                if (p >= 1f)
                {
                    shadowStart = -1f;
                    shadow.enabled = false;
                    nextShadow = t + Random.Range(shadowGap.x, shadowGap.y);
                }
                else
                {
                    shadow.transform.position = Vector3.Lerp(shadowFrom, shadowTo, p) + Vector3.up * (0.03f * Mathf.Sin(p * 18f));   // a slow gait
                    SetShadowAlpha(shadowAlpha * Mathf.Clamp01(Mathf.Min(p, 1f - p) * 4f));
                }
            }
        }
    }

    void SetShadowAlpha(float a)
    {
        shadow.GetPropertyBlock(block);
        block.SetColor(BaseColor, new Color(0f, 0f, 0f, a));
        shadow.SetPropertyBlock(block);
    }

    /// <summary>For tests: the shadow walks now.</summary>
    public void PassShadowNow() { nextShadow = Time.unscaledTime; }
    public bool ShadowPassing => shadowStart >= 0f;
}
