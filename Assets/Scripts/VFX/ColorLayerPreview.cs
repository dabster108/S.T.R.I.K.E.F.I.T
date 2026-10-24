// ColorLayerPreview.cs
// Purpose: Editor-only preview of the colour layers: add it next to a MonsterColorCompositor or a PlayerWeaponGlow in Play mode and
//          fire each layer from the Inspector buttons (Editor/ColorLayerPreviewEditor.cs): telegraph, the four hit
//          tiers and the weak-point hit, clash, parry, strike, cues, guard on/off, a simulated HP for the wound tint, rage and death.
//          Everything goes through the compositor's layer API, so what you see is exactly what the game does. Compiled out of
//          players (the class stays, empty, so a leftover component never shows as a missing script); do not save it on prefabs.
// Dependencies: MonsterColorCompositor, PlayerWeaponGlow, StrikeFit.ColorFx
// Attach to: a monster root or a player weapon root (temporarily)

using UnityEngine;

[AddComponentMenu("StrikeFit/Debug/Colour Layer Preview")]
public class ColorLayerPreview : MonoBehaviour
{
#if UNITY_EDITOR
    [Tooltip("Attack telegraph colour for the preview.")]
    public Color telegraphColor = new Color(1f, 0.2f, 0.2f);
    [Tooltip("Seconds the telegraph pulses.")]
    public float telegraphSeconds = 1.2f;
    [Range(0f, 1f)] public float hpFraction = 1f;
    [Range(0f, 1f)] public float rage = 0f;
    public Color enrageTint = new Color(1f, 0.55f, 0.45f);

    public MonsterColorCompositor Monster => GetComponent<MonsterColorCompositor>();
    public PlayerWeaponGlow Weapon => GetComponent<PlayerWeaponGlow>();
#endif
}
