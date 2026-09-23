// SessionReporter.cs
// Posts battle/session results + live hit events to the Node backend (web/backend).
// Attach to same GameObject as RoundManager (or any persistent manager).
// Backend URL defaults match web/BACKEND_URL.json — override in Inspector for LAN/headset.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SessionReporter : MonoBehaviour
{
    public static SessionReporter Instance;

    [Header("Backend")]
    [Tooltip("Base URL only, no trailing slash. See web/BACKEND_URL.json")]
    public string backendBaseUrl = "http://localhost:4000";
    public string playerName = "Player1";
    public bool reportLiveHits = true;

    [Header("Runtime (read-only)")]
    public string currentSessionId;
    public float sessionStartedAt;
    public int hitCount;
    public bool sessionOpen;

    string SessionsUrl => backendBaseUrl.TrimEnd('/') + "/api/sessions";
    string EventsUrl => backendBaseUrl.TrimEnd('/') + "/api/events";

    void Awake()
    {
        Instance = this;
        if (string.IsNullOrWhiteSpace(playerName) && RoundManager.Instance != null)
            playerName = RoundManager.Instance.playerName;
    }

    public void BeginSession()
    {
        currentSessionId = Guid.NewGuid().ToString("N");
        sessionStartedAt = Time.realtimeSinceStartup;
        hitCount = 0;
        sessionOpen = true;
        Debug.Log($"[SessionReporter] Session started id={currentSessionId} → {backendBaseUrl}");

        StartCoroutine(PostEvent("session_start", $"Session started ({playerName})", null));
    }

    public void RegisterHit(float damage, int scoreAfter)
    {
        if (!sessionOpen)
        {
            BeginSession();
        }
        hitCount++;

        if (!reportLiveHits) return;

        var payload =
            "{\"damage\":" + damage.ToString("F2") +
            ",\"score\":" + scoreAfter +
            ",\"hitIndex\":" + hitCount + "}";

        StartCoroutine(PostEvent(
            "hit",
            $"{playerName} hit for {damage:0.#} dmg (score {scoreAfter})",
            payload));
    }

    public void ReportSessionComplete(
        string outcome,
        int score,
        float damageDealt,
        int tapCount,
        string level,
        float playerHpRemaining,
        float monsterHpRemaining,
        string notes = "")
    {
        if (!sessionOpen)
            BeginSession();

        float duration = Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartedAt);
        string startedIso = DateTime.UtcNow.AddSeconds(-duration).ToString("o");
        string endedIso = DateTime.UtcNow.ToString("o");

        string json =
            "{" +
            "\"id\":\"" + Escape(currentSessionId) + "\"," +
            "\"playerName\":\"" + Escape(playerName) + "\"," +
            "\"level\":\"" + Escape(level) + "\"," +
            "\"outcome\":\"" + Escape(outcome) + "\"," +
            "\"score\":" + score + "," +
            "\"pointsEarned\":" + score + "," +
            "\"damageDealt\":" + damageDealt.ToString("F2") + "," +
            "\"hitCount\":" + hitCount + "," +
            "\"tapCount\":" + tapCount + "," +
            "\"durationSec\":" + duration.ToString("F1") + "," +
            "\"playerHpRemaining\":" + playerHpRemaining.ToString("F1") + "," +
            "\"monsterHpRemaining\":" + monsterHpRemaining.ToString("F1") + "," +
            "\"startedAt\":\"" + Escape(startedIso) + "\"," +
            "\"endedAt\":\"" + Escape(endedIso) + "\"," +
            "\"source\":\"unity\"," +
            "\"notes\":\"" + Escape(notes) + "\"" +
            "}";

        StartCoroutine(PostJson(SessionsUrl, json, "session"));
        sessionOpen = false;
    }

    IEnumerator PostEvent(string type, string message, string payloadJsonOrNull)
    {
        string json =
            "{" +
            "\"type\":\"" + Escape(type) + "\"," +
            "\"sessionId\":\"" + Escape(currentSessionId ?? "") + "\"," +
            "\"playerName\":\"" + Escape(playerName) + "\"," +
            "\"message\":\"" + Escape(message) + "\"" +
            (payloadJsonOrNull != null ? ",\"payload\":" + payloadJsonOrNull : "") +
            "}";

        yield return PostJson(EventsUrl, json, "event");
    }

    IEnumerator PostJson(string url, string json, string label)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[SessionReporter] {label} failed: {req.error} ({url})");
            else
                Debug.Log($"[SessionReporter] {label} OK");
        }
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
