using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

// Attach this to an empty GameObject in the scene (e.g. "GameManager").
// Wire your "Start Round" Button's OnClick() to RoundManager.StartRound().
//
// Battle mode: when battleMode=true, StartRound will NOT spawn the cube target
// (BattleManager spawns the monster instead). Fitness tap counting still works.
public class RoundManager : MonoBehaviour
{
    public static RoundManager Instance;

    [Header("Round Setup")]
    public GameObject cubePrefab;
    public Transform cubeSpawnPoint;
    public GameObject weaponModel;
    public GameObject startRoundButton;
    public float roundDuration = 40f;

    [Header("Battle Integration")]
    [Tooltip("When true, skips cube spawn/destroy — used by BattleManager.")]
    public bool battleMode = false;

    [Header("Backend")]
    [Tooltip("Full sessions endpoint. Prefer SessionReporter + web/BACKEND_URL.json. Default: localhost.")]
    public string backendUrl = "http://localhost:4000/api/sessions";
    public string playerName = "Player1";
    [Tooltip("When true and SessionReporter exists, skip legacy tap-only POST (battle reports via SessionReporter).")]
    public bool preferSessionReporter = true;

    [Header("Status (read-only)")]
    public bool roundActive = false;
    public int currentTapCount = 0;

    /// <summary>Fired when a fitness round begins.</summary>
    public event Action OnRoundStarted;

    /// <summary>Fired when a fitness round ends. Arg = tap count.</summary>
    public event Action<int> OnRoundEnded;

    /// <summary>Fired each time RegisterTap succeeds.</summary>
    public event Action<int> OnTapRegistered;

    private GameObject currentCube;
    private Coroutine timerRoutine;
    private float roundEndTime;

    void Awake()
    {
        Instance = this;
    }

    public void StartRound()
    {
        if (roundActive)
        {
            Debug.Log("Round already in progress, ignoring start request.");
            return;
        }

        currentTapCount = 0;
        roundActive = true;
        roundEndTime = Time.time + roundDuration;

        if (startRoundButton != null)
            startRoundButton.SetActive(false);

        if (weaponModel != null)
            weaponModel.SetActive(true);

        // Cube target only in classic tap mode
        if (!battleMode)
        {
            if (currentCube != null)
                Destroy(currentCube);

            if (cubePrefab != null && cubeSpawnPoint != null)
            {
                currentCube = Instantiate(cubePrefab, cubeSpawnPoint.position, cubeSpawnPoint.rotation);
                Debug.Log("Round started. Cube spawned, weapon enabled, timer running.");
            }
            else
            {
                Debug.LogWarning("Round started but cubePrefab/cubeSpawnPoint missing.");
            }
        }
        else
        {
            Debug.Log("Battle-mode round started. Monster handled by BattleManager.");
        }

        if (timerRoutine != null)
            StopCoroutine(timerRoutine);
        timerRoutine = StartCoroutine(RoundTimer());

        OnRoundStarted?.Invoke();
    }

    public void RegisterTap()
    {
        if (!roundActive) return;
        currentTapCount++;
        OnTapRegistered?.Invoke(currentTapCount);
    }

    /// <summary>
    /// Used by BattleManager when combat ends before the timer.
    /// </summary>
    public void ForceEndRound()
    {
        if (!roundActive) return;
        if (timerRoutine != null)
        {
            StopCoroutine(timerRoutine);
            timerRoutine = null;
        }
        EndRound();
    }

    public float GetSecondsRemaining()
    {
        if (!roundActive) return 0f;
        return Mathf.Max(0f, roundEndTime - Time.time);
    }

    private IEnumerator RoundTimer()
    {
        while (Time.time < roundEndTime)
        {
            if (CombatUIController.Instance != null)
                CombatUIController.Instance.UpdateTimer(GetSecondsRemaining());
            yield return null;
        }
        timerRoutine = null;
        EndRound();
    }

    private void EndRound()
    {
        if (!roundActive) return;

        roundActive = false;

        // Final tick: RoundTimer exits before pushing 0, which left the HUD stuck on "Time 1s".
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateTimer(0f);

        if (startRoundButton != null && !battleMode)
            startRoundButton.SetActive(true);

        if (weaponModel != null && !battleMode)
            weaponModel.SetActive(false);

        if (!battleMode && currentCube != null)
        {
            Destroy(currentCube);
            currentCube = null;
        }

        Debug.Log($"Round ended. Total taps: {currentTapCount}. Sending to backend...");
        OnRoundEnded?.Invoke(currentTapCount);

        // Battle mode: BattleManager/SessionReporter posts the full session.
        if (preferSessionReporter && SessionReporter.Instance != null && battleMode)
            return;

        StartCoroutine(SendResultToBackend(currentTapCount));
    }

    private IEnumerator SendResultToBackend(int taps)
    {
        // Legacy fitness-only payload (non-battle). Keep for cube-tap mode.
        string json =
            "{\"playerName\":\"" + Escape(playerName) +
            "\",\"tapCount\":" + taps +
            ",\"score\":" + (taps * 5) +
            ",\"pointsEarned\":" + (taps * 5) +
            ",\"outcome\":\"completed\"," +
            "\"level\":\"Fitness\"," +
            "\"hitCount\":" + taps +
            ",\"damageDealt\":0,\"source\":\"unity-round\"}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(backendUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            Debug.LogError("Failed to send tap result: " + request.error);
        else
            Debug.Log("Tap result sent successfully: " + taps + " taps");
    }

    private string Escape(string s)
    {
        return s.Replace("\"", "\\\"");
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            StartRound();
        }
    }
#endif
}
