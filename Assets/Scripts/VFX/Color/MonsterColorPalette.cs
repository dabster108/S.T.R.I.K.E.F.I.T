// MonsterColorPalette.cs
// Purpose: One level's monster look for MonsterColorCompositor (assets in Assets/Settings/Color/): the resting tint and accent of the
//          body, its armour and its weapon, and the colour, strength and timing of every effect layer (wound, rage, guard, hit tiers,
//          clash / parry, strike, telegraph, death), plus the emission clamp. Visual only: no gameplay value lives here.
// Dependencies: ColorStack (BlendSettings, SwingTierFx)
// Attach to: nothing (ScriptableObject, referenced by MonsterColorCompositor)

using UnityEngine;

namespace StrikeFit.ColorFx
{
    [CreateAssetMenu(menuName = "StrikeFit/Monster Colour Palette", fileName = "MonsterColorPalette")]
    public class MonsterColorPalette : ScriptableObject
    {
        [Header("Blend")]
        public BlendSettings blend = BlendSettings.Default;

        [Header("Resting look (multiplies each material's own colour; emission is added to the material's own)")]
        public Color bodyTint = Color.white;
        public Color bodyEmission = Color.black;
        [Tooltip("Armour / cloth slots, or the armour mask of a one-material body.")]
        public Color accentTint = Color.white;
        public Color accentEmission = Color.black;
        [Tooltip("Level 3 glow-mask slots (their emission is driven by MonsterGlow).")]
        public Color glowTint = Color.white;
        public Color weaponTint = Color.white;
        public Color weaponEmission = Color.black;
        [Tooltip("Blade / edge of the monster's weapon (or its glow planes).")]
        public Color weaponAccentTint = Color.white;
        public Color weaponAccentEmission = Color.black;

        [Tooltip("Scale of the fire-aura smoulder on the body (MonsterFireAura). Level 1's never showed before the compositor (its body had no\n" +
                 "emission keyword), so it is kept low there.")]
        public float auraScale = 1f;

        [Header("Wound (grows as HP drops)")]
        public Color woundColor = new Color(0.62f, 0.1f, 0.08f);
        [Range(0f, 1f)] public float woundMaxWeight = 0.35f;
        public Color woundEmission = new Color(0.45f, 0.04f, 0.02f);
        public float woundMaxIntensity = 0.3f;
        [Tooltip("HP fraction above which there is no wound tint.")]
        [Range(0f, 1f)] public float woundStartsBelow = 0.95f;

        [Header("Rage (Level 3 enrage)")]
        public Color rageRimColor = new Color(1f, 0.35f, 0.05f);
        public float rageRimIntensity = 0.3f;

        [Header("Guard")]
        public Color guardColor = new Color(0.62f, 0.8f, 1f);
        [Range(0f, 1f)] public float guardWeight = 0.8f;
        public float guardIntensity = 0.55f;

        [Header("Short cues (guard raise, block, stagger, opening)")]
        [Range(0f, 1f)] public float cueWeight = 0.6f;
        public float cueIntensity = 0.35f;

        [Header("Hit flash by swing tier (same colours as the damage numbers)")]
        public Color tapColor = new Color(0.9f, 0.92f, 1f);
        public Color lightColor = new Color(1f, 0.9f, 0.25f);
        public Color mediumColor = new Color(1f, 0.55f, 0.12f);
        public Color fullColor = new Color(1f, 0.16f, 0.14f);
        public Color weakPointColor = new Color(0.55f, 1f, 0.35f);
        public Color blockedColor = new Color(0.55f, 0.75f, 1f);
        public Color resistedColor = new Color(0.62f, 0.62f, 0.66f);
        public float tapIntensity = 0.25f, lightIntensity = 0.4f, mediumIntensity = 0.6f, fullIntensity = 0.85f, weakIntensity = 0.9f;
        public float tapSeconds = 0.15f, lightSeconds = 0.17f, mediumSeconds = 0.2f, fullSeconds = 0.25f, weakSeconds = 0.25f;
        [Range(0f, 1f)] public float hitWeight = 0.5f;

        [Header("Clash / parry")]
        public Color clashColor = new Color(1f, 0.85f, 0.45f);
        public Color parryColor = new Color(0.55f, 0.85f, 1f);
        public float clashSeconds = 0.22f, parrySeconds = 0.32f;
        public float clashIntensity = 0.9f;
        [Range(0f, 1f)] public float clashWeight = 0.45f;
        [Tooltip("Body rim pulse that goes with a clash / parry.")]
        public float rimIntensity = 0.28f;

        [Header("Perfect clash (EngagementTracker): a clash just before the strike connects")]
        public Color perfectClashColor = new Color(1f, 0.97f, 0.62f);
        public float perfectClashSeconds = 0.45f;
        public float perfectClashIntensity = 1.4f;

        [Header("Telegraph outline: a rim drawn around the body while a telegraph shows (for dark textures)")]
        [Tooltip("Off on most monsters. Level 2's dark-red texture and emission mask keep the telegraph's colour weak, so it gets a rim.")]
        public bool telegraphOutline = false;
        public Material telegraphOutlineMaterial;
        [Tooltip("Rim thickness in metres (world space).")]
        public float telegraphOutlineWidth = 0.012f;
        [Tooltip("Rim colour = the telegraph colour x this (HDR-ish brightness on an unlit shell).")]
        public float telegraphOutlineIntensity = 1.6f;

        [Header("Strike (the monster's attack landed)")]
        public float strikeSeconds = 0.2f;
        public float strikeIntensity = 0.7f;
        [Range(0f, 1f)] public float strikeWeight = 0.35f;

        [Header("Telegraph")]
        public float telegraphPulseHz = 3f;
        [Range(0f, 1f)] public float telegraphWeight = 0.85f;
        public float telegraphIntensity = 0.55f;

        [Header("Death")]
        public Color deathColor = new Color(0.14f, 0.13f, 0.15f);
        [Range(0f, 1f)] public float deathWeight = 0.7f;
        [Tooltip("Lets the death flash / collapse (Level 3 glow) play first.")]
        public float deathDelay = 0.6f;
        public float deathFadeSeconds = 1.6f;

        public void Tier(SwingTierFx tier, out Color color, out float intensity, out float seconds)
        {
            switch (tier)
            {
                case SwingTierFx.Tap: color = tapColor; intensity = tapIntensity; seconds = tapSeconds; break;
                case SwingTierFx.Light: color = lightColor; intensity = lightIntensity; seconds = lightSeconds; break;
                case SwingTierFx.Medium: color = mediumColor; intensity = mediumIntensity; seconds = mediumSeconds; break;
                case SwingTierFx.WeakPoint: color = weakPointColor; intensity = weakIntensity; seconds = weakSeconds; break;
                default: color = fullColor; intensity = fullIntensity; seconds = fullSeconds; break;
            }
        }

        public void Base(ColorGroup g, out Color tint, out Color emission)
        {
            switch (g)
            {
                case ColorGroup.Accent: tint = accentTint; emission = accentEmission; break;
                case ColorGroup.Glow: tint = glowTint; emission = Color.black; break;
                case ColorGroup.WeakPoint: tint = Color.white; emission = Color.black; break;
                case ColorGroup.Weapon: tint = weaponTint; emission = weaponEmission; break;
                case ColorGroup.WeaponAccent: tint = weaponAccentTint; emission = weaponAccentEmission; break;
                default: tint = bodyTint; emission = bodyEmission; break;
            }
        }
    }
}
