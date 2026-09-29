// SessionReporter.cs
// Purpose: Streams a battle to the STRIKEFIT backend (web/backend) while it is being played:
//          live session at battle start, batched combat events + 1 s HP snapshots during the
//          fight, final stats at the end. One component, used unchanged by every level.
// Dependencies: BattleManager, HitDetection, MonsterAI, PlayerHealth, XPManager, RoundManager,
//               LevelManager, PlayerCombat (all optional at runtime).
// Attach to: BattleSystems (same GameObject as BattleManager). BattleManager adds one if missing.
//
// Networking never touches gameplay: events go into a bounded buffer, at most one request is in
// flight, requests are sent every flushInterval seconds, and any failure drops that batch silently.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class SessionReporter : MonoBehaviour
{
    public static SessionReporter Instance;

    [Header("Backend")]
    [Tooltip("Base URL only, no trailing slash. Must be https on Quest (plain http is blocked). " +
             "For a backend on this Mac use http://localhost:4000 in the Editor only.")]
    public string baseUrl = "https://s-t-r-i-k-e-f-i-t.onrender.com";
    [Tooltip("Name shown on the dashboard. Each player's history is grouped by this exact name. " +
             "Default only: PlayerNamePicker sets it in-game before the Start button appears.")]
    public string playerName = DefaultPlayerName;
    [Tooltip("Send combat events and HP snapshots during the fight (the live dashboard). " +
             "Off = only the session start and final result are sent.")]
    public bool sendLiveEvents = true;

    [Header("Pacing")]
    [Tooltip("Seconds between HP/XP snapshots while a battle is running.")]
    public float snapshotInterval = 1f;
    [Tooltip("Seconds between network sends. 0.4 = at most 2.5 requests per second.")]
    public float flushInterval = 0.4f;
    [Tooltip("Events kept while offline or slow; the oldest are dropped beyond this.")]
    public int maxBufferedEvents = 256;
    public int maxEventsPerRequest = 50;
    public int requestTimeoutSeconds = 8;
    [Tooltip("Log every request result to the Console (off in builds).")]
    public bool verboseLogging = false;

    [Header("Runtime (read-only)")]
    public string currentSessionId;
    [Tooltip("Name locked in when the battle started; every request of that battle uses it.")]
    public string sessionPlayerName;
    public bool sessionOpen;
    public int hitCount;
    public int bufferedEvents;
    public int droppedRequests;

    string SessionsUrl => baseUrl.TrimEnd('/') + "/api/sessions";
    string EventsUrl => baseUrl.TrimEnd('/') + "/api/events";

    struct PendingRequest
    {
        public string url;
        public string json;
        public string label;
    }

    readonly List<string> eventBuffer = new List<string>();
    readonly Queue<PendingRequest> requests = new Queue<PendingRequest>();
    readonly StringBuilder sb = new StringBuilder(512);
    bool requestInFlight;
    float nextFlushTime;
    float nextSnapshotTime;
    float sessionStartedAt;
    bool startPending;
    float lastFailureLogTime = -999f;

    // Latest swing seen by HitDetection, consumed by RegisterHit in the same TryHit call.
    bool hasPendingSwing;
    float pendingSwingSpeed;
    Hand pendingSwingHand;
    bool pendingSwingDefended;
    readonly float[] lastWeakSwingTime = { -999f, -999f };

    // Last monster attack, so player damage can be tagged with the attack that caused it.
    string lastAttackName = "";
    string lastAttackCategory = "";
    MonsterHealth monsterHealth;

    const float WeakSwingRepeatWindow = 0.35f;
    public const string DefaultPlayerName = "Player 1";
    const int MaxPlayerNameLength = 40; // backend limit (PATCH /api/players/:name)

    void Awake()
    {
        Instance = this;
#if !UNITY_EDITOR
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Debug.LogWarning($"[SessionReporter] baseUrl '{baseUrl}' is not https; Quest may block it.");
#endif
    }

    /// <summary>
    /// Name for the next battle. Ignored while a battle is being reported, so one fight is never split
    /// across two names. Returns the name that will be used.
    /// </summary>
    public string SetPlayerName(string name)
    {
        string clean = CleanName(name);
        if (sessionOpen)
        {
            Debug.LogWarning($"[SessionReporter] Name change to '{clean}' ignored during a battle (keeping '{sessionPlayerName}').");
            return sessionPlayerName;
        }
        playerName = clean;
        return playerName;
    }

    public static string CleanName(string name)
    {
        string clean = string.IsNullOrWhiteSpace(name) ? DefaultPlayerName : name.Trim();
        return clean.Length > MaxPlayerNameLength ? clean.Substring(0, MaxPlayerNameLength) : clean;
    }

    void OnEnable()
    {
        HitDetection.HitLanded += OnHitLanded;
        HitDetection.SwingTooSlow += OnSwingTooSlow;
        HitDetection.WeaponClashed += OnWeaponClashed;
        MonsterAI.AttackStarted += OnMonsterAttackStarted;
        MonsterAI.AttackResolved += OnMonsterAttackResolved;
        MonsterAI.DefendStarted += OnMonsterDefendStarted;
        PlayerHealth.DamageTaken += OnPlayerDamageTaken;
    }

    void OnDisable()
    {
        HitDetection.HitLanded -= OnHitLanded;
        HitDetection.SwingTooSlow -= OnSwingTooSlow;
        HitDetection.WeaponClashed -= OnWeaponClashed;
        MonsterAI.AttackStarted -= OnMonsterAttackStarted;
        MonsterAI.AttackResolved -= OnMonsterAttackResolved;
        MonsterAI.DefendStarted -= OnMonsterDefendStarted;
        PlayerHealth.DamageTaken -= OnPlayerDamageTaken;
    }

    void OnDestroy()
    {
        // Scene reload right after a result: send whatever is still queued without waiting.
        MoveBufferToRequests();
        while (requests.Count > 0) Send(requests.Dequeue(), true);
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────── Session lifecycle (called by BattleManager) ───────────────────────────

    public void BeginSession()
    {
        currentSessionId = Guid.NewGuid().ToString("N");
        sessionPlayerName = CleanName(playerName);
        sessionStartedAt = Time.realtimeSinceStartup;
        hitCount = 0;
        sessionOpen = true;
        hasPendingSwing = false;
        monsterHealth = null;
        lastAttackName = lastAttackCategory = "";
        eventBuffer.Clear();

        // StartBattle calls this before it spawns the monster and sets HP, so the live session is
        // posted on the next Update with the real starting values.
        startPending = true;
        nextSnapshotTime = Time.realtimeSinceStartup + snapshotInterval;
        if (verboseLogging) Debug.Log($"[SessionReporter] Session {currentSessionId} ({sessionPlayerName}) → {baseUrl}");
    }

    /// <summary>Called by BattleManager for every hit that counted (after score was added).</summary>
    public void RegisterHit(float damage, int scoreAfter)
    {
        if (!sessionOpen) BeginSession();
        hitCount++;
        if (!sendLiveEvents) return;

        string tier = hasPendingSwing ? TierFor(pendingSwingSpeed) : "";
        Begin("hit", hasPendingSwing
            ? $"{tier.ToUpperInvariant()} hit for {damage.ToString("0.#", CultureInfo.InvariantCulture)} ({pendingSwingSpeed.ToString("0.0", CultureInfo.InvariantCulture)} m/s)"
            : $"Hit for {damage.ToString("0.#", CultureInfo.InvariantCulture)}");
        Num("damage", damage);
        Num("hitIndex", hitCount);
        if (hasPendingSwing)
        {
            Num("swingSpeed", pendingSwingSpeed);
            Str("tier", tier);
            Str("hand", pendingSwingHand == Hand.Left ? "left" : "right");
            Bool("defended", pendingSwingDefended);
        }
        AppendState(scoreAfter);
        End();
        hasPendingSwing = false;
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
        if (!sessionOpen) BeginSession();
        if (startPending) SendSessionStart();

        float duration = Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartedAt);
        if (sendLiveEvents)
        {
            Begin("hp", "Final state");
            AppendState(score);
            End();
        }

        sb.Clear();
        sb.Append('{');
        JStr("id", currentSessionId, true);
        JStr("playerName", sessionPlayerName);
        JStr("level", level);
        JStr("outcome", outcome);
        JStr("status", "finished");
        JNum("score", score);
        JNum("pointsEarned", score);
        JNum("damageDealt", damageDealt);
        JNum("hitCount", hitCount);
        JNum("tapCount", tapCount);
        JNum("durationSec", duration);
        JNum("playerHpRemaining", playerHpRemaining);
        JNum("monsterHpRemaining", monsterHpRemaining);
        JNum("xp", XPManager.Instance != null ? XPManager.Instance.CurrentXP : 0);
        JNum("fullSwings", XPManager.Instance != null ? XPManager.Instance.FullSwings : 0);
        JStr("startedAt", DateTime.UtcNow.AddSeconds(-duration).ToString("o"));
        JStr("endedAt", DateTime.UtcNow.ToString("o"));
        JStr("source", "unity");
        JStr("notes", notes ?? "");
        sb.Append('}');

        // Events first so the result arrives after the last hit.
        MoveBufferToRequests();
        requests.Enqueue(new PendingRequest { url = SessionsUrl, json = sb.ToString(), label = "result" });
        sessionOpen = false;
    }

    void SendSessionStart()
    {
        startPending = false;
        // Without live events nothing would keep the session alive, and the backend would mark
        // it abandoned after 60 s; the final result alone is enough in that mode.
        if (!sendLiveEvents) return;
        sb.Clear();
        sb.Append('{');
        JStr("id", currentSessionId, true);
        JStr("playerName", sessionPlayerName);
        JStr("level", CurrentLevel());
        JStr("status", "live");
        JStr("startedAt", DateTime.UtcNow.AddSeconds(-(Time.realtimeSinceStartup - sessionStartedAt)).ToString("o"));
        JStr("source", "unity");
        AppendStateFields(BattleManager.Instance != null ? BattleManager.Instance.CurrentScore : 0);
        sb.Append('}');
        requests.Enqueue(new PendingRequest { url = SessionsUrl, json = sb.ToString(), label = "start" });

        Begin("session_start", $"{sessionPlayerName} started {LevelLabel(CurrentLevel())}");
        Str("level", CurrentLevel());
        AppendState(BattleManager.Instance != null ? BattleManager.Instance.CurrentScore : 0);
        End();
    }

    // ─────────────────────────── Gameplay hooks (static events) ───────────────────────────

    void OnHitLanded(HitDetection source, float swingSpeed, bool defended)
    {
        hasPendingSwing = true;
        pendingSwingSpeed = swingSpeed;
        pendingSwingHand = source != null ? source.hand : Hand.Right;
        pendingSwingDefended = defended;
    }

    void OnSwingTooSlow(HitDetection source, float swingSpeed, float minSpeed)
    {
        if (!Recording()) return;
        int h = source != null && source.hand == Hand.Left ? 1 : 0;
        // Several monster colliders can report the same slow contact; count it once.
        if (Time.time - lastWeakSwingTime[h] < WeakSwingRepeatWindow) return;
        lastWeakSwingTime[h] = Time.time;

        Begin("weak_swing", $"Too slow: {swingSpeed.ToString("0.0", CultureInfo.InvariantCulture)} m/s (min {minSpeed.ToString("0.0", CultureInfo.InvariantCulture)})");
        Num("swingSpeed", swingSpeed);
        Num("min", minSpeed);
        Str("hand", h == 1 ? "left" : "right");
        End();
    }

    void OnWeaponClashed(HitDetection source, float swingSpeed)
    {
        if (!Recording()) return;
        Begin("clash", $"Weapon clash ({swingSpeed.ToString("0.0", CultureInfo.InvariantCulture)} m/s)");
        Num("swingSpeed", swingSpeed);
        Str("hand", source != null && source.hand == Hand.Left ? "left" : "right");
        End();
    }

    void OnMonsterAttackStarted(MonsterAI ai, string attackName, string category)
    {
        lastAttackName = attackName ?? "";
        lastAttackCategory = category ?? "";
        if (!Recording()) return;
        Begin("monster_attack", $"Monster {attackName} ({category})");
        Str("attack", lastAttackName);
        Str("category", lastAttackCategory);
        End();
    }

    void OnMonsterAttackResolved(MonsterAI ai, string attackName, bool landed)
    {
        if (landed || !Recording()) return; // a landed attack is reported as player_hurt
        Begin("dodge", $"Avoided {attackName}");
        Str("attack", attackName ?? "");
        Str("category", lastAttackCategory);
        End();
    }

    void OnMonsterDefendStarted(MonsterAI ai)
    {
        if (!Recording()) return;
        Begin("monster_defend", "Monster raised its guard");
        End();
    }

    void OnPlayerDamageTaken(float amount, bool blocked, float healthAfter)
    {
        if (!Recording()) return;
        Begin(amount > 0.001f ? "player_hurt" : "block",
            amount > 0.001f ? $"Took {amount.ToString("0.#", CultureInfo.InvariantCulture)} damage" : "Blocked the attack");
        Num("amount", amount);
        Bool("blocked", blocked);
        Str("attack", lastAttackName);
        Str("category", lastAttackCategory);
        AppendState(BattleManager.Instance != null ? BattleManager.Instance.CurrentScore : 0);
        End();
    }

    bool Recording() => sessionOpen && sendLiveEvents;

    // ─────────────────────────── Pump ───────────────────────────

    void Update()
    {
        float now = Time.realtimeSinceStartup;

        if (sessionOpen && startPending) SendSessionStart();

        if (Recording() && now >= nextSnapshotTime)
        {
            nextSnapshotTime = now + Mathf.Max(0.25f, snapshotInterval);
            Begin("hp", "HP snapshot");
            AppendState(BattleManager.Instance != null ? BattleManager.Instance.CurrentScore : 0);
            End();
        }

        if (now < nextFlushTime || requestInFlight) return;
        nextFlushTime = now + Mathf.Max(0.1f, flushInterval);

        if (requests.Count == 0) MoveBufferToRequests(maxEventsPerRequest);
        if (requests.Count > 0) Send(requests.Dequeue(), false);
    }

    // Packs buffered events into batch requests (all of them, or one batch of `limit`).
    void MoveBufferToRequests(int limit = int.MaxValue)
    {
        while (eventBuffer.Count > 0)
        {
            int n = Mathf.Min(eventBuffer.Count, Mathf.Max(1, Mathf.Min(limit, maxEventsPerRequest)));
            var batch = new StringBuilder(64 + n * 200);
            batch.Append("{\"events\":[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) batch.Append(',');
                batch.Append(eventBuffer[i]);
            }
            batch.Append("]}");
            eventBuffer.RemoveRange(0, n);
            requests.Enqueue(new PendingRequest { url = EventsUrl, json = batch.ToString(), label = $"{n} event(s)" });
            if (limit != int.MaxValue) break;
        }
        bufferedEvents = eventBuffer.Count;
    }

    void Send(PendingRequest r, bool fireAndForget)
    {
        UnityWebRequest req;
        try
        {
            req = new UnityWebRequest(r.url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(r.json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.Max(1, requestTimeoutSeconds),
            };
            req.SetRequestHeader("Content-Type", "application/json");
        }
        catch (Exception e)
        {
            // A malformed baseUrl throws here; never let it reach gameplay.
            LogFailure(r.label, e.Message);
            return;
        }

        if (!fireAndForget) requestInFlight = true;
        string label = r.label;
        bool verbose = verboseLogging;
        var op = req.SendWebRequest();
        // The callback outlives this component if the scene unloads mid-request.
        op.completed += _ =>
        {
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (this != null) { droppedRequests++; LogFailure(label, req.error); }
            }
            else if (verbose)
            {
                Debug.Log($"[SessionReporter] {label} OK ({req.responseCode})");
            }
            req.Dispose();
            if (!fireAndForget) requestInFlight = false;
        };
    }

    void LogFailure(string label, string error)
    {
        // One warning per 10 s at most; dropped data is expected when offline.
        if (!verboseLogging && Time.realtimeSinceStartup - lastFailureLogTime < 10f) return;
        lastFailureLogTime = Time.realtimeSinceStartup;
        Debug.LogWarning($"[SessionReporter] {label} not delivered ({error}); dropped. Backend: {baseUrl}");
    }

    // ─────────────────────────── State snapshot ───────────────────────────

    // Event payloads already start with "t" (see Begin).
    void AppendState(int score)
    {
        AppendStateFields(score, includeTime: false);
    }

    // Current fight state as JSON fields, each preceded by a comma.
    void AppendStateFields(int score, bool includeTime = true)
    {
        bool first = false;
        void F(string key, float value)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(key).Append("\":").Append(FormatNum(value));
        }

        if (includeTime) F("t", Time.realtimeSinceStartup - sessionStartedAt);
        var ph = PlayerHealth.Instance;
        if (ph != null) { F("playerHp", ph.currentHealth); F("playerMaxHp", ph.maxHealth); }
        var mh = CurrentMonsterHealth();
        if (mh != null) { F("monsterHp", mh.currentHealth); F("monsterMaxHp", mh.maxHealth); }
        F("score", score);
        F("hits", hitCount);
        var bm = BattleManager.Instance;
        if (bm != null) F("damageTotal", bm.TotalDamageDealt);
        var xp = XPManager.Instance;
        if (xp != null) { F("xp", xp.CurrentXP); F("fullSwings", xp.FullSwings); }
        var rm = RoundManager.Instance;
        if (rm != null && rm.roundActive) F("timeLeft", rm.GetSecondsRemaining());
        var pc = PlayerCombat.Instance;
        if (pc != null) { F("stamina", pc.currentStamina); F("maxStamina", pc.maxStamina); }
    }

    MonsterHealth CurrentMonsterHealth()
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.ActiveMonster == null) return null;
        if (monsterHealth == null || monsterHealth.gameObject != bm.ActiveMonster.gameObject)
            monsterHealth = bm.ActiveMonster.GetComponent<MonsterHealth>();
        return monsterHealth;
    }

    string CurrentLevel()
    {
        return LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";
    }

    static string LevelLabel(string level)
    {
        switch (level)
        {
            case "Easy": return "Level 1";
            case "Medium": return "Level 2";
            case "Hard": return "Level 3";
            default: return level;
        }
    }

    string TierFor(float speed)
    {
        if (XPManager.Instance != null) return XPManager.Instance.GetSwingTier(speed).ToLowerInvariant();
        return "";
    }

    // ─────────────────────────── Tiny JSON writer (invariant culture: "1.5", never "1,5") ───────────────────────────

    // Event: {"type":..,"sessionId":..,"playerName":..,"message":..,"payload":{"t":.., ...}}
    void Begin(string type, string message)
    {
        sb.Clear();
        sb.Append('{');
        JStr("type", type, true);
        JStr("sessionId", currentSessionId ?? "");
        JStr("playerName", sessionPlayerName);
        JStr("message", message);
        sb.Append(",\"payload\":{\"t\":").Append(FormatNum(Time.realtimeSinceStartup - sessionStartedAt));
    }

    void End()
    {
        sb.Append("}}");
        if (eventBuffer.Count >= Mathf.Max(8, maxBufferedEvents)) eventBuffer.RemoveAt(0); // offline: keep the newest
        eventBuffer.Add(sb.ToString());
        bufferedEvents = eventBuffer.Count;
    }

    void Num(string key, float value) => sb.Append(",\"").Append(key).Append("\":").Append(FormatNum(value));
    void Str(string key, string value) => sb.Append(",\"").Append(key).Append("\":\"").Append(Escape(value)).Append('"');
    void Bool(string key, bool value) => sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");

    void JStr(string key, string value, bool first = false)
    {
        if (!first) sb.Append(',');
        sb.Append('"').Append(key).Append("\":\"").Append(Escape(value)).Append('"');
    }

    void JNum(string key, float value)
    {
        sb.Append(",\"").Append(key).Append("\":").Append(FormatNum(value));
    }

    static string FormatNum(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
        return Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var o = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': o.Append("\\\\"); break;
                case '"': o.Append("\\\""); break;
                case '\n': o.Append("\\n"); break;
                case '\r': o.Append("\\r"); break;
                case '\t': o.Append("\\t"); break;
                default:
                    if (c < ' ') o.Append("\\u").Append(((int)c).ToString("x4"));
                    else o.Append(c);
                    break;
            }
        }
        return o.ToString();
    }
}
