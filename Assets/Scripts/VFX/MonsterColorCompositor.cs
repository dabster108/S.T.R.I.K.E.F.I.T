// MonsterColorCompositor.cs
// Purpose: The only writer of a monster's colour (body, armour accent, Level 3 glow and weak-point slots, its weapons). Every effect
//          reaches it as a layer (ColorStack), so effects stack instead of overwriting each other:
//            palette        per-level resting tint and accent (MonsterColorPalette, Assets/Settings/Color)
//            wound          follows the monster's HP every frame (stronger as HP drops)
//            rage           Level 3 enrage blend (BossPhaseController)
//            guard          held while MonsterAI.IsDefending (and the monster has a guard tint)
//            cues           guard raise, block, stagger, guard opening (MonsterAI)
//            hit            every damage the monster takes, coloured by the swing tier that caused it (MonsterController)
//            strike         the monster's weapon flashes when its attack lands on the player (MonsterController)
//            clash / parry  a player weapon met this monster's weapon (HitDetection.WeaponClashed; parry from MonsterAI): weapon
//                           flash plus a body rim pulse, and the clashing player weapon flashes too (PlayerWeaponGlow)
//            telegraph      the attack wind-up (MonsterAI), pulsing, always readable over every other layer
//            death          fades the monster out after its death flash
//          Emission sources that used to write the block themselves now feed held layers: MonsterGlow (BodyGlow), MonsterWeakPoint
//          (WeakPointGlow), MonsterFireAura (AuraPulse), MonsterEntrance drop eyes (EntranceGlow).
//          Visual only: nothing here reads or changes damage, health, timing or any gameplay value.
// Dependencies: ColorCompositor, StrikeFit.ColorFx (MonsterColorPalette), MonsterAI, MonsterController, MonsterHealth, HitDetection,
//               PlayerWeaponGlow, CombatUIController (DamageStyle)
// Attach to: the monster root (Levels 1-3 monster prefabs, wired by Editor/ColorCompositorSetup)

using StrikeFit.ColorFx;
using UnityEngine;

public class MonsterColorCompositor : ColorCompositor
{
    public MonsterColorPalette palette;

    /// <summary>Preview tool (ColorLayerPreview): hold the guard layer / show the wound at this HP fraction (-1 = the real HP).</summary>
    [System.NonSerialized] public bool previewGuard;
    [System.NonSerialized] public float previewHpFraction = -1f;

    public int Hits { get; private set; }
    public int Clashes { get; private set; }
    public int Parries { get; private set; }
    public SwingTierFx LastHitTier { get; private set; }

    MonsterAI ai;
    MonsterController controller;
    MonsterHealth health;
    int shownHpPercent = -1;
    bool pendingClash, pendingParry, pendingPerfect, dead;
    HitDetection lastClashSource;
    public int PerfectClashes { get; private set; }

    // Telegraph outline (palette.telegraphOutline): an unlit inverted-hull shell per body SkinnedMeshRenderer, same mesh and bones,
    // built on the first telegraph and shown only while the telegraph layer is up. Its colour goes through its own block.
    SkinnedMeshRenderer[] outlineShells;
    MaterialPropertyBlock outlineMpb;
    bool outlineOn;
    Color lastTelegraphColor = new Color(1f, 0.2f, 0.2f);
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    public bool OutlineShowing => outlineOn;

    protected override void Awake()
    {
        ai = GetComponent<MonsterAI>();
        controller = GetComponent<MonsterController>();
        health = GetComponent<MonsterHealth>();
        if (palette != null) Stack.settings = palette.blend;
        base.Awake();
    }

    void OnEnable()
    {
        HitDetection.WeaponClashed += HandleClash;
        if (controller != null) controller.OnMonsterDeath += HandleDeath;
    }

    void OnDisable()
    {
        HitDetection.WeaponClashed -= HandleClash;
        if (controller != null) controller.OnMonsterDeath -= HandleDeath;
    }

    protected override void BaseFor(ColorGroup group, out Color tint, out Color emission)
    {
        if (palette != null) palette.Base(group, out tint, out emission);
        else { tint = Color.white; emission = Color.black; }
    }

    protected override float DefaultWeight(ColorLayer id)
    {
        var p = palette;
        if (p == null) return 0.5f;
        switch (id)
        {
            case ColorLayer.Guard: return p.guardWeight;
            case ColorLayer.Cue: return p.cueWeight;
            case ColorLayer.Hit: return p.hitWeight;
            case ColorLayer.Strike: return p.strikeWeight;
            case ColorLayer.Clash: return p.clashWeight;
            case ColorLayer.ClashRim: return 0.15f;
            case ColorLayer.Telegraph: return p.telegraphWeight;
            case ColorLayer.Death: return p.deathWeight;
            case ColorLayer.Wound: return p.woundMaxWeight;
            case ColorLayer.Rage: return 1f;
            default: return 0f;   // emission-only sources
        }
    }

    protected override ColorGroup DefaultMask(ColorLayer id)
    {
        switch (id)
        {
            case ColorLayer.BodyGlow: return ColorGroup.Glow;
            case ColorLayer.WeakPointGlow: return ColorGroup.WeakPoint;
            case ColorLayer.AuraPulse:
            case ColorLayer.EntranceGlow: return ColorGroup.Body | ColorGroup.Accent;
            case ColorLayer.Strike:
            case ColorLayer.Clash: return ColorGroup.AllWeapon;
            case ColorLayer.Telegraph:
            case ColorLayer.Death: return ColorGroup.All;
            default: return ColorGroup.AllBody;
        }
    }

    // ───────────────────────── per frame: state the monster is in ─────────────────────────

    protected override void Tick(float now)
    {
        var p = palette;
        if (p == null) return;

        // Wound: from the HP (1 % steps), nothing while healthy.
        if (previewHpFraction >= 0f || (health != null && health.maxHealth > 0f))
        {
            float frac = previewHpFraction >= 0f ? previewHpFraction : health.currentHealth / health.maxHealth;
            int pct = Mathf.Clamp(Mathf.RoundToInt(100f * frac), 0, 100);
            if (pct != shownHpPercent)
            {
                shownHpPercent = pct;
                float w = Mathf.Clamp01((p.woundStartsBelow - pct / 100f) / Mathf.Max(0.01f, p.woundStartsBelow));
                w = w * w * (3f - 2f * w);   // gentle at first, clearly stronger near the end
                if (w > 0f) Stack.SetHeld(ColorLayer.Wound, p.woundColor, p.woundEmission, p.woundMaxWeight * w, p.woundMaxIntensity * w, ColorGroup.AllBody, now);
                else Stack.Clear(ColorLayer.Wound);
            }
        }

        // Guard: held while the monster guards (only monsters with a guard tint).
        bool guarding = (previewGuard || (ai != null && ai.IsDefending && ai.defendTintEnabled)) && !dead;
        if (guarding) Stack.SetHeld(ColorLayer.Guard, p.guardColor, p.guardColor, p.guardWeight, p.guardIntensity, ColorGroup.AllBody, now);
        else Stack.Clear(ColorLayer.Guard);

        // Perfect clash (EngagementTracker): stronger, longer and its own colour; it wins over a plain clash or parry the same frame.
        if (pendingPerfect)
        {
            Color c = p.perfectClashColor;
            Stack.Set(ColorLayer.Clash, c, c, Mathf.Max(p.clashWeight, 0.6f), p.perfectClashIntensity, p.perfectClashSeconds, LayerCurve.Decay, ColorGroup.AllWeapon, now);
            Stack.Set(ColorLayer.ClashRim, c, c, 0.25f, p.rimIntensity * 2.2f, p.perfectClashSeconds, LayerCurve.Decay, ColorGroup.AllBody, now);
            if (pendingParry) Parries++;
            PerfectClashes++;
            pendingPerfect = pendingClash = pendingParry = false;
        }

        UpdateTelegraphOutline(p, now);

        // Clash / parry: decided here so the result does not depend on the order the clash listeners ran in.
        if (pendingClash || pendingParry)
        {
            bool parry = pendingParry;
            Color c = parry ? p.parryColor : p.clashColor;
            float s = parry ? p.parrySeconds : p.clashSeconds;
            Stack.Set(ColorLayer.Clash, c, c, p.clashWeight, p.clashIntensity, s, LayerCurve.Decay, ColorGroup.AllWeapon, now);
            Stack.Set(ColorLayer.ClashRim, c, c, 0.15f, p.rimIntensity, s, LayerCurve.Decay, ColorGroup.AllBody, now);
            if (parry) { Parries++; PlayerWeaponGlow.NotifyParry(lastClashSource); }
            else Clashes++;
            pendingClash = pendingParry = false;
        }
    }

    // ───────────────────────── events ─────────────────────────

    void HandleClash(HitDetection source, float speed)
    {
        if (source == null || source.LastClashWeapon == null || !source.LastClashWeapon.IsChildOf(transform)) return;
        pendingClash = true;
        lastClashSource = source;
    }

    void HandleDeath()
    {
        dead = true;
        var p = palette;
        if (p == null) return;
        Stack.Clear(ColorLayer.Telegraph);
        Stack.Set(ColorLayer.Death, p.deathColor, Color.black, p.deathWeight, 0f, p.deathFadeSeconds, LayerCurve.FadeIn, ColorGroup.All, Time.time, 0f, p.deathDelay);
    }

    // ───────────────────────── named helpers (called by the monster's scripts) ─────────────────────────

    /// <summary>Attack wind-up: a pulsing warning for <paramref name="seconds"/>, readable over every other layer.</summary>
    public void Telegraph(Color color, float seconds)
    {
        var p = palette;
        lastTelegraphColor = color;
        float hz = p != null ? p.telegraphPulseHz : 3f;
        Stack.Set(ColorLayer.Telegraph, color, color, p != null ? p.telegraphWeight : 0.85f, p != null ? p.telegraphIntensity : 0.55f,
                  Mathf.Max(0.05f, seconds), LayerCurve.Pulse, ColorGroup.All, Time.time, hz);
    }

    /// <summary>A short readability cue (guard raise, block, stagger, opening).</summary>
    public void Cue(Color color, float seconds)
    {
        var p = palette;
        Stack.Set(ColorLayer.Cue, color, color, p != null ? p.cueWeight : 0.6f, p != null ? p.cueIntensity : 0.35f,
                  Mathf.Max(0.04f, seconds), LayerCurve.Decay, ColorGroup.AllBody, Time.time);
    }

    /// <summary>The monster took damage: the hit flash from the tier (DamageStyle) the damage numbers use.</summary>
    public void NotifyDamage(CombatUIController.DamageStyle style)
    {
        var p = palette;
        if (p == null || dead) return;
        Color c; float intensity, seconds;
        switch (style)
        {
            case CombatUIController.DamageStyle.Tap: p.Tier(SwingTierFx.Tap, out c, out intensity, out seconds); LastHitTier = SwingTierFx.Tap; break;
            case CombatUIController.DamageStyle.Light: p.Tier(SwingTierFx.Light, out c, out intensity, out seconds); LastHitTier = SwingTierFx.Light; break;
            case CombatUIController.DamageStyle.Medium: p.Tier(SwingTierFx.Medium, out c, out intensity, out seconds); LastHitTier = SwingTierFx.Medium; break;
            case CombatUIController.DamageStyle.Weak: p.Tier(SwingTierFx.WeakPoint, out c, out intensity, out seconds); LastHitTier = SwingTierFx.WeakPoint; break;
            case CombatUIController.DamageStyle.Blocked: c = p.blockedColor; intensity = p.tapIntensity; seconds = p.tapSeconds; break;
            case CombatUIController.DamageStyle.Resisted: c = p.resistedColor; intensity = p.tapIntensity; seconds = p.tapSeconds; break;
            default: p.Tier(SwingTierFx.Full, out c, out intensity, out seconds); LastHitTier = SwingTierFx.Full; break;
        }
        Hits++;
        Stack.Set(ColorLayer.Hit, c, c, p.hitWeight, intensity, seconds, LayerCurve.Decay, ColorGroup.AllBody, Time.time);
    }

    /// <summary>The monster's attack landed on the player: its weapon flashes in the attack's colour.</summary>
    public void Strike(Color color)
    {
        var p = palette;
        if (p == null) return;
        Stack.Set(ColorLayer.Strike, color, color, p.strikeWeight, p.strikeIntensity, p.strikeSeconds, LayerCurve.Decay, ColorGroup.AllWeapon, Time.time);
    }

    /// <summary>MonsterAI decided a clash was a parry of its wind-up.</summary>
    public void NotifyParry() => pendingParry = true;

    /// <summary>EngagementTracker: the clash was a perfect clash (just before the strike connected).</summary>
    public void NotifyPerfectClash() => pendingPerfect = true;

    void UpdateTelegraphOutline(MonsterColorPalette p, float now)
    {
        if (!p.telegraphOutline || p.telegraphOutlineMaterial == null) { if (outlineOn) ShowOutline(false); return; }
        // Shown for the whole telegraph: the layer is a pulse whose strength touches 0 at every trough, so its value cannot decide this.
        var layer = Stack.Get(ColorLayer.Telegraph);
        float lt = now - layer.start - layer.delay;
        bool telegraphing = !dead && layer.active && lt >= 0f && (layer.duration <= 0f || lt < layer.duration);
        float s = telegraphing ? Stack.Strength(ColorLayer.Telegraph, now) : 0f;
        if (!telegraphing)
        {
            if (outlineOn) ShowOutline(false);
            return;
        }
        if (outlineShells == null) BuildOutlineShells(p);
        if (!outlineOn) ShowOutline(true);
        float k = p.telegraphOutlineIntensity * (0.6f + 0.4f * s);   // pulses with the telegraph, never fully dark while it shows
        outlineMpb.SetColor(OutlineColorId, new Color(lastTelegraphColor.r * k, lastTelegraphColor.g * k, lastTelegraphColor.b * k, 1f));
        outlineMpb.SetFloat(OutlineWidthId, p.telegraphOutlineWidth);
        for (int i = 0; i < outlineShells.Length; i++) if (outlineShells[i] != null) outlineShells[i].SetPropertyBlock(outlineMpb);
    }

    void BuildOutlineShells(MonsterColorPalette p)
    {
        outlineMpb = new MaterialPropertyBlock();
        var list = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (var slot in slots)
        {
            var src = slot.renderer as SkinnedMeshRenderer;
            if (src == null || slot.group != ColorGroup.Body || src.sharedMesh == null) continue;
            bool seen = false; foreach (var l in list) if (l.sharedMesh == src.sharedMesh && l.transform.parent == src.transform.parent) seen = true;
            if (seen) continue;
            var go = new GameObject("TelegraphOutline");
            go.layer = src.gameObject.layer;
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
            var shell = go.AddComponent<SkinnedMeshRenderer>();
            shell.sharedMesh = src.sharedMesh;
            shell.bones = src.bones;
            shell.rootBone = src.rootBone;
            shell.localBounds = src.localBounds;
            shell.quality = src.quality;
            shell.updateWhenOffscreen = src.updateWhenOffscreen;
            var mats = new Material[src.sharedMesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = p.telegraphOutlineMaterial;
            shell.sharedMaterials = mats;
            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            shell.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            shell.enabled = false;
            list.Add(shell);
        }
        outlineShells = list.ToArray();
    }

    void ShowOutline(bool on)
    {
        outlineOn = on;
        if (outlineShells == null) return;
        for (int i = 0; i < outlineShells.Length; i++) if (outlineShells[i] != null) outlineShells[i].enabled = on;
    }

    /// <summary>Level 3 enrage blend 0..1 toward <paramref name="enrageTint"/> (base) with the palette's rim emission.</summary>
    public void SetRage(float k, Color enrageTint)
    {
        var p = palette;
        if (k <= 0f) { Stack.Clear(ColorLayer.Rage); return; }
        Color rim = p != null ? p.rageRimColor : new Color(1f, 0.35f, 0.05f);
        float ri = p != null ? p.rageRimIntensity : 0.3f;
        Stack.SetHeld(ColorLayer.Rage, enrageTint, rim, Mathf.Clamp01(k), ri * Mathf.Clamp01(k), ColorGroup.AllBody, Time.time);
    }

    /// <summary>MonsterFireAura smoulder (added to the material's own emission).</summary>
    public void SetAura(Color emission) => Stack.SetHeld(ColorLayer.AuraPulse, emission, emission, 0f, palette != null ? palette.auraScale : 1f, ColorGroup.Body | ColorGroup.Accent, Time.time);

    /// <summary>MonsterEntrance drop: replaces the body's own emission while the eyes open.</summary>
    public void SetEntranceEmission(Color emission) =>
        Stack.SetHeld(ColorLayer.EntranceGlow, emission, emission, 0f, 1f, ColorGroup.Body | ColorGroup.Accent, Time.time, true);

    public void ClearEntranceEmission() => Stack.Clear(ColorLayer.EntranceGlow);

    /// <summary>MonsterGlow (Level 3): the glow-mask slots' emission this frame.</summary>
    public void SetBodyGlow(Color emission) => Stack.SetHeld(ColorLayer.BodyGlow, emission, emission, 0f, 1f, ColorGroup.Glow, Time.time);

    /// <summary>MonsterWeakPoint (Level 3): the weak-point slot's emission this frame.</summary>
    public void SetWeakPointGlow(Color emission) => Stack.SetHeld(ColorLayer.WeakPointGlow, emission, emission, 0f, 1f, ColorGroup.WeakPoint, Time.time);
}
