// ArenaMoodController.cs
// Purpose: Level 3 arena lighting that escalates with the boss fight, in the same colours as the boss's glow
//          (MonsterGlow): a dim, cold, stormy night at full HP (denser fog, wind-flickered lamps), a first warm shift
//          in phase 2, a harsh blood-orange moon / red fog / roaring lamps while Enraged, hotter still at the very end;
//          on victory the storm clears (cool bright moon, thin fog, steady lamps). Blends are smooth and run on real
//          time, so the victory slow motion does not stall them.
// Dependencies: BattleManager (ActiveMonster, OnVictory), MonsterHealth, MonsterAI (enraged)
// Attach to: any scene object (Level 3: BattleSystems/ArenaMood). Authored scene lighting is read in Awake and
//            restored when this is destroyed.

using UnityEngine;

public class ArenaMoodController : MonoBehaviour
{
    [System.Serializable]
    public struct Mood
    {
        public Color moonColor;
        public float moonIntensity;
        [Tooltip("Multiplies the scene's authored trilight ambient colours.")]
        public Color ambientTint;
        public Color fogColor;
        public float fogDensity;
        [Tooltip("Lamp intensity multiplier and colour tint.")]
        public float lampScale;
        public Color lampTint;
        [Tooltip("Lamp flicker depth (0 = steady).")]
        [Range(0f, 1f)] public float lampFlicker;
    }

    public Light moon;
    public Light[] lamps = new Light[0];
    [Tooltip("Seconds (real time) to settle into a new mood.")]
    public float blendSeconds = 2.5f;
    [Range(0f, 1f)] public float phase2HpFraction = 0.66f;
    [Range(0f, 1f)] public float finalHpFraction = 0.15f;

    public Mood storm = new Mood { moonColor = new Color(0.55f, 0.66f, 0.9f), moonIntensity = 0.36f, ambientTint = new Color(0.82f, 0.86f, 0.95f), fogColor = new Color(0.10f, 0.12f, 0.16f), fogDensity = 0.055f, lampScale = 0.9f, lampTint = Color.white, lampFlicker = 0.18f };
    public Mood phase2 = new Mood { moonColor = new Color(0.72f, 0.6f, 0.68f), moonIntensity = 0.38f, ambientTint = new Color(0.95f, 0.82f, 0.82f), fogColor = new Color(0.13f, 0.10f, 0.12f), fogDensity = 0.05f, lampScale = 1.05f, lampTint = new Color(1f, 0.85f, 0.75f), lampFlicker = 0.25f };
    public Mood enraged = new Mood { moonColor = new Color(1f, 0.42f, 0.26f), moonIntensity = 0.62f, ambientTint = new Color(1.5f, 0.6f, 0.45f), fogColor = new Color(0.22f, 0.07f, 0.05f), fogDensity = 0.05f, lampScale = 1.4f, lampTint = new Color(1f, 0.55f, 0.35f), lampFlicker = 0.38f };
    public Mood final = new Mood { moonColor = new Color(1f, 0.62f, 0.38f), moonIntensity = 0.8f, ambientTint = new Color(1.7f, 0.85f, 0.55f), fogColor = new Color(0.28f, 0.11f, 0.05f), fogDensity = 0.048f, lampScale = 1.6f, lampTint = new Color(1f, 0.65f, 0.4f), lampFlicker = 0.45f };
    public Mood cleared = new Mood { moonColor = new Color(0.75f, 0.82f, 0.95f), moonIntensity = 0.6f, ambientTint = new Color(1.1f, 1.1f, 1.12f), fogColor = new Color(0.12f, 0.14f, 0.18f), fogDensity = 0.03f, lampScale = 1f, lampTint = Color.white, lampFlicker = 0f };

    public string CurrentMoodName { get; private set; } = "storm";

    private Mood current;
    private Color baseSky, baseEquator, baseGround, baseFog;
    private float baseFogDensity, baseMoonIntensity;
    private Color baseMoonColor;
    private float[] lampBase;
    private Color[] lampBaseColor;
    private bool victory;
    private BattleManager battle;

    void Awake()
    {
        baseSky = RenderSettings.ambientSkyColor; baseEquator = RenderSettings.ambientEquatorColor; baseGround = RenderSettings.ambientGroundColor;
        baseFog = RenderSettings.fogColor; baseFogDensity = RenderSettings.fogDensity;
        if (moon != null) { baseMoonIntensity = moon.intensity; baseMoonColor = moon.color; }
        lampBase = new float[lamps.Length]; lampBaseColor = new Color[lamps.Length];
        for (int i = 0; i < lamps.Length; i++) if (lamps[i] != null) { lampBase[i] = lamps[i].intensity; lampBaseColor[i] = lamps[i].color; }
        current = storm;
        Apply(0f);
    }

    void Start()
    {
        battle = BattleManager.Instance;
        if (battle != null) battle.OnVictory += HandleVictory;
    }

    void OnDestroy()
    {
        if (battle != null) battle.OnVictory -= HandleVictory;
        // Leave the authored lighting as it was (editor Play mode / scene reuse).
        RenderSettings.ambientSkyColor = baseSky; RenderSettings.ambientEquatorColor = baseEquator; RenderSettings.ambientGroundColor = baseGround;
        RenderSettings.fogColor = baseFog; RenderSettings.fogDensity = baseFogDensity;
        if (moon != null) { moon.intensity = baseMoonIntensity; moon.color = baseMoonColor; }
        for (int i = 0; i < lamps.Length; i++) if (lamps[i] != null) { lamps[i].intensity = lampBase[i]; lamps[i].color = lampBaseColor[i]; }
    }

    void HandleVictory(int score) { victory = true; }

    void Update()
    {
        if (GamePause.IsPaused) return;   // the mood blends on real time; it holds while paused
        Mood target = storm; CurrentMoodName = "storm";
        var m = battle != null ? battle.ActiveMonster : null;
        if (victory || (m != null && !m.isAlive)) { target = cleared; CurrentMoodName = "cleared"; }
        else if (m != null)
        {
            var h = m.GetComponent<MonsterHealth>();
            var ai = m.GetComponent<MonsterAI>();
            float hp = h != null && h.maxHealth > 0f ? h.currentHealth / h.maxHealth : 1f;
            bool enr = ai != null && ai.enraged;
            if (enr && hp <= finalHpFraction) { target = final; CurrentMoodName = "final"; }
            else if (enr) { target = enraged; CurrentMoodName = "enraged"; }
            else if (hp <= phase2HpFraction) { target = phase2; CurrentMoodName = "phase2"; }
        }
        float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f / Mathf.Max(0.05f, blendSeconds));
        current = Lerp(current, target, k);
        Apply(Time.unscaledTime);
    }

    static Mood Lerp(Mood a, Mood b, float t) => new Mood
    {
        moonColor = Color.Lerp(a.moonColor, b.moonColor, t), moonIntensity = Mathf.Lerp(a.moonIntensity, b.moonIntensity, t),
        ambientTint = Color.Lerp(a.ambientTint, b.ambientTint, t), fogColor = Color.Lerp(a.fogColor, b.fogColor, t),
        fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t), lampScale = Mathf.Lerp(a.lampScale, b.lampScale, t),
        lampTint = Color.Lerp(a.lampTint, b.lampTint, t), lampFlicker = Mathf.Lerp(a.lampFlicker, b.lampFlicker, t),
    };

    void Apply(float time)
    {
        if (moon != null) { moon.color = current.moonColor; moon.intensity = current.moonIntensity; }
        RenderSettings.ambientSkyColor = baseSky * current.ambientTint;
        RenderSettings.ambientEquatorColor = baseEquator * current.ambientTint;
        RenderSettings.ambientGroundColor = baseGround * current.ambientTint;
        RenderSettings.fogColor = current.fogColor;
        RenderSettings.fogDensity = current.fogDensity;
        for (int i = 0; i < lamps.Length; i++)
        {
            if (lamps[i] == null) continue;
            // Gusting flame: two noise octaves, a different seed per lamp.
            float n = Mathf.PerlinNoise(time * 2.3f, i * 7.1f) * 0.7f + Mathf.PerlinNoise(time * 9f, i * 3.3f + 50f) * 0.3f;
            lamps[i].intensity = lampBase[i] * current.lampScale * (1f - current.lampFlicker * n);
            lamps[i].color = lampBaseColor[i] * current.lampTint;
        }
    }
}
