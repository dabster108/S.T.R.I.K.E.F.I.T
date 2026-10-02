// PlayerLowHealthFeedback.cs
// Purpose: Tension feedback when the PLAYER is low on HP (Level 3): a blood-red vignette at the edge of vision that
//          pulses with a heartbeat (lub-dub audio, faster as HP falls), and heavy breathing that no longer stops when
//          the player rests (PlayerExertionAudio floor), so the boss's rage and the player's own danger are both felt.
//          Off outside an active battle.
// Dependencies: PlayerHealth, PlayerExertionAudio (ExternalFloorVolume), BattleManager
// Attach to: OVRCameraRig. vignette = head-locked quad under CenterEyeAnchor (radial-alpha texture, transparent).

using UnityEngine;

public class PlayerLowHealthFeedback : MonoBehaviour
{
    public PlayerHealth health;
    public PlayerExertionAudio exertion;
    public Renderer vignette;
    public Color vignetteColor = new Color(0.45f, 0f, 0f, 1f);
    [Range(0f, 1f)] public float maxAlpha = 0.6f;

    [Header("Thresholds (fraction of max HP)")]
    [Tooltip("Feedback starts below this.")]
    [Range(0f, 1f)] public float startFraction = 0.35f;
    [Tooltip("Full strength at or below this.")]
    [Range(0f, 1f)] public float fullFraction = 0.1f;

    [Header("Heartbeat")]
    public AudioSource heartSource;
    public AudioClip heartbeatClip;
    public float bpmStart = 72f;
    public float bpmFull = 128f;
    [Range(0f, 1f)] public float heartVolumeStart = 0.25f;
    [Range(0f, 1f)] public float heartVolumeFull = 0.65f;

    [Header("Breathing")]
    [Tooltip("Breathing volume kept up even at rest, at full strength.")]
    [Range(0f, 1f)] public float breathingFloor = 0.22f;
    public float fadeSeconds = 0.6f;

    public float Intensity { get; private set; }
    public int Beats { get; private set; }
    public float VignetteAlpha { get; private set; }

    private MaterialPropertyBlock mpb;
    private float beatPhase = 1f;
    private float lastBeatTime = -10f;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (exertion == null) exertion = GetComponent<PlayerExertionAudio>();
        if (vignette != null) vignette.enabled = false;
    }

    void Update()
    {
        bool active = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive && health != null && !health.IsDead;
        float target = 0f;
        if (active && health.maxHealth > 0f)
            target = Mathf.Clamp01(Mathf.InverseLerp(startFraction, fullFraction, health.currentHealth / health.maxHealth));
        Intensity = Mathf.MoveTowards(Intensity, target, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds));

        // Heartbeat clock (real time).
        float pulse = 0f;
        if (Intensity > 0.001f)
        {
            float bpm = Mathf.Lerp(bpmStart, bpmFull, Intensity);
            beatPhase += Time.unscaledDeltaTime * bpm / 60f;
            if (beatPhase >= 1f)
            {
                beatPhase -= Mathf.Floor(beatPhase);
                lastBeatTime = Time.unscaledTime;
                Beats++;
                if (heartSource != null && heartbeatClip != null)
                    heartSource.PlayOneShot(heartbeatClip, Mathf.Lerp(heartVolumeStart, heartVolumeFull, Intensity));
            }
            // Lub-dub envelope: the clip's two thumps are 0.2 s apart.
            float s = Time.unscaledTime - lastBeatTime;
            pulse = Mathf.Max(Mathf.Exp(-s / 0.09f), 0.7f * Mathf.Exp(-Mathf.Max(0f, s - 0.2f) / 0.09f) * (s >= 0.2f ? 1f : 0f));
        }
        else beatPhase = 1f; // first beat right away next time

        VignetteAlpha = maxAlpha * Intensity * (0.65f + 0.35f * pulse);
        if (vignette != null)
        {
            bool show = VignetteAlpha > 0.005f;
            if (vignette.enabled != show) vignette.enabled = show;
            if (show)
            {
                vignette.GetPropertyBlock(mpb);
                var c = vignetteColor; c.a = VignetteAlpha;
                mpb.SetColor("_BaseColor", c);
                vignette.SetPropertyBlock(mpb);
            }
        }

        if (exertion != null) exertion.ExternalFloorVolume = breathingFloor * Intensity;
    }

    void OnDisable()
    {
        if (exertion != null) exertion.ExternalFloorVolume = 0f;
        if (vignette != null) vignette.enabled = false;
    }
}
