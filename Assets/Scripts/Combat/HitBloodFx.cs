// HitBloodFx.cs
// Purpose: Hit feedback that comes out of the monster's body: a burst of dark-red droplets with gravity at the contact point,
//          sprayed along the swing (Blood), bright sparks (Sparks) or nothing (Off). Scaled by swing tier, bigger for full-tier
//          and weak-point hits. A few drips keep running from the wound for a couple of seconds (the emit point follows the
//          struck bone), and the monster bleeds a bigger burst when it dies. A guarded or parried hit never bleeds; a weapon
//          clash throws sparks instead. No decals, no pools on the floor, nothing persistent.
//          One shared component per level; one pooled ParticleSystem per monster, created on the monster's first hit and reused
//          for bursts, drips, death and clash sparks (one material, so one draw call).
// Dependencies: HitDetection (HitLanded / WeaponClashed + the point and collider it recorded), CombatUIController.StyleForSpeed
//               (tier), MonsterController (OnMonsterDeath)
// Attach to: BattleSystems (one per level scene)
//
// Cost: no allocation per hit (ParticleSystem.Emit with a reused EmitParams, a fixed drip-slot array, no Instantiate after the
//       first hit of a monster). One extra draw call while particles are alive, none otherwise. Particles capped by maxParticles.

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
    [Range(1, 50)] public int tapCount = 18;
    [Range(1, 50)] public int lightCount = 22;
    [Range(1, 50)] public int mediumCount = 26;
    [Range(1, 50)] public int fullCount = 36;
    [Tooltip("Extra particles for a weak-point hit (and a weak-point hit is never below weakPointMinimum).")]
    [Range(0, 20)] public int weakPointBonus = 10;
    [Range(1, 50)] public int weakPointMinimum = 32;
    [Range(1, 50)] public int maxBurst = 50;
    [Tooltip("Particle size multiplier for weak-point and full-tier hits.")]
    public float bigHitSizeScale = 1.25f;

    [Header("Particle look")]
    [Tooltip("Seconds each droplet lives.")]
    public Vector2 lifetime = new Vector2(0.5f, 0.9f);
    [Tooltip("Droplet size (m). Sized to read at 1.5 to 2 m in VR.")]
    public Vector2 size = new Vector2(0.035f, 0.075f);
    public Vector2 speed = new Vector2(0.8f, 2.2f);
    [Tooltip("Gravity on droplets (m/s^2).")]
    public float gravity = 6f;
    [Tooltip("Cone half-angle (degrees) around the spray direction.")]
    [Range(5f, 80f)] public float spread = 32f;
    [Tooltip("Blood colour of this level's monster (one monster per level scene).")]
    public Color bloodColor = new Color(0.55f, 0.03f, 0.04f, 1f);
    public Color bloodColorDark = new Color(0.32f, 0.01f, 0.02f, 1f);
    public Color sparkColor = new Color(1f, 0.78f, 0.3f, 1f);
    public Color sparkColorHot = new Color(1f, 0.95f, 0.7f, 1f);

    [Header("Drips from the wound")]
    [Tooltip("A few drips keep running from the struck bone after a landed hit.")]
    public bool drips = true;
    [Tooltip("Seconds the wound keeps dripping.")]
    public float dripSeconds = 2.5f;
    [Tooltip("Seconds between drips from one wound.")]
    public float dripInterval = 0.25f;
    public Vector2 dripSize = new Vector2(0.035f, 0.055f);
    [Tooltip("Wounds dripping at once; the oldest is reused.")]
    [Range(1, 8)] public int maxWounds = 4;

    [Header("Death and clash")]
    [Tooltip("Bigger burst from the chest when the monster dies.")]
    public bool deathBurst = true;
    [Range(1, 80)] public int deathCount = 60;
    [Tooltip("Sparks where a player weapon clashes with the monster's weapon (no blood on a clash).")]
    public bool clashSparks = true;
    [Range(1, 30)] public int clashSparkCount = 14;

    [Header("Runtime (read-only)")]
    [SerializeField] private int bursts;
    [SerializeField] private int lastBurstSize;
    [SerializeField] private int pooledSystems;

    public int Bursts => bursts;
    public int LastBurstSize => lastBurstSize;
    public int PooledSystems => pooledSystems;
    public int DripsEmitted { get; private set; }
    public int DeathBursts { get; private set; }
    public int ClashBursts { get; private set; }
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
    private struct Wound { public Transform bone; public Vector3 local; public float until, next; public ParticleSystem ps; }
    private readonly Dictionary<MonsterController, Pooled> systems = new Dictionary<MonsterController, Pooled>();
    private readonly List<MonsterController> stale = new List<MonsterController>();
    private readonly Wound[] wounds = new Wound[8];
    private int nextWound;
    private ParticleSystem.EmitParams emit;
    private MonsterController lastMonster;
    private Pooled lastPooled;

    void OnEnable() { HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash; }
    void OnDisable() { HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash; }

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
        if (weak) count = Mathf.Max(count + weakPointBonus, weakPointMinimum);
        count = Mathf.Clamp(count, 1, maxBurst);
        float sizeScale = (weak || style == CombatUIController.DamageStyle.Full) ? bigHitSizeScale : 1f;

        bool blood = mode == Mode.Blood;
        SetMaterial(pool, blood);

        // Spray along the swing and out of the wound, with a little lift. The part of the swing that drives into the body is
        // dropped, otherwise a straight thrust would spray out of the monster's back where the player never sees it.
        Vector3 point = source.LastHitPoint;
        Vector3 outward = point - monster.transform.position; outward.y = 0f;
        outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -monster.transform.forward;
        Vector3 swing = source.CurrentVelocity;
        swing = swing.sqrMagnitude > 1e-4f ? swing.normalized : outward;
        Vector3 along = swing - outward * Mathf.Min(0f, Vector3.Dot(swing, outward));
        Vector3 dir = (along * 0.75f + outward * 0.4f + Vector3.up * 0.2f).normalized;

        Spray(pool.ps, point, dir, count, sizeScale, blood);
        bursts++;
        lastBurstSize = count;

        if (blood && drips && source.LastHitCollider != null) AddWound(pool.ps, source.LastHitCollider.transform, point);
    }

    void OnClash(HitDetection source, float speed)
    {
        if (mode == Mode.Off || !clashSparks || source == null) return;
        var monster = BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
        if (monster == null) return;
        var pool = PoolFor(monster);
        if (pool.ps == null) return;
        Vector3 v = source.CurrentVelocity;
        Vector3 dir = v.sqrMagnitude > 1e-4f ? (-v.normalized + Vector3.up * 0.4f).normalized : Vector3.up;
        Spray(pool.ps, source.LastClashPoint, dir, clashSparkCount, 0.8f, false);
        ClashBursts++;
    }

    /// <summary>
    /// A short celebratory burst (the result screen's NEW BEST / RANK UP): sparks from the active monster's pooled system, through the
    /// same reused EmitParams, so nothing is allocated. Returns false when there is no pooled system yet (no hit landed this fight).
    /// </summary>
    public bool Celebrate(Vector3 point, Vector3 dir, int count, Color a, Color b)
    {
        var monster = BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
        if (monster == null) return false;
        var pool = PoolFor(monster);
        if (pool.ps == null) return false;
        if (sparksMaterial != null && pool.renderer.sharedMaterial != sparksMaterial) pool.renderer.sharedMaterial = sparksMaterial;
        emit.position = point;
        for (int i = 0; i < count; i++)
        {
            Vector3 d = Vector3.Slerp(dir, Random.onUnitSphere, 0.55f);
            emit.velocity = d * Random.Range(1.2f, 2.6f);
            emit.startLifetime = Random.Range(0.45f, 0.9f);
            emit.startSize = Random.Range(size.x, size.y) * 0.7f;
            emit.startColor = Color.Lerp(a, b, Random.value);
            pool.ps.Emit(emit, 1);
        }
        return true;
    }

    void OnMonsterDied(MonsterController monster)
    {
        if (mode != Mode.Blood || !deathBurst || monster == null) return;
        var pool = PoolFor(monster);
        if (pool.ps == null) return;
        SetMaterial(pool, true);
        var def = monster.GetComponent<MonsterDefense>();
        Vector3 chest = def != null && def.guardTarget != null ? def.guardTarget.position : monster.transform.position + Vector3.up * 1.3f;
        Vector3 fwd = monster.transform.forward;
        Spray(pool.ps, chest, (fwd + Vector3.up * 0.3f).normalized, deathCount, 1.35f, true, 70f);
        DeathBursts++;
    }

    void Spray(ParticleSystem ps, Vector3 point, Vector3 dir, int count, float sizeScale, bool blood, float cone = -1f)
    {
        float sp = (cone > 0f ? cone : spread) / 180f;
        emit.position = point;
        for (int i = 0; i < count; i++)
        {
            Vector3 d = Vector3.Slerp(dir, Random.onUnitSphere, sp);
            emit.velocity = d * Random.Range(speed.x, speed.y) * (blood ? 1f : 1.4f);
            emit.startLifetime = blood ? Random.Range(lifetime.x, lifetime.y) : Random.Range(0.18f, 0.35f);
            emit.startSize = Random.Range(size.x, size.y) * sizeScale * (blood ? 1f : 0.55f);
            emit.startColor = blood ? Color.Lerp(bloodColorDark, bloodColor, Random.value) : Color.Lerp(sparkColor, sparkColorHot, Random.value);
            ps.Emit(emit, 1);
        }
    }

    void SetMaterial(Pooled pool, bool blood)
    {
        var mat = blood || mode == Mode.Blood ? bloodMaterial : sparksMaterial;
        if (mat != null && pool.renderer.sharedMaterial != mat) pool.renderer.sharedMaterial = mat;
    }

    void AddWound(ParticleSystem ps, Transform bone, Vector3 point)
    {
        int slots = Mathf.Clamp(maxWounds, 1, wounds.Length);
        // A hit on a bone that is already dripping refreshes that wound.
        int i = -1;
        for (int k = 0; k < slots; k++) if (wounds[k].bone == bone && Time.time < wounds[k].until) { i = k; break; }
        if (i < 0) { i = nextWound % slots; nextWound++; }
        wounds[i].bone = bone;
        wounds[i].local = bone.InverseTransformPoint(point);
        wounds[i].until = Time.time + dripSeconds;
        wounds[i].next = Time.time + dripInterval * 0.5f;
        wounds[i].ps = ps;
    }

    void Update()
    {
        if (!drips || mode != Mode.Blood) return;
        int slots = Mathf.Clamp(maxWounds, 1, wounds.Length);
        for (int i = 0; i < slots; i++)
        {
            ref var w = ref wounds[i];
            if (w.bone == null || w.ps == null || Time.time >= w.until || Time.time < w.next) continue;
            w.next = Time.time + dripInterval * Random.Range(0.7f, 1.3f);
            emit.position = w.bone.TransformPoint(w.local);
            emit.velocity = new Vector3(Random.Range(-0.05f, 0.05f), -0.15f, Random.Range(-0.05f, 0.05f));
            emit.startLifetime = Random.Range(0.6f, 0.9f);
            emit.startSize = Random.Range(dripSize.x, dripSize.y);
            emit.startColor = Color.Lerp(bloodColorDark, bloodColor, Random.value * 0.5f);
            w.ps.Emit(emit, 1);
            DripsEmitted++;
        }
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
            for (int i = 0; i < wounds.Length; i++) wounds[i].bone = null;
            p = Build(monster);
            systems[monster] = p;
            pooledSystems = systems.Count;
            monster.OnMonsterDeath += () => OnMonsterDied(monster); // once per monster, not per hit
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
        main.loop = true; // emission is off: the system only plays so Emit()ted particles keep simulating
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;
        main.gravityModifier = gravity / 9.81f;
        main.startLifetime = 0.7f;
        main.startSize = 0.05f;
        main.startSpeed = 0f;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 1.3f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.sharedMaterial = mode == Mode.Sparks ? sparksMaterial : bloodMaterial;
        ps.Play();
        return new Pooled { monster = monster, ps = ps, renderer = r };
    }
}
