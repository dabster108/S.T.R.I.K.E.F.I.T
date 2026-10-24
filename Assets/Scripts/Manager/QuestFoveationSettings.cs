// QuestFoveationSettings.cs
// Purpose: Config of QuestFoveation (asset: Assets/Resources/QuestFoveationSettings.asset): whether fixed foveated rendering is
//          switched on at start-up, the level (High by default) and whether the runtime may lower it dynamically.
// Dependencies: OVRManager (FoveatedRenderingLevel)
// Attach to: nothing (ScriptableObject)

using UnityEngine;

[CreateAssetMenu(menuName = "StrikeFit/Quest Foveation Settings", fileName = "QuestFoveationSettings")]
public class QuestFoveationSettings : ScriptableObject
{
    public bool enabledOnStart = true;
    public OVRManager.FoveatedRenderingLevel level = OVRManager.FoveatedRenderingLevel.High;
    [Tooltip("Let the runtime lower the level when the GPU has headroom (Meta's dynamic FFR).")]
    public bool dynamic = true;

    /// <summary>Unity's SRP foveation takes 0..1.</summary>
    public float SrpLevel
    {
        get
        {
            switch (level)
            {
                case OVRManager.FoveatedRenderingLevel.Off: return 0f;
                case OVRManager.FoveatedRenderingLevel.Low: return 0.33f;
                case OVRManager.FoveatedRenderingLevel.Medium: return 0.66f;
                default: return 1f;
            }
        }
    }
}
