// McpBroadcastController.cs
// Purpose: The automation entry point for the headset livestream. The MCP tools
//          start_gameplay_broadcast / stop_gameplay_broadcast (Assets/Editor/GameplayBroadcastTools.cs)
//          and any other remote caller go through these static methods. Each one may be called from any
//          thread: the work is routed to Unity's main thread through MainThreadDispatcher and the
//          result comes back as a Task.
// Dependencies: WebRtcStreamer, MainThreadDispatcher.
// Attach to: BroadcastSystems (persistent). If no controller exists when a call arrives, one is
//            created on a new DontDestroyOnLoad "BroadcastSystems" object together with a WebRtcStreamer.

using System;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class BroadcastStatus
{
    public string signalingUrl;
    public string room;
    public string signalingState;
    public int viewers;
    public int liveViewers;
    public bool capturing;
    public string lastError;
    public string viewerPageUrl;
}

[DisallowMultipleComponent]
public class McpBroadcastController : MonoBehaviour
{
    public static McpBroadcastController Instance;

    [Tooltip("Public page that shows the stream; returned to MCP callers so they can hand it to a person.")]
    public string viewerPageUrl = "https://strikefit.vercel.app/broadcast";

    WebRtcStreamer streamer;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        // Persist across level loads; only a root object can be marked DontDestroyOnLoad.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        else Debug.LogWarning("[McpBroadcastController] Put BroadcastSystems at the scene root so the stream survives level loads.");
        streamer = GetComponent<WebRtcStreamer>();
        if (streamer == null) streamer = gameObject.AddComponent<WebRtcStreamer>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------------- Thread-safe entry points ----------------

    // Connects the streamer to the relay and resolves once signaling is connected or after waitSeconds,
    // whichever comes first. Viewers on /broadcast then get video with no headset interaction.
    public static Task<BroadcastStatus> StartBroadcastAsync(string signalingUrl, string room = null, float waitSeconds = 15f)
    {
        var done = new TaskCompletionSource<BroadcastStatus>();
        MainThreadDispatcher.Run(() =>
        {
            try
            {
                var c = EnsureInstance();
                if (!string.IsNullOrWhiteSpace(room)) c.streamer.room = room.Trim();
                c.streamer.StartSignaling(signalingUrl);
                if (!c.streamer.IsSignalingActive)
                {
                    done.TrySetResult(c.Snapshot());
                    return;
                }
                c.StartCoroutine(c.WaitForSignaling(waitSeconds, done));
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        });
        return done.Task;
    }

    public static Task<BroadcastStatus> StopBroadcastAsync()
    {
        var done = new TaskCompletionSource<BroadcastStatus>();
        MainThreadDispatcher.Run(() =>
        {
            try
            {
                if (Instance != null) Instance.streamer.StopSignaling();
                done.TrySetResult(Instance != null ? Instance.Snapshot() : null);
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        });
        return done.Task;
    }

    public static Task<BroadcastStatus> GetStatusAsync()
    {
        var done = new TaskCompletionSource<BroadcastStatus>();
        MainThreadDispatcher.Run(() => done.TrySetResult(Instance != null ? Instance.Snapshot() : null));
        return done.Task;
    }

    // ---------------- Main thread ----------------

    static McpBroadcastController EnsureInstance()
    {
        if (Instance != null) return Instance;
        var existing = WebRtcStreamer.Instance;
        var go = existing != null ? existing.gameObject : new GameObject("BroadcastSystems");
        if (existing == null)
        {
            // Created on demand: the explicit StartSignaling call decides the URL, not connectOnStart.
            var s = go.AddComponent<WebRtcStreamer>();
            s.connectOnStart = false;
        }
        return go.AddComponent<McpBroadcastController>();
    }

    System.Collections.IEnumerator WaitForSignaling(float waitSeconds, TaskCompletionSource<BroadcastStatus> done)
    {
        float until = Time.realtimeSinceStartup + Mathf.Max(0f, waitSeconds);
        while (Time.realtimeSinceStartup < until && streamer.signalingState != nameof(SignalingClient.State.Connected))
            yield return null;
        done.TrySetResult(Snapshot());
    }

    BroadcastStatus Snapshot()
    {
        string pageUrl = viewerPageUrl;
        if (!string.IsNullOrEmpty(streamer.room) && streamer.room != "strikefit")
            pageUrl += "?room=" + Uri.EscapeDataString(streamer.room);
        return new BroadcastStatus
        {
            signalingUrl = streamer.IsSignalingActive ? streamer.signalingUrl : null,
            room = streamer.room,
            signalingState = streamer.signalingState,
            viewers = streamer.viewerCount,
            liveViewers = streamer.liveViewerCount,
            capturing = streamer.capturing,
            lastError = streamer.lastError,
            viewerPageUrl = pageUrl,
        };
    }
}
