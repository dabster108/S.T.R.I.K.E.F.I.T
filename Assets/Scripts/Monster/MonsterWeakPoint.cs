// MonsterWeakPoint.cs
// Purpose: Marks a monster body collider as a weak point. A player hit on this collider deals bonus damage and plays
//          its own sound (distinct from a normal hit) so the player knows they found the right spot. Optional pulsing
//          emission on the weak-point material makes it readable.
// Dependencies: HitDetection (reads damageMultiplier / plays hitClip), MonsterController (parent)
// Attach to: the weak-point collider GameObject (child of the monster). Level 3: Twin-Axe Brute right forearm
//            (with a hit-stop: monster + attack clock freeze, controller buzz, body glow flash).

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

    [Header("Hit-stop (optional, Level 3)")]
    [Tooltip("Seconds the monster freezes (Animator + attack clock) when this spot is struck. 0 = off.")]
    public float hitStopSeconds = 0f;
    [Tooltip("Controller buzz on the hand that struck (0 = off).")]
    [Range(0f, 1f)] public float hapticAmplitude = 0f;
    public float hapticSeconds = 0.12f;
    [Tooltip("Optional: the body glow flashes this colour for the freeze.")]
    public MonsterGlow bodyGlow;
    public Color bodyFlashColor = new Color(1f, 0.95f, 0.85f);

    public int HitCount { get; private set; }

    private MaterialPropertyBlock mpb;
    private float flashUntil = -1f;

    // The monster's colour writer (Level 3): the weak-point pulse is its WeakPointGlow layer.
    private MonsterColorCompositor colors;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        colors = GetComponentInParent<MonsterColorCompositor>();
    }

    void Update()
    {
        if (glowRenderer == null) return;
        float pulse = Mathf.Lerp(pulseMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI));
        Color c = Time.time < flashUntil ? glowColor * 3f : glowColor * pulse;
        if (colors != null) { colors.SetWeakPointGlow(c); return; }
        glowRenderer.GetPropertyBlock(mpb, glowMaterialIndex);
        mpb.SetColor(ShaderIds.EmissionColor, c);
        glowRenderer.SetPropertyBlock(mpb, glowMaterialIndex);
    }

    /// <summary>Called by HitDetection when a player hit lands here.</summary>
    public void NotifyHit()
    {
        NotifyHit(null);
    }

    /// <summary>Called by HitDetection when a player hit lands here (source = the weapon that struck).</summary>
    public void NotifyHit(HitDetection source)
    {
        HitCount++;
        flashUntil = Time.time + Mathf.Max(hitFlashSeconds, hitStopSeconds);
        if (hitStopSeconds > 0f)
        {
            var ai = GetComponentInParent<MonsterAI>();
            if (ai != null) ai.HitStop(hitStopSeconds);
            if (bodyGlow != null) bodyGlow.Flash(bodyFlashColor, hitStopSeconds);
        }
        if (hapticAmplitude > 0f && source != null)
            StartCoroutine(Buzz(source.hand == Hand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch));
    }

    System.Collections.IEnumerator Buzz(OVRInput.Controller c)
    {
        OVRInput.SetControllerVibration(1f, hapticAmplitude, c);
        yield return new WaitForSecondsRealtime(hapticSeconds);
        OVRInput.SetControllerVibration(0f, 0f, c);
    }
}
