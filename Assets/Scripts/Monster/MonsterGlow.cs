// MonsterGlow.cs
// Purpose: Level 3 boss body glow (eyes, mouth, skin cracks: the emission mask on the body materials). The glow
//          breathes with the idle (phase-locked to the Idle / IdleEnraged clip while idling, same period otherwise),
//          surges during every attack wind-up toward the attack's telegraph colour, and escalates with the fight:
//          deep red at full HP -> brighter red in phase 2 -> hot orange when enraged -> near white-hot at the end.
//          A point light on the chest spills the same colour onto the skin and the dock. Weak-point hits flash it.
//          Phase shift power-up (BossPhaseController.PhaseShiftBegan): the emission ramps up to hot gold over the roar, an
//          eye / mask flare peaks near its end together with a ground ring that ripples outwards, then it settles into the
//          new stage. Death: one white-hot flash, then the glow collapses to dark.
// Dependencies: MonsterAI (AttackStarted, enraged, tempo), MonsterHealth, MonsterController, Animator
// Attach to: monster root (Level 3 Twin-Axe Brute). Drives _EmissionColor through per-slot property blocks, so it
//            coexists with MonsterAI's _BaseColor tints and MonsterWeakPoint's own slot.

using UnityEngine;

public class MonsterGlow : MonoBehaviour
{
    [Header("Renderer")]
    public Renderer bodyRenderer;
    [Tooltip("Material slots that carry the glow mask (not the weak-point slot).")]
    public int[] materialIndices = { 0, 1 };
    public Animator animator;
    public string idleState = "Idle";
    public string enragedIdleState = "IdleEnraged";
    [Tooltip("Seconds per breath: the L3_Idle / L3_IdleEnraged loop lengths.")]
    public float idlePeriod = 3.2f;
    public float enragedPeriod = 1.2f;

    [Header("Escalation (colour x intensity)")]
    public Color calmColor = new Color(0.85f, 0.07f, 0.03f);
    public Color phase2Color = new Color(1f, 0.16f, 0.04f);
    public Color enragedColor = new Color(1f, 0.42f, 0.08f);
    public Color finalColor = new Color(1f, 0.78f, 0.45f);
    public float calmIntensity = 1f;
    public float phase2Intensity = 1.2f;
    public float enragedIntensity = 1.55f;
    public float finalIntensity = 1.9f;
    [Range(0f, 1f)] public float phase2HpFraction = 0.66f;
    [Range(0f, 1f)] public float finalHpFraction = 0.15f;
    [Tooltip("Share of the glow that rises and falls with each breath.")]
    [Range(0f, 1f)] public float pulseDepth = 0.35f;
    [Range(0f, 1f)] public float enragedPulseDepth = 0.45f;
    [Tooltip("Seconds to blend to a new escalation stage.")]
    public float stageBlendSeconds = 1.5f;

    [Header("Attack wind-up surge")]
    [Tooltip("Glow multiplier reached on the hit frame of a wind-up.")]
    public float windUpBoost = 1.9f;
    [Tooltip("How far the colour moves toward the attack's telegraph colour at the peak of the surge.")]
    [Range(0f, 1f)] public float windUpTint = 0.45f;

    [Header("Light spill (optional)")]
    public Light glowLight;
    public float glowLightCalm = 0.12f;
    public float glowLightEnraged = 1.8f;

    [Header("Phase-shift power-up (BossPhaseController.PhaseShiftBegan)")]
    [Tooltip("Emission ramp + flare + ground ripple during the roar of every phase break. Off = the old glow.")]
    public bool powerUp = true;
    [Tooltip("Length of the power-up (s): the phase-shift roar is 2.4 s.")]
    public float powerUpSeconds = 2.4f;
    [Tooltip("Hot gold: not the red telegraph colour, not the cool white-blue guard flash.")]
    public Color powerUpColor = new Color(1f, 0.66f, 0.18f);
    public float powerUpIntensity = 2.6f;
    public Color flareColor = new Color(1f, 0.93f, 0.62f);
    public float flareIntensity = 4.4f;
    public float flareSeconds = 0.4f;
    [Tooltip("Share of the power-up at which the flare and the ring start.")]
    [Range(0.3f, 1f)] public float flareAt = 0.72f;
    [Tooltip("Ground quad (radius 1 at scale 1) under the boss; scaled and faded by the glow. Optional.")]
    public Renderer rippleRenderer;
    public float rippleStartRadius = 0.8f, rippleEndRadius = 5.5f, rippleSeconds = 1.5f;
    public Color rippleColor = new Color(1f, 0.66f, 0.2f, 0.9f);

    [Header("Death")]
    [Tooltip("One white-hot flash, then the glow collapses (off = the old slow ember fade).")]
    public bool deathCollapse = true;
    public float deathFlashSeconds = 0.16f, deathFlashIntensity = 3.4f, deathCollapseSeconds = 1.2f;
    public float deathFadeSeconds = 2.5f;

    public Color CurrentColor { get; private set; }
    public float CurrentIntensity { get; private set; }
    /// <summary>0..1: how far the phase-shift power-up is (verification).</summary>
    public float PowerUpLevel { get; private set; }
    public float FlareLevel { get; private set; }
    public float RippleRadius { get; private set; }
    public int PowerUps { get; private set; }

    private MonsterAI ai;
    private MonsterHealth health;
    private MonsterController controller;
    private MaterialPropertyBlock mpb;
    private Color stageColor;
    private float stageIntensity;
    private float breathPhase;
    private float surgeStart = -1f, surgeEnd = -1f;
    private Color surgeColor;
    private float flashUntil = -1f;
    private Color flashColor = Color.white;
    private float deathTime = -1f;
    private int idleHash, enragedIdleHash;
    private BossPhaseController phases;
    private float puStart = -1f, rippleStart = -1f;
    private bool rippleDone;
    private MaterialPropertyBlock rippleMpb;

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        health = GetComponent<MonsterHealth>();
        controller = GetComponent<MonsterController>();
        phases = GetComponent<BossPhaseController>();
        mpb = new MaterialPropertyBlock();
        rippleMpb = new MaterialPropertyBlock();
        if (rippleRenderer != null) rippleRenderer.enabled = false;
        stageColor = calmColor; stageIntensity = calmIntensity;
        idleHash = Animator.StringToHash(idleState);
        enragedIdleHash = Animator.StringToHash(enragedIdleState);
    }

    void OnEnable()
    {
        MonsterAI.AttackStarted += HandleAttackStarted;
        if (phases != null) phases.PhaseShiftBegan += HandlePhaseShift;
    }

    void OnDisable()
    {
        MonsterAI.AttackStarted -= HandleAttackStarted;
        if (phases != null) phases.PhaseShiftBegan -= HandlePhaseShift;
        if (rippleRenderer != null) rippleRenderer.enabled = false;
    }

    void HandlePhaseShift(int phase)
    {
        if (!powerUp) return;
        puStart = Time.time; rippleStart = -1f; rippleDone = false; PowerUps++;
    }

    void HandleAttackStarted(MonsterAI source, string attackName, string tier)
    {
        if (source != ai || ai.CurrentAttack == null) return;
        float windUp = ai.CurrentAttack.windUp / Mathf.Max(0.25f, ai.tempoScale);
        surgeStart = Time.time; surgeEnd = Time.time + Mathf.Max(0.1f, windUp);
        surgeColor = ai.CurrentAttack.telegraphColor;
    }

    /// <summary>Brief bright flash (weak-point hit).</summary>
    public void Flash(Color c, float seconds)
    {
        flashColor = c;
        flashUntil = Time.time + seconds;
    }

    void Update()
    {
        if (bodyRenderer == null) return;
        bool alive = controller == null || controller.isAlive;
        if (!alive && deathTime < 0f) deathTime = Time.time;

        // --- Escalation stage from HP and enrage ---
        float hp = health != null && health.maxHealth > 0f ? Mathf.Clamp01(health.currentHealth / health.maxHealth) : 1f;
        bool enraged = ai != null && ai.enraged;
        Color targetC; float targetI;
        if (enraged && hp <= finalHpFraction) { targetC = finalColor; targetI = finalIntensity; }
        else if (enraged) { targetC = enragedColor; targetI = enragedIntensity; }
        else if (hp <= phase2HpFraction) { targetC = phase2Color; targetI = phase2Intensity; }
        else { targetC = calmColor; targetI = calmIntensity; }
        float k = 1f - Mathf.Exp(-Time.deltaTime * 3f / Mathf.Max(0.05f, stageBlendSeconds));
        stageColor = Color.Lerp(stageColor, targetC, k);
        stageIntensity = Mathf.Lerp(stageIntensity, targetI, k);

        // --- Breath: phase-locked to the idle clip while idling, free-running at the same period otherwise ---
        float period = enraged ? enragedPeriod : idlePeriod;
        bool locked = false;
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            if (!animator.IsInTransition(0) && (st.shortNameHash == idleHash || st.shortNameHash == enragedIdleHash))
            {
                breathPhase = Mathf.Repeat(st.normalizedTime, 1f) * 2f * Mathf.PI;
                locked = true;
            }
        }
        if (!locked) breathPhase += Time.deltaTime * 2f * Mathf.PI / Mathf.Max(0.1f, period);
        float depth = enraged ? enragedPulseDepth : pulseDepth;
        // The idle clips swell the chest on sin > 0: the glow rises with the in-breath.
        float breath = 1f - depth + depth * (0.5f + 0.5f * Mathf.Sin(breathPhase));

        Color c = stageColor;
        float intensity = stageIntensity * breath;

        // --- Wind-up surge: ramps to the hit frame, falls off quickly after ---
        if (alive && surgeStart >= 0f)
        {
            float s;
            if (Time.time <= surgeEnd) s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(surgeStart, surgeEnd, Time.time));
            else s = 1f - Mathf.Clamp01((Time.time - surgeEnd) / 0.35f);
            if (s <= 0f && Time.time > surgeEnd) surgeStart = -1f;
            intensity *= Mathf.Lerp(1f, windUpBoost, s);
            c = Color.Lerp(c, surgeColor, windUpTint * s);
        }

        // --- Phase-shift power-up: ramp, flare, ripple ---
        float pu = 0f, flare = 0f;
        if (alive && puStart >= 0f)
        {
            float t = Time.time - puStart, T = Mathf.Max(0.5f, powerUpSeconds);
            float settle = T * 0.35f;
            float up = Mathf.SmoothStep(0f, 1f, t / (T * flareAt));
            float down = 1f - Mathf.SmoothStep(0f, 1f, (t - T * flareAt - flareSeconds) / settle);
            pu = Mathf.Min(up, down);
            float ft = t - T * flareAt;
            if (ft >= 0f && ft <= flareSeconds) flare = Mathf.Sin(ft / Mathf.Max(0.05f, flareSeconds) * Mathf.PI);
            if (ft >= 0f && rippleStart < 0f && !rippleDone) rippleStart = Time.time;
            if (t > T * flareAt + flareSeconds + settle + 0.05f) puStart = -1f;
        }
        PowerUpLevel = pu; FlareLevel = flare;
        if (pu > 0f) { c = Color.Lerp(c, powerUpColor, 0.85f * pu); intensity = Mathf.Lerp(intensity, Mathf.Max(intensity, powerUpIntensity), pu); }
        if (flare > 0f) { c = Color.Lerp(c, flareColor, flare); intensity = Mathf.Max(intensity, flareIntensity * flare); }
        UpdateRipple(alive);

        if (Time.time < flashUntil) { c = flashColor; intensity = Mathf.Max(intensity, 2.2f); }

        // --- Death: one white-hot flash, then the glow collapses (or the old slow fade) ---
        if (!alive)
        {
            float td = Time.time - deathTime;
            if (deathCollapse)
            {
                float f = Mathf.Clamp01(td / Mathf.Max(0.02f, deathFlashSeconds));
                float collapse = Mathf.Clamp01((td - deathFlashSeconds) / Mathf.Max(0.05f, deathCollapseSeconds));
                intensity = Mathf.Lerp(intensity, deathFlashIntensity, f) * (1f - collapse) * (1f - collapse);
                c = Color.Lerp(flareColor, c, collapse);
            }
            else intensity *= 1f - Mathf.Clamp01(td / Mathf.Max(0.05f, deathFadeSeconds));
        }

        CurrentColor = c; CurrentIntensity = intensity;
        Color e = c * intensity;
        foreach (int i in materialIndices)
        {
            bodyRenderer.GetPropertyBlock(mpb, i);
            mpb.SetColor("_EmissionColor", e);
            bodyRenderer.SetPropertyBlock(mpb, i);
        }

        if (glowLight != null)
        {
            float li = Mathf.Lerp(glowLightCalm, glowLightEnraged, Mathf.InverseLerp(calmIntensity, finalIntensity, stageIntensity));
            glowLight.color = c;
            glowLight.intensity = li * intensity / Mathf.Max(0.01f, stageIntensity);
        }
    }

    /// <summary>The ground ring: grows from rippleStartRadius to rippleEndRadius over rippleSeconds and fades out.</summary>
    void UpdateRipple(bool alive)
    {
        if (rippleRenderer == null) return;
        if (!alive || rippleStart < 0f) { if (rippleRenderer.enabled) rippleRenderer.enabled = false; RippleRadius = 0f; return; }
        float k = (Time.time - rippleStart) / Mathf.Max(0.1f, rippleSeconds);
        if (k >= 1f) { rippleRenderer.enabled = false; rippleStart = -1f; rippleDone = true; RippleRadius = 0f; return; }
        float eased = 1f - (1f - k) * (1f - k);
        float r = Mathf.Lerp(rippleStartRadius, rippleEndRadius, eased);
        RippleRadius = r;
        rippleRenderer.enabled = true;
        rippleRenderer.transform.localScale = new Vector3(r, 1f, r);
        var col = rippleColor; col.a *= Mathf.Pow(1f - k, 1.5f);
        rippleRenderer.GetPropertyBlock(rippleMpb);
        rippleMpb.SetColor("_BaseColor", col);
        rippleRenderer.SetPropertyBlock(rippleMpb);
    }
}
