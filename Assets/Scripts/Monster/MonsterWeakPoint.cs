// MonsterWeakPoint.cs
// Purpose: Marks a monster body collider as a weak point. A player hit on this collider deals bonus damage and plays
//          its own sound (distinct from a normal hit) so the player knows they found the right spot. Optional pulsing
//          emission on the weak-point material makes it readable.
// Dependencies: HitDetection (reads damageMultiplier / plays hitClip), MonsterController (parent)
// Attach to: the weak-point collider GameObject (child of the monster). Level 3: Executioner left forearm.

using UnityEngine;

public class MonsterWeakPoint : MonoBehaviour
{
    [Tooltip("Damage multiplier for player hits on this collider (applied on top of the normal swing damage).")]
    public float damageMultiplier = 1.75f;
    [Tooltip("Played instead of the weapon's normal hit sound.")]
    public AudioClip hitClip;
    [Range(0f, 1f)] public float hitVolume = 1f;

    [Header("Glow (optional)")]
    public Renderer glowRenderer;
    [Tooltip("Material slot on glowRenderer that holds the weak-point material.")]
    public int glowMaterialIndex = 1;
    public Color glowColor = new Color(0.35f, 1.6f, 0.25f);
    public float pulseSpeed = 2.4f;
    [Range(0f, 1f)] public float pulseMin = 0.35f;
    [Tooltip("Brief bright flash when struck.")]
    public float hitFlashSeconds = 0.15f;

    public int HitCount { get; private set; }

    private MaterialPropertyBlock mpb;
    private float flashUntil = -1f;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (glowRenderer == null) return;
        float pulse = Mathf.Lerp(pulseMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI));
        Color c = Time.time < flashUntil ? glowColor * 3f : glowColor * pulse;
        glowRenderer.GetPropertyBlock(mpb, glowMaterialIndex);
        mpb.SetColor("_EmissionColor", c);
        glowRenderer.SetPropertyBlock(mpb, glowMaterialIndex);
    }

    /// <summary>Called by HitDetection when a player hit lands here.</summary>
    public void NotifyHit()
    {
        HitCount++;
        flashUntil = Time.time + hitFlashSeconds;
    }
}
