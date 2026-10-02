// BossPhaseController.cs
// Purpose: Level 3 boss phases. Puts phase-break markers on the monster HP bar, tracks which phase the HP is in, and
//          below the enrage threshold switches the monster into Enrage: faster attacks and shorter telegraphs
//          (MonsterAI.tempoScale + Animator.speed together, so hit frames stay on the visible impact), shorter
//          cooldown, enraged attack weights (unblockable slam far more frequent), a red body tint, an aura light +
//          embers and a faster breathing idle. The switch is announced with a roar that the AI waits for.
// Dependencies: MonsterHealth, MonsterAI, MonsterAnimator, CombatUIController, MonsterEntrance (optional)
// Attach to: monster root (Level 3 Executioner).

using System.Collections;
using UnityEngine;

public class BossPhaseController : MonoBehaviour
{
    [Header("Phases (HP fractions, high to low)")]
    [Tooltip("Phase breaks shown on the HP bar. The last one is the enrage threshold.")]
    public float[] phaseThresholds = { 0.66f, 0.33f };
    public string[] phaseNames = { "PHASE 1", "PHASE 2", "ENRAGED" };

    [Header("Enrage")]
    [Tooltip("Attack tempo while enraged: wind-ups, combo gaps, recovery and the Animator all run this much faster.")]
    public float enrageTempo = 1.3f;
    [Tooltip("Attack cooldown multiplier while enraged (on top of the HP-based shrink).")]
    public float enrageCooldownScale = 0.8f;
    public Color enrageTint = new Color(1f, 0.55f, 0.48f);
    public string enragedBool = "Enraged";
    public string roarTrigger = "Roar";
    public float enrageRoarSeconds = 1.8f;
    public AudioClip enrageRoarClip;
    [Range(0f, 1f)] public float enrageRoarVolume = 1f;
    public AudioClip phaseChangeClip;
    [Range(0f, 1f)] public float phaseChangeVolume = 0.8f;
    public Light enrageAura;
    public float enrageAuraIntensity = 3f;
    public ParticleSystem enrageEmbers;

    [Header("Runtime (read-only)")]
    [SerializeField] private int phase;
    [Tooltip("Latch: the enrage sequence (roar) has started. The enraged STATE itself is MonsterAI.enraged.")]
    [SerializeField] private bool enrageStarted;
    public int Phase => phase;
    /// <summary>
    /// The single source of truth for "is the monster enraged": MonsterAI.enraged, which only EnterEnrage writes (at
    /// the end of the enrage roar). Attack weights, tempo, MonsterGlow, ArenaMoodController, MonsterAxeFlames and
    /// MonsterSerpentAura all read that same bool.
    /// </summary>
    public bool IsEnraged => ai != null && ai.enraged;

    private MonsterHealth health;
    private MonsterAI ai;
    private MonsterAnimator anim;
    private MonsterController controller;
    private MonsterEntrance entrance;
    private bool active;

    void Awake()
    {
        health = GetComponent<MonsterHealth>();
        ai = GetComponent<MonsterAI>();
        anim = GetComponent<MonsterAnimator>();
        controller = GetComponent<MonsterController>();
        entrance = GetComponent<MonsterEntrance>();
        if (enrageAura != null) enrageAura.intensity = 0f;
    }

    void Start()
    {
        if (entrance != null && !entrance.Complete) entrance.EntranceComplete += Activate;
        else Activate();
        // Markers are visible from spawn (the bar is already on screen during the entrance).
        if (CombatUIController.Instance != null)
        {
            CombatUIController.Instance.SetMonsterPhaseMarkers(phaseThresholds);
            CombatUIController.Instance.SetMonsterPhaseLabel(NameFor(0));
        }
    }

    void OnDestroy()
    {
        if (entrance != null) entrance.EntranceComplete -= Activate;
    }

    void Activate()
    {
        active = true;
        if (health != null) health.OnHealthChanged += HandleHealth;
        HandleHealth(health != null ? health.currentHealth : 1f, health != null ? health.maxHealth : 1f);
    }

    string NameFor(int p) => phaseNames != null && p < phaseNames.Length ? phaseNames[p] : "PHASE " + (p + 1);

    void HandleHealth(float current, float max)
    {
        if (!active || controller == null || !controller.isAlive) return;
        float f = max > 0f ? current / max : 0f;
        int p = 0;
        for (int i = 0; i < phaseThresholds.Length; i++) if (f <= phaseThresholds[i]) p = i + 1;
        if (p <= phase) return;

        phase = p;
        if (CombatUIController.Instance != null) CombatUIController.Instance.SetMonsterPhaseLabel(NameFor(phase));
        if (phase >= phaseThresholds.Length && !enrageStarted) StartCoroutine(EnterEnrage());
        else if (phaseChangeClip != null && ai != null && ai.sfxAudioSource != null)
            ai.sfxAudioSource.PlayOneShot(phaseChangeClip, phaseChangeVolume);
    }

    IEnumerator EnterEnrage()
    {
        enrageStarted = true;
        // Let the current swing finish, then hold the AI for the roar.
        while (ai != null && ai.CurrentAttack != null) yield return null;
        if (controller == null || !controller.isAlive) yield break;

        if (ai != null) ai.enabled = false;
        controller.isAttacking = true; // no flinch (Hit) may cut the roar; also blocks CanAttack
        if (anim != null) anim.PlayNamedTrigger(roarTrigger);
        if (enrageRoarClip != null && ai != null && ai.sfxAudioSource != null)
            ai.sfxAudioSource.PlayOneShot(enrageRoarClip, enrageRoarVolume);
        if (enrageEmbers != null) enrageEmbers.Play(true);

        // Tint and aura ramp in over the roar.
        float t = 0f;
        while (t < enrageRoarSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / enrageRoarSeconds);
            if (ai != null) { ai.restingTint = Color.Lerp(Color.white, enrageTint, k); ai.ApplyRestingTint(); }
            if (enrageAura != null) enrageAura.intensity = enrageAuraIntensity * k;
            yield return null;
        }

        if (controller == null || !controller.isAlive) yield break;
        controller.isAttacking = false;
        if (anim != null)
        {
            anim.SetBoolIfPresent(enragedBool, true);
            if (anim.animator != null) anim.animator.speed = enrageTempo;
        }
        if (ai != null)
        {
            ai.enraged = true;
            ai.tempoScale = enrageTempo;
            ai.lowHpCooldownScale = Mathf.Max(0.5f, ai.lowHpCooldownScale * enrageCooldownScale);
            ai.enabled = true;
        }
    }

    void Update()
    {
        // Aura flicker while enraged (cheap, one light).
        if (IsEnraged && enrageAura != null && controller != null && controller.isAlive)
            enrageAura.intensity = enrageAuraIntensity * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 4f, 0.3f));
        if (controller != null && !controller.isAlive)
        {
            if (enrageAura != null) enrageAura.intensity = 0f;
            if (enrageEmbers != null && enrageEmbers.isPlaying) enrageEmbers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (anim != null && anim.animator != null) anim.animator.speed = 1f;
        }
    }
}
