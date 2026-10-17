// ControlSettings.cs
// Purpose: The one place for the controller-mapping and pause-menu toggles. A ScriptableObject loaded from Resources
//          ("ControlSettings"), so the same values apply to the menu and all three levels without a per-scene copy. If the
//          asset is missing the defaults below are used, so nothing depends on it existing.
// Dependencies: none
// Attach to: nothing (asset at Assets/Resources/ControlSettings.asset)

using UnityEngine;

public enum UiHand { Right, Left }

[CreateAssetMenu(menuName = "StrikeFit/Control Settings", fileName = "ControlSettings")]
public class ControlSettings : ScriptableObject
{
    [Header("Controller mapping")]
    [Tooltip("Master toggle. Off = the menu and panels behave exactly as before this feature (either trigger clicks, the old laser).")]
    public bool mappingEnabled = true;
    [Tooltip("Which hand points the laser and clicks. A PlayerPrefs value (StrikeFit.UiHand: 0 right, 1 left) overrides this once set.")]
    public UiHand defaultHand = UiHand.Right;
    [Tooltip("Off (default): only the pointing hand shows a laser and clicks. On: the other hand's trigger moves the laser to that hand.")]
    public bool leftLaser = false;
    [Tooltip("Off (default): the left Menu button does nothing. On: it pauses and resumes like B.")]
    public bool leftMenuButtonPauses = false;
    [Tooltip("A button is acted on at most once per this many real seconds, and only on button-down.")]
    [Range(0.05f, 1f)] public float debounceSeconds = 0.25f;
    [Tooltip("Laser length (m) when nothing is hit.")]
    [Range(0.5f, 10f)] public float laserLength = 3f;
    [Tooltip("Reticle diameter (m) at 1 m from the hand; it grows with distance so it keeps the same apparent size.")]
    [Range(0.005f, 0.05f)] public float reticleSize = 0.018f;

    [Header("Haptics")]
    [Tooltip("A short tick on the pointing hand when the laser moves onto a button, and a firmer one on a click.")]
    public bool hapticTick = true;
    [Range(0f, 1f)] public float hoverAmplitude = 0.12f;
    public float hoverSeconds = 0.02f;
    [Range(0f, 1f)] public float clickAmplitude = 0.4f;
    public float clickSeconds = 0.04f;

    [Header("Pause menu")]
    [Tooltip("Master toggle. Off = B does nothing in a fight and no pause component is created.")]
    public bool pauseEnabled = true;
    [Tooltip("Pause when the headset is taken off or the app loses VR focus (never resumes by itself).")]
    public bool autoPause = true;
    [Tooltip("Music volume while paused, as a share of its normal volume.")]
    [Range(0f, 1f)] public float pauseMusicDuck = 0.4f;
    [Tooltip("Restart, Level Select and Exit ask once before ending the fight.")]
    public bool confirmDestructive = true;
    [Tooltip("Show \"B: pause\" at the start of a fight, once per launch.")]
    public bool showPauseHint = true;
    public float pauseHintSeconds = 5f;
    [Tooltip("Distance (m) in front of the head where the pause panel is placed.")]
    [Range(1f, 3f)] public float panelDistance = 1.8f;
    [Tooltip("Log one [StrikeFitPause] line per pause event (adb logcat -s Unity:I | grep StrikeFitPause).")]
    public bool logEvents = true;

    static ControlSettings cached;
    static bool loaded;

    /// <summary>The asset from Resources, or a default instance (not saved) when there is none.</summary>
    public static ControlSettings Current
    {
        get
        {
            if (!loaded || cached == null)
            {
                cached = Resources.Load<ControlSettings>("ControlSettings");
                if (cached == null) cached = CreateInstance<ControlSettings>();
                loaded = true;
            }
            return cached;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cached = null; loaded = false; }
}
