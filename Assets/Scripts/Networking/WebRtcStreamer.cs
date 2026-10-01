// WebRtcStreamer.cs
// Purpose: Streams the player's view to strikefit.vercel.app/broadcast over WebRTC, with no input on the
//          headset. On launch it connects to the signaling relay (web/backend/broadcast.js) and idles;
//          when a browser opens /broadcast the relay forwards "start", and only then does this script
//          create the capture camera, the video track and a peer connection for that viewer. When the
//          last viewer leaves, the capture is torn down again, so an unwatched game pays nothing.
// Capture: a mono "BroadcastCamera" child of the HMD camera renders into a RenderTexture at
//          frameRate (not the headset's 72/90 Hz). The stereo eye buffers are left untouched.
// Encoding: H.264 is put first in the codec list; on Quest (Android Vulkan/GLES) that is the hardware
//           MediaCodec encoder. VP8 (software) remains as fallback for browsers without H.264.
// Dependencies: com.unity.webrtc 3.x, SignalingClient, MainThreadDispatcher.
// Attach to: BroadcastSystems (one persistent GameObject; McpBroadcastController adds it if missing).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class WebRtcStreamer : MonoBehaviour
{
    public static WebRtcStreamer Instance;

    [Header("Signaling")]
    [Tooltip("WebSocket URL of the relay, no query string. Must be wss:// on Quest.")]
    public string signalingUrl = "wss://s-t-r-i-k-e-f-i-t.onrender.com/api/broadcast/ws";
    [Tooltip("Used instead of signalingUrl when connectOnStart runs in the Unity Editor. Empty = the Editor " +
             "uses signalingUrl (the Render backend), same as the Quest build.")]
    // For a local backend (cd web/backend && npm start) set this to: ws://localhost:4000/api/broadcast/ws
    public string editorSignalingUrl = "";
    [Tooltip("Room name; the web page joins the same room (/broadcast?room=<name>, default strikefit).")]
    public string room = "strikefit";
    [Tooltip("Only needed when the backend sets BROADCAST_KEY.")]
    public string streamerKey = "";
    [Tooltip("Connect to the relay when the scene starts, so the website can start the stream with no " +
             "headset interaction. Off = wait for StartSignaling() (e.g. from the MCP tool).")]
    public bool connectOnStart = true;

    [Header("Capture")]
    [Tooltip("Camera whose pose is streamed. Empty = Camera.main (the HMD camera).")]
    public Camera sourceCamera;
    public int width = 1280;
    public int height = 720;
    [Tooltip("Frames per second rendered for the stream. Each frame is an extra mono render, so keep it low on Quest.")]
    [Range(10, 60)] public int frameRate = 30;
    [Tooltip("Upper bound for the encoder; WebRTC adapts below it to the network.")]
    public int maxBitrateKbps = 3500;
    [Range(30f, 110f)] public float fieldOfView = 80f;
    [Tooltip("Peer connections at once. Every viewer is a separate encode on the headset.")]
    [Range(1, 4)] public int maxViewers = 2;

    [Header("ICE")]
    public string[] stunUrls =
    {
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302",
        "stun:stun2.l.google.com:19302",
    };

    [Header("Runtime (read-only)")]
    public string signalingState = "Disconnected";
    public int viewerCount;
    public int liveViewerCount;
    public bool capturing;
    public string lastError = "";

    public event Action StatusChanged;

    class Viewer
    {
        public string id;
        public RTCPeerConnection pc;
        public bool remoteDescriptionSet;
        public bool connected;
        public readonly List<RTCIceCandidateInit> pendingCandidates = new List<RTCIceCandidateInit>();
    }

    readonly Dictionary<string, Viewer> viewers = new Dictionary<string, Viewer>();
    SignalingClient signaling;
    Camera captureCamera;
    RenderTexture captureTexture;
    VideoStreamTrack videoTrack;
    Coroutine webRtcUpdate;
    float nextCaptureAt;

    public bool IsSignalingActive => signaling != null;
    public string ActiveSignalingUrl => signaling?.Url;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WebRtcStreamer] A second streamer was found and disabled; keep one per game.");
            enabled = false;
            return;
        }
        Instance = this;
        // The headset must keep running (and rendering) while an operator watches remotely.
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.runInBackground = true;
    }

    void Start()
    {
        if (!connectOnStart || IsSignalingActive) return;
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(editorSignalingUrl))
        {
            StartSignaling(editorSignalingUrl);
            return;
        }
#endif
        StartSignaling(signalingUrl);
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        StopSignaling();
        Instance = null;
    }

    void Update()
    {
        if (captureCamera == null) return;
        // Render the mono camera only at the stream's frame rate, not at the display refresh rate.
        float now = Time.unscaledTime;
        bool due = now >= nextCaptureAt;
        captureCamera.enabled = due;
        if (due) nextCaptureAt = Mathf.Max(nextCaptureAt + 1f / frameRate, now);
    }

    // ---------------- Signaling ----------------

    // Connects to the relay as this room's streamer (replacing any earlier connection). Main thread only.
    public void StartSignaling(string url)
    {
        StopSignaling();
        if (!string.IsNullOrWhiteSpace(url)) signalingUrl = url.Trim();
        string full = BuildStreamerUrl(signalingUrl);
        if (full == null)
        {
            Fail($"Signaling URL '{signalingUrl}' is not a ws:// or wss:// URL.");
            return;
        }
#if !UNITY_EDITOR
        if (!full.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            Debug.LogWarning($"[WebRtcStreamer] '{signalingUrl}' is not wss://; Quest may block it.");
#endif
        lastError = "";
        // The relay answers a missing/wrong key with 401, which ClientWebSocket only reports as
        // "Unable to connect to the remote server", so say it plainly here.
        if (string.IsNullOrEmpty(streamerKey))
            Debug.LogWarning("[WebRtcStreamer] Streamer Key is empty. If the backend sets BROADCAST_KEY, the relay " +
                             "refuses this connection (shown below as 'Unable to connect to the remote server').");
        signaling = new SignalingClient(full);
        signaling.StateChanged += OnSignalingState;
        signaling.MessageReceived += OnSignal;
        signaling.Error += e => Debug.LogWarning($"[WebRtcStreamer] {e}");
        signaling.Start();
        Debug.Log($"[WebRtcStreamer] Waiting for viewers via {signalingUrl} (room '{room}').");
    }

    public void StopSignaling()
    {
        foreach (var id in viewers.Keys.ToList()) CloseViewer(id);
        ReleaseCapture();
        if (signaling == null) return;
        signaling.StateChanged -= OnSignalingState;
        signaling.MessageReceived -= OnSignal;
        signaling.Dispose();
        signaling = null;
        signalingState = SignalingClient.State.Disconnected.ToString();
        StatusChanged?.Invoke();
    }

    string BuildStreamerUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss")) return null;
        string q = $"role=streamer&room={Uri.EscapeDataString(string.IsNullOrWhiteSpace(room) ? "strikefit" : room.Trim())}";
        if (!string.IsNullOrEmpty(streamerKey)) q += $"&key={Uri.EscapeDataString(streamerKey)}";
        return $"{uri.GetLeftPart(UriPartial.Path)}?{q}";
    }

    void OnSignalingState(SignalingClient.State state)
    {
        signalingState = state.ToString();
        // The relay tells viewers the headset dropped; they re-send "start" when it is back.
        if (state != SignalingClient.State.Connected)
            foreach (var id in viewers.Keys.ToList()) CloseViewer(id);
        StatusChanged?.Invoke();
    }

    void OnSignal(SignalMessage msg)
    {
        switch (msg.type)
        {
            case "welcome":
                Debug.Log($"[WebRtcStreamer] Signaling connected (room '{room}').");
                break;
            case "start":
                StartViewer(msg.from);
                break;
            case "answer":
                if (viewers.TryGetValue(msg.from ?? "", out var v)) StartCoroutine(ApplyAnswer(v, msg.sdp));
                break;
            case "candidate":
                AddRemoteCandidate(msg);
                break;
            case "stop":
            case "viewer-left":
                CloseViewer(msg.from);
                break;
        }
    }

    void SendTo(string viewerId, SignalMessage msg)
    {
        msg.to = viewerId;
        signaling?.Send(msg);
    }

    // ---------------- Peers ----------------

    void StartViewer(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        CloseViewer(id); // a repeated "start" restarts that viewer's connection
        if (viewers.Count >= maxViewers)
        {
            SendTo(id, new SignalMessage { type = "error", message = $"The headset is already streaming to {viewers.Count} viewer(s), its limit." });
            return;
        }
        if (!EnsureCapture())
        {
            SendTo(id, new SignalMessage { type = "error", message = $"The headset could not start capturing: {lastError}" });
            return;
        }

        var config = new RTCConfiguration
        {
            iceServers = new[] { new RTCIceServer { urls = stunUrls } },
        };
        var viewer = new Viewer { id = id, pc = new RTCPeerConnection(ref config) };
        viewers[id] = viewer;

        viewer.pc.OnIceCandidate = c =>
        {
            if (!IsCurrent(viewer) || string.IsNullOrEmpty(c.Candidate)) return;
            SendTo(id, new SignalMessage { type = "candidate", candidate = c.Candidate, sdpMid = c.SdpMid, sdpMLineIndex = c.SdpMLineIndex ?? 0 });
        };
        viewer.pc.OnConnectionStateChange = state =>
        {
            if (!IsCurrent(viewer)) return;
            viewer.connected = state == RTCPeerConnectionState.Connected;
            Debug.Log($"[WebRtcStreamer] Viewer {id}: {state}");
            // Never dispose a peer connection inside its own callback; close it on the next frame.
            if (state == RTCPeerConnectionState.Failed || state == RTCPeerConnectionState.Closed)
                MainThreadDispatcher.Enqueue(() => { if (IsCurrent(viewer)) CloseViewer(id); });
            else UpdateCounts();
        };

        var transceiver = viewer.pc.AddTransceiver(videoTrack, new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.SendOnly,
            sendEncodings = new[]
            {
                new RTCRtpEncodingParameters
                {
                    active = true,
                    maxBitrate = (ulong)Mathf.Max(300, maxBitrateKbps) * 1000UL,
                    maxFramerate = (uint)frameRate,
                },
            },
        });
        var codecs = RTCRtpSender.GetCapabilities(TrackKind.Video).codecs;
        var ordered = codecs.Where(c => c.mimeType == "video/H264").Concat(codecs.Where(c => c.mimeType != "video/H264")).ToArray();
        var codecError = transceiver.SetCodecPreferences(ordered);
        if (codecError != RTCErrorType.None) Debug.LogWarning($"[WebRtcStreamer] SetCodecPreferences: {codecError}");

        UpdateCounts();
        StartCoroutine(SendOffer(viewer));
    }

    IEnumerator SendOffer(Viewer viewer)
    {
        var offerOp = viewer.pc.CreateOffer();
        yield return offerOp;
        if (!IsCurrent(viewer)) yield break;
        if (offerOp.IsError)
        {
            FailViewer(viewer, $"CreateOffer failed: {offerOp.Error.message}");
            yield break;
        }

        var desc = offerOp.Desc;
        var localOp = viewer.pc.SetLocalDescription(ref desc);
        yield return localOp;
        if (!IsCurrent(viewer)) yield break;
        if (localOp.IsError)
        {
            FailViewer(viewer, $"SetLocalDescription failed: {localOp.Error.message}");
            yield break;
        }
        SendTo(viewer.id, new SignalMessage { type = "offer", sdp = desc.sdp });
    }

    IEnumerator ApplyAnswer(Viewer viewer, string sdp)
    {
        if (string.IsNullOrEmpty(sdp)) yield break;
        var desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
        var op = viewer.pc.SetRemoteDescription(ref desc);
        yield return op;
        if (!IsCurrent(viewer)) yield break;
        if (op.IsError)
        {
            FailViewer(viewer, $"SetRemoteDescription failed: {op.Error.message}");
            yield break;
        }
        viewer.remoteDescriptionSet = true;
        foreach (var c in viewer.pendingCandidates) viewer.pc.AddIceCandidate(new RTCIceCandidate(c));
        viewer.pendingCandidates.Clear();
    }

    void AddRemoteCandidate(SignalMessage msg)
    {
        if (string.IsNullOrEmpty(msg.candidate) || !viewers.TryGetValue(msg.from ?? "", out var viewer)) return;
        var init = new RTCIceCandidateInit { candidate = msg.candidate, sdpMid = msg.sdpMid, sdpMLineIndex = msg.sdpMLineIndex };
        if (viewer.remoteDescriptionSet) viewer.pc.AddIceCandidate(new RTCIceCandidate(init));
        else viewer.pendingCandidates.Add(init);
    }

    bool IsCurrent(Viewer viewer) => viewers.TryGetValue(viewer.id, out var v) && v == viewer;

    void FailViewer(Viewer viewer, string error)
    {
        Fail(error);
        SendTo(viewer.id, new SignalMessage { type = "error", message = error });
        CloseViewer(viewer.id);
    }

    void CloseViewer(string id)
    {
        if (string.IsNullOrEmpty(id) || !viewers.TryGetValue(id, out var viewer)) return;
        viewers.Remove(id);
        viewer.pc.OnIceCandidate = null;
        viewer.pc.OnConnectionStateChange = null;
        viewer.pc.Close();
        viewer.pc.Dispose();
        if (viewers.Count == 0) ReleaseCapture();
        UpdateCounts();
    }

    void UpdateCounts()
    {
        viewerCount = viewers.Count;
        liveViewerCount = viewers.Values.Count(v => v.connected);
        StatusChanged?.Invoke();
    }

    // ---------------- Capture ----------------

    bool EnsureCapture()
    {
        if (videoTrack != null) return true;
        var source = sourceCamera != null ? sourceCamera : Camera.main;
        if (source == null)
        {
            Fail("No camera to stream: assign sourceCamera or tag the HMD camera MainCamera.");
            return false;
        }

        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
        captureTexture = new RenderTexture(width, height, 24, format) { name = "BroadcastCapture" };
        captureTexture.Create();

        var go = new GameObject("BroadcastCamera");
        go.transform.SetParent(source.transform, false);
        captureCamera = go.AddComponent<Camera>();
        captureCamera.CopyFrom(source);
        // Mono, never drawn to the headset (URP's per-camera switch; stereoTargetEye is built-in RP only).
        var urp = captureCamera.GetUniversalAdditionalCameraData();
        urp.allowXRRendering = false;
        urp.antialiasing = AntialiasingMode.None;
        urp.renderShadows = source.GetUniversalAdditionalCameraData().renderShadows;
        captureCamera.targetTexture = captureTexture;
        captureCamera.fieldOfView = fieldOfView;
        captureCamera.depth = source.depth - 1;
        captureCamera.allowHDR = false;
        captureCamera.allowMSAA = false;
        captureCamera.enabled = false;
        nextCaptureAt = 0f;

        videoTrack = new VideoStreamTrack(captureTexture);
        webRtcUpdate = StartCoroutine(WebRTC.Update());
        capturing = true;
        Debug.Log($"[WebRtcStreamer] Capture started: {width}x{height} @ {frameRate} fps from '{source.name}' ({format}).");
        return true;
    }

    void ReleaseCapture()
    {
        if (webRtcUpdate != null) StopCoroutine(webRtcUpdate);
        webRtcUpdate = null;
        videoTrack?.Dispose();
        videoTrack = null;
        if (captureCamera != null) Destroy(captureCamera.gameObject);
        captureCamera = null;
        if (captureTexture != null)
        {
            captureTexture.Release();
            Destroy(captureTexture);
        }
        captureTexture = null;
        if (capturing) Debug.Log("[WebRtcStreamer] Capture stopped (no viewers).");
        capturing = false;
    }

    void Fail(string error)
    {
        lastError = error;
        Debug.LogWarning($"[WebRtcStreamer] {error}");
        StatusChanged?.Invoke();
    }
}
