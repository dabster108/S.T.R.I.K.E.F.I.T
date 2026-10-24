// BossPhaseController.cs
// Purpose: Level 3 boss phases. Puts phase-break markers on the monster HP bar, tracks which phase the HP is in, and
//          below the enrage threshold switches the monster into Enrage: faster attacks and shorter telegraphs
//          (MonsterAI.tempoScale + Animator.speed together, so hit frames stay on the visible impact), shorter
//          cooldown, enraged attack weights (unblockable slam far more frequent), a red body tint, an aura light +
//          embers and a faster breathing idle. The switch is announced with a roar that the AI waits for.
//          Every phase break (including the enrage one) is a phase-shift pause: from the moment HP crosses the threshold
//          until the roar ends, hits are resisted (phaseShiftDamageMultiplier + a "RESISTED" cue), so a phase cannot be
//          burst through. The roar itself starts once the current swing / guard / stagger is over.
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

    [Header("Phase-shift pause")]
    [Tooltip("Roar + resist at every phase break. Off = the old behaviour (enrage roar only).")]
    public bool phaseShiftPauses = true;
    [Tooltip("Length of the phase-shift roar (s). The Roar clip is 2.4 s.")]
    public float phaseShiftSeconds = 2.4f;
    [Tooltip("Damage multiplier for hits during a phase shift.")]
    [Range(0f, 1f)] public float phaseShiftDamageMultiplier = 0.25f;
    [Tooltip("Played on a resisted hit (dull, not the guard clang).")]
    public AudioClip resistClip;
    [Range(0f, 1f)] public float resistVolume = 0.6f;

    [Header("Enrage")]
    [Tooltip("Attack tempo while enraged: wind-ups, combo gaps, recovery and the Animator all run this much faster. " +
             "Kept at a value where the shortest wind-up (0.525 s) stays a >= 0.45 s telegraph.")]
    public float enrageTempo = 1.15f;
    [Tooltip("Attack tempo in phase 2 (1 = authored). Phase 1 is 1; enraged (phase 3) uses enrageTempo.")]
    public float phase2Tempo = 1f;
    [Tooltip("Attack cooldown multiplier in phase 2 (enraged uses enrageCooldownScale).")]
    public float phase2CooldownScale = 1f;
    [Tooltip("Attack cooldown multiplier while enraged (on top of the HP-based shrink).")]
    public float enrageCooldownScale = 0.7f;
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
    /// <summary>True from the moment HP crosses a phase threshold until that phase's roar has finished.</summary>
    public bool InPhaseShift { get; private set; }
    public int PhaseShifts { get; private set; }
    /// <summary>Raised when a phase shift's roar starts (the new phase number: 1, 2). DragonHazard brings its dragons in on it.</summary>
    public event System.Action<int> PhaseShiftBegan;
    /// <summary>Raised when the phase shift is over and the monster acts again (the new phase number).</summary>
    public event System.Action<int> PhaseShiftEnded;
    public int ResistedHits { get; private set; }
    private float lastResistSound = -1f;
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

    // Rage colour goes through the monster's colour writer when it has one.
    private MonsterColorCompositor colors;

    void Awake()
    {
        health = GetComponent<MonsterHealth>();
        ai = GetComponent<MonsterAI>();
        colors = GetComponent<MonsterColorCompositor>();
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
        else if (phaseShiftPauses) StartCoroutine(PhaseShift());
        else if (phaseChangeClip != null && ai != null && ai.sfxAudioSource != null)
        {
            ai.sfxAudioSource.PlayOneShot(phaseChangeClip, phaseChangeVolume);
            SfxTrace.Note(this, phaseChangeClip, "phase shift roar");
        }
    }

    /// <summary>MonsterController: a hit landed during a phase shift (damage already reduced).</summary>
    public void NotifyResistedHit()
    {
        ResistedHits++;
        if (resistClip == null || ai == null || ai.sfxAudioSource == null || Time.time - lastResistSound < 0.15f) return;
        lastResistSound = Time.time;
        ai.sfxAudioSource.PlayOneShot(resistClip, resistVolume);
        SfxTrace.Note(this, resistClip, "phase shift: landed hit resisted");
    }

    /// <summary>
    /// Resist from now; hold new actions, let the current swing / guard / stagger end, then roar (real Idle -> Roar
    /// transition) with the AI off. Returns false if the monster died meanwhile (InPhaseShift is cleared either way).
    /// </summary>
    IEnumerator BeginShiftRoar(System.Action<bool> done)
    {
        InPhaseShift = true;
        PhaseShifts++;
        if (ai != null) ai.holdActions = true;
        while (ai != null && (ai.CurrentAttack != null || ai.IsBusy || controller.isAttacking) && controller.isAlive) yield return null;
        if (controller == null || !controller.isAlive) { InPhaseShift = false; if (ai != null) ai.holdActions = false; done(false); yield break; }

        if (ai != null) ai.enabled = false;
        controller.isAttacking = true; // no flinch (Hit) may cut the roar; also blocks CanAttack
        if (anim != null) anim.PlayNamedTrigger(roarTrigger);
        done(true);
        PhaseShiftBegan?.Invoke(phase);
    }

    void EndShift()
    {
        InPhaseShift = false;
        if (ai != null) { ai.holdActions = false; ai.enabled = true; }
        if (controller != null && controller.isAlive) controller.isAttacking = false;
        PhaseShiftEnded?.Invoke(phase);
    }

    IEnumerator PhaseShift()
    {
        bool ok = false;
        yield return BeginShiftRoar(r => ok = r);
        if (!ok) yield break;
        if (phaseChangeClip != null && ai != null && ai.sfxAudioSource != null)
        {
            ai.sfxAudioSource.PlayOneShot(phaseChangeClip, phaseChangeVolume);
            SfxTrace.Note(this, phaseChangeClip, "phase shift roar");
        }
        yield return new WaitForSeconds(phaseShiftSeconds);
        if (controller == null || !controller.isAlive) { InPhaseShift = false; yield break; }
        EndShift();
        if (anim != null) anim.PlayState(MonsterAnimState.Idle);
        if (ai != null && phase == 1) { ai.SetTempo(phase2Tempo); ai.phaseCooldownScale = phase2CooldownScale; }
    }

    IEnumerator EnterEnrage()
    {
        enrageStarted = true;
        if (phaseShiftPauses)
        {
            bool ok = false;
            yield return BeginShiftRoar(r => ok = r);
            if (!ok) yield break;
        }
        else
        {
            // Let the current swing finish, then hold the AI for the roar.
            while (ai != null && ai.CurrentAttack != null) yield return null;
            if (controller == null || !controller.isAlive) yield break;
            if (ai != null) ai.enabled = false;
            controller.isAttacking = true; // no flinch (Hit) may cut the roar; also blocks CanAttack
            if (anim != null) anim.PlayNamedTrigger(roarTrigger);
            PhaseShiftBegan?.Invoke(phase);
        }
        if (enrageRoarClip != null && ai != null && ai.sfxAudioSource != null)
        {
            ai.sfxAudioSource.PlayOneShot(enrageRoarClip, enrageRoarVolume);
            SfxTrace.Note(this, enrageRoarClip, "enrage roar");
        }
        if (enrageEmbers != null) enrageEmbers.Play(true);

        // Tint and aura ramp in over the roar (the roar lasts phaseShiftSeconds when the pause is on).
        float roarLen = phaseShiftPauses ? Mathf.Max(enrageRoarSeconds, phaseShiftSeconds) : enrageRoarSeconds;
        float t = 0f;
        while (t < roarLen)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / enrageRoarSeconds);
            if (ai != null) { ai.restingTint = Color.Lerp(Color.white, enrageTint, k); ai.ApplyRestingTint(); }
            if (colors != null) colors.SetRage(k, enrageTint);   // the rage layer (base tint + rim): ApplyRestingTint is a no-op then
            if (enrageAura != null) enrageAura.intensity = enrageAuraIntensity * k;
            yield return null;
        }

        if (controller == null || !controller.isAlive) { InPhaseShift = false; yield break; }
        controller.isAttacking = false;
        InPhaseShift = false;
        if (ai != null) ai.holdActions = false;
        PhaseShiftEnded?.Invoke(phase);
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
