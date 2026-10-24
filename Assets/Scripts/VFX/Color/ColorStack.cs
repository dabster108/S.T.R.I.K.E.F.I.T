// ColorStack.cs
// Purpose: The colour model behind MonsterColorCompositor and PlayerWeaponGlow, as plain logic EditMode tests can check. Every colour
//          effect on a monster or a weapon is a LAYER; layers never write a renderer, they only describe what happened (a hit of a
//          given tier, a guard, a telegraph...). Evaluate() combines them for one renderer GROUP in a fixed blend order:
//            - base colour (_BaseColor, a multiplier on each material's own colour): starts at the group's palette tint, then every
//              active layer lerps toward its colour by weight x strength, lowest layer first;
//            - emission (_EmissionColor, added to each material's own emission): every active layer ADDS colour x intensity x strength;
//              the sum is clamped (largest channel <= maxEmission, hue kept) so stacked effects never blow out to white;
//            - the TELEGRAPH comes after every other effect: the base colour reaches at least telegraphMinWeight toward its colour, and
//              the other emission is clamped to (maxEmission - telegraphReserve) while it shows, so its own emission (at least
//              telegraphMinEmission, at most the reserve) is never clamped away: an attack warning stays readable over any flash;
//            - DEATH fades last: base toward the death colour, emission toward black.
//          The result depends only on the layer states and the time, never on the order the layers were set in (deterministic), and
//          nothing here allocates.
// Dependencies: none (UnityEngine Color / Mathf)
// Attach to: nothing

using UnityEngine;

namespace StrikeFit.ColorFx
{
    /// <summary>Layers in BLEND ORDER (applied lowest first; the palette base is each group's starting colour, not a layer).</summary>
    public enum ColorLayer
    {
        AuraPulse = 0,  // emission: Level 1-2 body smoulder that follows the fire aura (MonsterFireAura)
        EntranceGlow,   // emission that REPLACES the material's own emission while it shows (MonsterEntrance drop: eyes in the dark)
        BodyGlow,       // emission: Level 3 glow mask (MonsterGlow)
        WeakPointGlow,  // emission: Level 3 weak-point slot (MonsterWeakPoint)
        Wound,          // HP: base tint and a faint emission, stronger as HP drops
        Rage,           // Level 3 enrage: base tint and a rim emission
        Guard,          // cool blue-white while guarding
        Cue,            // short readability cues: guard raise, block, stagger, guard opening
        Hit,            // hit flash, colour from the swing tier that caused it
        Strike,         // weapon flash when an attack lands (monster weapon; the player's weapons when the player is hurt)
        ClashRim,       // body rim pulse on a clash / parry
        Clash,          // weapon flash on a clash / parry (the monster's weapon and the player's weapon)
        Telegraph,      // attack warning: pulses, guaranteed minimum, never clamped away
        Death,          // fade out
        Count
    }

    /// <summary>Renderer groups. Each renderer slot belongs to one group; each layer covers a mask of groups.</summary>
    [System.Flags]
    public enum ColorGroup
    {
        None = 0,
        Body = 1, Accent = 2, Glow = 4, WeakPoint = 8, Weapon = 16, WeaponAccent = 32,
        AllBody = Body | Accent | Glow | WeakPoint,
        AllWeapon = Weapon | WeaponAccent,
        All = AllBody | AllWeapon,
    }

    public enum LayerCurve
    {
        Hold,    // 1 until the duration ends (duration <= 0: until cleared)
        Decay,   // flash: 1 -> 0 over the duration, ease-out ((1 - t)^2)
        Pulse,   // 0..1 cosine at pulseHz, starting bright, until the duration ends
        FadeIn,  // 0 -> 1 (smoothstep) over the duration, then stays at 1 until cleared
    }

    /// <summary>Swing tiers of a hit (the same four tiers as the telemetry and the damage numbers), plus a weak-point hit.</summary>
    public enum SwingTierFx { Tap = 0, Light = 1, Medium = 2, Full = 3, WeakPoint = 4 }

    public struct LayerState
    {
        public bool active;
        public Color color;      // base-colour target
        public Color emission;   // emission colour (often the same as color)
        public float weight;     // base-colour lerp weight at full strength (0 = emission only)
        public float intensity;  // emission = emission colour x intensity x strength
        public float start, duration, delay;
        public LayerCurve curve;
        public float pulseHz;
        public ColorGroup mask;
        public bool replacesMaterialEmission;
    }

    [System.Serializable]
    public struct BlendSettings
    {
        [Tooltip("Largest channel of the summed emission. Keeps stacked effects from blowing out to white.")]
        public float maxEmission;
        [Tooltip("While a telegraph shows, the base colour reaches at least this far toward its colour.")]
        public float telegraphMinWeight;
        [Tooltip("The telegraph's own emission (largest channel) never drops below this.")]
        public float telegraphMinEmission;
        [Tooltip("Emission headroom kept for the telegraph: the other layers are clamped to maxEmission minus this while it shows.")]
        public float telegraphReserve;

        public static BlendSettings Default => new BlendSettings { maxEmission = 1.6f, telegraphMinWeight = 0.55f, telegraphMinEmission = 0.35f, telegraphReserve = 0.6f };
    }

    public struct GroupResult
    {
        public Color baseTint;
        public Color emission;
        public bool suppressMaterialEmission;

        public bool Approximately(in GroupResult o, float eps = 0.002f) =>
            suppressMaterialEmission == o.suppressMaterialEmission && Near(baseTint, o.baseTint, eps) && Near(emission, o.emission, eps);

        static bool Near(Color a, Color b, float e) =>
            Mathf.Abs(a.r - b.r) <= e && Mathf.Abs(a.g - b.g) <= e && Mathf.Abs(a.b - b.b) <= e && Mathf.Abs(a.a - b.a) <= e;
    }

    public sealed class ColorStack
    {
        readonly LayerState[] layers = new LayerState[(int)ColorLayer.Count];
        public BlendSettings settings = BlendSettings.Default;

        /// <summary>Bumps on every change of a layer (dirty check for the writer).</summary>
        public int Version { get; private set; }

        public LayerState Get(ColorLayer id) => layers[(int)id];

        /// <summary>Starts (or restarts) a layer.</summary>
        public void Set(ColorLayer id, Color color, Color emission, float weight, float intensity, float duration, LayerCurve curve,
                        ColorGroup mask, float now, float pulseHz = 0f, float delay = 0f, bool replacesMaterialEmission = false)
        {
            layers[(int)id] = new LayerState
            {
                active = true, color = color, emission = emission, weight = Mathf.Clamp01(weight), intensity = Mathf.Max(0f, intensity),
                start = now, duration = duration, delay = Mathf.Max(0f, delay), curve = curve, pulseHz = pulseHz, mask = mask,
                replacesMaterialEmission = replacesMaterialEmission,
            };
            Version++;
        }

        /// <summary>A held layer whose values follow a source every frame (glow, wound, guard): updated in place, no restart, and the
        /// version only bumps when something actually changed.</summary>
        public void SetHeld(ColorLayer id, Color color, Color emission, float weight, float intensity, ColorGroup mask, float now,
                            bool replacesMaterialEmission = false)
        {
            ref var s = ref layers[(int)id];
            if (s.active && s.curve == LayerCurve.Hold && s.duration <= 0f && s.mask == mask && s.replacesMaterialEmission == replacesMaterialEmission &&
                Same(s.color, color) && Same(s.emission, emission) && Mathf.Abs(s.weight - weight) < 1e-4f && Mathf.Abs(s.intensity - intensity) < 1e-4f)
                return;
            Set(id, color, emission, weight, intensity, 0f, LayerCurve.Hold, mask, now, 0f, 0f, replacesMaterialEmission);
        }

        public void Clear(ColorLayer id)
        {
            if (!layers[(int)id].active) return;
            layers[(int)id].active = false;
            Version++;
        }

        /// <summary>Deactivates timed layers that have finished (Hold / Decay / Pulse past their duration).</summary>
        public void Expire(float now)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                ref var s = ref layers[i];
                if (!s.active || s.duration <= 0f || s.curve == LayerCurve.FadeIn) continue;
                if (now - s.start - s.delay >= s.duration) { s.active = false; Version++; }
            }
        }

        /// <summary>True while any active layer changes with time (a flash decaying, a pulse, a fade): the writer must re-evaluate.</summary>
        public bool Animating(float now)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                ref var s = ref layers[i];
                if (!s.active) continue;
                if (s.curve == LayerCurve.Pulse || s.curve == LayerCurve.Decay) return true;
                if (s.curve == LayerCurve.FadeIn && now - s.start < s.delay + s.duration) return true;
                if (s.delay > 0f && now - s.start < s.delay) return true;
                if (s.curve == LayerCurve.Hold && s.duration > 0f) return true;   // ends on its own: keep checking
            }
            return false;
        }

        public bool IsActive(ColorLayer id, float now) => Strength(id, now) > 0f;

        public float Strength(ColorLayer id, float now) => CurveValue(layers[(int)id], now);

        public static float CurveValue(in LayerState s, float now)
        {
            if (!s.active) return 0f;
            float t = now - s.start - s.delay;
            if (t < 0f) return 0f;
            switch (s.curve)
            {
                case LayerCurve.Hold:
                    return s.duration <= 0f || t < s.duration ? 1f : 0f;
                case LayerCurve.Decay:
                    if (s.duration <= 0f) return 1f;
                    if (t >= s.duration) return 0f;
                    float x = 1f - t / s.duration;
                    return x * x;
                case LayerCurve.Pulse:
                    if (s.duration > 0f && t >= s.duration) return 0f;
                    return s.pulseHz <= 0f ? 1f : 0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * s.pulseHz * t);
                case LayerCurve.FadeIn:
                    if (s.duration <= 0f) return 1f;
                    float u = Mathf.Clamp01(t / s.duration);
                    return u * u * (3f - 2f * u);
            }
            return 0f;
        }

        /// <summary>Scales the colour down so its largest channel is at most <paramref name="max"/> (hue kept). Alpha is set to 1.</summary>
        public static Color ClampEmission(Color e, float max)
        {
            float m = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
            if (max <= 0f) return new Color(0f, 0f, 0f, 1f);
            if (m > max) { float s = max / m; e.r *= s; e.g *= s; e.b *= s; }
            e.a = 1f;
            return e;
        }

        static Color LerpRgb(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a);
        }

        static Color Add(Color e, Color c, float k) => new Color(e.r + c.r * k, e.g + c.g * k, e.b + c.b * k, 1f);

        static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f && Mathf.Abs(a.a - b.a) < 1e-4f;

        /// <summary>The colour of one group at <paramref name="now"/>: the palette base, then every active layer in blend order.</summary>
        public GroupResult Evaluate(ColorGroup group, Color baseTint, Color baseEmission, float now)
        {
            Color b = baseTint;
            Color e = new Color(Mathf.Max(0f, baseEmission.r), Mathf.Max(0f, baseEmission.g), Mathf.Max(0f, baseEmission.b), 1f);
            bool suppress = false;
            int telegraph = (int)ColorLayer.Telegraph, death = (int)ColorLayer.Death;

            for (int i = 0; i < telegraph; i++)
            {
                ref var s = ref layers[i];
                if (!s.active || (s.mask & group) == 0) continue;
                float k = CurveValue(s, now);
                if (k <= 0f) continue;
                if (s.replacesMaterialEmission) suppress = true;
                if (s.weight > 0f) b = LerpRgb(b, s.color, s.weight * k);
                if (s.intensity > 0f) e = Add(e, s.emission, s.intensity * k);
            }

            ref var tl = ref layers[telegraph];
            float tk = (tl.active && (tl.mask & group) != 0) ? CurveValue(tl, now) : 0f;
            bool telegraphOn = tl.active && (tl.mask & group) != 0 && (tl.duration <= 0f || now - tl.start - tl.delay < tl.duration) && now - tl.start >= tl.delay;
            if (telegraphOn)
            {
                float reserve = Mathf.Clamp(settings.telegraphReserve, 0f, settings.maxEmission);
                e = ClampEmission(e, settings.maxEmission - reserve);
                b = LerpRgb(b, tl.color, Mathf.Max(settings.telegraphMinWeight, tl.weight * tk));
                float m = Mathf.Max(tl.emission.r, Mathf.Max(tl.emission.g, tl.emission.b));
                if (m > 1e-4f)
                {
                    float level = Mathf.Clamp(tl.intensity * tk, Mathf.Min(settings.telegraphMinEmission, reserve), reserve);
                    e = Add(e, tl.emission, level / m);   // the telegraph's largest channel is exactly `level`
                }
            }
            else e = ClampEmission(e, settings.maxEmission);

            ref var dl = ref layers[death];
            if (dl.active && (dl.mask & group) != 0)
            {
                float k = CurveValue(dl, now);
                if (k > 0f)
                {
                    b = LerpRgb(b, dl.color, dl.weight * k);
                    float keep = 1f - k;
                    e = new Color(e.r * keep, e.g * keep, e.b * keep, 1f);
                }
            }
            return new GroupResult { baseTint = b, emission = e, suppressMaterialEmission = suppress };
        }
    }
}
