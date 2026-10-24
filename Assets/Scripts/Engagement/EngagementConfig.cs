// EngagementConfig.cs
// Purpose: The tunables of the engagement features (Assets/Resources/EngagementConfig.asset). Perfect clash: the window before the
//          strike's hit frame, the bonus XP, the short stagger and the haptic pulse; the result screen's celebration; whether the
//          website's daily challenges are used when reachable. No combat value lives here: the perfect clash never changes damage.
// Attach to: nothing (ScriptableObject, loaded from Resources)

using UnityEngine;

[CreateAssetMenu(menuName = "StrikeFit/Engagement Config", fileName = "EngagementConfig")]
public class EngagementConfig : ScriptableObject
{
    [Header("Perfect clash")]
    [Tooltip("Seconds BEFORE the monster's hit frame in which a clash counts as perfect.")]
    [Range(0.03f, 0.5f)] public float perfectClashWindowSeconds = 0.15f;
    public int perfectClashXp = 50;
    [Tooltip("The monster's short stagger after a perfect clash, applied once its attack has finished.")]
    [Range(0f, 1.5f)] public float perfectClashStaggerSeconds = 0.35f;
    [Range(0.1f, 1f)] public float perfectClashStaggerAnimScale = 0.6f;
    public Color perfectClashColor = new Color(1f, 0.97f, 0.62f);
    [Header("Perfect clash haptics (both hands)")]
    [Range(0f, 1f)] public float hapticAmplitude = 0.95f;
    public float hapticSeconds = 0.07f;
    [Range(0f, 1f)] public float hapticTailAmplitude = 0.45f;
    public float hapticTailSeconds = 0.16f;

    [Header("Result screen")]
    public int celebrationSparks = 36;
    public Color newBestColor = new Color(1f, 0.82f, 0.25f);
    public Color rankUpColor = new Color(0.55f, 0.85f, 1f);
    public Color goalColor = new Color(0.45f, 1f, 0.55f);

    [Header("Daily goals")]
    [Tooltip("Use the website's daily challenges when the headset can reach it (otherwise three seeded local goals).")]
    public bool useServerChallenges = true;

    static EngagementConfig cached;
    public static EngagementConfig Get()
    {
        if (cached != null) return cached;
        cached = Resources.Load<EngagementConfig>("EngagementConfig");
        if (cached == null) cached = CreateInstance<EngagementConfig>();
        return cached;
    }
}
