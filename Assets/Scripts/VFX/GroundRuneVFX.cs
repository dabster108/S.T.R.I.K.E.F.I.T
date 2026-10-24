// GroundRuneVFX.cs
// Purpose: Flat glowing rune circle on the ground: rotates, pulses, fades in/out. Used for the Level 3 summoning
//          circle (monster entrance) and, through DangerZoneIndicator, for the unblockable slam's impact zone.
// Dependencies: a MeshRenderer (quad lying on the ground) with an additive/transparent material that has _BaseColor
// Attach to: the rune quad object.

using System.Collections;
using UnityEngine;

public class GroundRuneVFX : MonoBehaviour
{
    public Renderer runeRenderer;
    public Color color = new Color(0.35f, 1f, 0.25f, 1f);
    [Tooltip("Degrees per second around the up axis.")]
    public float spinSpeed = 35f;
    public float pulseSpeed = 1.6f;
    [Range(0f, 1f)] public float pulseMin = 0.55f;
    [Tooltip("Optional particles (rising embers) started / stopped with the circle.")]
    public ParticleSystem particles;
    [Tooltip("Optional light whose intensity follows the circle's alpha.")]
    public Light glowLight;
    public float glowLightIntensity = 2f;

    public float Alpha { get; private set; }
    public bool IsVisible => Alpha > 0.001f;

    private MaterialPropertyBlock mpb;
    private Coroutine fade;
    private float flash;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (runeRenderer == null) runeRenderer = GetComponent<Renderer>();
        Apply();
        if (runeRenderer != null) runeRenderer.enabled = false;
    }

    void Update()
    {
        if (!IsVisible) return;
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
        Apply();
    }

    void Apply()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock(); // can be called (Hide) before Awake on a fresh spawn
        if (runeRenderer == null) runeRenderer = GetComponent<Renderer>();
        if (runeRenderer == null) return;
        float pulse = Mathf.Lerp(pulseMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
        Color c = color * (pulse + flash * 2f);
        c.a = color.a * Alpha;
        runeRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(ShaderIds.BaseColor, c);
        runeRenderer.SetPropertyBlock(mpb);
        if (glowLight != null) glowLight.intensity = glowLightIntensity * Alpha * pulse;
    }

    public void Show(float fadeSeconds)
    {
        if (runeRenderer != null) runeRenderer.enabled = true;
        if (particles != null) particles.Play(true);
        StartFade(1f, fadeSeconds);
    }

    public void Hide(float fadeSeconds)
    {
        if (particles != null) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        StartFade(0f, fadeSeconds);
    }

    /// <summary>Brief brightness spike (impact).</summary>
    public void Flash() { flash = 1f; }

    void StartFade(float target, float seconds)
    {
        if (fade != null) StopCoroutine(fade);
        if (!isActiveAndEnabled) { Alpha = target; Apply(); if (runeRenderer != null) runeRenderer.enabled = target > 0f; return; }
        fade = StartCoroutine(FadeRoutine(target, seconds));
    }

    IEnumerator FadeRoutine(float target, float seconds)
    {
        float from = Alpha;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Alpha = Mathf.Lerp(from, target, Mathf.Clamp01(t / Mathf.Max(0.0001f, seconds)));
            Apply();
            yield return null;
        }
        Alpha = target;
        Apply();
        if (target <= 0f && runeRenderer != null) runeRenderer.enabled = false;
        fade = null;
    }
}
