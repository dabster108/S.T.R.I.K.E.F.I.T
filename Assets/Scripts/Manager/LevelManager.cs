// LevelManager.cs
// Purpose: Per-scene difficulty + data-driven weapon loadouts + monster prefab for THIS scene.
// Each level scene owns its own LevelManager with sceneDifficulty fixed (Easy/Medium/Hard).
// Dependencies: BattleManager, PlayerCombat, MonsterController, WeaponType

using UnityEngine;
using UnityEngine.SceneManagement;

public enum DifficultyLevel
{
    Easy = 1,
    Medium = 2,
    Hard = 3
}

[System.Serializable]
public struct DifficultyParams
{
    public DifficultyLevel level;
    public float monsterMaxHealth;
    public float monsterDamage;
    public float monsterAttackCooldown;
    public float monsterAttackRange;
    public float monsterMoveSpeed;
    public float aggression;
    public float playerMaxHealth;
    public WeaponType playerPrimaryWeapon;
    public WeaponType playerOffhandWeapon;
    public WeaponType monsterWeapon;
}

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance;

    [Header("THIS SCENE IS THIS LEVEL")]
    [Tooltip("Set once per scene: Level1_Easy=Easy, Level2_Medium=Medium, Level3_Hard=Hard.")]
    public DifficultyLevel sceneDifficulty = DifficultyLevel.Easy;

    [Tooltip("If true, reads difficulty from scene name (Level1_Easy / Level2_Medium / Level3_Hard).")]
    public bool autoDetectDifficultyFromSceneName = true;

    [Header("Progression")]
    public int highestUnlocked = 1;

    [Header("Monster Prefab For This Scene")]
    [Tooltip("Level 1: Hip Hop Mixamo. Level 2/3: Warrok or other.")]
    public GameObject monsterPrefabForThisScene;

    [Header("Weapon Loadout (override defaults — Inspector wins)")]
    public bool overrideDefaultWeapons = true;
    public WeaponType playerPrimaryWeapon = WeaponType.Hammer;
    public WeaponType playerOffhandWeapon = WeaponType.None;
    public WeaponType monsterWeapon = WeaponType.None;

    [Header("Auto-start")]
    [Tooltip("Start the battle automatically when this level scene loads.")]
    public bool autoStartBattleOnLoad = true;
    public float autoStartDelay = 0.5f;

    public DifficultyLevel currentLevel => sceneDifficulty;

    void Awake()
    {
        Instance = this;
        highestUnlocked = Mathf.Max(1, PlayerPrefs.GetInt("FMB_HighestUnlocked", 1));

        if (autoDetectDifficultyFromSceneName)
            DetectDifficultyFromSceneName();

        ApplyDefaultWeaponsIfNeeded();
    }

    void Start()
    {
        ApplyLoadoutToPlayer();

        if (autoStartBattleOnLoad && BattleManager.Instance != null)
            Invoke(nameof(AutoStart), autoStartDelay);
    }

    void AutoStart()
    {
        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive)
            BattleManager.Instance.StartBattle(sceneDifficulty);
    }

    void DetectDifficultyFromSceneName()
    {
        string n = SceneManager.GetActiveScene().name;
        if (string.IsNullOrEmpty(n)) return;

        string lower = n.ToLowerInvariant();
        if (lower.Contains("level3") || lower.Contains("hard"))
            sceneDifficulty = DifficultyLevel.Hard;
        else if (lower.Contains("level2") || lower.Contains("medium"))
            sceneDifficulty = DifficultyLevel.Medium;
        else if (lower.Contains("level1") || lower.Contains("easy"))
            sceneDifficulty = DifficultyLevel.Easy;
    }

    void ApplyDefaultWeaponsIfNeeded()
    {
        if (overrideDefaultWeapons) return; // Inspector values already set

        // Recommended defaults (still changeable in Inspector when override is on)
        switch (sceneDifficulty)
        {
            case DifficultyLevel.Easy:
                playerPrimaryWeapon = WeaponType.Hammer;
                playerOffhandWeapon = WeaponType.Hammer; // L1: dual-wield (was Shield — no block in Level 1)
                monsterWeapon = WeaponType.Sword;          // L1: Hip Hop monster now wields a sword
                break;
            case DifficultyLevel.Medium:
                playerPrimaryWeapon = WeaponType.Sword;
                playerOffhandWeapon = WeaponType.Sword;
                // WeaponType has no Axe entry; Medium axe monster uses Hammer flavor stats/slot.
                monsterWeapon = WeaponType.Hammer;
                break;
            case DifficultyLevel.Hard:
                playerPrimaryWeapon = WeaponType.Hammer; // flexible — change in Inspector
                playerOffhandWeapon = WeaponType.None;
                monsterWeapon = WeaponType.Hammer;
                break;
        }
    }

    public void ApplyLoadoutToPlayer()
    {
        if (PlayerCombat.Instance != null)
            PlayerCombat.Instance.SetLoadout(playerPrimaryWeapon, playerOffhandWeapon);
    }

    /// <summary>
    /// Prefer this scene's single prefab; falls back to legacy multi-prefab fields if null.
    /// </summary>
    public GameObject GetMonsterPrefab(DifficultyLevel level)
    {
        if (monsterPrefabForThisScene != null)
            return monsterPrefabForThisScene;
        return null;
    }

    public void SelectLevel(DifficultyLevel level)
    {
        // In per-scene architecture the scene already IS the level.
        // Keep API for BattleManager compatibility; do not change sceneDifficulty unless debugging.
        if (level != sceneDifficulty)
            Debug.LogWarning($"[LevelManager] StartBattle asked for {level} but this scene is {sceneDifficulty}. Using scene difficulty.");
    }

    public DifficultyParams GetSceneParams()
    {
        var p = GetParams(sceneDifficulty);
        p.playerPrimaryWeapon = playerPrimaryWeapon;
        p.playerOffhandWeapon = playerOffhandWeapon;
        p.monsterWeapon = monsterWeapon;
        return p;
    }

    public void UnlockNextLevel()
    {
        int next = (int)sceneDifficulty + 1;
        if (next > highestUnlocked && next <= 3)
        {
            highestUnlocked = next;
            PlayerPrefs.SetInt("FMB_HighestUnlocked", highestUnlocked);
            PlayerPrefs.Save();
            Debug.Log($"[LevelManager] Unlocked level {highestUnlocked}");
        }
    }

    public static DifficultyParams GetParams(DifficultyLevel level)
    {
        switch (level)
        {
            case DifficultyLevel.Medium:
                return new DifficultyParams
                {
                    level = DifficultyLevel.Medium,
                    monsterMaxHealth = 800f,
                    monsterDamage = 14f,           // ×1.2 axe (Hammer slot) = 16.8 base; per-attack multipliers in MonsterAI.attackTable
                    monsterAttackCooldown = 2.2f,  // start-to-start (L1: 3.2). MonsterAI shortens to ×0.85 as HP drops
                    monsterAttackRange = 1.55f,    // L2 MonsterAI.useHeadTarget: HORIZONTAL metres to the headset (axe reach ~1.4 m)
                    monsterMoveSpeed = 0.75f,
                    aggression = 1.25f,
                    playerMaxHealth = 800f,
                    playerPrimaryWeapon = WeaponType.Sword,
                    playerOffhandWeapon = WeaponType.Sword,
                    // WeaponType has no Axe entry; Medium axe monster uses Hammer flavor slot.
                    monsterWeapon = WeaponType.Hammer
                };
            case DifficultyLevel.Hard:
                return new DifficultyParams
                {
                    level = DifficultyLevel.Hard,
                    monsterMaxHealth = 280f,
                    monsterDamage = 22f,
                    monsterAttackCooldown = 1.2f,
                    monsterAttackRange = 3.0f,
                    monsterMoveSpeed = 1.3f,
                    aggression = 2.0f,
                    playerMaxHealth = 100f,
                    playerPrimaryWeapon = WeaponType.Hammer,
                    playerOffhandWeapon = WeaponType.None,
                    monsterWeapon = WeaponType.Hammer
                };
            default:
                return new DifficultyParams
                {
                    level = DifficultyLevel.Easy,
                    monsterMaxHealth = 500f,   // Item 5: Level 1 monster HP = 500 (multi-hit fight). Inspector-exposed on MonsterHealth too.
                    monsterDamage = 8f,       // Per-hit base damage (×1.1 sword flavor = ~8.8). With player HP 500 → ~57 hits to kill player.
                    monsterAttackCooldown = 3.2f, // L1: slow, telegraphed (~3-4s between attacks)
                    monsterAttackRange = 2.2f,
                    monsterMoveSpeed = 0.35f,
                    aggression = 1.0f,
                    playerMaxHealth = 500f,   // Item 6: Level 1 player HP = 500 (matches monster for long fights)
                    playerPrimaryWeapon = WeaponType.Hammer,
                    playerOffhandWeapon = WeaponType.Hammer, // Item 4: dual-wield (was Shield)
                    monsterWeapon = WeaponType.Sword
                };
        }
    }

    // UI helpers — load the matching scene instead of swapping difficulty in-place
    public void StartEasy() => GameStateManager.Instance?.LoadLevel1();
    public void StartMedium() => GameStateManager.Instance?.LoadLevel2();
    public void StartHard() => GameStateManager.Instance?.LoadLevel3();
}
