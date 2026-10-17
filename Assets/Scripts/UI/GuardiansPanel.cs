// GuardiansPanel.cs
// Purpose: The main menu's Guardians page: one card at a time (portrait, name, weapon, lore, tips from Resources/story.json),
//          left and right arrows on the laser, Back. lockUntilCleared (default off) hides a guardian's lore and tips until the
//          player has cleared that chapter in Story Mode. Portraits: Resources/Guardians/level1..3 (the same renders the website
//          uses, synced by content/sync-story.js). Text only changes on a page turn; no per-frame work.
// Dependencies: StoryContent, StoryProgress, MainMenuController, MenuLog
// Attach to: MenuSystems in MainMenu.unity (built by Editor/MenuSceneBuilder)

using UnityEngine;
using UnityEngine.UI;

public class GuardiansPanel : MonoBehaviour
{
    [Tooltip("Off (default): all three guardians are shown. On: a guardian's lore and tips stay hidden until its chapter is cleared.")]
    public bool lockUntilCleared = false;

    [Header("Wiring (built by MenuSceneBuilder)")]
    public MainMenuController menu;
    public GameObject page;
    public Button openButton, prevButton, nextButton, backButton;
    public Image portrait;
    public Text nameText, weaponText, loreText, tipsText, counterText;

    public int Index { get; private set; }
    public bool ShownLocked { get; private set; }

    void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (prevButton != null) prevButton.onClick.AddListener(() => Turn(-1));
        if (nextButton != null) nextButton.onClick.AddListener(() => Turn(1));
        if (backButton != null) backButton.onClick.AddListener(() => menu.Back("guardians"));
    }

    public void Open()
    {
        if (menu == null || menu.Blocked) return;
        menu.ShowPage(page, "guardians");
        Show(Index);
    }

    public void Turn(int dir)
    {
        int n = StoryContent.Load().guardians.Length;
        if (n == 0) return;
        Show(((Index + dir) % n + n) % n);
        MenuLog.Event("guardians: card " + (Index + 1));
    }

    public void Show(int i)
    {
        var all = StoryContent.Load().guardians;
        if (all.Length == 0) { nameText.text = "No guardians in story.json"; return; }
        Index = Mathf.Clamp(i, 0, all.Length - 1);
        var g = all[Index];
        bool locked = lockUntilCleared && StoryProgress.Cleared(StoryProgress.CurrentPlayer) < g.level;
        ShownLocked = locked;
        var sprite = Resources.Load<Sprite>("Guardians/level" + g.level);
        portrait.sprite = sprite;
        portrait.enabled = sprite != null;
        portrait.color = locked ? new Color(0.05f, 0.05f, 0.08f, 1f) : Color.white;   // a silhouette while locked
        nameText.text = g.name;
        weaponText.text = "Level " + g.level + "   Weapon: " + g.weapon;
        if (locked)
        {
            loreText.text = "Clear Chapter " + g.level + " in Story Mode to read about this guardian.";
            tipsText.text = "";
        }
        else
        {
            loreText.text = g.lore;
            var sb = new System.Text.StringBuilder();
            foreach (var t in g.tips) { if (sb.Length > 0) sb.Append('\n'); sb.Append("- ").Append(t); }
            tipsText.text = sb.ToString();
        }
        counterText.text = (Index + 1) + " / " + all.Length;
    }
}
