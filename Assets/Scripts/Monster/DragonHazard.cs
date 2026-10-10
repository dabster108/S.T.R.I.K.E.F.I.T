// DragonHazard.cs
// Purpose: Level 3 aerial fire hazard. Dragons circle high above the arena and, one at a time, breathe a fire strip at a spot the
//          player is warned about first. It is a dodge-the-ground-marker mechanic like the boss slam; the dragons are never targets
//          (no colliders for weapons, nothing to hit) unless `hittable` is switched on, and then only on their own layer.
//          - Count per boss phase (dragonsByPhase = phase 0 / 1 / 2, default 0 / 1 / 2). New dragons fly in during the phase-shift
//            pause (BossPhaseController.PhaseShiftBegan) with a roar and settle into a circle >= circleHeight above the dock and
//            >= minHeadDistance from the player's head (clamped, never inside it).
//          - Fire event = marker (the ground zone is fixed when it appears and stays >= markerSeconds, with its own warning
//            sound) -> flame cone from the dragon's mouth to that zone for fireSeconds -> damage ticks every tickSeconds, each
//            tickDamageFraction of the player's max HP, ONLY if the player's head is inside the zone at that tick.
//            Flame particles within flameCullRadius of the head are removed (nothing is ever drawn through the face).
//          - Fairness: one event at a time; gap after an event >= gapSeconds (phase 1: 7 s, phase 2: 3.5 s, dragons alternate);
//            never started during a phase-shift pause, the entrance, or within mustDodgeGuardSeconds of a boss must-dodge (unblockable)
//            wind-up; and the boss may not start a must-dodge attack while a fire event (or its guard tail) is running
//            (MonsterAI.mustDodgeGate).
//          - Needs a dragon prefab (`dragonPrefab`, a skinned model within the budget: <= ~15k triangles, <= 2 materials, textures
//            <= 1024). Without one the system stays off (dragonsEnabled is false in the shipped scene); `allowStandIn` runs the whole
//            mechanic with an invisible emitter for tests only.
//          - Telemetry: DragonHazard.FireResolved -> SessionReporter "dragon_fire" (zone hit yes/no, damage) and the damage shows as
//            player_hurt with attack "Dragon fire".
// Dependencies: BossPhaseController, MonsterAI, PlayerHealth, BattleManager, DangerZoneIndicator (optional), OVRCameraRig
// Attach to: an empty "DragonDirector" object in the Level 3 scene (built by Editor/DragonSetup)

using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class DragonHazard : MonoBehaviour
{
    [Header("Master switch")]
    [Tooltip("Off in the shipped scene: no dragon model within the asset budget has been provided yet.")]
    public bool dragonsEnabled = false;
    [Tooltip("Skinned dragon model (<= ~15k triangles, <= 2 materials, textures <= 1024). Its Animator should loop an idle / flap clip.")]
    public GameObject dragonPrefab;
    [Tooltip("Test only: run the mechanic with an invisible emitter when there is no prefab.")]
    public bool allowStandIn = false;
    public float dragonScale = 1f;
    [Tooltip("Yaw (deg) that turns the model's own forward onto the direction of flight.")]
    public float modelYawOffset = 0f;
    [Tooltip("Optional child name of the model that the flame leaves from (the mouth). Missing = the model root.")]
    public string mouthChildName = "Mouth";
    [Tooltip("Dragons may be hit by weapons. Off = no colliders at all (the shipped behaviour).")]
    public bool hittable = false;

    [Header("Count per boss phase (0 / 1 / 2)")]
    public int[] dragonsByPhase = { 0, 1, 2 };

    [Header("Flight")]
    public float circleHeight = 9f;
    [Tooltip("Horizontal radius of the circle around the arena centre (the player's start position).")]
    public float circleRadius = 13f;
    [Tooltip("Never closer than this (m, 3-D) to the player's head.")]
    public float minHeadDistance = 6f;
    public float orbitSpeed = 4.5f;
    public float bobAmount = 0.6f;
    [Tooltip("Seconds a new dragon takes to arrive from far away (the phase-shift pause is 2.4 s).")]
    public float enterSeconds = 2.2f;
    public float enterFromRadius = 40f;
    public float enterFromHeight = 28f;

    [Header("Fire event")]
    [Tooltip("Ground marker is fixed when it appears and stays at least this long before any flame (>= 1.4).")]
    public float markerSeconds = 1.5f;
    public float fireSeconds = 1.6f;
    public float tickSeconds = 0.4f;
    [Tooltip("Seconds after the flame starts before the first tick (the flame has to arrive).")]
    public float firstTickDelay = 0.4f;
    [Tooltip("Share of the player's max HP per tick.")]
    public float tickDamageFraction = 0.015f;
    [Tooltip("Radius (m) of the zone: >= 1.5 gives the >= 3 m wide strip.")]
    public float zoneRadius = 1.6f;
    [Tooltip("Gap (s) after an event before the next may start, in phase 1 (one dragon) and phase 2 (dragons alternate).")]
    public float gapPhase1 = 7f;
    public float gapPhase2 = 3.5f;
    [Tooltip("No event within this long of a boss must-dodge wind-up (before or after).")]
    public float mustDodgeGuardSeconds = 2.5f;
    public float firstEventDelay = 3.5f;

    [Header("Flame")]
    [Range(16, 120)] public int fireParticleCount = 60;
    public float flameSpeed = 22f;
    public float flameConeAngle = 9f;
    public Color flameColor = new Color(1f, 0.45f, 0.1f, 0.85f);
    public Material flameMaterial;
    [Tooltip("Flame particles closer than this to the player's head are removed.")]
    public float flameCullRadius = 1.0f;
    public DangerZoneIndicator zoneIndicator;

    [Header("Audio (existing clips; wing and flame loops have no dedicated clip yet)")]
    public AudioClip roarClip; [Range(0f, 1f)] public float roarVolume = 0.9f; public float roarPitch = 0.8f;
    public AudioClip wingClip; [Range(0f, 1f)] public float wingVolume = 0.35f; public float wingPitch = 0.45f; public float wingPeriod = 2.6f;
    public AudioClip warningClip; [Range(0f, 1f)] public float warningVolume = 1f; public float warningPitch = 0.85f;
    public AudioClip flameClip; [Range(0f, 1f)] public float flameVolume = 0.5f; public float flamePitch = 0.8f;

    // ── static state read by MonsterAI / SessionReporter ──
    public static DragonHazard Instance { get; private set; }
    /// <summary>MonsterAI.mustDodgeGate: a fire event (marker, flame, or the guard tail after it) is running.</summary>
    public static bool MustDodgeBlocked => Instance != null && Instance.IsLockedForBoss;
    /// <summary>Set around PlayerHealth.TakeDamage so the telemetry names the source.</summary>
    public static string DamageSourceOverride;

    public struct FireReport
    {
        public int dragon, phase, ticks, ticksHit;
        public bool zoneHit;
        public float damage, markerSeconds, fireSeconds;
    }
    public static event Action<FireReport> FireResolved;

    // ── runtime ──
    public int ActiveDragons => dragons.Count;
    public bool EventRunning => state != State.Idle;
    public bool IsLockedForBoss => state != State.Idle || Time.time < lockedUntil;
    public int EventsStarted { get; private set; }
    public readonly List<string> EventLog = new List<string>();
    /// <summary>Verification: one record per event (only while LogEvents is on).</summary>
    public class EventRecord { public float marker, fire, end; public int dragon, phase; public Vector3 zone; public bool aborted; public int ticks, ticksHit; public float damage; }
    public readonly List<EventRecord> Records = new List<EventRecord>();
    public bool LogEvents;
    public int LiveFlameParticles { get; private set; }
    public float MinFlameDistanceToHead { get; private set; } = 99f;
    public int CulledParticles { get; private set; }
    public float MinDragonHeadDistance { get; private set; } = 99f;
    public float LastTickDamage { get; private set; }
    /// <summary>Verification: the zone centre of the running event (fixed when the marker appears).</summary>
    public Vector3 ZoneCentre => zoneCentre;
    public string StateName => state.ToString();
    public int FiringIndex => firing != null ? firing.index : -1;
    public ParticleSystem[] FlameSystems { get { var a = new ParticleSystem[dragons.Count]; for (int i = 0; i < a.Length; i++) a[i] = dragons[i].flame; return a; } }
    public Vector3[] DragonPositions { get { var a = new Vector3[dragons.Count]; for (int i = 0; i < a.Length; i++) a[i] = dragons[i].t.position; return a; } }

    enum State { Idle, Marker, Fire }

    class Dragon
    {
        public Transform t, mouth;
        public Animator animator;
        public ParticleSystem flame;
        public ParticleSystemRenderer flameRenderer;
        public AudioSource audio, loopAudio;
        public float angle, enterT, nextWing;
        public bool entering;
        public Vector3 enterFrom;
        public int index;
        public bool leaving; public float leaveT;
        public Vector3 leaveFrom;
        public Collider col;
    }

    readonly List<Dragon> dragons = new List<Dragon>(4);
    BossPhaseController boss;
    MonsterAI bossAi;
    MonsterController bossController;
    Transform head;
    Vector3 centre;
    State state;
    Dragon firing;
    int nextIndex, lastFirer = -1, spawned;
    float stateStart, nextFireAllowed, lockedUntil, lastMustDodgeEnd = -99f;
    bool mustDodgeActive;
    Vector3 zoneCentre;
    float tickDamageAccum; int ticksDone, ticksHit; float nextTick;
    bool battleSeen, warnedNoModel;
    ParticleSystem.Particle[] particleBuf;
    static readonly ProfilerMarker Marker = new ProfilerMarker("DragonHazard.Update");

    void Awake()
    {
        Instance = this;
        particleBuf = new ParticleSystem.Particle[Mathf.Max(16, fireParticleCount) + 8];
    }

    void OnEnable()
    {
        Instance = this;
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (bossAi != null && bossAi.mustDodgeGate == GateFunc) bossAi.mustDodgeGate = null;
        if (boss != null) { boss.PhaseShiftBegan -= OnPhaseShiftBegan; boss.PhaseShiftEnded -= OnPhaseShiftEnded; }
        StopFire(false);
        if (zoneIndicator != null && zoneIndicator.IsShowing) zoneIndicator.Hide();
    }

    void OnDestroy()
    {
        foreach (var d in dragons) if (d.t != null) Destroy(d.t.gameObject);
        dragons.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; FireResolved = null; DamageSourceOverride = null; }

    bool GateFunc() => MustDodgeBlocked;

    bool Usable()
    {
        if (!dragonsEnabled) return false;
        if (dragonPrefab != null) return true;
        if (allowStandIn) return true;
        if (!warnedNoModel) { warnedNoModel = true; Debug.LogWarning("[DragonHazard] enabled but no dragonPrefab (and no stand-in): staying off."); }
        return false;
    }

    // ───────────────────────── wiring (once, when the boss exists) ─────────────────────────
    bool Bind()
    {
        if (bossController != null) return true;
        var bm = BattleManager.Instance;
        var m = bm != null ? bm.ActiveMonster : null;
        if (m == null) return false;
        boss = m.GetComponent<BossPhaseController>();
        if (boss == null) return false;     // Levels 1 and 2 have no phases: never active there
        bossController = m;
        bossAi = m.GetComponent<MonsterAI>();
        boss.PhaseShiftBegan += OnPhaseShiftBegan;
        boss.PhaseShiftEnded += OnPhaseShiftEnded;
        if (bossAi != null) bossAi.mustDodgeGate = GateFunc;
        var rig = FindAnyObjectByType<OVRCameraRig>();
        head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
        centre = head != null ? new Vector3(head.position.x, 0f, head.position.z) : Vector3.zero;
        nextFireAllowed = Time.time + firstEventDelay;
        return true;
    }

    // ───────────────────────── phase hooks ─────────────────────────
    void OnPhaseShiftBegan(int phase)
    {
        if (!Usable()) return;
        int want = dragonsByPhase != null && phase >= 0 && phase < dragonsByPhase.Length ? dragonsByPhase[phase] : 0;
        while (dragons.Count < want) SpawnDragon();
        // a dragon is never in the middle of a fire event during the pause
        if (state != State.Idle) AbortForShift();
        nextFireAllowed = Mathf.Max(nextFireAllowed, Time.time + (boss != null ? boss.phaseShiftSeconds : 2.4f) + Gap);
    }

    void AbortForShift()
    {
        StopFire(true);
        nextFireAllowed = Mathf.Max(nextFireAllowed, Time.time + (boss != null ? boss.phaseShiftSeconds : 2.4f) + Gap);
    }

    void OnPhaseShiftEnded(int phase)
    {
        nextFireAllowed = Mathf.Max(nextFireAllowed, Time.time + 1.0f);
    }

    // ───────────────────────── spawn ─────────────────────────
    void SpawnDragon()
    {
        var d = new Dragon { index = spawned++ };
        GameObject go;
        if (dragonPrefab != null) go = Instantiate(dragonPrefab);
        else go = new GameObject("DragonStandIn");
        go.name = "Dragon_" + d.index;
        go.transform.localScale = Vector3.one * dragonScale;
        d.t = go.transform;
        d.animator = go.GetComponentInChildren<Animator>();
        d.mouth = go.transform;
        if (!string.IsNullOrEmpty(mouthChildName)) foreach (var c in go.GetComponentsInChildren<Transform>(true)) if (c.name == mouthChildName) { d.mouth = c; break; }
        // Weapons never touch a dragon: colliders only exist when `hittable` is on (then on Default; otherwise none at all).
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = hittable;
        d.audio = go.AddComponent<AudioSource>(); d.audio.playOnAwake = false; d.audio.spatialBlend = 1f; d.audio.minDistance = 8f; d.audio.maxDistance = 60f; d.audio.rolloffMode = AudioRolloffMode.Linear;
        d.loopAudio = go.AddComponent<AudioSource>(); d.loopAudio.playOnAwake = false; d.loopAudio.spatialBlend = 1f; d.loopAudio.minDistance = 8f; d.loopAudio.maxDistance = 60f; d.loopAudio.loop = true; d.loopAudio.rolloffMode = AudioRolloffMode.Linear;
        BuildFlame(d);
        // slot on the circle: spread evenly as the count grows, a new dragon takes the angle opposite the busiest
        d.angle = dragons.Count == 0 ? Mathf.PI * 0.5f : (Mathf.PI * 0.5f + dragons.Count * Mathf.PI * 2f / 3f);
        d.entering = true; d.enterT = 0f;
        d.enterFrom = OrbitPoint(d.angle, enterFromRadius, enterFromHeight);
        d.t.position = d.enterFrom;
        dragons.Add(d);
        if (roarClip != null) { d.audio.pitch = roarPitch; d.audio.PlayOneShot(roarClip, roarVolume); SfxTrace.Note(this, roarClip, "dragon roar (enters during the phase shift)"); }
        d.nextWing = Time.time + 0.5f;
    }

    void BuildFlame(Dragon d)
    {
        var go = new GameObject("DragonFlame");
        go.transform.SetParent(d.t, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true; main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = fireParticleCount;
        main.startLifetime = 0.9f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startColor = flameColor; main.gravityModifier = 0f;
        var em = ps.emission; em.enabled = false;                        // emitted by script along the aimed line (no allocation: Emit(count))
        var shape = ps.shape; shape.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.08f), 0.5f), new GradientColorKey(new Color(0.4f, 0.1f, 0.05f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.4f));
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = flameMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        d.flame = ps; d.flameRenderer = r;
    }

    Vector3 OrbitPoint(float angle, float radius, float height) => centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius + Vector3.up * height;

    // ───────────────────────── per frame ─────────────────────────
    void Update()
    {
        using (Marker.Auto())
        {
            if (!Usable()) { if (dragons.Count > 0) { Dismiss(); MoveAll(); } return; }
            if (!Bind()) return;
            bool battle = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive && bossController != null && bossController.isAlive;
            if (battle) battleSeen = true;
            if (!battle && battleSeen) Dismiss();     // idempotent: flames and marker stop, the dragons fly off

            TrackMustDodge();
            MoveAll();
            if (!battle) return;
            // The phase pause starts the moment HP crosses a threshold (the boss may still be finishing a swing): no marker or flame
            // may run through it, so a running event is cut here, not when the roar begins.
            if (state != State.Idle && boss != null && boss.InPhaseShift) AbortForShift();
            switch (state)
            {
                case State.Idle: TryStartEvent(); break;
                case State.Marker: UpdateMarker(); break;
                case State.Fire: UpdateFire(); break;
            }
        }
    }

    void MoveAll()
    {
        for (int i = 0; i < dragons.Count; i++) MoveDragon(dragons[i]);
        for (int i = dragons.Count - 1; i >= 0; i--)
            if (dragons[i].leaving && dragons[i].leaveT >= 1f) { Destroy(dragons[i].t.gameObject); dragons.RemoveAt(i); }
    }

    void LateUpdate()
    {
        if (state == State.Fire && firing != null) CullFlame(firing);
    }

    void TrackMustDodge()
    {
        bool now = bossAi != null && bossAi.CurrentAttack != null && bossAi.CurrentAttack.unblockable;
        if (mustDodgeActive && !now) lastMustDodgeEnd = Time.time;
        mustDodgeActive = now;
    }

    void MoveDragon(Dragon d)
    {
        float dt = Time.deltaTime;
        Vector3 pos;
        if (d.leaving)
        {
            d.leaveT = Mathf.Min(1f, d.leaveT + dt / 2f);
            pos = Vector3.Lerp(d.leaveFrom, OrbitPoint(d.angle, enterFromRadius, enterFromHeight), d.leaveT * d.leaveT);
        }
        else
        {
            d.angle += orbitSpeed / Mathf.Max(1f, circleRadius) * dt;
            Vector3 orbit = OrbitPoint(d.angle, circleRadius, circleHeight + Mathf.Sin(Time.time * 0.7f + d.index * 2f) * bobAmount);
            if (d.entering)
            {
                d.enterT += dt / Mathf.Max(0.3f, enterSeconds);
                float k = Mathf.Clamp01(d.enterT); k = k * k * (3f - 2f * k);
                pos = Vector3.Lerp(d.enterFrom, orbit, k);
                if (d.enterT >= 1f) d.entering = false;
            }
            else pos = orbit;
        }
        // never inside the head's personal space (clamped in 3-D)
        if (head != null)
        {
            Vector3 toD = pos - head.position;
            float dist = toD.magnitude;
            if (dist < minHeadDistance && dist > 0.001f) { pos = head.position + toD / dist * minHeadDistance; dist = minHeadDistance; }
            if (dist < MinDragonHeadDistance) MinDragonHeadDistance = dist;
        }
        Vector3 fwd = pos - d.t.position; fwd.y = 0f;
        d.t.position = pos;
        if (firing == d && state != State.Idle)
        {
            Vector3 toZone = zoneCentre - pos; toZone.y = 0f;
            if (toZone.sqrMagnitude > 0.01f) d.t.rotation = Quaternion.Slerp(d.t.rotation, Quaternion.LookRotation(toZone) * Quaternion.Euler(0f, modelYawOffset, 0f), 1f - Mathf.Exp(-6f * dt));
        }
        else if (fwd.sqrMagnitude > 1e-6f)
            d.t.rotation = Quaternion.Slerp(d.t.rotation, Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, modelYawOffset, 0f), 1f - Mathf.Exp(-4f * dt));
        // wing beats (no wing clip exists: a low, quiet whoosh on a slow period, only if a wing clip is assigned)
        if (wingClip != null && !d.leaving && Time.time >= d.nextWing && d.audio != null)
        {
            d.nextWing = Time.time + wingPeriod * UnityEngine.Random.Range(0.9f, 1.1f);
            d.audio.pitch = wingPitch; d.audio.PlayOneShot(wingClip, wingVolume);
        }
    }

    // ───────────────────────── fire events ─────────────────────────
    float Gap => boss != null && boss.Phase >= 2 ? gapPhase2 : gapPhase1;

    bool CanStart()
    {
        if (Time.time < nextFireAllowed) return false;
        if (boss == null || boss.InPhaseShift) return false;
        var entrance = bossController != null ? bossController.GetComponent<MonsterEntrance>() : null;
        if (entrance != null && !entrance.Complete) return false;
        if (mustDodgeActive) return false;
        if (Time.time - lastMustDodgeEnd < mustDodgeGuardSeconds) return false;
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) return false;
        return true;
    }

    void TryStartEvent()
    {
        if (dragons.Count == 0 || !CanStart()) return;
        // alternate between dragons; a dragon still flying in is skipped
        Dragon pick = null;
        for (int k = 0; k < dragons.Count; k++)
        {
            var d = dragons[(lastFirer + 1 + k + dragons.Count) % dragons.Count];
            if (!d.entering && !d.leaving) { pick = d; break; }
        }
        if (pick == null) return;
        lastFirer = dragons.IndexOf(pick);
        firing = pick;
        // the zone is fixed now, where the player's head is (horizontal), and does not follow the player afterwards
        Vector3 p = head != null ? head.position : Vector3.zero;
        zoneCentre = new Vector3(p.x, 0.02f, p.z);
        state = State.Marker; stateStart = Time.time; EventsStarted++;
        lockedUntil = Time.time + markerSeconds + fireSeconds + mustDodgeGuardSeconds;
        if (zoneIndicator != null) zoneIndicator.Show(zoneCentre, zoneRadius, markerSeconds);
        if (warningClip != null && firing.audio != null) { firing.audio.pitch = warningPitch; firing.audio.PlayOneShot(warningClip, warningVolume); SfxTrace.Note(this, warningClip, "dragon fire: ground marker warning"); }
        if (LogEvents) { Log($"marker dragon {firing.index} zone {zoneCentre.x:0.00},{zoneCentre.z:0.00} phase {boss.Phase}"); Records.Add(new EventRecord { marker = Time.time, dragon = firing.index, phase = boss.Phase, zone = zoneCentre }); }
    }

    void UpdateMarker()
    {
        if (Time.time - stateStart < markerSeconds) return;
        state = State.Fire; stateStart = Time.time;
        ticksDone = 0; ticksHit = 0; tickDamageAccum = 0f; nextTick = firstTickDelay;
        if (flameClip != null && firing.loopAudio != null) { firing.loopAudio.clip = flameClip; firing.loopAudio.pitch = flamePitch; firing.loopAudio.volume = flameVolume; firing.loopAudio.Play(); SfxTrace.Note(this, flameClip, "dragon fire: flame"); }
        firing.flame.Play();
        if (LogEvents) { Log($"fire dragon {firing.index}"); if (Records.Count > 0) Records[Records.Count - 1].fire = Time.time; }
    }

    void UpdateFire()
    {
        float t = Time.time - stateStart;
        // emit along the mouth -> zone line
        if (t < fireSeconds) EmitFlame(firing);
        // damage ticks: only the player's head inside the zone at that tick
        while (ticksDone < Mathf.FloorToInt(fireSeconds / tickSeconds + 0.001f) && t >= nextTick)
        {
            ticksDone++; nextTick += tickSeconds;
            bool inside = head != null && HorizontalDistance(head.position, zoneCentre) <= zoneRadius;
            if (inside && PlayerHealth.Instance != null && !PlayerHealth.Instance.IsDead)
            {
                float dmg = PlayerHealth.Instance.maxHealth * tickDamageFraction;
                float before = PlayerHealth.Instance.currentHealth;
                DamageSourceOverride = "Dragon fire";
                PlayerHealth.Instance.TakeDamage(dmg, true);
                DamageSourceOverride = null;
                float real = before - PlayerHealth.Instance.currentHealth;
                tickDamageAccum += Mathf.Max(0f, real); ticksHit++; LastTickDamage = real;
            }
            if (LogEvents) Log($"tick {ticksDone} inside {inside}");
        }
        if (t >= fireSeconds + 0.1f) EndEvent();
    }

    static float HorizontalDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

    void EmitFlame(Dragon d)
    {
        if (d.flame == null) return;
        // ~ fireParticleCount live at once: emit rate = count / lifetime, spread by frame time
        float rate = d.flame.main.maxParticles / 0.9f;
        int n = Mathf.Max(1, Mathf.RoundToInt(rate * Time.deltaTime));
        Vector3 from = d.mouth.position;
        Vector3 to = zoneCentre;
        Vector3 axis = (to - from);
        float dist = axis.magnitude; if (dist < 0.1f) return;
        axis /= dist;
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), axis) * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, flameConeAngle * 0.5f), Vector3.Cross(axis, Vector3.up).normalized) * axis;
            ep.position = from; ep.velocity = dir * flameSpeed; ep.startLifetime = Mathf.Clamp(dist / flameSpeed, 0.3f, 1.2f) * UnityEngine.Random.Range(0.95f, 1.1f);
            ep.startSize = UnityEngine.Random.Range(0.5f, 0.9f); ep.startColor = flameColor;
            d.flame.Emit(ep, 1);
        }
    }

    /// <summary>Remove any flame particle within flameCullRadius of the head (nothing is drawn through the face). No allocation.</summary>
    void CullFlame(Dragon d)
    {
        if (d.flame == null || head == null) return;
        int n = d.flame.GetParticles(particleBuf);
        LiveFlameParticles = n;
        bool changed = false;
        float dt = Time.deltaTime; float minD = MinFlameDistanceToHead;
        Vector3 h = head.position;
        for (int i = 0; i < n; i++)
        {
            // where the particle will be drawn this frame (it moves after LateUpdate), and how big it is
            Vector3 next = particleBuf[i].position + particleBuf[i].velocity * dt * 1.5f;
            float size = particleBuf[i].GetCurrentSize(d.flame);
            float dist = (next - h).magnitude;
            if (dist < flameCullRadius + 0.5f * size) { particleBuf[i].remainingLifetime = -1f; changed = true; CulledParticles++; }
            else if (dist - 0.5f * size < minD) minD = dist - 0.5f * size;
        }
        MinFlameDistanceToHead = minD;
        if (changed) d.flame.SetParticles(particleBuf, n);
    }

    void EndEvent()
    {
        bool hit = ticksHit > 0;
        var report = new FireReport { dragon = firing != null ? firing.index : -1, phase = boss != null ? boss.Phase : 0, ticks = ticksDone, ticksHit = ticksHit, zoneHit = hit, damage = tickDamageAccum, markerSeconds = markerSeconds, fireSeconds = fireSeconds };
        if (LogEvents) { Log($"end hit {hit} damage {tickDamageAccum:0.0} ticks {ticksHit}/{ticksDone}"); if (Records.Count > 0) { var rr = Records[Records.Count - 1]; rr.end = Time.time; rr.ticks = ticksDone; rr.ticksHit = ticksHit; rr.damage = tickDamageAccum; } }
        StopFire(false);
        nextFireAllowed = Time.time + Gap;
        lockedUntil = Time.time + mustDodgeGuardSeconds;
        FireResolved?.Invoke(report);
    }

    /// <summary>Stop the flame and hide the marker. abort = true when the event is cut short (phase shift, battle end): no report.</summary>
    void StopFire(bool abort)
    {
        if (firing != null)
        {
            if (firing.flame != null) firing.flame.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (firing.loopAudio != null) firing.loopAudio.Stop();
        }
        if (zoneIndicator != null && zoneIndicator.IsShowing) { if (state == State.Fire && !abort) zoneIndicator.Impact(); else zoneIndicator.Hide(); }
        if (state != State.Idle && abort) { lockedUntil = Time.time + 0.1f; if (LogEvents) { Log("aborted"); if (Records.Count > 0) { var rr = Records[Records.Count - 1]; rr.aborted = true; rr.end = Time.time; } } }
        state = State.Idle; firing = null;
    }

    /// <summary>Battle over (boss dead, player dead, scene leaving): flames and marker stop at once, the dragons fly off.</summary>
    void Dismiss()
    {
        if (state != State.Idle) StopFire(true);
        foreach (var d in dragons)
        {
            if (d.leaving) continue;
            d.leaving = true; d.leaveT = 0f; d.leaveFrom = d.t.position;
        }
    }

    void Log(string s)
    {
        if (LogEvents) EventLog.Add($"{Time.time:0.00} {s}");
    }
}
