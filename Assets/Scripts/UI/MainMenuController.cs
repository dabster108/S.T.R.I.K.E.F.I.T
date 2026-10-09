// MainMenuController.cs
// Purpose: The main menu scene's logic: three level buttons (each shows "Loading..." while its scene loads, nothing else moves
//          or fades) and Exit Game with a confirm panel. Level buttons go straight to SceneFlow, so every level is selectable
//          (GameStateManager's "highest unlocked" gate is for the old progression and is not used here).
// Dependencies: SceneFlow, ExitConfirmPanel
// Attach to: MenuSystems in MainMenu.unity

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Serializable]
    public class LevelCard
    {
        public string scene;
        public Button button;
        public Text status;    // "Loading..." goes here
    }

    public LevelCard[] levels;
    public Button exitButton;
    public ExitConfirmPanel confirm;
    public CanvasGroup content;
    public string loadingText = "Loading...";

    void Awake()
    {
        for (int i = 0; i < levels.Length; i++)
        {
            int index = i;
            if (levels[i].button != null) levels[i].button.onClick.AddListener(() => OnLevel(index));
            if (levels[i].status != null) levels[i].status.text = "";
        }
        if (exitButton != null) exitButton.onClick.AddListener(OnExit);
    }

    void OnLevel(int index)
    {
        if (SceneFlow.Busy || (confirm != null && (confirm.IsOpen || confirm.Quitting))) return;
        StartCoroutine(Go(index));
    }

    IEnumerator Go(int index)
    {
        var card = levels[index];
        if (card.status != null) card.status.text = loadingText;
        SetInteractable(false);
        yield return null;   // one frame so the label is drawn before the load starts
        if (!SceneFlow.Load(card.scene))
        {
            if (card.status != null) card.status.text = "Not available";
            SetInteractable(true);
        }
    }

    void OnExit()
    {
        if (SceneFlow.Busy || confirm == null) return;
        confirm.Show();
    }

    void SetInteractable(bool on)
    {
        if (content != null) content.interactable = on;
    }
}
