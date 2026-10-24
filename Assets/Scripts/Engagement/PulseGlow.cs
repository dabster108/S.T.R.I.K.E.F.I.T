// PulseGlow.cs
// Purpose: A soft breathing glow behind the most important button (Play Again on the result panel). Changes one Graphic's alpha on
//          unscaled time; no allocation. Visual only.
// Attach to: the glow Image (added by OutcomeMenuButtons)

using UnityEngine;
using UnityEngine.UI;

public class PulseGlow : MonoBehaviour
{
    public Graphic graphic;
    public Color color = new Color(0.45f, 1f, 0.55f, 1f);
    public float minAlpha = 0.12f, maxAlpha = 0.5f, hz = 0.9f;

    void Update()
    {
        if (graphic == null) return;
        float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * hz * 6.2831853f);
        var c = color; c.a = Mathf.Lerp(minAlpha, maxAlpha, k);
        graphic.color = c;
    }
}
