// WeaponSelectionStage.cs
// Purpose: Pre-battle weapon choice gate (Level 3). Until a WeaponPickup pair is in the player's hands the Start
//          button stays hidden, BattleManager refuses to start (laser, B button, 3D button) and a prompt tells the
//          player to grab a weapon. Once armed, the prompt shows the chosen weapon and the Start button appears
//          (after the name picker, if it is still open, has been completed).
// Dependencies: PlayerCombat (HasWeaponSetEquipped), WeaponPickup, PlayerNamePicker, BattleManager
// Attach to: BattleSystems in a level with weapon pickups.

using UnityEngine;

public class WeaponSelectionStage : MonoBehaviour
{
    public static WeaponSelectionStage Instance;

    /// <summary>True while this level requires a weapon pick and none is held.</summary>
    public static bool BlocksStart => Instance != null && Instance.isActiveAndEnabled && !Instance.Armed;

    public string startButtonPath = "CombatCanvas/StartBattleUIButton";
    [Tooltip("Prompt shown before the fight (world-space text near the weapons / start area).")]
    public TMPro.TMP_Text prompt;
    public string unarmedPrompt = "GRAB A WEAPON PAIR\n<size=60%>Reach a weapon and squeeze GRIP</size>";
    public string armedPromptFormat = "{0} READY\n<size=60%>Swap any time before the fight</size>";

    public bool Armed => PlayerCombat.Instance != null && PlayerCombat.Instance.HasWeaponSetEquipped;

    private GameObject startButton;
    private bool lastArmed;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        startButton = GameObject.Find(startButtonPath);
        lastArmed = !Armed; // force the first refresh
    }

    void LateUpdate()
    {
        bool battle = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;
        if (battle)
        {
            if (prompt != null && prompt.gameObject.activeSelf) prompt.gameObject.SetActive(false);
            return;
        }

        bool armed = Armed;
        // Runs after PlayerNamePicker re-shows Start in Update, so an unarmed player never sees it.
        if (!armed && startButton != null && startButton.activeSelf) startButton.SetActive(false);

        if (armed != lastArmed)
        {
            lastArmed = armed;
            if (armed && startButton != null && !PlayerNamePicker.BlocksStart && !OutcomeShown())
                startButton.SetActive(true);
            if (prompt != null)
            {
                prompt.gameObject.SetActive(true);
                prompt.text = armed ? string.Format(armedPromptFormat, HeldName()) : unarmedPrompt;
            }
        }
    }

    string HeldName()
    {
        foreach (var p in WeaponPickup.All) if (p.IsHeld) return p.displayName;
        return PlayerCombat.Instance != null ? PlayerCombat.Instance.primaryWeapon.ToString().ToUpper() : "";
    }

    bool OutcomeShown()
    {
        var ui = CombatUIController.Instance;
        return ui != null && ((ui.victoryPanel != null && ui.victoryPanel.activeSelf) || (ui.defeatPanel != null && ui.defeatPanel.activeSelf));
    }
}
