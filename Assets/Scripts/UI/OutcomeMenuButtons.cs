// OutcomeMenuButtons.cs
// Purpose: "Play again?" row under the Victory and Defeat panels: Play Again (reload this level), Level Select (main menu) and
//          Exit Game (confirm, then flush and quit). Built once at Start as children of the existing panels, so the row appears
//          and disappears with them; the A-button retry, the stats and the "Rate this session" line are untouched. The panels
//          are lifted a little so the row stays inside the canvas. One click only (SceneFlow blocks a second load), and a
//          pressed button greys out. Everything is rebuilt by the scene reload, so nothing carries over.
// Dependencies: CombatUIController (victoryPanel / defeatPanel), SceneFlow, ExitConfirmPanel, MenuUi
// Attach to: CombatCanvas (each level scene)

using UnityEngine;
using UnityEngine.UI;

public class OutcomeMenuButtons : MonoBehaviour
{
    [Tooltip("Master toggle. Off = the panels are exactly as before.")]
    public bool buttonsEnabled = true;
    [Tooltip("Panels move up by this share of the canvas height so the row fits under them.")]
    [Range(0f, 0.2f)] public float panelLift = 0.08f;
    public string prompt = "Play again?";
    public Vector2 buttonSize = new Vector2(168f, 64f);
    public int fontSize = 26;
    public Color playAgainColor = new Color(0.12f, 0.55f, 0.25f, 0.95f);
    public Color levelSelectColor = new Color(0.2f, 0.35f, 0.6f, 0.95f);
    public Color exitColor = new Color(0.45f, 0.45f, 0.5f, 0.95f);

    [Header("Runtime (read-only)")]
    public Button[] built;
    public ExitConfirmPanel confirm;

    void Start()
    {
        if (!buttonsEnabled) return;
        var ui = CombatUIController.Instance;
        if (ui == null) return;
        var list = new System.Collections.Generic.List<Button>();
        var groups = new System.Collections.Generic.List<CanvasGroup>();
        Build(ui.victoryPanel, list, groups);
        Build(ui.defeatPanel, list, groups);
        built = list.ToArray();
        if (built.Length > 0) confirm = ExitConfirmPanel.Build(transform, groups.ToArray(), new Vector2(460f, 220f), 40);
        // The confirm exists now: wire the Exit buttons (index 2 of each row).
        for (int i = 2; i < built.Length; i += 3) built[i].onClick.AddListener(() => { if (!SceneFlow.Busy) confirm.Show(); });
    }

    void Build(GameObject panel, System.Collections.Generic.List<Button> buttons, System.Collections.Generic.List<CanvasGroup> groups)
    {
        if (panel == null) return;
        var prt = (RectTransform)panel.transform;
        if (prt.Find("PlayAgainRow") != null) return;   // already built (idempotent)
        if (panelLift > 0f)
        {
            prt.anchorMin += new Vector2(0f, panelLift);
            prt.anchorMax += new Vector2(0f, panelLift);
        }
        // The row hangs below the panel's bottom edge (panel is 0.6 x 0.7 of the 900 x 500 canvas = 540 x 350 units).
        float panelWidth = 540f;
        var row = MenuUi.Rect(prt, "PlayAgainRow", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(panelWidth, 108f));
        var group = row.gameObject.AddComponent<CanvasGroup>();
        groups.Add(group);
        MenuUi.Label(row, "Prompt", prompt, 28, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 40f), new Vector2(panelWidth, 34f), FontStyle.Bold);
        float gap = (panelWidth - buttonSize.x * 3f) / 4f;
        float x0 = -(buttonSize.x + gap);
        var again = MenuUi.MakeButton(row, "PlayAgainButton", "Play Again", fontSize, playAgainColor, new Vector2(x0, -18f), buttonSize);
        var select = MenuUi.MakeButton(row, "LevelSelectButton", "Level Select", fontSize, levelSelectColor, new Vector2(0f, -18f), buttonSize);
        var exit = MenuUi.MakeButton(row, "ExitGameButton", "Exit Game", fontSize, exitColor, new Vector2(-x0, -18f), buttonSize);
        again.onClick.AddListener(() => Pressed(again, group, SceneFlow.PlayAgain()));
        select.onClick.AddListener(() => Pressed(select, group, SceneFlow.LevelSelect()));
        buttons.Add(again); buttons.Add(select); buttons.Add(exit);
    }

    // After a click that started a load: grey the whole row so nothing else can be pressed.
    static void Pressed(Button b, CanvasGroup group, bool started)
    {
        if (started && group != null) group.interactable = false;
    }
}
