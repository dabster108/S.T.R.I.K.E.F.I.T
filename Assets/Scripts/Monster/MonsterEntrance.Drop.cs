// MonsterEntrance.Drop.cs
// Purpose: The Level 2 entrance style (DropFromAbove), the other half of MonsterEntrance. A vampire dropping in from the dark:
//          1. the arena's torches dim (slow flicker, under 2 Hz, no new lights) and a low violet mist gathers where it will land;
//          2. two violet eyes glow up in the dark above that spot (the body's own emission map, tinted) and a low growl plays;
//          3. it drops (the Mutant Jump clip from its apex + an extra eased fall of the root) and lands at the stand-off
//             distance: dust and a ground-ring burst thrown away from the player, a heavy impact, a rumble on both hands, a short
//             positional tremor of the play space (Off / Light / Strong), a little dust falling from above and one brief dip of the
//             torches;
//          4. it crouches and rises (the same clip), then roars with a 2 s violet glow-up: body emission ramps, the axe fire
//             ignites from the hand up the haft, an eye flare at the end;
//          5. the torches come back up; only then does the fight start.
//          Until Complete the monster cannot attack or be damaged (colliders off, MonsterAI off, HitDetection's entrance gate).
//          A Retry (GameStateManager.ReloadCurrentLevel on this scene) plays a 1.5 s version: drop, land, quick glow, fight.
//          VR comfort: the camera is never rotated or faded. The only motion is the landing tremor: positional only (no roll,
//          pitch or yaw), 6-8 Hz, decaying over 0.5 s, 1 cm Light / 2.5 cm Strong, applied to the tracking space while combat is
//          still locked (so the controllers' own movement cannot read as a swing); the world-space HUD canvas is moved with it so
//          the HUD stays steady in view.
// Dependencies: MonsterEntrance (same class), MonsterAnimator, CombatFeedback (haptics, optional), GameStateManager (retry flag)
// Attach to: monster root (same MonsterEntrance component, style = DropFromAbove)

using System.Collections;
using UnityEngine;

public partial class MonsterEntrance
{
    public enum EntranceStyle { RiseFromCircle = 0, DropFromAbove = 1 }

    [Header("Style (Level 3 = RiseFromCircle, Level 2 = DropFromAbove)")]
    public EntranceStyle style = EntranceStyle.RiseFromCircle;

    [Header("Drop from above: animator")]
    public string dropTrigger = "EntranceDrop";
    [Tooltip("Clip of the drop state (the jump clip; the Idle -> EntranceDrop transition's offset starts it at its apex).")]
    public string dropStateClip = "MutantJump_HeightBaked";
    [Tooltip("Clip time (s) of the jump's apex, where the drop state starts and is held while the eyes glow.")]
    public float dropApexTime = 1.0f;
    [Tooltip("Clip time (s) the feet land.")]
    public float dropLandTime = 1.6f;
    [Tooltip("Clip time (s) the crouch has risen back to standing.")]
    public float dropRiseEndTime = 2.9f;
    [Tooltip("Extra height (m) of the root above the clip's own apex, eased out during the fall (gravity curve).")]
    public float dropExtraHeight = 1.0f;
    public string entranceRoarTrigger = "EntranceRoar";
    public string entranceRoarStateClip = "L3_Roar";

    [Header("Drop from above: placement")]
    [Tooltip("Land this far (horizontal m) from the player's head on the spawn line; 0 = the spawn point.")]
    public float landAtDistance = 1.85f;
    [Tooltip("Never land closer than this (m): outside the personal-space clamp and the player's reach.")]
    public float minLandDistance = 1.5f;

    [Header("Drop from above: timeline (s)")]
    public float torchDimSeconds = 1.2f;
    [Range(0f, 1f)] public float torchDimTo = 0.3f;
    [Tooltip("Torch flicker while dimmed (Hz). Kept under 2 Hz.")]
    [Range(0f, 2f)] public float torchFlickerHz = 1.2f;
    [Range(0f, 0.5f)] public float torchFlickerDepth = 0.12f;
    [Tooltip("Eyes glow in the dark this long before the drop.")]
    public float eyesSeconds = 1.6f;
    public float glowUpSeconds = 2.0f;
    public float torchRestoreSeconds = 1.0f;
    [Tooltip("Length of the shortened entrance played on a Retry.")]
    public float retrySeconds = 1.5f;
    [Tooltip("Play the full entrance every time (off = the 1.5 s version after a Retry).")]
    public bool alwaysFull = false;

    [Header("Drop from above: emission (the body's own emission map, eyes brightest)")]
    public Renderer emissionRenderer;
    public Color eyeColor = new Color(0.6f, 0.25f, 1f);
    public float eyeIntensity = 1.6f;
    public float glowIntensity = 2.6f;
    public float eyeFlareIntensity = 5f;
    public float eyeFlareSeconds = 0.35f;
    [Tooltip("Weapon fire that ignites during the glow-up (from the hand to the blade).")]
    public ParticleSystem weaponFire;
    [Tooltip("Hand the weapon fire starts at.")]
    public Transform weaponHand;
    public float igniteSeconds = 0.8f;

    [Header("Drop from above: particles (one system, one material: mist, dust, ring)")]
    public Material fxMaterial;
    public Color mistColor = new Color(0.45f, 0.2f, 0.7f, 0.35f);
    [Range(0, 40)] public int mistParticles = 24;
    public Color dustColor = new Color(0.42f, 0.38f, 0.45f, 0.55f);
    [Range(0, 40)] public int dustParticles = 16;
    public Color ringColor = new Color(0.7f, 0.35f, 1f, 0.8f);
    [Range(0, 48)] public int ringParticles = 24;

    [Header("Drop from above: audio and haptics")]
    public AudioClip growlClip;
    [Range(0f, 1f)] public float growlVolume = 0.6f;
    public AudioClip landClip;
    [Range(0f, 1f)] public float landVolume = 0.7f;
    public AudioClip entranceRoarClip;
    [Range(0f, 1f)] public float entranceRoarVolume = 0.7f;
    [Tooltip("Impact pulse on both hands at the landing (amplitude, seconds).")]
    public Vector2 landHaptic = new Vector2(0.5f, 0.12f);
    [Tooltip("Low rumble that follows the impact pulse, matching the tremor (amplitude, seconds).")]
    public Vector2 landRumbleTail = new Vector2(0.22f, 0.4f);
    [Range(0f, 1f)] [Tooltip("OVR vibration frequency of the landing rumble (low = deeper).")]
    public float rumbleFrequency = 0.25f;

    public enum TremorStrength { Off = 0, Light = 1, Strong = 2 }
    [Header("Drop from above: landing tremor (positional only, tracking space, while combat is locked)")]
    [Tooltip("Off / Light (peak 1 cm) / Strong (peak 2.5 cm). Positional only, never rotation.")]
    public TremorStrength tremor = TremorStrength.Light;
    public float tremorLightMeters = 0.010f;
    public float tremorStrongMeters = 0.025f;
    [Range(6f, 8f)] public float tremorHz = 7f;
    public float tremorSeconds = 0.5f;
    [Tooltip("Move the world-space HUD canvas with the tracking space so it does not appear to shake.")]
    public bool tremorKeepsHudSteady = true;

    [Header("Drop from above: landing dust from above and one torch dip")]
    [Range(0, 20)] public int fallingDust = 12;
    [Tooltip("One smooth dip of the torches at the landing (a single event, well under 2 per second).")]
    public float torchDipSeconds = 0.5f;
    [Range(0f, 0.8f)] public float torchDipDepth = 0.45f;
    [Tooltip("Dim the arena's flame particles together with the torch lights.")]
    public bool dimFlames = true;

    // ── runtime (verification) ──
    public bool ShortVersion { get; private set; }
    public float LandDistance { get; private set; }
    /// <summary>Seconds from spawn to each step: torches dimmed, eyes lit, drop start, land, risen, roar, torches back, complete.</summary>
    public readonly float[] StepTimes = new float[8];
    public static readonly string[] StepNames = { "torches dimmed", "eyes lit", "drop", "land", "risen", "roar + glow-up", "torches back", "fight starts" };
    public int LiveFxParticles => fx != null ? fx.particleCount : 0;

    /// <summary>Largest tremor offset (m) applied to the tracking space this entrance (verification).</summary>
    public float TremorPeakApplied { get; private set; }
    public bool TremorActive { get; private set; }
    public Vector3 TremorOffset { get; private set; }

    private ParticleSystem fx;
    private ParticleSystem.EmitParams emit;
    private Light[] torches;
    private float[] torchBase;
    private ParticleSystemRenderer[] flames;
    private Color[] flameBase;
    private MaterialPropertyBlock flameMpb;
    private float dipStart = -99f;
    private Transform tremorSpace; private Vector3 tremorSpaceBase;
    private Transform[] tremorHud; private Vector3[] tremorHudBase;
    private MaterialPropertyBlock mpb;
    private Color baseEmission;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void AwakeDrop()
    {
        if (ai != null) ai.enabled = false;
        mpb = new MaterialPropertyBlock();
        if (emissionRenderer != null)
        {
            var m = emissionRenderer.sharedMaterial;
            baseEmission = m != null && m.HasProperty(EmissionId) && m.IsKeywordEnabled("_EMISSION") ? m.GetColor(EmissionId) : Color.black;
            SetEmission(Color.black);   // dark until the eyes open
        }
        // Up in the dark from the very first frame.
        transform.position += Vector3.up * dropExtraHeight;
    }

    IEnumerator DropSequence()
    {
        inProgress = true;
        float t0 = Time.time;
        colliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in colliders) if (c != null) c.enabled = false;
        ShortVersion = !alwaysFull && GameStateManager.ConsumeRetry(gameObject.scene.name);

        // Landing spot: on the spawn line at the stand-off distance (never inside the personal-space clamp).
        Vector3 ground = transform.position - Vector3.up * dropExtraHeight;
        // MonsterAI is off (its Start has not run), so the head comes from the rig: once, at the start of the entrance.
        var rig = FindAnyObjectByType<OVRCameraRig>();
        Transform head = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
        if (head != null && landAtDistance > 0f)
        {
            Vector3 flat = ground - head.position; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
            {
                float d = Mathf.Max(minLandDistance, landAtDistance);
                Vector3 p = head.position + flat.normalized * d; p.y = ground.y;
                ground = p;
            }
        }
        transform.position = ground + Vector3.up * dropExtraHeight;
        if (head != null) { Vector3 f = head.position - ground; f.y = 0f; LandDistance = f.magnitude; }

        BuildFx();
        var animator = anim != null ? anim.animator : GetComponentInChildren<Animator>();
        if (anim != null) anim.PlayNamedTrigger(dropTrigger);    // Idle -> EntranceDrop (the transition's offset starts it at the apex)
        yield return null;                                       // let the transition happen at normal speed
        if (animator != null && !ShortVersion) animator.speed = 0f;   // held at the apex while the eyes glow

        if (!ShortVersion)
        {
            // 1. Torches dim, mist gathers.
            FindTorches();
            EmitMist(ground);
            float t = 0f;
            while (t < torchDimSeconds) { t += Time.deltaTime; SetTorches(Mathf.Lerp(1f, torchDimTo, t / torchDimSeconds), false); yield return null; }
            StepTimes[0] = Time.time - t0;
            // 2. Eyes glow in the dark, low growl.
            if (growlClip != null && audioSource != null) { audioSource.PlayOneShot(growlClip, growlVolume); SfxTrace.Note(this, growlClip, "entrance: growl"); }
            StepTimes[1] = Time.time - t0;
            t = 0f;
            while (t < eyesSeconds) { t += Time.deltaTime; SetEmission(eyeColor * (eyeIntensity * Mathf.Clamp01(t / 0.4f))); SetTorches(torchDimTo, true); yield return null; }
        }
        else
        {
            StepTimes[0] = StepTimes[1] = 0f;
            SetEmission(eyeColor * eyeIntensity);
        }

        // 3. Drop: the clip falls from its apex to the landing frame; the root's extra height eases out on the same timing.
        StepTimes[2] = Time.time - t0;
        float fall = Mathf.Max(0.1f, dropLandTime - dropApexTime);
        if (animator != null) animator.speed = 1f;
        float ft = 0f;
        while (ft < fall)
        {
            ft += Time.deltaTime;
            float k = Mathf.Clamp01(ft / fall);
            transform.position = ground + Vector3.up * (dropExtraHeight * (1f - k * k));   // gravity: slow at the top, fast at the bottom
            if (!ShortVersion) SetTorches(torchDimTo, true);
            yield return null;
        }
        transform.position = ground;

        // Landing: dust + ground ring away from the player, heavy impact, light pulse on both hands.
        StepTimes[3] = Time.time - t0;
        EmitLanding(ground, head);
        if (landClip != null && audioSource != null) { audioSource.PlayOneShot(landClip, landVolume); SfxTrace.Note(this, landClip, "entrance: landing"); }
        var fb = FindAnyObjectByType<CombatFeedback>();
        if (fb != null) fb.PulseBoth(landHaptic.x, landHaptic.y, landRumbleTail.x, landRumbleTail.y, rumbleFrequency, "entrance landing rumble");
        if (!ShortVersion) dipStart = Time.time;
        if (rig != null && tremor != TremorStrength.Off) StartCoroutine(TremorRoutine(rig));

        if (ShortVersion)
        {
            // 1.5 s total: crouch-rise at speed, quick glow, straight into the fight.
            float rest = Mathf.Max(0.3f, retrySeconds - (Time.time - t0));
            if (animator != null) animator.speed = Mathf.Max(1f, (dropRiseEndTime - dropLandTime) / (rest * 0.8f));
            float g = 0f;
            while (g < rest)
            {
                g += Time.deltaTime;
                float k = Mathf.Clamp01(g / rest);
                SetEmission(eyeColor * Mathf.Lerp(eyeIntensity, glowIntensity, Mathf.Sin(k * Mathf.PI)));
                yield return null;
            }
            if (animator != null) animator.speed = 1f;
            if (anim != null) anim.PlayState(MonsterAnimState.Idle);   // EntranceDrop -> Idle
            StepTimes[4] = StepTimes[5] = StepTimes[6] = Time.time - t0;
        }
        else
        {
            // 4. Crouch and rise (the same clip), torches still low.
            float rise = Mathf.Max(0.1f, dropRiseEndTime - dropLandTime);
            float rt = 0f;
            while (rt < rise) { rt += Time.deltaTime; SetTorches(torchDimTo, true); yield return null; }
            StepTimes[4] = Time.time - t0;

            // 5. Roar + 2 s violet glow-up: body emission ramps, the weapon fire ignites from the hand up, eye flare at the end.
            if (anim != null) anim.PlayNamedTrigger(entranceRoarTrigger);   // EntranceDrop -> EntranceRoar
            if (entranceRoarClip != null && audioSource != null) { audioSource.PlayOneShot(entranceRoarClip, entranceRoarVolume); SfxTrace.Note(this, entranceRoarClip, "entrance: roar"); }
            StepTimes[5] = Time.time - t0;
            Vector3 fireHome = weaponFire != null ? weaponFire.transform.localPosition : Vector3.zero;
            Transform fireParent = weaponFire != null ? weaponFire.transform.parent : null;
            if (weaponFire != null) weaponFire.Play(true);
            float roarLen = Mathf.Max(glowUpSeconds, ClipLength(entranceRoarStateClip, glowUpSeconds));
            float gt = 0f;
            while (gt < roarLen)
            {
                gt += Time.deltaTime;
                float k = Mathf.Clamp01(gt / glowUpSeconds);
                float intensity = Mathf.Lerp(eyeIntensity, glowIntensity, Mathf.SmoothStep(0f, 1f, k));
                float flareStart = glowUpSeconds - eyeFlareSeconds;
                if (gt > flareStart && gt < glowUpSeconds + eyeFlareSeconds)
                    intensity = Mathf.Max(intensity, Mathf.Lerp(glowIntensity, eyeFlareIntensity, 1f - Mathf.Abs(gt - glowUpSeconds) / eyeFlareSeconds));
                SetEmission(eyeColor * intensity);
                if (weaponFire != null && weaponHand != null && fireParent != null)
                {
                    float ik = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(gt / igniteSeconds));
                    weaponFire.transform.position = Vector3.Lerp(weaponHand.position, fireParent.TransformPoint(fireHome), ik);
                }
                SetTorches(torchDimTo, true);
                yield return null;
            }
            if (weaponFire != null) { weaponFire.Stop(true, ParticleSystemStopBehavior.StopEmitting); weaponFire.transform.localPosition = fireHome; }

            // 6. Torches come back up while the glow settles; the fight starts after.
            float bt = 0f;
            while (bt < torchRestoreSeconds)
            {
                bt += Time.deltaTime;
                float k = Mathf.Clamp01(bt / torchRestoreSeconds);
                SetTorches(Mathf.Lerp(torchDimTo, 1f, k), false);
                SetEmission(Color.Lerp(eyeColor * glowIntensity, baseEmission, k));
                yield return null;
            }
            SetTorches(1f, false);
            StepTimes[6] = Time.time - t0;
        }

        // Back to the material's own emission (MonsterFireAura's body pulse adds on top from here on).
        RestoreEmission();
        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (ai != null) ai.enabled = true;
        inProgress = false;
        complete = true;
        StepTimes[7] = Time.time - t0;
        EntranceComplete?.Invoke();
        if (fx != null) Destroy(fx.gameObject, 3f);   // the last dust settles, then the system goes
    }

    // ───────────────────────── landing tremor ─────────────────────────
    float TremorPeakMeters => tremor == TremorStrength.Strong ? Mathf.Min(0.03f, tremorStrongMeters) : (tremor == TremorStrength.Light ? Mathf.Min(0.03f, tremorLightMeters) : 0f);

    /// <summary>Positional shake of the tracking space (head, hands and the weapons ride with it), 6-8 Hz, squared decay. Runs only
    /// while the entrance is in progress (combat locked) and always ends exactly where it started.</summary>
    IEnumerator TremorRoutine(OVRCameraRig rig)
    {
        float peak = TremorPeakMeters;
        if (peak <= 0f || rig == null || rig.trackingSpace == null || tremorSeconds <= 0f) yield break;
        tremorSpace = rig.trackingSpace; tremorSpaceBase = tremorSpace.position;
        tremorHud = null; tremorHudBase = null;
        if (tremorKeepsHudSteady)
        {
            // Once, at the landing: world-space HUD canvases that are not part of the rig stay put in view by moving with it.
            int n = 0;
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
            foreach (var c in canvases) if (c.isRootCanvas && c.renderMode == RenderMode.WorldSpace && !c.transform.IsChildOf(rig.transform)) n++;
            tremorHud = new Transform[n]; tremorHudBase = new Vector3[n]; n = 0;
            foreach (var c in canvases)
                if (c.isRootCanvas && c.renderMode == RenderMode.WorldSpace && !c.transform.IsChildOf(rig.transform)) { tremorHud[n] = c.transform; tremorHudBase[n] = c.transform.position; n++; }
        }
        TremorActive = true; TremorPeakApplied = 0f;
        float t = 0f, w = 2f * Mathf.PI * tremorHz;
        while (t < tremorSeconds && inProgress)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - t / tremorSeconds);
            float env = peak * k * k;
            // mostly vertical, a little sideways (a different phase), never rotation
            Vector3 off = Vector3.up * (env * Mathf.Sin(w * t)) + tremorSpace.right * (env * 0.3f * Mathf.Sin(w * 0.87f * t + 1.1f));
            ApplyTremor(off);
            yield return null;
        }
        RestoreTremor();
    }

    void ApplyTremor(Vector3 off)
    {
        TremorOffset = off;
        TremorPeakApplied = Mathf.Max(TremorPeakApplied, off.magnitude);
        if (tremorSpace != null) tremorSpace.position = tremorSpaceBase + off;
        if (tremorHud != null) for (int i = 0; i < tremorHud.Length; i++) if (tremorHud[i] != null) tremorHud[i].position = tremorHudBase[i] + off;
    }

    void RestoreTremor()
    {
        if (tremorSpace != null) tremorSpace.position = tremorSpaceBase;
        if (tremorHud != null) for (int i = 0; i < tremorHud.Length; i++) if (tremorHud[i] != null) tremorHud[i].position = tremorHudBase[i];
        TremorOffset = Vector3.zero; TremorActive = false;
    }

    // ───────────────────────── torches ─────────────────────────
    void FindTorches()
    {
        // Once, at the start of the entrance (never per frame): the arena's point / spot lights that are on.
        var all = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        int n = 0;
        foreach (var l in all) if (l.enabled && l.intensity > 0f && (l.type == LightType.Point || l.type == LightType.Spot)) n++;
        torches = new Light[n]; torchBase = new float[n];
        n = 0;
        foreach (var l in all)
            if (l.enabled && l.intensity > 0f && (l.type == LightType.Point || l.type == LightType.Spot)) { torches[n] = l; torchBase[n] = l.intensity; n++; }
        // The arena's flame particles (not the monster's own fire) dim with the lights: their material's base colour x level.
        flames = null;
        if (dimFlames)
        {
            var prs = FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Exclude);
            int f = 0;
            foreach (var r in prs) if (IsArenaFlame(r)) f++;
            flames = new ParticleSystemRenderer[f]; flameBase = new Color[f]; f = 0;
            foreach (var r in prs)
                if (IsArenaFlame(r)) { flames[f] = r; var m = r.sharedMaterial; flameBase[f] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white; f++; }
            flameMpb = new MaterialPropertyBlock();
        }
    }

    bool IsArenaFlame(ParticleSystemRenderer r)
    {
        if (r == null || r.sharedMaterial == null || r.transform.IsChildOf(transform)) return false;
        return r.sharedMaterial.name.IndexOf("fire", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.sharedMaterial.HasProperty("_BaseColor");
    }

    /// <summary>1 outside the landing dip; one smooth dip (a single event) of torchDipDepth over torchDipSeconds after the landing.</summary>
    float DipFactor()
    {
        float k = (Time.time - dipStart) / Mathf.Max(0.05f, torchDipSeconds);
        if (k < 0f || k > 1f) return 1f;
        float s = Mathf.Sin(k * Mathf.PI);
        return 1f - torchDipDepth * s * s;
    }

    void SetTorches(float level, bool flicker)
    {
        if (torches == null) return;
        float dip = DipFactor();
        for (int i = 0; i < torches.Length; i++)
        {
            if (torches[i] == null) continue;
            // Slow flicker (under 2 Hz), each torch out of phase.
            float f = flicker && torchFlickerHz > 0f ? 1f + torchFlickerDepth * Mathf.Sin((Time.time * torchFlickerHz + i * 0.37f) * 2f * Mathf.PI) : 1f;
            torches[i].intensity = torchBase[i] * level * f * dip;
        }
        if (flames != null)
        {
            bool reset = level >= 0.999f && !flicker && dip >= 0.999f;
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null) continue;
                if (reset) { flames[i].SetPropertyBlock(null); continue; }
                float f = flicker && torchFlickerHz > 0f ? 1f + torchFlickerDepth * Mathf.Sin((Time.time * torchFlickerHz + i * 0.37f) * 2f * Mathf.PI) : 1f;
                float lv = Mathf.Clamp01(level * f * dip);
                flames[i].GetPropertyBlock(flameMpb);
                flameMpb.SetColor("_BaseColor", new Color(flameBase[i].r * lv, flameBase[i].g * lv, flameBase[i].b * lv, flameBase[i].a));
                flames[i].SetPropertyBlock(flameMpb);
            }
        }
    }

    void OnDisable()
    {
        // Never leave the arena dark (scene unload or a destroyed monster mid-entrance).
        if (style == EntranceStyle.DropFromAbove && !complete) { dipStart = -99f; SetTorches(1f, false); }
        if (style == EntranceStyle.DropFromAbove) RestoreTremor();
    }

    // ───────────────────────── emission ─────────────────────────
    void SetEmission(Color c)
    {
        if (emissionRenderer == null) return;
        emissionRenderer.GetPropertyBlock(mpb);   // read-modify-write: tints (_BaseColor) stay untouched
        mpb.SetColor(EmissionId, c);
        emissionRenderer.SetPropertyBlock(mpb);
    }

    void RestoreEmission()
    {
        if (emissionRenderer == null) return;
        SetEmission(baseEmission);
    }

    // ───────────────────────── particles ─────────────────────────
    void BuildFx()
    {
        if (fxMaterial == null || fx != null) return;
        var go = new GameObject("EntranceDropFX");
        go.transform.position = transform.position;
        fx = go.AddComponent<ParticleSystem>();
        fx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = fx.main;
        main.playOnAwake = false; main.loop = false; main.duration = 10f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = mistParticles + dustParticles + ringParticles + fallingDust + 8;
        main.startSpeed = 0f; main.gravityModifier = 0f;
        var em = fx.emission; em.enabled = false;
        var shape = fx.shape; shape.enabled = false;
        var col = fx.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var size = fx.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.3f));
        var r = fx.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = fxMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        fx.Play();
    }

    void EmitMist(Vector3 ground)
    {
        if (fx == null) return;
        float life = torchDimSeconds + eyesSeconds + (dropLandTime - dropApexTime) + 0.8f;
        for (int i = 0; i < mistParticles; i++)
        {
            Vector2 d = Random.insideUnitCircle * 1.0f;
            emit.position = ground + new Vector3(d.x, Random.Range(0.04f, 0.22f), d.y);
            emit.velocity = new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0f, 0.03f), Random.Range(-0.05f, 0.05f));
            emit.startLifetime = life * Random.Range(0.85f, 1.05f);
            emit.startSize = Random.Range(0.6f, 1.1f);
            emit.startColor = mistColor;
            emit.rotation = Random.Range(0f, 360f);
            fx.Emit(emit, 1);
        }
    }

    void EmitLanding(Vector3 ground, Transform head)
    {
        if (fx == null) return;
        Vector3 toPlayer = head != null ? head.position - ground : -transform.forward;
        toPlayer.y = 0f; toPlayer = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : -transform.forward;
        // Dust: thrown up and out, only into the half that faces away from the player.
        for (int i = 0; i < dustParticles; i++)
        {
            Vector3 dir = AwayFromPlayer(toPlayer, 200f);
            emit.position = ground + dir * Random.Range(0.1f, 0.35f) + Vector3.up * 0.05f;
            emit.velocity = dir * Random.Range(0.6f, 1.4f) + Vector3.up * Random.Range(0.3f, 0.9f);
            emit.startLifetime = Random.Range(0.8f, 1.3f);
            emit.startSize = Random.Range(0.25f, 0.5f);
            emit.startColor = dustColor;
            emit.rotation = Random.Range(0f, 360f);
            fx.Emit(emit, 1);
        }
        // Dust shaken loose from above: a few small motes drifting down around the player (not in the face: 0.7-1.5 m out).
        for (int i = 0; i < fallingDust; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(0.7f, 1.5f);
            Vector3 centre = head != null ? head.position : ground + Vector3.up * 1.6f;
            emit.position = centre + new Vector3(c.x, Random.Range(0.1f, 1.4f), c.y);
            emit.velocity = new Vector3(Random.Range(-0.05f, 0.05f), -Random.Range(0.35f, 0.7f), Random.Range(-0.05f, 0.05f));
            emit.startLifetime = Random.Range(1.1f, 1.7f);
            emit.startSize = Random.Range(0.03f, 0.06f);
            emit.startColor = dustColor;
            emit.rotation = Random.Range(0f, 360f);
            fx.Emit(emit, 1);
        }
        // Ground ring: an expanding arc of 240 degrees on the floor, the gap facing the player.
        for (int i = 0; i < ringParticles; i++)
        {
            float a = Mathf.Lerp(-120f, 120f, ringParticles > 1 ? i / (float)(ringParticles - 1) : 0.5f);
            Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * -toPlayer;
            emit.position = ground + dir * 0.3f + Vector3.up * 0.04f;
            emit.velocity = dir * 2.4f;
            emit.startLifetime = 0.45f;
            emit.startSize = 0.22f;
            emit.startColor = ringColor;
            emit.rotation = 0f;
            fx.Emit(emit, 1);
        }
    }

    static Vector3 AwayFromPlayer(Vector3 toPlayer, float arcDegrees)
    {
        float a = Random.Range(-arcDegrees * 0.5f, arcDegrees * 0.5f);
        return Quaternion.AngleAxis(a, Vector3.up) * -toPlayer;
    }
}
