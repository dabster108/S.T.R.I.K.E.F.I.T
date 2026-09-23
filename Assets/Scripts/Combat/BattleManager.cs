// BattleManager.cs
// Purpose: Battle flow — spawn monster, win/lose, score, RoundManager sync.
// Dependencies: RoundManager, LevelManager, MonsterController, PlayerHealth, CombatUIController, GameStateManager
// Attach to: Empty "BattleSystems" GameObject in Battle scene

using System;
using System.Collections;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance;

    [Header("Spawn")]
    public Transform monsterSpawnPoint;
    public GameObject fallbackCubeMonsterPrefab; // use training/Cube.prefab until character prefab ready

    [Header("Rules")]
    [Tooltip("If true, round timer ending while monster lives = defeat.")]
    public bool timerExpireMeansDefeat = false;
    [Tooltip("If true, round timer ending while monster lives = victory by survival score.")]
    public bool timerExpireMeansSurvivalWin = true;

    [Header("Score")]
    public int scorePerDamagePoint = 1;
    public int victoryBonus = 500;
    public int survivalBonus = 250;

    [Header("Runtime")]
    public bool IsBattleActive { get; private set; }
    public int CurrentScore { get; private set; }
    public float TotalDamageDealt { get; private set; }
    public MonsterController ActiveMonster { get; private set; }

    public event Action OnBattleStarted;
    public event Action<int> OnVictory;   // score
    public event Action<int> OnDefeat;    // score
    public event Action<int> OnScoreChanged;

    private bool outcomeHandled;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStarted += HandleRoundStarted;
            RoundManager.Instance.OnRoundEnded += HandleRoundEnded;
        }
    }

    void OnDisable()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStarted -= HandleRoundStarted;
            RoundManager.Instance.OnRoundEnded -= HandleRoundEnded;
        }
    }

    void Start()
    {
        // Re-subscribe if RoundManager awoke after us
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStarted -= HandleRoundStarted;
            RoundManager.Instance.OnRoundEnded -= HandleRoundEnded;
            RoundManager.Instance.OnRoundStarted += HandleRoundStarted;
            RoundManager.Instance.OnRoundEnded += HandleRoundEnded;
        }
    }

    /// <summary>
    /// Call from Level Select / Start Battle button.
    /// </summary>
    public void StartBattle(DifficultyLevel level)
    {
        if (IsBattleActive) return;

        outcomeHandled = false;
        CurrentScore = 0;
        TotalDamageDealt = 0f;
        IsBattleActive = true;

        if (XPManager.Instance != null)
            XPManager.Instance.ResetSession();

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.SetState(GameState.Battle);

        // Per-scene architecture: prefer this scene's difficulty over the argument
        if (LevelManager.Instance != null)
        {
            LevelManager.Instance.SelectLevel(level);
            level = LevelManager.Instance.sceneDifficulty;
            LevelManager.Instance.ApplyLoadoutToPlayer();
        }

        DifficultyParams parms = LevelManager.Instance != null
            ? LevelManager.Instance.GetSceneParams()
            : LevelManager.GetParams(level);

        if (PlayerHealth.Instance != null)
            PlayerHealth.Instance.SetMaxHealth(parms.playerMaxHealth, true);

        if (PlayerCombat.Instance != null)
        {
            PlayerCombat.Instance.SetLoadout(parms.playerPrimaryWeapon, parms.playerOffhandWeapon);
            PlayerCombat.Instance.ResetStamina();
        }

        SpawnMonster(level);

        if (CombatUIController.Instance != null)
        {
            CombatUIController.Instance.ShowBattleHUD(true);
            CombatUIController.Instance.HideOutcomePanels();
            CombatUIController.Instance.UpdateScore(CurrentScore);
        }

        // Enable RoundManager battle mode and start the fitness round timer
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.battleMode = true;
            RoundManager.Instance.StartRound();
        }

        OnBattleStarted?.Invoke();
        Debug.Log($"[BattleManager] Battle started — {level}");
    }

    public void StartBattleFromInspector()
    {
        DifficultyLevel level = LevelManager.Instance != null
            ? LevelManager.Instance.currentLevel
            : DifficultyLevel.Easy;
        StartBattle(level);
    }

    void SpawnMonster(DifficultyLevel level)
    {
        if (ActiveMonster != null)
        {
            Destroy(ActiveMonster.gameObject);
            ActiveMonster = null;
        }

        GameObject prefab = null;
        if (LevelManager.Instance != null)
            prefab = LevelManager.Instance.GetMonsterPrefab(level);

        if (prefab == null)
            prefab = fallbackCubeMonsterPrefab;

        Vector3 pos = monsterSpawnPoint != null ? monsterSpawnPoint.position : transform.position + transform.forward * 2.5f;
        Quaternion rot = monsterSpawnPoint != null ? monsterSpawnPoint.rotation : Quaternion.LookRotation(-transform.forward);

        if (prefab == null)
        {
            // Last-resort runtime cube so combat is testable with zero prefabs
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "RuntimeCubeMonster";
            cube.transform.SetPositionAndRotation(pos, rot);
            cube.tag = "Monster";
            EnsureMonsterComponents(cube);
            ActiveMonster = cube.GetComponent<MonsterController>();
        }
        else
        {
            var go = Instantiate(prefab, pos, rot);
            go.tag = "Monster";
            EnsureMonsterComponents(go);
            ActiveMonster = go.GetComponent<MonsterController>();
        }

        var parms = LevelManager.Instance != null
            ? LevelManager.Instance.GetSceneParams()
            : LevelManager.GetParams(level);
        ActiveMonster.Configure(parms);
        ActiveMonster.SetWeapon(parms.monsterWeapon);
        ActiveMonster.OnMonsterDeath += HandleMonsterDeathInternal;

        if (CombatUIController.Instance != null && ActiveMonster.GetComponent<MonsterHealth>() != null)
        {
            var h = ActiveMonster.GetComponent<MonsterHealth>();
            CombatUIController.Instance.UpdateMonsterHealth(h.currentHealth, h.maxHealth);
        }
    }

    void EnsureMonsterComponents(GameObject go)
    {
        if (go.GetComponent<MonsterHealth>() == null) go.AddComponent<MonsterHealth>();
        if (go.GetComponent<MonsterController>() == null) go.AddComponent<MonsterController>();
        if (go.GetComponent<MonsterAI>() == null) go.AddComponent<MonsterAI>();
        if (go.GetComponent<MonsterAnimator>() == null) go.AddComponent<MonsterAnimator>();

        // Cube primitives have a non-trigger collider — fine for HitDetection OnCollisionEnter
        var col = go.GetComponent<Collider>();
        if (col == null)
        {
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.4f;
            capsule.center = new Vector3(0f, 1f, 0f);
        }
    }

    public void RegisterSuccessfulHit(float damage)
    {
        if (!IsBattleActive) return;
        TotalDamageDealt += damage;
        AddScore(Mathf.RoundToInt(damage * scorePerDamagePoint));
    }

    void AddScore(int delta)
    {
        CurrentScore += delta;
        OnScoreChanged?.Invoke(CurrentScore);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateScore(CurrentScore);
    }

    public void NotifyMonsterDefeated(MonsterController monster)
    {
        if (!IsBattleActive || outcomeHandled) return;
        if (monster != ActiveMonster) return;
        StartCoroutine(VictorySequence());
    }

    void HandleMonsterDeathInternal()
    {
        // Backup path if BattleManager subscription missed
        NotifyMonsterDefeated(ActiveMonster);
    }

    public void NotifyPlayerDefeated()
    {
        if (!IsBattleActive || outcomeHandled) return;
        StartCoroutine(DefeatSequence("Player defeated"));
    }

    void HandleRoundStarted()
    {
        Debug.Log("[BattleManager] RoundManager round started (fitness timer running).");
    }

    void HandleRoundEnded(int tapCount)
    {
        if (!IsBattleActive || outcomeHandled) return;

        // Fitness round ended — resolve based on rules
        if (ActiveMonster != null && ActiveMonster.isAlive)
        {
            if (timerExpireMeansDefeat)
            {
                StartCoroutine(DefeatSequence("Time expired"));
            }
            else if (timerExpireMeansSurvivalWin)
            {
                AddScore(survivalBonus + tapCount * 5);
                StartCoroutine(VictorySequence());
            }
            else
            {
                // Soft end: keep battling without timer (no auto outcome)
                Debug.Log("[BattleManager] Round ended; battle continues without timer.");
            }
        }
    }

    IEnumerator VictorySequence()
    {
        if (outcomeHandled) yield break;
        outcomeHandled = true;
        IsBattleActive = false;

        AddScore(victoryBonus);

        if (XPManager.Instance != null)
            XPManager.Instance.AwardVictoryXP();

        if (RoundManager.Instance != null && RoundManager.Instance.roundActive)
            RoundManager.Instance.ForceEndRound();

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.SetState(GameState.Victory);

        if (LevelManager.Instance != null)
            LevelManager.Instance.UnlockNextLevel();

        yield return new WaitForSeconds(0.75f);

        if (CombatUIController.Instance != null)
            CombatUIController.Instance.ShowVictory(CurrentScore, TotalDamageDealt);

        OnVictory?.Invoke(CurrentScore);
        Debug.Log($"[BattleManager] VICTORY — score {CurrentScore}");

        // Optional: auto-advance after a short celebration (wire a UI button instead if preferred)
        // GameStateManager.Instance?.LoadNextLevelOrResults();
    }

    IEnumerator DefeatSequence(string reason)
    {
        if (outcomeHandled) yield break;
        outcomeHandled = true;
        IsBattleActive = false;

        if (RoundManager.Instance != null && RoundManager.Instance.roundActive)
            RoundManager.Instance.ForceEndRound();

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.SetState(GameState.Defeat);

        yield return new WaitForSeconds(0.5f);

        if (CombatUIController.Instance != null)
            CombatUIController.Instance.ShowDefeat(CurrentScore, reason);

        OnDefeat?.Invoke(CurrentScore);
        Debug.Log($"[BattleManager] DEFEAT — {reason}, score {CurrentScore}");
    }

#if UNITY_EDITOR
    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        // B = start Easy battle in editor
        if (kb.bKey.wasPressedThisFrame)
            StartBattle(DifficultyLevel.Easy);

        // R = retry after defeat (reload current level scene)
        if (kb.rKey.wasPressedThisFrame && !IsBattleActive && outcomeHandled)
        {
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.ReloadCurrentLevel();
        }
    }
#endif
}
