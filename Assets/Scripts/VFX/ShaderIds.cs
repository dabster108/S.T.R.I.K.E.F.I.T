// ShaderIds.cs
// Purpose: Shader property IDs used by the per-frame MaterialPropertyBlock tints (glow, telegraph flash, vignette, danger zones,
//          serpent aura...). SetColor("_BaseColor", c) hashes the name on every call; SetColor(ShaderIds.BaseColor, c) does not.
//          Same properties, same results.
// Dependencies: none
// Attach to: nothing (static)

using UnityEngine;

public static class ShaderIds
{
    public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    public static readonly int Color = Shader.PropertyToID("_Color");
    public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
}
