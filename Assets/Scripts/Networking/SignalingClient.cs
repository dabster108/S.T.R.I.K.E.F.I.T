// SignalingClient.cs
// Purpose: WebSocket connection to the STRIKEFIT signaling relay (web/backend/broadcast.js) as the
//          "streamer". Reconnects with backoff, and hands every message and state change to the main
//          thread through MainThreadDispatcher (the socket itself runs on thread-pool threads).
// Dependencies: MainThreadDispatcher.
// Attach to: nothing; WebRtcStreamer owns one.

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Every field the relay protocol uses; JsonUtility fills what is present and leaves the rest default.
[Serializable]
public class SignalMessage
{
    public string type;
    public string id;     // welcome: our id
    public string from;   // viewer -> streamer: which viewer
    public string to;     // streamer -> viewer: which viewer
    public string sdp;
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
    public string message;
    public IceServerInfo[] iceServers; // welcome: STUN + TURN (with credentials) from the relay
}

[Serializable]
public class IceServerInfo
{
    public string[] urls;
    public string username;
    public string credential;
}

public sealed class SignalingClient : IDisposable
{
    public enum State { Disconnected, Connecting, Connected }

    // All three are raised on the main thread.
    public event Action<SignalMessage> MessageReceived;
    public event Action<State> StateChanged;
    public event Action<string> Error;

    public State CurrentState { get; private set; } = State.Disconnected;
    public string Url { get; }

    const int MinBackoffMs = 1000;
    const int MaxBackoffMs = 15000;
    const int ConnectTimeoutMs = 75000; // a sleeping Render instance can take a minute to answer

    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    CancellationTokenSource lifetime;
    ClientWebSocket socket;

    public SignalingClient(string url)
    {
        Url = url;
    }

    public void Start()
    {
        if (lifetime != null) return;
        lifetime = new CancellationTokenSource();
        _ = RunAsync(lifetime.Token);
    }

    public void Stop()
    {
        if (lifetime == null) return;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = null;
        var s = socket;
        socket = null;
        try { s?.Abort(); } catch { }
        s?.Dispose();
        SetState(State.Disconnected);
    }

    public void Dispose() => Stop();

    // Fire-and-forget; messages are dropped while disconnected (the viewer re-asks on reconnect).
    public void Send(SignalMessage msg)
    {
        var s = socket;
        if (s == null || s.State != WebSocketState.Open) return;
        _ = SendAsync(s, JsonUtility.ToJson(msg));
    }

    async Task SendAsync(ClientWebSocket s, string json)
    {
        var bytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
        await sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (s.State == WebSocketState.Open)
                await s.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            RaiseError($"Signaling send failed: {e.Message}");
        }
        finally
        {
            sendLock.Release();
        }
    }

    async Task RunAsync(CancellationToken token)
    {
        int backoff = MinBackoffMs;
        while (!token.IsCancellationRequested)
        {
            var s = new ClientWebSocket();
            s.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket = s;
            SetState(State.Connecting);
            try
            {
                using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectTimeout.CancelAfter(ConnectTimeoutMs);
                    await s.ConnectAsync(new Uri(Url), connectTimeout.Token).ConfigureAwait(false);
                }
                backoff = MinBackoffMs;
                SetState(State.Connected);
                await ReceiveLoop(s, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                RaiseError($"Signaling connection to {Url} lost: {e.Message}");
            }
            catch
            {
                // cancelled by Stop()
            }
            finally
            {
                if (socket == s) socket = null;
                s.Dispose();
            }

            if (token.IsCancellationRequested) break;
            SetState(State.Disconnected);
            try { await Task.Delay(backoff, token).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
            backoff = Math.Min(backoff * 2, MaxBackoffMs);
        }
    }

    async Task ReceiveLoop(ClientWebSocket s, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        using (var message = new MemoryStream())
        {
            while (s.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await s.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    RaiseError($"Signaling server closed the connection ({result.CloseStatus} {result.CloseStatusDescription}).");
                    return;
                }
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                MainThreadDispatcher.Enqueue(() =>
                {
                    SignalMessage msg;
                    try { msg = JsonUtility.FromJson<SignalMessage>(json); }
                    catch { return; }
                    if (msg != null && !string.IsNullOrEmpty(msg.type)) MessageReceived?.Invoke(msg);
                });
            }
        }
    }

    void SetState(State state)
    {
        MainThreadDispatcher.Run(() =>
        {
            if (CurrentState == state) return;
            CurrentState = state;
            StateChanged?.Invoke(state);
        });
    }

    void RaiseError(string text) => MainThreadDispatcher.Enqueue(() => Error?.Invoke(text));
}
