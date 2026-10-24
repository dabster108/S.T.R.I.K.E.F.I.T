// QuestFoveation.cs
// Purpose: Turns on fixed foveated rendering once the headset's XR session is running (the performance audit found it enabled as an
//          OpenXR feature but never set: the level is a runtime setting, not a serialized one). Config: Resources/QuestFoveationSettings
//          (enabled, level, dynamic). With the OpenXR loader, OVRManager.foveatedRenderingLevel calls MetaXRFoveationFeature, which
//          sets XR_FB_foveation on the live session (FBSetFoveationLevel); if that path reports foveation unsupported, Unity's own
//          XRDisplaySubsystem.foveatedRenderingLevel is used instead (the Android OpenXR settings select the SRP foveation API).
//          Visual / performance only. Logs one line with the result. Does nothing in the Editor unless an XR display is running.
//          UNVERIFIED: only the headset (OVR Metrics Tool, "FFR level") can confirm it is active.
// Dependencies: OVRManager (Meta XR SDK), UnityEngine.XR (XRDisplaySubsystem), QuestFoveationSettings
// Attach to: nothing (installs itself at start-up)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class QuestFoveation : MonoBehaviour
{
    public static bool Applied { get; private set; }
    public static string Result { get; private set; } = "not run";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var cfg = Resources.Load<QuestFoveationSettings>("QuestFoveationSettings");
        if (cfg == null || !cfg.enabledOnStart) return;
        var go = new GameObject("QuestFoveation") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<QuestFoveation>().StartCoroutine(Apply(cfg));
    }

    static IEnumerator Apply(QuestFoveationSettings cfg)
    {
        // The FB foveation call needs the OpenXR session: wait (up to ~10 s) until an XR display is running.
        var displays = new List<XRDisplaySubsystem>();
        XRDisplaySubsystem display = null;
        for (int i = 0; i < 600 && display == null; i++)
        {
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) { display = d; break; }
            if (display == null) yield return null;
        }
        if (display == null) { Result = "no running XR display (Editor without a headset?)"; yield break; }
        yield return null;   // one frame after the session starts

        if (OVRManager.fixedFoveatedRenderingSupported)
        {
            OVRManager.foveatedRenderingLevel = cfg.level;
            OVRManager.useDynamicFoveatedRendering = cfg.dynamic;
            Applied = true;
            Result = "Meta XR foveation: level " + OVRManager.foveatedRenderingLevel + ", dynamic " + cfg.dynamic;
        }
        else
        {
            display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
            display.foveatedRenderingLevel = cfg.SrpLevel;
            Applied = true;
            Result = "XR_FB_foveation not reported: XRDisplaySubsystem.foveatedRenderingLevel = " + cfg.SrpLevel.ToString("0.00");
        }
        Debug.Log("[QuestFoveation] " + Result);
    }
}
