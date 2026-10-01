// GameplayBroadcastTools.cs
// Purpose: MCP for Unity tools that start/stop the WebRTC gameplay broadcast from an AI client.
//          MCP for Unity discovers [McpForUnityTool] classes on domain reload and registers them with
//          its server (127.0.0.1:8080/mcp), so no server-side change is needed.
// Scope: Editor only. MCP drives the Editor in Play mode; a Quest build is started from the website
//        instead (WebRtcStreamer.connectOnStart), since MCP never runs on the headset.
// Dependencies: McpBroadcastController (runtime), com.coplaydev.unity-mcp.

using System;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

[McpForUnityTool("start_gameplay_broadcast",
    Description = "Starts the STRIKEFIT gameplay livestream from the running game (Unity Editor in Play mode). " +
                  "Connects the game to the WebRTC signaling relay as the streamer; anyone who opens the " +
                  "returned viewerPageUrl then receives the player's view with no further action. " +
                  "Returns the signaling state, viewer counts and the viewer page URL.")]
public static class StartGameplayBroadcastTool
{
    public const string DefaultSignalingUrl = "wss://s-t-r-i-k-e-f-i-t.onrender.com/api/broadcast/ws";

    public class Parameters
    {
        [ToolParameter("WebSocket URL of the signaling relay, without a query string. Production: " +
                       DefaultSignalingUrl + "; local backend: ws://localhost:4000/api/broadcast/ws.")]
        public string signalingUrl { get; set; }

        [ToolParameter("Room to stream into; the viewer page joins it with /broadcast?room=<name>.",
            Required = false, DefaultValue = "strikefit")]
        public string room { get; set; }

        [ToolParameter("Seconds to wait for the relay to accept the connection before returning (a sleeping " +
                       "Render backend can need up to 60).", Required = false, DefaultValue = "20")]
        public int waitSeconds { get; set; }
    }

    public static async Task<object> HandleCommand(JObject @params)
    {
        if (!EditorApplication.isPlaying)
            return new ErrorResponse("not_playing", new
            {
                reason = "The broadcast streams the running game. Enter Play mode first (manage_editor action=play), then call this again.",
            });

        string url = (string)(@params?["signalingUrl"] ?? @params?["signaling_url"]);
        if (string.IsNullOrWhiteSpace(url))
            return new ErrorResponse($"'signalingUrl' is required (e.g. {DefaultSignalingUrl}).");
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            return new ErrorResponse($"'signalingUrl' must be a ws:// or wss:// URL, got '{url}'.");

        string room = (string)@params?["room"];
        int waitSeconds = Math.Clamp(@params?["waitSeconds"]?.Value<int?>() ?? @params?["wait_seconds"]?.Value<int?>() ?? 20, 0, 90);

        try
        {
            var status = await McpBroadcastController.StartBroadcastAsync(url.Trim(), room, waitSeconds);
            bool connected = status.signalingState == "Connected";
            return new SuccessResponse(
                connected
                    ? $"Broadcast ready. Open {status.viewerPageUrl} to watch; video starts as soon as the page loads."
                    : $"Streamer is still connecting to the relay ({status.signalingState}). It keeps retrying; check again with stop/start or the Console. {status.lastError}",
                status);
        }
        catch (Exception e)
        {
            return new ErrorResponse($"start_failed: {e.Message}");
        }
    }
}

[McpForUnityTool("stop_gameplay_broadcast",
    Description = "Stops the STRIKEFIT gameplay livestream: disconnects every viewer and the signaling relay and releases the capture camera.")]
public static class StopGameplayBroadcastTool
{
    public static async Task<object> HandleCommand(JObject @params)
    {
        if (!EditorApplication.isPlaying)
            return new SuccessResponse("Not in Play mode; nothing is streaming.");
        var status = await McpBroadcastController.StopBroadcastAsync();
        return new SuccessResponse("Broadcast stopped.", status);
    }
}
