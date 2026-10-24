// PlayerWeaponGlow.cs
// Purpose: The same compositor for one player weapon (the only writer of that weapon's MaterialPropertyBlocks). Layers:
//            hit     the weapon flashes in the tier colour of the swing that landed (HitDetection.HitLanded, this weapon only)
//            clash   the weapon flashes when it met the monster's weapon (HitDetection.WeaponClashed, this weapon only); a parry
//                    (MonsterColorCompositor decides it) turns the flash parry-coloured
//            strike  both held weapons flash red when the player is hit (PlayerHealth.DamageTaken)
//          Tiers come from XPManager.GetSwingTier (the same tier names as the telemetry and the damage numbers).
//          Visual only.
// Dependencies: ColorCompositor, StrikeFit.ColorFx (WeaponGlowProfile), HitDetection, PlayerHealth, XPManager
// Attach to: each player weapon root (Level 1 hammers, Level 2 swords, Level 3 mace / flail / katana pairs), wired by
//            Editor/ColorCompositorSetup with the weapon's renderers as Weapon slots.

using System.Collections.Generic;
using StrikeFit.ColorFx;
using UnityEngine;

public class PlayerWeaponGlow : ColorCompositor
{
    public WeaponGlowProfile profile;

    public int Hits { get; private set; }
    public int Clashes { get; private set; }
    public SwingTierFx LastHitTier { get; private set; }

    static readonly List<PlayerWeaponGlow> all = new List<PlayerWeaponGlow>();
    HitDetection[] mine = new HitDetection[0];
    bool pendingParry, pendingPerfect;
    Color perfectColor = new Color(1f, 0.97f, 0.62f);
    int seenCosmetic = -1;

    protected override void Awake()
    {
        mine = GetComponentsInChildren<HitDetection>(true);
        if (profile != null) Stack.settings = profile.blend;
        base.Awake();
    }

    void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        HitDetection.HitLanded += HandleHit;
        HitDetection.WeaponClashed += HandleClash;
        PlayerHealth.DamageTaken += HandleHurt;
    }

    void OnDisable()
    {
        all.Remove(this);
        HitDetection.HitLanded -= HandleHit;
        HitDetection.WeaponClashed -= HandleClash;
        PlayerHealth.DamageTaken -= HandleHurt;
    }

    bool Owns(HitDetection hd)
    {
        for (int i = 0; i < mine.Length; i++) if (mine[i] == hd) return true;
        return false;
    }

    protected override void BaseFor(ColorGroup group, out Color tint, out Color emission)
    {
        // A cosmetic glow palette (GlowCosmetics, unlocked by rank) replaces only this resting look; every flash keeps its colour.
        if (GlowCosmetics.Active) { tint = GlowCosmetics.Tint; emission = GlowCosmetics.Emission; return; }
        if (profile != null) { tint = profile.tint; emission = profile.emission; }
        else { tint = Color.white; emission = Color.black; }
    }

    protected override float DefaultWeight(ColorLayer id)
    {
        var p = profile;
        if (p == null) return 0.3f;
        switch (id)
        {
            case ColorLayer.Hit: return p.hitWeight;
            case ColorLayer.Clash: return p.clashWeight;
            case ColorLayer.Strike: return p.hurtWeight;
            default: return 0f;
        }
    }

    protected override ColorGroup DefaultMask(ColorLayer id) => ColorGroup.AllWeapon;

    protected override void Tick(float now)
    {
        if (seenCosmetic != GlowCosmetics.Version) { seenCosmetic = GlowCosmetics.Version; Invalidate(); }
        if (profile == null) return;
        if (pendingPerfect)
        {
            pendingPerfect = pendingParry = false;
            Stack.Set(ColorLayer.Clash, perfectColor, perfectColor, Mathf.Max(profile.clashWeight, 0.6f), profile.clashIntensity * 1.6f, profile.parrySeconds * 1.4f,
                      LayerCurve.Decay, ColorGroup.AllWeapon, now);
            return;
        }
        if (!pendingParry) return;
        pendingParry = false;
        Stack.Set(ColorLayer.Clash, profile.parryColor, profile.parryColor, profile.clashWeight, profile.clashIntensity, profile.parrySeconds,
                  LayerCurve.Decay, ColorGroup.AllWeapon, now);
    }

    static SwingTierFx TierOf(float speed)
    {
        string t = XPManager.Instance != null ? XPManager.Instance.GetSwingTier(speed) : null;
        if (string.IsNullOrEmpty(t)) return speed >= 6f ? SwingTierFx.Full : speed >= 4f ? SwingTierFx.Medium : speed >= 2.5f ? SwingTierFx.Light : SwingTierFx.Tap;
        switch (t[0])   // "TAP" / "LIGHT" / "MEDIUM" / "FULL" (first letter: no allocation)
        {
            case 'T': case 't': return SwingTierFx.Tap;
            case 'L': case 'l': return SwingTierFx.Light;
            case 'M': case 'm': return SwingTierFx.Medium;
            default: return SwingTierFx.Full;
        }
    }

    void HandleHit(HitDetection source, float speed, bool monsterDefending)
    {
        if (profile == null || !Owns(source)) return;
        var tier = TierOf(speed);
        profile.Tier(tier, out Color c, out float intensity, out float seconds);
        if (monsterDefending) intensity *= 0.5f;   // a blocked hit still reads, but softer
        LastHitTier = tier; Hits++;
        Stack.Set(ColorLayer.Hit, c, c, profile.hitWeight, intensity, seconds, LayerCurve.Decay, ColorGroup.AllWeapon, Time.time);
    }

    void HandleClash(HitDetection source, float speed)
    {
        if (profile == null || !Owns(source)) return;
        Clashes++;
        Stack.Set(ColorLayer.Clash, profile.clashColor, profile.clashColor, profile.clashWeight, profile.clashIntensity, profile.clashSeconds,
                  LayerCurve.Decay, ColorGroup.AllWeapon, Time.time);
    }

    void HandleHurt(float amount, bool blocked, float hpAfter)
    {
        if (profile == null || amount <= 0.001f || !isActiveAndEnabled) return;
        Stack.Set(ColorLayer.Strike, profile.hurtColor, profile.hurtColor, profile.hurtWeight, profile.hurtIntensity * (blocked ? 0.5f : 1f),
                  profile.hurtSeconds, LayerCurve.Decay, ColorGroup.AllWeapon, Time.time);
    }

    /// <summary>EngagementTracker: the clash of <paramref name="source"/> was a perfect clash (its weapon flashes in <paramref name="color"/>).</summary>
    public static void NotifyPerfectClash(HitDetection source, Color color)
    {
        if (source == null) return;
        for (int i = 0; i < all.Count; i++) if (all[i].Owns(source)) { all[i].pendingPerfect = true; all[i].perfectColor = color; }
    }

    /// <summary>MonsterColorCompositor: the clash of <paramref name="source"/> was a parry.</summary>
    public static void NotifyParry(HitDetection source)
    {
        if (source == null) return;
        for (int i = 0; i < all.Count; i++) if (all[i].Owns(source)) all[i].pendingParry = true;
    }
}
