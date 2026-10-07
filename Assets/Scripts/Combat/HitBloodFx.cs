// HitBloodFx.cs
// Purpose: Stylized hit feedback on the monster: a short burst of dark-red droplets (Blood), bright sparks (Sparks) or nothing (Off)
//          at the contact point of every landed player hit. Scaled by swing tier, a little bigger for weak-point and full-tier
//          hits. No decals, no pools, nothing persistent: every particle is gone in under half a second.
//          One shared component per level; one pooled ParticleSystem per monster, created on the monster's first hit and reused.
// Dependencies: HitDetection (HitLanded + the hit point it recorded), CombatUIController.StyleForSpeed (tier), MonsterController
// Attach to: BattleSystems (one per level scene)
//
// Cost: no allocation per hit (ParticleSystem.Emit with a reused EmitParams, no Instantiate after the first hit of a monster).
//       One extra draw call while droplets are alive, none otherwise. Particles are capped by maxParticles.

using System.Collections.Generic;
using UnityEngine;

public class HitBloodFx : MonoBehaviour
{
    public enum Mode { Off, Sparks, Blood }

    [Tooltip("Off / Sparks / Blood. Read on every hit, so it can be changed while playing.")]
    public Mode mode = Mode.Blood;

    [Header("Materials (assets, so the particle shader ships in the build)")]
    public Material bloodMaterial;
    public Material sparksMaterial;

    [Header("Burst size (particles) by swing tier")]
    [Range(1, 14)] public int tapCount = 6;
    [Range(1, 14)] public int lightCount = 8;
    [Range(1, 14)] public int mediumCount = 10;
    [Range(1, 14)] public int fullCount = 12;
    [Tooltip("Extra particles for a weak-point hit. The total never exceeds maxBurst.")]
    [Range(0, 6)] public int weakPointBonus = 2;
    [Range(1, 14)] public int maxBurst = 14;
    [Tooltip("Particle size multiplier for weak-point and full-tier hits.")]
    public float bigHitSizeScale = 1.2f;

    [Header("Particle look")]
    [Tooltip("Seconds each particle lives. Keep under 0.5.")]
    public Vector2 lifetime = new Vector2(0.25f, 0.45f);
    public Vector2 size = new Vector2(0.03f, 0.06f);
    public Vector2 speed = new Vector2(1.1f, 2.7f);
    public float gravity = 1.6f;
    [Tooltip("Cone half-angle (degrees) around the spray direction.")]
    [Range(5f, 80f)] public float spread = 38f;
    public Color bloodColor = new Color(0.55f, 0.03f, 0.04f, 1f);
    public Color bloodColorDark = new Color(0.32f, 0.01f, 0.02f, 1f);
    public Color sparkColor = new Color(1f, 0.78f, 0.3f, 1f);
    public Color sparkColorHot = new Color(1f, 0.95f, 0.7f, 1f);

    [Header("Runtime (read-only)")]
    [SerializeField] private int bursts;
    [SerializeField] private int lastBurstSize;
    [SerializeField] private int pooledSystems;

    public int Bursts => bursts;
    public int LastBurstSize => lastBurstSize;
    public int PooledSystems => pooledSystems;
    /// <summary>Particles alive right now across this component's systems (verification).</summary>
    public int LiveParticles
    {
        get
        {
            int n = 0;
            foreach (var kv in systems) if (kv.Value.ps != null) n += kv.Value.ps.particleCount;
            return n;
        }
    }

    private struct Pooled { public MonsterController monster; public ParticleSystem ps; public ParticleSystemRenderer renderer; }
    private readonly Dictionary<MonsterController, Pooled> systems = new Dictionary<MonsterController, Pooled>();
    private readonly List<MonsterController> stale = new List<MonsterController>();
    private ParticleSystem.EmitParams emit;
    private MonsterController lastMonster;
    private Pooled lastPooled;

    void OnEnable() { HitDetection.HitLanded += OnHit; }
    void OnDisable() { HitDetection.HitLanded -= OnHit; }

    void OnHit(HitDetection source, float swingSpeed, bool defended)
    {
        if (mode == Mode.Off || source == null || defended) return; // a guarded hit does not draw blood
        var monster = source.LastMonster;
        if (monster == null) return;
        var pool = PoolFor(monster);
        if (pool.ps == null) return;

        var style = CombatUIController.StyleForSpeed(swingSpeed);
        bool weak = source.LastWasWeakPoint;
        int count = style == CombatUIController.DamageStyle.Full ? fullCount
                  : style == CombatUIController.DamageStyle.Medium ? mediumCount
                  : style == CombatUIController.DamageStyle.Light ? lightCount : tapCount;
        if (weak) count += weakPointBonus;
        count = Mathf.Clamp(count, 1, Mathf.Min(14, maxBurst));
        float sizeScale = (weak || style == CombatUIController.DamageStyle.Full) ? bigHitSizeScale : 1f;

        bool blood = mode == Mode.Blood;
        var rend = pool.renderer;
        var mat = blood ? bloodMaterial : sparksMaterial;
        if (mat != null && rend.sharedMaterial != mat) rend.sharedMaterial = mat;

        // Spray away from the weapon and out of the body, with a little lift.
        Vector3 point = source.LastHitPoint;
        Vector3 outward = point - monster.transform.position; outward.y = 0f;
        outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -monster.transform.forward;
        Vector3 swing = source.CurrentVelocity;
        swing = swing.sqrMagnitude > 1e-4f ? swing.normalized : outward;
        Vector3 dir = (swing * 0.6f + outward * 0.6f + Vector3.up * 0.2f).normalized;

        emit.position = point;
        for (int i = 0; i < count; i++)
        {
            Vector3 d = Vector3.Slerp(dir, Random.onUnitSphere, spread / 180f);
            emit.velocity = d * Random.Range(speed.x, speed.y) * (blood ? 1f : 1.35f);
            emit.startLifetime = Random.Range(lifetime.x, Mathf.Min(lifetime.y, 0.49f));
            emit.startSize = Random.Range(size.x, size.y) * sizeScale * (blood ? 1f : 0.7f);
            emit.startColor = blood ? Color.Lerp(bloodColorDark, bloodColor, Random.value) : Color.Lerp(sparkColor, sparkColorHot, Random.value);
            pool.ps.Emit(emit, 1);
        }
        bursts++;
        lastBurstSize = count;
    }

    Pooled PoolFor(MonsterController monster)
    {
        if (monster == lastMonster && lastPooled.ps != null) return lastPooled;
        if (!systems.TryGetValue(monster, out var p) || p.ps == null)
        {
            // A new monster (next battle): drop the systems of monsters that no longer exist, then build one for this monster.
            stale.Clear();
            foreach (var kv in systems) if (kv.Key == null || kv.Value.ps == null) stale.Add(kv.Key);
            foreach (var k in stale)
            {
                if (systems.TryGetValue(k, out var old) && old.ps != null) Destroy(old.ps.gameObject);
                systems.Remove(k);
            }
            p = Build(monster);
            systems[monster] = p;
            pooledSystems = systems.Count;
        }
        lastMonster = monster;
        lastPooled = p;
        return p;
    }

    Pooled Build(MonsterController monster)
    {
        var go = new GameObject("HitBloodFx_" + monster.name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 48;
        main.gravityModifier = gravity / 9.81f;
        main.startLifetime = 0.35f;
        main.startSize = 0.04f;
        main.startSpeed = 0f;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 1.4f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.sharedMaterial = mode == Mode.Sparks ? sparksMaterial : bloodMaterial;
        ps.Play();
        return new Pooled { monster = monster, ps = ps, renderer = r };
    }
}
