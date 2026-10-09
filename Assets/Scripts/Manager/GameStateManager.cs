// GameStateManager.cs
// Purpose: Scene flow — MainMenu → Level1_Easy → Level2_Medium → Level3_Hard → Results
// Attach to: Bootstrap on MainMenu (DontDestroyOnLoad recommended) OR per-scene BattleSystems

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameState
{
    MainMenu,
    LevelSelect,
    Battle,
    Victory,
    Defeat,
    Pause,
    Results
}

public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance;

    [Header("Scene Names (must match Build Settings)")]
    public string mainMenuScene = "MainMenu";
    public string level1Scene = "Level1_Easy";
    public string level2Scene = "Level2_Medium";
    public string level3Scene = "Level3_Hard";
    public string resultsScene = "Results";

    [Header("Legacy / optional")]
    public string levelSelectScene = ""; // unused if MainMenu has level buttons

    [Header("Panel Mode (optional overlays inside a scene)")]
    public GameObject mainMenuPanel;
    public GameObject levelSelectPanel;
    public GameObject battlePanel;
    public GameObject resultsPanel;

    [Header("Runtime")]
    public GameState currentState = GameState.MainMenu;

    public event Action<GameState, GameState> OnStateChanged;

    [Header("Persistence")]
    public bool dontDestroyOnLoad = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        SyncStateFromActiveScene();
        ApplyVisualState(currentState);
    }

    void SyncStateFromActiveScene()
    {
        string n = SceneManager.GetActiveScene().name;
        if (n == mainMenuScene) currentState = GameState.MainMenu;
        else if (n == resultsScene) currentState = GameState.Results;
        else if (n == level1Scene || n == level2Scene || n == level3Scene)
            currentState = GameState.Battle;
    }

    public void SetState(GameState newState)
    {
        if (newState == currentState) return;
        var prev = currentState;
        currentState = newState;

        if (newState == GameState.Pause)
            Time.timeScale = 0f;
        else if (prev == GameState.Pause)
            Time.timeScale = 1f;

        ApplyVisualState(newState);
        OnStateChanged?.Invoke(prev, newState);
        Debug.Log($"[GameState] {prev} → {newState}");
    }

    void ApplyVisualState(GameState state)
    {
        SetActiveSafe(mainMenuPanel, state == GameState.MainMenu);
        SetActiveSafe(levelSelectPanel, state == GameState.LevelSelect);
        SetActiveSafe(battlePanel, state == GameState.Battle || state == GameState.Pause);
        SetActiveSafe(resultsPanel, state == GameState.Results || state == GameState.Victory || state == GameState.Defeat);
    }

    void SetActiveSafe(GameObject go, bool on)
    {
        if (go != null) go.SetActive(on);
    }

    // --- Scene loads ---

    public void GoMainMenu()
    {
        Time.timeScale = 1f;
        LoadSceneIfExists(mainMenuScene);
        SetState(GameState.MainMenu);
    }

    public void GoLevelSelect()
    {
        Time.timeScale = 1f;
        // Prefer MainMenu with level buttons; optional LevelSelect scene
        if (!string.IsNullOrEmpty(levelSelectScene) && SceneExists(levelSelectScene))
            LoadSceneIfExists(levelSelectScene);
        else
            LoadSceneIfExists(mainMenuScene);
        SetState(GameState.LevelSelect);
    }

    public void LoadLevel1()
    {
        Time.timeScale = 1f;
        LoadSceneIfExists(level1Scene);
        SetState(GameState.Battle);
    }

    public void LoadLevel2()
    {
        int unlocked = PlayerPrefs.GetInt("FMB_HighestUnlocked", 1);
        if (unlocked < 2)
        {
            Debug.LogWarning("[GameState] Level 2 locked.");
            return;
        }
        Time.timeScale = 1f;
        LoadSceneIfExists(level2Scene);
        SetState(GameState.Battle);
    }

    public void LoadLevel3()
    {
        int unlocked = PlayerPrefs.GetInt("FMB_HighestUnlocked", 1);
        if (unlocked < 3)
        {
            Debug.LogWarning("[GameState] Level 3 locked.");
            return;
        }
        Time.timeScale = 1f;
        LoadSceneIfExists(level3Scene);
        SetState(GameState.Battle);
    }

    public void GoResults()
    {
        Time.timeScale = 1f;
        LoadSceneIfExists(resultsScene);
        SetState(GameState.Results);
    }

    // Retry flag for the entrance: the next load of this scene plays the short entrance (MonsterEntrance, Level 2 drop).
    // Static, so it survives the scene reload; consumed once by the entrance of that scene.
    static string retryScene;
    public static void FlagRetry(string sceneName) { retryScene = sceneName; }
    public static bool ConsumeRetry(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || retryScene != sceneName) return false;
        retryScene = null;
        return true;
    }

    /// <summary>
    /// Retry the current level scene (used by defeat panel / R key).
    /// </summary>
    public void ReloadCurrentLevel()
    {
        Time.timeScale = 1f;
        string current = SceneManager.GetActiveScene().name;
        FlagRetry(current);
        if (SceneExists(current))
        {
            SceneManager.LoadScene(current);
            SetState(GameState.Battle);
        }
        else
        {
            LoadLevel1();
        }
    }

    /// <summary>
    /// After victory: advance to next level scene, or Results if Hard cleared.
    /// </summary>
    public void LoadNextLevelOrResults()
    {
        var lm = LevelManager.Instance;
        if (lm == null)
        {
            GoResults();
            return;
        }

        switch (lm.sceneDifficulty)
        {
            case DifficultyLevel.Easy:
                LoadLevel2();
                break;
            case DifficultyLevel.Medium:
                LoadLevel3();
                break;
            default:
                GoResults();
                break;
        }
    }

    // Back-compat name used by older UI
    public void GoBattleScene() => LoadLevel1();

    public void PauseGame() => SetState(GameState.Pause);

    public void ResumeBattle()
    {
        Time.timeScale = 1f;
        SetState(GameState.Battle);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void LoadSceneIfExists(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[GameState] Empty scene name.");
            return;
        }
        if (!SceneExists(sceneName))
        {
            Debug.LogError($"[GameState] Scene '{sceneName}' is not in Build Settings. Add it via File → Build Profiles / Build Settings.");
            return;
        }
        SceneManager.LoadScene(sceneName);
    }

    bool SceneExists(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName) return true;
        }
        return false;
    }
}
