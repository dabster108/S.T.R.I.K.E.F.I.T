// MonsterFireAura.cs
// Purpose: Fire wrapping the monster's body (Level 1 yellow-gold, Level 2 deep violet with a magenta core; the per-level look
//          is the serialized profile below). Flame licks rise from points that follow the skeleton (head, neck, chest,
//          spine, hips, shoulders, arms, thighs) every frame, so the fire stays on the body through every attack. It flares
//          during Heavy / Special attacks, burns brighter once the monster drops under its low-HP event, and dies out on
//          death. A faint emission pulse on the body material (MaterialPropertyBlock, added on top of the material's own
//          emission) makes the skin smoulder with it.
//          Level 3 has its own fire (MonsterAxeFlames / MonsterGlow / MonsterSerpentAura) and does not use this component.
// Cost: ONE world-space ParticleSystem built at runtime (one material = one draw call), particles emitted from cached bone
//       transforms with a reused EmitParams: no allocation per frame. No light, no shadows, no post-processing.
// Dependencies: MonsterAI (AttackStarted), MonsterController (isAlive, isAttacking, OnMonsterDeath), MonsterHealth,
//               MonsterEntrance (optional), BattleManager
// Attach to: monster root (Levels 1 and 2).

using UnityEngine;

public class MonsterFireAura : MonoBehaviour
{
    [Tooltip("Master toggle.")]
    public bool auraEnabled = true;
    [Tooltip("Additive flame material (shared with the weapon fire, so the shader already ships).")]
    public Material flameMaterial;

    [Header("Profile: colour")]
    public Color outerColor = new Color(1f, 0.62f, 0.12f, 1f);
    [Tooltip("Colour of the smaller, brighter core licks.")]
    public Color coreColor = new Color(1f, 0.9f, 0.45f, 1f);
    [Range(0f, 1f)] public float coreShare = 0.3f;

    [Header("Profile: amount (live particles ~ rate x lifetime)")]
    public float ratePerSecond = 40f;
    public Vector2 lifetime = new Vector2(0.4f, 0.7f);
    public Vector2 size = new Vector2(0.07f, 0.14f);
    [Tooltip("Upward speed of a lick (m/s).")]
    public Vector2 rise = new Vector2(0.35f, 0.7f);
    [Tooltip("Radius (m) of the shell the licks start on around each body point (torso, limbs): about the body's half-width, " +
             "so the fire sits on the silhouette instead of inside the mesh where the body hides it.")]
    public Vector2 scatter = new Vector2(0.3f, 0.12f);
    [Tooltip("Use the radius of the hit-volume capsule on each bone (plus shellMargin) instead of scatter, where one exists.")]
    public bool fitToHitVolumes = true;
    public float shellMargin = 0.04f;
    public int maxParticles = 70;

    [Header("Moments")]
    [Tooltip("Rate / size multipliers while a Heavy or Special attack plays (from its wind-up).")]
    public float flareRate = 1.8f;
    public float flareSize = 1.3f;
    [Tooltip("Health fraction of the low-HP event; under it the fire burns brighter.")]
    [Range(0f, 1f)] public float lowHpFraction = 0.35f;
    public float lowHpRate = 1.35f;
    public float lowHpSize = 1.15f;
    [Tooltip("Seconds the fire takes to die out after death.")]
    public float deathFadeSeconds = 1.2f;
    [Tooltip("Seconds to ease between levels (calm / flare / low HP).")]
    public float blendSeconds = 0.25f;

    [Header("Body smoulder (MaterialPropertyBlock on _EmissionColor)")]
    public bool bodyPulse = true;
    public Renderer bodyRenderer;
    public Color pulseColor = new Color(1f, 0.5f, 0.1f, 1f);
    public Vector2 pulseIntensity = new Vector2(0.04f, 0.12f);
    public float pulsePeriod = 1.6f;

    /// <summary>Particles alive (verification).</summary>
    public int LiveParticles => ps != null ? ps.particleCount : 0;
    public float Level { get; private set; }
    public bool Flaring { get; private set; }
    public bool LowHp { get; private set; }
    public ParticleSystem Particles => ps;

    private ParticleSystem ps;
    private ParticleSystemRenderer psr;
    private ParticleSystem.EmitParams emit;
    private MonsterController ctrl;
    private MonsterAI ai;
    private MonsterHealth health;
    private MonsterEntrance entrance;
    private Transform[] points;
    private bool[] torso;
    private float[] radius;
    private float accumulate, rateMul = 1f, sizeMul = 1f, fade = 1f, nextPulse;
    private bool flareAttack, dead;
    private MaterialPropertyBlock mpb;
    private Color[] baseEmission;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    static readonly HumanBodyBones[] TorsoBones = { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips };
    static readonly HumanBodyBones[] LimbBones = { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                                                   HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };

    void Awake()
    {
        ctrl = GetComponent<MonsterController>();
        ai = GetComponent<MonsterAI>();
        health = GetComponent<MonsterHealth>();
        entrance = GetComponent<MonsterEntrance>();
        colors = GetComponent<MonsterColorCompositor>();
        mpb = new MaterialPropertyBlock();
        CacheBones();
        Build();
        if (bodyRenderer != null)
        {
            var mats = bodyRenderer.sharedMaterials;
            baseEmission = new Color[mats.Length];
            for (int i = 0; i < mats.Length; i++)
                baseEmission[i] = mats[i] != null && mats[i].HasProperty(EmissionId) && mats[i].IsKeywordEnabled("_EMISSION") ? mats[i].GetColor(EmissionId) : Color.black;
        }
    }

    void OnEnable()
    {
        MonsterAI.AttackStarted += OnAttackStarted;
        if (ctrl != null) ctrl.OnMonsterDeath += OnDeath;
        dead = false; fade = 1f; flareAttack = false; rateMul = sizeMul = 1f; accumulate = 0f;
    }

    void OnDisable()
    {
        MonsterAI.AttackStarted -= OnAttackStarted;
        if (ctrl != null) ctrl.OnMonsterDeath -= OnDeath;
        if (ps != null) ps.Clear();
        SetPulse(0f);
    }

    void CacheBones()
    {
        var an = GetComponentInChildren<Animator>();
        int n = 0;
        points = new Transform[TorsoBones.Length + LimbBones.Length];
        torso = new bool[points.Length];
        if (an == null || !an.isHuman)
        {
            points[0] = transform; torso[0] = true;
            System.Array.Resize(ref points, 1); System.Array.Resize(ref torso, 1);
            radius = new[] { scatter.x };
            return;
        }
        foreach (var b in TorsoBones) { var t = an.GetBoneTransform(b); if (t != null) { torso[n] = true; points[n++] = t; } }
        foreach (var b in LimbBones) { var t = an.GetBoneTransform(b); if (t != null) { torso[n] = false; points[n++] = t; } }
        System.Array.Resize(ref points, n);
        System.Array.Resize(ref torso, n);
        FitRadii();
    }

    void FitRadii()
    {
        radius = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            radius[i] = torso[i] ? scatter.x : scatter.y;
            if (!fitToHitVolumes || points[i] == null) continue;
            // Hit volumes (HitVol_*) are direct children of the bones they cover.
            for (int c = 0; c < points[i].childCount; c++)
            {
                var cap = points[i].GetChild(c).GetComponent<CapsuleCollider>();
                if (cap == null || cap.isTrigger) continue;
                var sc = cap.transform.lossyScale;
                radius[i] = cap.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z)) + shellMargin;
                break;
            }
        }
    }

    void Build()
    {
        var go = new GameObject("FireAura");
        go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true; // emission is off: the system only plays so Emit()ted particles keep simulating
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(8, maxParticles);
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));
        var rol = ps.rotationOverLifetime;
        rol.enabled = true;
        rol.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;
        psr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        psr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        psr.sharedMaterial = flameMaterial;
        ps.Play();
    }

    void OnAttackStarted(MonsterAI who, string name, string category)
    {
        if (who != ai) return;
        flareAttack = category == "Heavy" || category == "Special" || category == "IdleSlam";
    }

    void OnDeath() { dead = true; }

    void Update()
    {
        if (ps == null) return;
        float dt = Time.deltaTime;
        bool battle = BattleManager.Instance == null || BattleManager.Instance.IsBattleActive || dead;
        bool ready = entrance == null || entrance.Complete;
        bool on = auraEnabled && flameMaterial != null && battle && ready && ctrl != null && (ctrl.isAlive || dead);

        if (flareAttack && ctrl != null && !ctrl.isAttacking) flareAttack = false;
        Flaring = flareAttack;
        LowHp = health != null && health.maxHealth > 0f && health.currentHealth <= health.maxHealth * lowHpFraction;

        float wantRate = (Flaring ? flareRate : 1f) * (LowHp ? lowHpRate : 1f);
        float wantSize = (Flaring ? flareSize : 1f) * (LowHp ? lowHpSize : 1f);
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, blendSeconds));
        rateMul = Mathf.Lerp(rateMul, wantRate, k);
        sizeMul = Mathf.Lerp(sizeMul, wantSize, k);
        if (dead) fade = Mathf.MoveTowards(fade, 0f, dt / Mathf.Max(0.05f, deathFadeSeconds));
        Level = on ? rateMul * fade : 0f;

        if (Level > 0.001f && points.Length > 0)
        {
            accumulate += ratePerSecond * Level * dt;
            int n = (int)accumulate;
            accumulate -= n;
            for (int i = 0; i < n; i++) EmitOne();
        }
        else accumulate = 0f;

        if (bodyPulse && bodyRenderer != null && Time.time >= nextPulse)
        {
            nextPulse = Time.time + 0.05f; // 20 Hz is smooth for a 1.6 s pulse
            float wave = 0.5f - 0.5f * Mathf.Cos(Time.time * 2f * Mathf.PI / Mathf.Max(0.2f, pulsePeriod));
            SetPulse(on ? Mathf.Lerp(pulseIntensity.x, pulseIntensity.y, wave) * Level : 0f);
        }
    }

    void EmitOne()
    {
        int i = Random.Range(0, points.Length);
        var p = points[i];
        if (p == null) return;
        bool core = Random.value < coreShare;
        float r = radius[i];
        Vector2 h = Random.insideUnitCircle.normalized * (r * Random.Range(0.8f, 1.15f));
        emit.position = p.position + new Vector3(h.x, Random.Range(-0.1f, 0.1f), h.y);
        emit.velocity = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(rise.x, rise.y), Random.Range(-0.08f, 0.08f));
        emit.startLifetime = Random.Range(lifetime.x, lifetime.y) * (core ? 0.8f : 1f);
        emit.startSize = Random.Range(size.x, size.y) * sizeMul * (core ? 0.6f : 1f);
        emit.startColor = core ? coreColor : outerColor;
        emit.rotation = Random.Range(0f, 360f);
        ps.Emit(emit, 1);
    }

    float shownPulse = -1f;
    // With the compositor the smoulder is its AuraPulse layer (added to the material's own emission there).
    MonsterColorCompositor colors;

    void SetPulse(float intensity)
    {
        if (bodyRenderer == null || baseEmission == null) return;
        if (Mathf.Abs(intensity - shownPulse) < 0.002f) return;
        shownPulse = intensity;
        if (colors != null) { colors.SetAura(pulseColor * intensity); return; }
        // Read-modify-write of the renderer-wide block: MonsterAI's telegraph / guard tints (_BaseColor) stay untouched.
        bodyRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(EmissionId, baseEmission.Length > 0 ? baseEmission[0] + pulseColor * intensity : pulseColor * intensity);
        bodyRenderer.SetPropertyBlock(mpb);
    }
}
