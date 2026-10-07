// PlayerDamageVignette.cs
// Purpose: The red hit-flash, as a screen-edge vignette. A head-locked quad with a radial texture (clear in the middle, red toward
//          the edges) replaces the old full-rect flash: it flashes when the player takes damage (same peak and duration as the
//          old flash), and below the low-HP threshold it adds a slow pulse, paired with the fast-breathing tier (both are driven
//          by PlayerExertionAudio.LowHpActive, so they start and stop together).
//          Distinct from the monster's red attack telegraph: that tints the monster; this lives only at the edge of the player's view.
// Dependencies: PlayerHealth (DamageTaken, hitFlashDuration), PlayerExertionAudio (LowHpActive), BattleManager
// Attach to: OVRCameraRig. `vignette` = head-locked quad under CenterEyeAnchor (a Quad + MeshRenderer sharing the radial material).

using UnityEngine;

public class PlayerDamageVignette : MonoBehaviour
{
    public PlayerHealth health;
    public PlayerExertionAudio exertion;
    [Tooltip("Head-locked quad. Left empty = the component does nothing and PlayerHealth keeps its old flash.")]
    public Renderer vignette;
    public Color color = new Color(0.85f, 0f, 0f, 1f);

    [Header("Hit flash (replaces PlayerHealth's full-rect flash)")]
    public bool edgeFlash = true;
    [Range(0f, 1f)] public float flashPeakAlpha = 0.6f;
    [Tooltip("Seconds for the flash to fade; 0 = PlayerHealth.hitFlashDuration.")]
    public float flashSeconds = 0f;

    [Header("Low-HP pulse (paired with the fast breathing tier)")]
    public bool lowHpPulse = true;
    [Tooltip("Seconds per pulse. Slow on purpose.")]
    public float pulsePeriod = 2.4f;
    [Range(0f, 1f)] public float pulseMinAlpha = 0.10f;
    [Range(0f, 1f)] public float pulseMaxAlpha = 0.30f;
    [Tooltip("Seconds to fade the pulse in / out when the low-HP state starts / ends.")]
    public float pulseFadeSeconds = 0.8f;

    public float Alpha { get; private set; }
    public int Flashes { get; private set; }
    public bool PulseActive => pulseLevel > 0.001f;

    private MaterialPropertyBlock mpb;
    private float flashT = 999f;
    private float pulseLevel;
    private float shownAlpha = -1f;
    private bool registered;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (exertion == null) exertion = GetComponent<PlayerExertionAudio>();
        if (vignette != null) vignette.enabled = false;
    }

    void OnEnable()
    {
        PlayerHealth.DamageTaken += OnDamage;
        if (health != null && vignette != null && edgeFlash) { health.flashReplacedByVignette = true; registered = true; }
    }

    void OnDisable()
    {
        PlayerHealth.DamageTaken -= OnDamage;
        if (registered && health != null) health.flashReplacedByVignette = false;
        registered = false;
        if (vignette != null) vignette.enabled = false;
    }

    void OnDamage(float amount, bool blocked, float hpAfter)
    {
        if (!edgeFlash || vignette == null || amount <= 0f) return;
        flashT = 0f;
        Flashes++;
    }

    void Update()
    {
        if (vignette == null) return;
        // Honour the toggle live: with the flash off the old rect flash comes back.
        if (health != null && health.flashReplacedByVignette != edgeFlash) health.flashReplacedByVignette = edgeFlash;

        bool battle = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;
        float dt = Time.unscaledDeltaTime;

        float flash = 0f;
        if (flashT < 10f)
        {
            float dur = flashSeconds > 0f ? flashSeconds : (health != null ? Mathf.Max(0.05f, health.hitFlashDuration) : 0.25f);
            flashT += dt;
            flash = flashT >= dur ? 0f : Mathf.Lerp(flashPeakAlpha, 0f, flashT / dur);
            if (flashT >= dur) flashT = 999f;
        }

        bool low = lowHpPulse && battle && exertion != null && exertion.LowHpActive;
        pulseLevel = Mathf.MoveTowards(pulseLevel, low ? 1f : 0f, dt / Mathf.Max(0.05f, pulseFadeSeconds));
        float pulse = 0f;
        if (pulseLevel > 0.001f)
        {
            float wave = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * 2f * Mathf.PI / Mathf.Max(0.2f, pulsePeriod));
            pulse = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, wave) * pulseLevel;
        }

        float a = battle || flash > 0f ? Mathf.Max(flash, pulse) : 0f;
        Alpha = a;
        bool show = a > 0.004f;
        if (vignette.enabled != show) vignette.enabled = show;
        // Only touch the material when the alpha moved visibly.
        if (show && Mathf.Abs(a - shownAlpha) > 0.003f)
        {
            shownAlpha = a;
            vignette.GetPropertyBlock(mpb);
            var c = color; c.a = a;
            mpb.SetColor("_BaseColor", c);
            vignette.SetPropertyBlock(mpb);
        }
    }
}
