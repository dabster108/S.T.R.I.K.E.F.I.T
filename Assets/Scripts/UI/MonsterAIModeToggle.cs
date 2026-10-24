// MonsterAIModeToggle.cs
// Purpose: The "Monster AI: Normal / Expert (AI model)" laser-pointer toggle, built at runtime in the game's own button style
//          (MenuUi.MakeButton), so no scene needs editing:
//            - Main menu: under the Play button on the level page. Active only while the Level 3 card is selected; any other card
//              shows it greyed out as "Monster AI: Level 3 only".
//            - Level scenes (pre-fight): under the Start button on the combat canvas, shown whenever the Start button is. Level 3 can
//              switch it; Levels 1 and 2 show it greyed out as "Level 3 only". During a fight it is locked (and hidden with the
//              Start button), so the mode cannot change mid-fight.
//          The choice is saved in PlayerPrefs (MonsterAIModeSetting); Story Mode's Level 3 follows it.
// Dependencies: MonsterAIModeSetting, MainMenuController, MenuUi, BattleManager, LevelManager
// Attach to: nothing (installs itself on every scene load)

using StrikeFit.ExpertAI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MonsterAIModeToggle : MonoBehaviour
{
    public const string StartButtonPath = "CombatCanvas/StartBattleUIButton";
    static readonly Color On = new Color(0.55f, 0.18f, 0.62f), Off = new Color(0.22f, 0.32f, 0.5f), Grey = new Color(0.3f, 0.3f, 0.3f);

    Button button;
    Text label;
    GameObject follow;            // level scenes: shown while this is active (the Start button)
    MainMenuController menu;      // main menu: active for the Level 3 card only
    int menuLevel3Index = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Install();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Install();

    static void Install()
    {
        if (FindAnyObjectByType<MonsterAIModeToggle>(FindObjectsInactive.Include) != null) return;   // also under a hidden page
        var menu = FindAnyObjectByType<MainMenuController>();
        if (menu != null && menu.playButton != null) { Build(menu.playButton.transform, menu, null); return; }
        var start = GameObject.Find(StartButtonPath);
        if (start != null) Build(start.transform, null, start);
    }

    static void Build(Transform anchor, MainMenuController menu, GameObject follow)
    {
        var art = anchor as RectTransform;
        if (art == null || anchor.parent == null) return;
        Vector2 size = new Vector2(Mathf.Max(art.rect.width, 300f), Mathf.Max(art.rect.height * 0.8f, 40f));
        Vector2 pos = art.anchoredPosition - new Vector2(0f, art.rect.height * 0.5f + size.y * 0.5f + 12f);
        int font = Mathf.Clamp(Mathf.RoundToInt(size.y * 0.42f), 14, 34);
        var b = MenuUi.MakeButton(anchor.parent, "MonsterAIModeToggle", "", font, Off, pos, size);
        var rt = (RectTransform)b.transform;
        rt.anchorMin = art.anchorMin; rt.anchorMax = art.anchorMax; rt.pivot = art.pivot; rt.anchoredPosition = pos;
        rt.localScale = art.localScale; rt.localRotation = art.localRotation;
        // The logic lives on its own holder, not on the button it hides (a hidden button would stop its own Update).
        var holder = new GameObject("MonsterAIModeToggleLogic");
        holder.transform.SetParent(anchor.parent, false);
        var t = holder.AddComponent<MonsterAIModeToggle>();
        t.button = b; t.label = b.GetComponentInChildren<Text>(); t.menu = menu; t.follow = follow;
        if (t.label != null) { t.label.resizeTextForBestFit = true; t.label.resizeTextMinSize = 10; t.label.resizeTextMaxSize = font; }   // one line
        if (menu != null && menu.levels != null)
            for (int i = 0; i < menu.levels.Length; i++)
                if (menu.levels[i] != null && menu.levels[i].scene != null && menu.levels[i].scene.Contains("Level3")) t.menuLevel3Index = i;
        b.onClick.AddListener(t.OnClick);
        if (menu != null) menu.LevelSelected += _ => t.Refresh();
        t.Refresh();
    }

    bool Level3Here => menu != null ? menu.SelectedIndex == menuLevel3Index : MonsterAIModeSetting.CurrentLevel == 3;

    void OnClick()
    {
        if (!Level3Here || MonsterAIModeSetting.Locked) return;
        var next = MonsterAIModeSetting.Saved == MonsterAIMode.Expert ? MonsterAIMode.Normal : MonsterAIMode.Expert;
        if (MonsterAIModeSetting.TrySet(next)) MenuLog.Event("monster AI mode " + next);
        Refresh();
    }

    void Update()
    {
        if (button == null) return;
        bool show = follow == null || (follow.activeInHierarchy && !MonsterAIModeSetting.Locked);
        if (button.gameObject.activeSelf != show) { button.gameObject.SetActive(show); return; }
        Refresh();
    }

    void Refresh()
    {
        if (button == null || label == null) return;
        bool l3 = Level3Here, locked = MonsterAIModeSetting.Locked;
        bool expert = MonsterAIModeSetting.Saved == MonsterAIMode.Expert;
        button.interactable = l3 && !locked;
        string text = !l3 ? "Monster AI: Level 3 only" : expert ? "Monster AI: Expert (AI model)" : "Monster AI: Normal";
        if (label.text != text) label.text = text;
        var cb = button.colors;
        Color want = !l3 ? Grey : expert ? On : Off;
        if (cb.normalColor != want) { cb.normalColor = want; cb.selectedColor = want; cb.highlightedColor = Color.Lerp(want, Color.white, 0.35f); button.colors = cb; }
    }
}
