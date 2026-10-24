// WeaponGlowProfile.cs
// Purpose: The player's weapon look for PlayerWeaponGlow (Assets/Settings/Color/): a faint resting accent, the hit flash by swing tier
//          (the same tiers and colours as the monster's hit flash and the damage numbers), the clash / parry flash, the "you were hit"
//          flash, and the emission clamp. Visual only.
// Dependencies: ColorStack (BlendSettings, SwingTierFx)
// Attach to: nothing (ScriptableObject, referenced by PlayerWeaponGlow)

using UnityEngine;

namespace StrikeFit.ColorFx
{
    [CreateAssetMenu(menuName = "StrikeFit/Weapon Glow Profile", fileName = "WeaponGlowProfile")]
    public class WeaponGlowProfile : ScriptableObject
    {
        [Header("Blend")]
        public BlendSettings blend = new BlendSettings { maxEmission = 1.2f, telegraphMinWeight = 0f, telegraphMinEmission = 0f, telegraphReserve = 0f };

        [Header("Resting look")]
        public Color tint = Color.white;
        public Color emission = Color.black;

        [Header("Hit landed, by swing tier")]
        public Color tapColor = new Color(0.9f, 0.92f, 1f);
        public Color lightColor = new Color(1f, 0.9f, 0.25f);
        public Color mediumColor = new Color(1f, 0.55f, 0.12f);
        public Color fullColor = new Color(1f, 0.16f, 0.14f);
        public float tapIntensity = 0.3f, lightIntensity = 0.55f, mediumIntensity = 0.85f, fullIntensity = 1.1f;
        public float tapSeconds = 0.15f, lightSeconds = 0.17f, mediumSeconds = 0.2f, fullSeconds = 0.25f;
        [Range(0f, 1f)] public float hitWeight = 0.4f;

        [Header("Clash / parry")]
        public Color clashColor = new Color(1f, 0.85f, 0.45f);
        public Color parryColor = new Color(0.55f, 0.85f, 1f);
        public float clashSeconds = 0.22f, parrySeconds = 0.32f;
        public float clashIntensity = 0.9f;
        [Range(0f, 1f)] public float clashWeight = 0.4f;

        [Header("The player was hit")]
        public Color hurtColor = new Color(1f, 0.12f, 0.1f);
        public float hurtSeconds = 0.25f;
        public float hurtIntensity = 0.45f;
        [Range(0f, 1f)] public float hurtWeight = 0.3f;

        public void Tier(SwingTierFx tier, out Color color, out float intensity, out float seconds)
        {
            switch (tier)
            {
                case SwingTierFx.Tap: color = tapColor; intensity = tapIntensity; seconds = tapSeconds; break;
                case SwingTierFx.Light: color = lightColor; intensity = lightIntensity; seconds = lightSeconds; break;
                case SwingTierFx.Medium: color = mediumColor; intensity = mediumIntensity; seconds = mediumSeconds; break;
                default: color = fullColor; intensity = fullIntensity; seconds = fullSeconds; break;
            }
        }
    }
}
