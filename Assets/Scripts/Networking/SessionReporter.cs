// SessionReporter.cs
// Purpose: Streams a battle to the STRIKEFIT backend (web/backend) while it is being played:
//          live session at battle start, batched combat events + 1 s HP snapshots during the
//          fight, final stats at the end. One component, used unchanged by every level.
//          Also the pre-battle player requests for PlayerNamePicker: player list, rename, and the
//          current name of a past session.
// Dependencies: BattleManager, HitDetection, MonsterAI, PlayerHealth, XPManager, RoundManager,
//               LevelManager, PlayerCombat (all optional at runtime), PlayerNameStore.
// Attach to: BattleSystems (same GameObject as BattleManager). BattleManager adds one if missing.
//
// Telemetry fields the dashboard reads: hit events carry swingSpeed, tier, hand, defended and (via the state block) combo;
// every state block (hit, player_hurt, hp snapshot, session start) carries combo, effort (0-1) and kcal (estimate).
// They add no events and no requests: they ride on the events that were already sent.
// Guard telemetry (sendGuardEvents): monster_guard (one per guard the monster raised: swingClass, guardType, blocked,
// blockedHits, damagePrevented, openings, reactive, broken, seconds) and opening_hit (swingClass, bonusDamage) go into the
// same bounded buffer and the same batched requests as every other event, so the request rate is unchanged. clash
// (hand, swingSpeed) was already sent. A failed batch is dropped silently, as before.
//
// Networking never touches gameplay: events go into a bounded buffer, at most one request is in
// flight, requests are sent every flushInterval seconds, and any failure drops that batch silently.
// The player requests are sent straight away (nothing else is in flight before a battle), each with
// its own timeout, and always answer through a callback, also on failure.

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
    [Tooltip("Base URL only, no trailing slash. The single backend URL for every request in every level. " +
             "Must be https on Quest (plain http is blocked).")]
    public string baseUrl = "https://s-t-r-i-k-e-f-i-t.onrender.com";
    [Tooltip("GET /api/health once when the scene loads, so a sleeping Render instance is awake by the time " +
             "the player presses Start.")]
    public bool wakeBackendOnLoad = true;
    [Tooltip("Timeout for the wake-up ping only. A Render cold start takes 30-60 s; the ping runs in the " +
             "background and never blocks gameplay, so it can wait that long.")]
    public int wakeTimeoutSeconds = 60;
    [Tooltip("Name shown on the dashboard. Each player's history is grouped by this exact name. " +
             "Default only: PlayerNamePicker sets it in-game before the Start button appears.")]
    public string playerName = DefaultPlayerName;
    [Tooltip("Send combat events and HP snapshots during the fight (the live dashboard). " +
             "Off = only the session start and final result are sent.")]
    public bool sendLiveEvents = true;
    [Tooltip("Also send monster_guard and opening_hit events (the adaptive guard). They share the existing batches.")]
    public bool sendGuardEvents = true;

    [Header("Pacing")]
    [Tooltip("Seconds between HP/XP snapshots while a battle is running.")]
    public float snapshotInterval = 1f;
    [Tooltip("Seconds between network sends. 0.4 = at most 2.5 requests per second.")]
    public float flushInterval = 0.4f;
    [Tooltip("Events kept while offline or slow; the oldest are dropped beyond this.")]
    public int maxBufferedEvents = 256;
    public int maxEventsPerRequest = 50;
    [Tooltip("Seconds before a POST is abandoned. Requests are async, so this never stalls gameplay.")]
    public int requestTimeoutSeconds = 5;
    [Tooltip("Seconds before the player-list request for the name panel is abandoned. The panel shows the " +
             "locally remembered names meanwhile, so this never delays a fight.")]
    public int playerListTimeoutSeconds = 3;
    [Tooltip("Seconds before a rename request is abandoned (the name panel waits for the answer).")]
    public int renameTimeoutSeconds = 8;
    [Tooltip("Seconds before the personal-bests request (sent in the background when a battle starts, for the NEW PB line " +
             "on the result panel) is abandoned. Failure is silent: the result panel just shows no PB line.")]
    public int bestsTimeoutSeconds = 5;
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

    string SessionsUrl => ApiUrl("/api/sessions");
    string EventsUrl => ApiUrl("/api/events");
    string HealthUrl => ApiUrl("/api/health");
    string ApiUrl(string path) => baseUrl.Trim().TrimEnd('/') + path;

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
    public const int MaxPlayerNameLength = 40; // backend limit (PATCH /api/players/:name)

    void Awake()
    {
        Instance = this;
#if !UNITY_EDITOR
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            Debug.LogWarning($"[SessionReporter] baseUrl '{baseUrl}' is not https; Quest may block it.");
#endif
    }

    void Start()
    {
        if (wakeBackendOnLoad) WakeBackend();
    }

    // Render's free tier sleeps when idle; this starts the cold boot while the player is still on the
    // start screen. Result is logged only; nothing waits on it.
    void WakeBackend()
    {
        UnityWebRequest req;
        UnityWebRequestAsyncOperation op;
        try
        {
            req = UnityWebRequest.Get(HealthUrl);
            req.timeout = Mathf.Max(1, wakeTimeoutSeconds);
            op = req.SendWebRequest();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SessionReporter] Wake-up ping not sent ({e.Message}). Backend: {baseUrl}");
            return;
        }

        float sentAt = Time.realtimeSinceStartup;
        string url = HealthUrl;
        op.completed += _ =>
        {
            float secs = Time.realtimeSinceStartup - sentAt;
            if (req.result == UnityWebRequest.Result.Success)
                Debug.Log($"[SessionReporter] Backend awake: GET {url} → {req.responseCode} in {secs:0.0}s");
            else
                Debug.LogWarning($"[SessionReporter] Wake-up ping failed after {secs:0.0}s ({req.error}); gameplay continues offline-safe.");
            req.Dispose();
        };
    }

    /// <summary>
    /// Queues a JSON POST to baseUrl + path (e.g. "/api/sessions"). Same timeout, error handling and
    /// one-request-at-a-time pacing as the battle telemetry; failures are logged and dropped.
    /// </summary>
    public void PostJson(string path, string json, string label)
    {
        requests.Enqueue(new PendingRequest { url = ApiUrl(path), json = json, label = label });
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
        MonsterDefense.GuardEnded += OnMonsterGuardEnded;
        MonsterDefense.OpeningLanded += OnOpeningLanded;
        DragonHazard.FireResolved += OnDragonFire;
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
        MonsterDefense.GuardEnded -= OnMonsterGuardEnded;
        MonsterDefense.OpeningLanded -= OnOpeningLanded;
        DragonHazard.FireResolved -= OnDragonFire;
    }

    void OnDestroy()
    {
        // Scene reload right after a result: send whatever is still queued without waiting.
        FlushNow();
        if (Instance == this) Instance = null;
    }

    /// <summary>Hands everything still buffered or queued to the network now (fire and forget, same 5 s timeout, same silent
    /// failure). Used by the scene flow before Exit Game; <see cref="SendsInFlight"/> says when the requests have finished.</summary>
    public void FlushNow()
    {
        MoveBufferToRequests();
        while (requests.Count > 0) Send(requests.Dequeue(), true);
    }

    /// <summary>Requests handed to the network and not yet finished (success or failure).</summary>
    public int SendsInFlight => sendsInFlight;
    int sendsInFlight;

    // ─────────────────────────── Players (pre-battle, used by PlayerNamePicker) ───────────────────────────

    public enum RenameStatus
    {
        Renamed,      // 200: the player's sessions now carry the new name
        NotOnServer,  // 404 "Player not found": no sessions under the old name, nothing to move
        Conflict,     // 409: the new name is taken, or the player is in a live fight
        Rejected,     // any other answer from the server (invalid name, no rename route, 5xx)
        Unreachable,  // no answer: offline, timeout, malformed baseUrl
        InBattle,     // refused locally: a battle is being reported
    }

    public struct RenameResult
    {
        public RenameStatus status;
        public long httpStatus;
        /// <summary>The server's own error text when it sent one, else the transport error.</summary>
        public string message;
    }

    [Serializable] class PlayerRow { public string playerName; public string lastPlayedAt; }
    [Serializable] class PlayersResponse { public List<PlayerRow> players; }
    [Serializable] class SessionRow { public string playerName; }
    [Serializable] class SessionResponse { public SessionRow session; }
    [Serializable] class ErrorResponse { public string error; }

    /// <summary>
    /// GET /api/players. Calls back with the player names, most recently played first, or with
    /// ok = false (and an empty list) when the backend did not answer within playerListTimeoutSeconds.
    /// </summary>
    public void FetchPlayerNames(Action<bool, List<string>> done)
    {
        SendNow(UnityWebRequest.kHttpVerbGET, ApiUrl("/api/players"), null, playerListTimeoutSeconds, "player list",
            (answered, code, body, error) =>
            {
                var names = new List<string>();
                if (!answered || code != 200) { done(false, names); return; }
                try
                {
                    var parsed = JsonUtility.FromJson<PlayersResponse>(body);
                    if (parsed == null || parsed.players == null) { done(false, names); return; }
                    parsed.players.Sort((a, b) => PlayedAt(b).CompareTo(PlayedAt(a)));
                    foreach (var p in parsed.players)
                        if (!string.IsNullOrWhiteSpace(p.playerName) && !names.Contains(p.playerName))
                            names.Add(p.playerName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SessionReporter] player list: unreadable answer ({e.Message}). Backend: {baseUrl}");
                    names.Clear();
                    done(false, names);
                    return;
                }
                done(true, names);
            });
    }

    /// <summary>
    /// GET /api/sessions/:id. Calls back with found = true and the name that session is stored under now
    /// (it follows dashboard renames), found = false when the backend has no such session (404), and
    /// answered = false when the backend could not be asked.
    /// </summary>
    public void FetchSessionPlayerName(string sessionId, Action<bool, bool, string> done)
    {
        SendNow(UnityWebRequest.kHttpVerbGET, ApiUrl("/api/sessions/" + Uri.EscapeDataString(sessionId ?? "")), null,
            playerListTimeoutSeconds, "session lookup",
            (answered, code, body, error) =>
            {
                if (!answered || (code != 200 && code != 404)) { done(false, false, null); return; }
                if (code == 404) { done(true, false, null); return; }
                string name = null;
                try
                {
                    var parsed = JsonUtility.FromJson<SessionResponse>(body);
                    if (parsed != null && parsed.session != null) name = parsed.session.playerName;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SessionReporter] session lookup: unreadable answer ({e.Message}). Backend: {baseUrl}");
                }
                if (string.IsNullOrWhiteSpace(name)) done(false, false, null);
                else done(true, true, name);
            });
    }

    /// <summary>
    /// PATCH /api/players/:oldName { newName }: moves the player's whole history to the new name on the
    /// backend. Does not change playerName here; the caller does that from the result. Refused during a
    /// battle (same rule as SetPlayerName), so a fight is never split across two names.
    /// </summary>
    public void RenamePlayer(string oldName, string newName, Action<RenameResult> done)
    {
        if (sessionOpen)
        {
            Debug.LogWarning($"[SessionReporter] Rename '{oldName}' -> '{newName}' refused during a battle.");
            done(new RenameResult { status = RenameStatus.InBattle, message = "A battle is running" });
            return;
        }

        // EscapeDataString, not UnityWebRequest.EscapeURL: a '+' in a URL path is a literal plus, not a space.
        string url = ApiUrl("/api/players/" + Uri.EscapeDataString(oldName ?? ""));
        string json = "{\"newName\":\"" + Escape(newName) + "\"}";
        SendNow("PATCH", url, json, renameTimeoutSeconds, "rename",
            (answered, code, body, error) =>
            {
                var result = new RenameResult { httpStatus = code, message = error };
                if (!answered)
                {
                    result.status = RenameStatus.Unreachable;
                    done(result);
                    return;
                }

                string serverError = null;
                try
                {
                    var parsed = JsonUtility.FromJson<ErrorResponse>(body);
                    if (parsed != null) serverError = parsed.error;
                }
                catch (Exception) { /* not JSON (proxy page): the status code still decides */ }
                if (!string.IsNullOrEmpty(serverError)) result.message = serverError;

                if (code >= 200 && code < 300) result.status = RenameStatus.Renamed;
                else if (code == 409) result.status = RenameStatus.Conflict;
                // Only the rename route's own 404 means "no such player"; an unknown route is a failure.
                else if (code == 404 && serverError == "Player not found") result.status = RenameStatus.NotOnServer;
                else result.status = RenameStatus.Rejected;
                done(result);
            });
    }

    // GET /api/players/:name -> personalBests. Only the three records the result panel compares are read.
    [Serializable] class BestEntry { public float value; }
    [Serializable] class BestsBlock { public BestEntry highestSwingVelocity; public BestEntry highestEstimatedCalories; public BestEntry highestScore; }
    [Serializable] class BestsResponse { public BestsBlock personalBests; }

    /// <summary>A player's records from earlier sessions. A value of 0 means "no record yet" (nothing to beat).</summary>
    public struct PlayerBests
    {
        public bool ok;
        public float swingSpeed, kcal, score;
    }

    /// <summary>
    /// GET /api/players/:name in the background (bestsTimeoutSeconds). Calls back once, with ok = false when the backend did not
    /// answer, the player has no sessions yet (404) or the answer was unreadable. Nothing waits on it.
    /// </summary>
    public void FetchPlayerBests(string name, Action<PlayerBests> done)
    {
        SendNow(UnityWebRequest.kHttpVerbGET, ApiUrl("/api/players/" + Uri.EscapeDataString(name ?? "")), null, bestsTimeoutSeconds, "personal bests",
            (answered, code, body, error) =>
            {
                var r = new PlayerBests();
                if (answered && code == 200)
                {
                    try
                    {
                        var parsed = JsonUtility.FromJson<BestsResponse>(body);
                        var b = parsed != null ? parsed.personalBests : null;
                        if (b != null)
                        {
                            r.ok = true;
                            r.swingSpeed = b.highestSwingVelocity != null ? b.highestSwingVelocity.value : 0f;
                            r.kcal = b.highestEstimatedCalories != null ? b.highestEstimatedCalories.value : 0f;
                            r.score = b.highestScore != null ? b.highestScore.value : 0f;
                        }
                    }
                    catch (Exception) { /* unreadable: no PB line */ }
                }
                done(r);
            });
    }

    static DateTime PlayedAt(PlayerRow p)
    {
        DateTime t;
        return p != null && DateTime.TryParse(p.lastPlayedAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) ? t : DateTime.MinValue;
    }

    // One request, sent immediately, with a timeout. `done` is always called exactly once, unless this
    // component was destroyed meanwhile: answered = the server replied (any status code, body included).
    void SendNow(string method, string url, string json, int timeoutSeconds, string label,
        Action<bool, long, string, string> done)
    {
        UnityWebRequest req = null;
        UnityWebRequestAsyncOperation op;
        try
        {
            req = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.Max(1, timeoutSeconds),
            };
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            op = req.SendWebRequest();
        }
        catch (Exception e)
        {
            // A malformed baseUrl throws here; never let it reach gameplay.
            req?.Dispose();
            Debug.LogWarning($"[SessionReporter] {label} not sent ({e.Message}). Backend: {baseUrl}");
            done(false, 0, "", e.Message);
            return;
        }

        bool verbose = verboseLogging;
        op.completed += _ =>
        {
            // ProtocolError = the server answered with 4xx/5xx; the caller decides what that means.
            bool answered = req.result == UnityWebRequest.Result.Success || req.result == UnityWebRequest.Result.ProtocolError;
            long code = req.responseCode;
            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            string error = req.error;
            req.Dispose();

            if (this == null) return; // scene unloaded mid-request: nobody is waiting any more
            if (!answered) Debug.LogWarning($"[SessionReporter] {label}: no answer ({error}). Backend: {baseUrl}");
            else if (verbose) Debug.Log($"[SessionReporter] {label}: {method} {url} → {code}");
            try
            {
                done(answered, code, body ?? "", error);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        };
    }

    // ─────────────────────────── Session lifecycle (called by BattleManager) ───────────────────────────

    public void BeginSession()
    {
        currentSessionId = Guid.NewGuid().ToString("N");
        sessionPlayerName = CleanName(playerName);
        PlayerNameStore.RecordSession(sessionPlayerName, currentSessionId);
        sessionStartedAt = Time.realtimeSinceStartup;
        BattleStats.Begin(sessionStartedAt); // HUD kcal and the telemetry share this start time
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

    void OnMonsterGuardEnded(MonsterDefense defense, MonsterDefense.GuardReport r)
    {
        if (!Recording() || !sendGuardEvents) return;
        string cls = MonsterDefense.ClassNames[(int)r.swingClass];
        string pose = MonsterDefense.PoseNames[(int)r.pose];
        Begin("monster_guard", r.blocked > 0 ? $"Monster {pose} guard vs {cls}: blocked {r.blocked}" : $"Monster {pose} guard vs {cls}");
        Str("swingClass", cls);
        Str("guardType", pose);
        Bool("blocked", r.blocked > 0);
        Num("blockedHits", r.blocked);
        Num("damagePrevented", r.damagePrevented);
        Num("openings", r.openings);
        Bool("reactive", r.reactive);
        Bool("broken", r.broken);
        Num("seconds", r.seconds);
        End();
    }

    void OnOpeningLanded(MonsterDefense defense, MonsterDefense.SwingClass swingClass, float bonusDamage)
    {
        if (!Recording() || !sendGuardEvents) return;
        string cls = MonsterDefense.ClassNames[(int)swingClass];
        Begin("opening_hit", $"Opening found: {cls}");
        Str("swingClass", cls);
        Num("bonusDamage", bonusDamage);
        End();
    }

    /// <summary>One dragon fire strip resolved (Level 3 dragons): whether the player was in the zone and what it cost.</summary>
    void OnDragonFire(DragonHazard.FireReport r)
    {
        if (!Recording()) return;
        Begin("dragon_fire", r.zoneHit ? $"Dragon fire hit ({r.ticksHit}/{r.ticks} ticks)" : "Dragon fire dodged");
        Bool("zoneHit", r.zoneHit);
        Num("damage", r.damage);
        Num("ticksHit", r.ticksHit);
        Num("ticks", r.ticks);
        Num("phase", r.phase);
        Num("dragon", r.dragon);
        End();
    }

    void OnPlayerDamageTaken(float amount, bool blocked, float healthAfter)
    {
        if (!Recording()) return;
        bool fromDragon = DragonHazard.DamageSourceOverride != null;
        Begin(amount > 0.001f ? "player_hurt" : "block",
            amount > 0.001f ? $"Took {amount.ToString("0.#", CultureInfo.InvariantCulture)} damage" : "Blocked the attack");
        Num("amount", amount);
        Bool("blocked", blocked);
        Str("attack", fromDragon ? DragonHazard.DamageSourceOverride : lastAttackName);
        Str("category", fromDragon ? "Hazard" : lastAttackCategory);
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
        UnityWebRequest req = null;
        UnityWebRequestAsyncOperation op;
        try
        {
            req = new UnityWebRequest(r.url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(r.json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.Max(1, requestTimeoutSeconds),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            op = req.SendWebRequest();
        }
        catch (Exception e)
        {
            // A malformed baseUrl throws here; never let it reach gameplay.
            req?.Dispose();
            droppedRequests++;
            LogFailure(r.label, e.Message);
            return;
        }

        if (!fireAndForget) requestInFlight = true;
        sendsInFlight++;
        string label = r.label;
        bool verbose = verboseLogging;
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
            sendsInFlight--;
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
        if (xp != null) { F("xp", xp.CurrentXP); F("fullSwings", xp.FullSwings); F("combo", xp.CurrentCombo); }
        // Effort (0-1) is PlayerExertionAudio's value, the same one the breathing and the HUD bar use.
        var ex = PlayerExertionAudio.Instance;
        if (ex != null) F("effort", ex.Exertion01);
        // Running calorie estimate (est.): same formula as the backend, none until a swing with a tier has landed.
        if (BattleStats.TryGetKcal(Time.realtimeSinceStartup, out float kcal)) F("kcal", kcal);
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

    public static string Escape(string s)
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
