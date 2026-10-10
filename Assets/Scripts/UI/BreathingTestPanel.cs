// BreathingTestPanel.cs
// Purpose: The breathing hardware test on the main menu (Development builds only). Hidden until both grips are held for 3 s
//          (DevDebugGate); then a small panel shows a laser "Test breathing" button and a one-line readout of the exertion,
//          tier and both source volumes plus the measured listener output. The button plays the heavy loop for 5 s, then the
//          fast loop for 5 s, bypassing the exertion logic, through the same sources, caps and clips the fights use
//          (PlayerExertionAudio.HardwareTest). The readout text changes only when its shown values change.
// Dependencies: PlayerExertionAudio (on the menu rig), DevDebugGate
// Attach to: MenuSystems in MainMenu.unity (built by MenuSceneBuilder)

using UnityEngine;
using UnityEngine.UI;

public class BreathingTestPanel : MonoBehaviour
{
    public GameObject panel;
    public Button testButton;
    public Text readout;
    [Tooltip("Seconds each tier plays.")]
    public float secondsPerTier = 5f;

    private string shown = "";
    private bool wasOn;

    void Awake()
    {
        if (panel != null) panel.SetActive(false);
        if (testButton != null) testButton.onClick.AddListener(Run);
    }

    void Update()
    {
        DevDebugGate.Poll();
        bool on = DevDebugGate.On;
        if (on != wasOn)
        {
            wasOn = on;
            if (panel != null) panel.SetActive(on);
            shown = null;
        }
        if (!on || readout == null) return;
        var ex = PlayerExertionAudio.Instance;
        string now = ex == null ? "no PlayerExertionAudio in this scene"
            : ex.HardwareTestRunning || !string.IsNullOrEmpty(ex.HardwareTestStatus)
                ? ex.HardwareTestStatus
                : $"effort {ex.Exertion01:0.00} {ex.CurrentTier.ToString().ToUpperInvariant()}  heavy {ex.MediumVolume:0.00}  fast {ex.FastVolume:0.00}";
        if (now == shown) return;
        shown = now;
        readout.text = now;
    }

    void Run()
    {
        var ex = PlayerExertionAudio.Instance;
        if (ex == null || ex.HardwareTestRunning) return;
        StartCoroutine(ex.HardwareTest(secondsPerTier));
    }
}
