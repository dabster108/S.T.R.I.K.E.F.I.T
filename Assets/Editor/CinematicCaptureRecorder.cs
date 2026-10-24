// CinematicCaptureRecorder.cs
// Purpose: Unity Recorder side of CinematicCapture (the landing-page demo clip). Records the Game view to an H.264 MP4 at a fixed
//          frame rate (the Recorder sets Time.captureFramerate, so game time steps 1/fps whatever the Editor's real speed), no
//          audio. Also the entry points for Unity MCP:
//            CinematicCaptureRecorder.Arm()      before entering Play mode: SessionReporter is redirected to a dead local address
//                                                 from the first scene load (CinematicCapture.RedirectIfArmed);
//            CinematicCapture.Run()              in Play mode; poll CinematicCaptureRecorder.Status() until DONE.
// Dependencies: com.unity.recorder (UnityEditor.Recorder), CinematicCapture
// Attach to: nothing (static, Editor only)

using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

[InitializeOnLoad]
public static class CinematicCaptureRecorder
{
    static RecorderController controller;
    static RecorderControllerSettings settings;

    static CinematicCaptureRecorder()
    {
        CinematicCapture.BeginRecording = Begin;
        CinematicCapture.EndRecording = End;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("StrikeFit/Demo/Arm Cinematic Capture (redirect telemetry on next Play)")]
    public static string Arm()
    {
        SessionState.SetBool(CinematicCapture.ArmedKey, true);
        return "armed: SessionReporter -> " + CinematicCapture.DeadUrl + " on the next Play";
    }

    [MenuItem("StrikeFit/Demo/Run Cinematic Capture (Play mode)")]
    static void RunMenu() => Debug.Log("[CinematicCapture] " + CinematicCapture.Run());

    public static string Status() => CinematicCapture.Status + (IsRecording ? " (recorder on)" : "") + "\n" + CinematicCapture.Log;

    public static bool IsRecording => controller != null && controller.IsRecording();

    static bool Begin(string fileNoExtension, int width, int height, int fps)
    {
        End();
        settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "CinematicCapture";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingProfile = CoreEncoderSettings.H264EncodingProfile.High,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.CaptureAlpha = false;
        movie.CaptureAudio = false;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
        movie.OutputFile = fileNoExtension;   // the Recorder adds .mp4
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = fps;
        settings.CapFrameRate = true;
        controller = new RecorderController(settings);
        controller.PrepareRecording();
        bool ok = controller.StartRecording();
        Debug.Log("[CinematicCapture] recorder " + (ok ? "started: " : "FAILED to start: ") + fileNoExtension + ".mp4 " + width + "x" + height + " @" + fps);
        return ok;
    }

    static void End()
    {
        if (controller != null && controller.IsRecording())
        {
            controller.StopRecording();
            Debug.Log("[CinematicCapture] recorder stopped");
        }
        controller = null;
        settings = null;   // left to the garbage collector: the encoder may still be closing the file
    }

    static void OnPlayMode(PlayModeStateChange c)
    {
        if (c == PlayModeStateChange.ExitingPlayMode) { End(); SessionState.EraseBool(CinematicCapture.ArmedKey); }
    }
}
