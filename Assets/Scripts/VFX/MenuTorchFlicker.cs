// MenuTorchFlicker.cs
// Purpose: One of the main menu's two realtime torch lights: its intensity follows smooth noise around the authored value (two
//          Perlin octaves, both under 2 Hz, so it breathes rather than strobes), with a slight range wobble. The light is a
//          Mixed point light without shadows (its bounce is baked), so only the direct part flickers. No allocations.
// Dependencies: Light
// Attach to: the torch light objects under MenuDungeon/Torches (added by Editor/MenuDungeonBuilder)

using UnityEngine;

[RequireComponent(typeof(Light))]
public class MenuTorchFlicker : MonoBehaviour
{
    [Range(0f, 0.5f)] public float depth = 0.2f;
    [Tooltip("Main flicker rate (Hz). The second octave runs at about twice this; keep both under 2 Hz.")]
    [Range(0.2f, 1f)] public float speed = 0.85f;
    [Range(0f, 0.2f)] public float rangeWobble = 0.06f;

    Light lamp;
    float baseIntensity, baseRange, seed;

    void Awake()
    {
        lamp = GetComponent<Light>();
        baseIntensity = lamp.intensity;
        baseRange = lamp.range;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float t = Time.unscaledTime;
        float n = Mathf.PerlinNoise(t * speed, seed) * 0.7f + Mathf.PerlinNoise(t * speed * 2.2f, seed + 17.3f) * 0.3f;
        float k = 1f - depth + 2f * depth * n;
        lamp.intensity = baseIntensity * k;
        lamp.range = baseRange * (1f - rangeWobble + 2f * rangeWobble * n);
    }

    void OnDisable()
    {
        if (lamp != null) { lamp.intensity = baseIntensity; lamp.range = baseRange; }
    }
}
