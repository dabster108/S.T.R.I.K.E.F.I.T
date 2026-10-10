// MonsterAxeFlames.cs
// Purpose: Level 3 burning axes. Each axe carries a small Shuriken flame on its blade (child of the blade, world-space
//          particles with a short life, so a swing leaves a brief flicker of fire behind it, never a long streak) and a
//          spark emitter. The flame size / rate follows the danger of what the boss is doing RIGHT NOW:
//            idle (low lick of flame) -> Standard / Combo attack (a bit hotter) -> Heavy / Unblockable attack (roaring),
//          set per attack from MonsterAI.CurrentAttack.category the moment the attack starts, so the escalation shows
//          during the wind-up, before the swing connects. A thrown axe roars while in flight. Enraged (MonsterAI.enraged,
//          the one enrage flag) scales every level up and shifts the flame hotter (blue-white core -> white-yellow ->
//          orange). Particle life shortens with blade speed, so a fast swing leaves a short flicker, never a long
//          streak. When an attack lands on the player (MonsterController
//          .OnMonsterAttackDamage, the same hit event the hit sounds come from) the axe nearest the player bursts sparks.
//          Charge flare: from the start of every wind-up both axes flare up (rate x size) toward the hit frame, by tier
//          (standard < combo < heavy < must-dodge), then fall off. Ember trail: a blade faster than emberMinSpeed sheds a few
//          slow embers per frame, so a fast swing leaves a short glowing trail in the air.
// Dependencies: MonsterAI, MonsterController, MonsterAxeThrow (optional), MonsterEntrance (optional)
// Attach to: monster root (Level 3 Twin-Axe Brute). flames[i] / sparks[i]: children of axe i's blade.

using Unity.Profiling;
using UnityEngine;

public class MonsterAxeFlames : MonoBehaviour
{
    [System.Serializable]
    public struct Level
    {
        [Tooltip("Particles per second per axe.")] public float rate;
        [Tooltip("Start size (m).")] public float size;
        [Tooltip("Start speed (m/s).")] public float speed;
        [Tooltip("Particle life (s): alive count ~ rate x life.")] public float life;
    }

    [Header("Per axe (0 = right, 1 = left)")]
    public ParticleSystem[] flames = new ParticleSystem[2];
    public ParticleSystem[] sparks = new ParticleSystem[2];

    [Header("Levels")]
    // Alive per axe ~ rate x 0.875 life: idle ~10, standard ~15, heavy ~24 (enraged x1.25: ~13 / ~19 / ~30).
    public Level idle = new Level { rate = 28f, size = 0.08f, speed = 0.20f, life = 0.42f };
    public Level standard = new Level { rate = 48f, size = 0.11f, speed = 0.28f, life = 0.36f };
    public Level heavy = new Level { rate = 80f, size = 0.18f, speed = 0.45f, life = 0.34f };
    [Tooltip("Rate multiplier while enraged (MonsterAI.enraged).")]
    public float enragedRateScale = 1.25f;
    [Tooltip("Size multiplier while enraged.")]
    public float enragedSizeScale = 1.15f;
    [Tooltip("How fast the flame moves to a new level (1/s).")]
    public float blendSpeed = 10f;
    [Tooltip("Seconds to blend calm -> enraged colour.")]
    public float enrageBlendSeconds = 1f;

    [Header("Fast swings")]
    [Tooltip("Blade speed (m/s) at which particle life starts / stops shortening.")]
    public float swingSpeedMin = 2f, swingSpeedMax = 8f;
    [Tooltip("Particle life multiplier at swingSpeedMax.")]
    public float fastSwingLifeScale = 0.5f;

    [Header("Enraged colour (same key times as the calm gradient: 0, 0.25, 0.6, 1)")]
    public Color enragedCore = new Color(0.82f, 0.9f, 1f);
    public Color enragedHot = new Color(1f, 0.9f, 0.62f);
    public Color enragedBody = new Color(1f, 0.5f, 0.12f);
    public Color enragedTip = new Color(0.7f, 0.1f, 0.03f);

    [Header("Sparks on a landed hit")]
    public int sparkBurst = 14;

    [Header("Charge flare (both axes, per attack tier)")]
    public bool chargeFlare = true;
    [Tooltip("Flame rate multiplier reached on the hit frame of a wind-up, by tier.")]
    public float flareStandard = 1.5f, flareCombo = 1.8f, flareHeavy = 2.6f, flareMustDodge = 3.0f;
    [Tooltip("Share of the multiplier that also applies to the flame size.")]
    [Range(0f, 1f)] public float flareSizeShare = 0.5f;
    [Tooltip("Seconds the flare takes to die down after the hit frame.")]
    public float flareFallSeconds = 0.3f;

    [Header("Ember trail on fast swings")]
    public bool emberTrail = true;
    [Tooltip("Blade speed (m/s) at which embers start / are at full rate.")]
    public float emberMinSpeed = 6f, emberFullSpeed = 12f;
    [Tooltip("Embers per second per axe at full speed (alive ~ rate x lifetime; the spark system holds 40).")]
    public float emberRate = 55f;
    public float emberLifetime = 0.45f;
    public float emberSize = 0.045f;

    public float CurrentRate(int i) => cur[i].rate;
    public float CurrentSize(int i) => cur[i].size;
    public string CurrentLevelName(int i) => levelName[i];
    public int SparkBursts { get; private set; }
    public int EmbersEmitted { get; private set; }
    public float FlareLevel { get; private set; }
    public float LastFlareMultiplier { get; private set; }
    public string LastFlareTier { get; private set; } = "-";
    public int Flares { get; private set; }
    public float EnrageBlend { get; private set; }
    public float BladeSpeed(int i) => bladeSpeed[i];
    public int AliveCount(int i) => flames[i] != null ? flames[i].particleCount : 0;

    static readonly ProfilerMarker Marker = new ProfilerMarker("MonsterAxeFlames.Update");

    private MonsterAI ai;
    private MonsterController controller;
    private MonsterAxeThrow thrower;
    private MonsterEntrance entrance;
    private readonly Level[] cur = new Level[2];
    private readonly string[] levelName = { "off", "off" };
    private bool lit;
    private readonly float[] bladeSpeed = new float[2];
    private readonly Vector3[] lastPos = new Vector3[2];
    private Gradient calmGradient, blendGradient;
    private float appliedBlend = -1f;
    private float flareStart = -1f, flareEnd = -1f, flareMul = 1f;
    private readonly float[] emberAcc = new float[2];

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        controller = GetComponent<MonsterController>();
        thrower = GetComponent<MonsterAxeThrow>();
        entrance = GetComponent<MonsterEntrance>();
        cur[0] = cur[1] = idle;
        foreach (var f in flames) if (f != null) f.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (flames[0] != null) calmGradient = flames[0].colorOverLifetime.color.gradient;
    }

    void OnEnable()
    {
        if (controller != null) controller.OnMonsterAttackDamage += HandleHitLanded;
        MonsterAI.AttackStarted += HandleAttackStarted;
    }

    void OnDisable()
    {
        if (controller != null) controller.OnMonsterAttackDamage -= HandleHitLanded;
        MonsterAI.AttackStarted -= HandleAttackStarted;
    }

    void HandleAttackStarted(MonsterAI source, string attackName, string tier)
    {
        if (source != ai || ai.CurrentAttack == null || !chargeFlare) return;
        var a = ai.CurrentAttack;
        float mul; string name;
        if (a.unblockable) { mul = flareMustDodge; name = "must-dodge"; }
        else if (a.category == MonsterAI.AttackCategory.Heavy || a.category == MonsterAI.AttackCategory.Special || a.category == MonsterAI.AttackCategory.IdleSlam) { mul = flareHeavy; name = "heavy"; }
        else if (a.category == MonsterAI.AttackCategory.Combo) { mul = flareCombo; name = "combo"; }
        else { mul = flareStandard; name = "standard"; }
        float windUp = a.windUp / Mathf.Max(0.25f, ai.tempoScale);
        flareStart = Time.time; flareEnd = Time.time + Mathf.Max(0.1f, windUp); flareMul = mul;
        LastFlareMultiplier = mul; LastFlareTier = name; Flares++;
    }

    void HandleHitLanded(float damage)
    {
        if (ai == null || ai.playerTarget == null) return;
        // The axe that did it: the one nearest the player's head.
        int best = -1; float bestD = float.MaxValue;
        for (int i = 0; i < 2; i++)
        {
            if (sparks[i] == null) continue;
            float d = (sparks[i].transform.position - ai.playerTarget.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) return;
        sparks[best].Emit(sparkBurst);
        SparkBursts++;
    }

    void Update()
    {
        using (Marker.Auto())
        {
            bool alive = controller == null || controller.isAlive;
            bool ready = entrance == null || entrance.Complete; // the summoning moment stays its own (circle + rise + roar)
            bool on = alive && ready;
            if (on != lit)
            {
                lit = on;
                foreach (var f in flames)
                {
                    if (f == null) continue;
                    if (on) f.Play(true); else f.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            if (!on) return;

            var a = ai != null ? ai.CurrentAttack : null;
            bool heavyAttack = a != null && (a.unblockable || a.category == MonsterAI.AttackCategory.Heavy ||
                                             a.category == MonsterAI.AttackCategory.Special || a.category == MonsterAI.AttackCategory.IdleSlam);
            bool enraged = ai != null && ai.enraged;
            EnrageBlend = Mathf.MoveTowards(EnrageBlend, enraged ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, enrageBlendSeconds));
            float rateScale = Mathf.Lerp(1f, enragedRateScale, EnrageBlend);
            float sizeScale = Mathf.Lerp(1f, enragedSizeScale, EnrageBlend);
            // charge flare: ramps to the hit frame, falls off after it (both axes)
            float fl = 0f;
            if (flareStart >= 0f)
            {
                if (Time.time <= flareEnd) fl = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flareStart, flareEnd, Time.time));
                else { fl = 1f - Mathf.Clamp01((Time.time - flareEnd) / Mathf.Max(0.05f, flareFallSeconds)); if (fl <= 0f) flareStart = -1f; }
            }
            FlareLevel = fl;
            rateScale *= 1f + (flareMul - 1f) * fl;
            sizeScale *= 1f + (flareMul - 1f) * flareSizeShare * fl;
            ApplyColour();
            float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            for (int i = 0; i < 2; i++)
            {
                if (flames[i] == null) continue;
                Vector3 p = flames[i].transform.position;
                bladeSpeed[i] = (p - lastPos[i]).magnitude / dt;
                lastPos[i] = p;
                float lifeScale = Mathf.Lerp(1f, fastSwingLifeScale, Mathf.InverseLerp(swingSpeedMin, swingSpeedMax, bladeSpeed[i]));
                if (emberTrail && sparks[i] != null)
                {
                    float e = Mathf.InverseLerp(emberMinSpeed, emberFullSpeed, bladeSpeed[i]);
                    if (e > 0f)
                    {
                        emberAcc[i] += emberRate * e * dt;
                        int n = (int)emberAcc[i];
                        if (n > 0)
                        {
                            emberAcc[i] -= n;
                            var ep = new ParticleSystem.EmitParams();
                            for (int j = 0; j < n; j++)
                            {
                                ep.velocity = Random.insideUnitSphere * 0.35f; ep.startLifetime = emberLifetime * Random.Range(0.8f, 1.1f); ep.startSize = emberSize * Random.Range(0.7f, 1.2f);
                                sparks[i].Emit(ep, 1);
                            }
                            EmbersEmitted += n;
                        }
                    }
                    else emberAcc[i] = 0f;
                }
                Level target; string name;
                if (thrower != null && thrower.InFlight(i)) { target = heavy; name = "heavy (thrown)"; }
                else if (a == null) { target = idle; name = "idle"; }
                else if (heavyAttack) { target = heavy; name = a.unblockable ? "heavy (unblockable)" : "heavy"; }
                else { target = standard; name = "standard"; }
                levelName[i] = name;
                cur[i] = new Level
                {
                    rate = Mathf.Lerp(cur[i].rate, target.rate * rateScale, k),
                    size = Mathf.Lerp(cur[i].size, target.size * sizeScale, k),
                    speed = Mathf.Lerp(cur[i].speed, target.speed, k),
                    life = Mathf.Lerp(cur[i].life, target.life, k),
                };
                var main = flames[i].main;
                main.startSize = new ParticleSystem.MinMaxCurve(cur[i].size * 0.7f, cur[i].size * 1.15f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(cur[i].speed * 0.6f, cur[i].speed);
                main.startLifetime = new ParticleSystem.MinMaxCurve(cur[i].life * lifeScale * 0.75f, cur[i].life * lifeScale);
                var em = flames[i].emission;
                em.rateOverTime = cur[i].rate;
            }
        }
    }

    /// <summary>Calm gradient -> enraged gradient by EnrageBlend; rebuilt only when the blend moves a step.</summary>
    void ApplyColour()
    {
        if (calmGradient == null) return;
        float b = Mathf.Round(EnrageBlend * 20f) / 20f;
        if (Mathf.Approximately(b, appliedBlend)) return;
        appliedBlend = b;
        var ck = calmGradient.colorKeys;
        Color[] hot = { enragedCore, enragedHot, enragedBody, enragedTip };
        var keys = new GradientColorKey[ck.Length];
        for (int j = 0; j < ck.Length; j++)
            keys[j] = new GradientColorKey(Color.Lerp(ck[j].color, hot[Mathf.Min(j, hot.Length - 1)], b), ck[j].time);
        if (blendGradient == null) blendGradient = new Gradient();
        blendGradient.SetKeys(keys, calmGradient.alphaKeys);
        foreach (var f in flames)
        {
            if (f == null) continue;
            var col = f.colorOverLifetime;
            col.color = blendGradient;
        }
    }
}
