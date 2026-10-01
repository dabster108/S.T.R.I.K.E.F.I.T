// MainThreadDispatcher.cs
// Purpose: Runs actions queued from any thread (WebSocket receive loop, MCP bridge) on Unity's main
//          thread in Update. Unity and WebRTC objects must only be touched from the main thread.
// Dependencies: none.
// Attach to: nothing. It creates its own hidden DontDestroyOnLoad GameObject before the first scene
//            loads; it is safe to also place one in a scene (the extra copy destroys itself).

using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class MainThreadDispatcher : MonoBehaviour
{
    static MainThreadDispatcher instance;
    static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
    static int mainThreadId = -1;

    // Actions run per frame at most, so a burst of messages can't stall a frame.
    const int MaxActionsPerFrame = 64;

    public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == mainThreadId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        if (instance != null) return;
        var go = new GameObject(nameof(MainThreadDispatcher)) { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MainThreadDispatcher>();
    }

    // Queues the action for the next Update. Callable from any thread.
    public static void Enqueue(Action action)
    {
        if (action != null) queue.Enqueue(action);
    }

    // Runs now when already on the main thread, otherwise queues it.
    public static void Run(Action action)
    {
        if (action == null) return;
        if (IsMainThread) action();
        else queue.Enqueue(action);
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        for (int i = 0; i < MaxActionsPerFrame && queue.TryDequeue(out var action); i++)
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
