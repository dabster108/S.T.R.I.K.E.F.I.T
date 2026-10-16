// ExitConfirmPanel.cs
// Purpose: "Quit? Yes / No" panel shared by the main menu and the Victory / Defeat panels. Yes hands what SessionReporter still
//          holds to the network (SessionReporter.FlushNow), waits for those requests to finish for at most flushWaitSeconds
//          (real time, never stalls on a dead network), then closes the app: Application.Quit() on a device (Quest returns to
//          Home), stops Play mode in the Editor. One press only; nothing else on the canvas is clickable while it is open.
// Dependencies: SessionReporter (optional), SceneFlow.QuitNow, MenuUi, MenuLog
// Attach to: built by ExitConfirmPanel.Build under a world-space canvas

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ExitConfirmPanel : MonoBehaviour
{
    public Button yesButton, noButton;
    public Text message;
    [Tooltip("Groups made non-interactable while this panel is open (the buttons behind it).")]
    public CanvasGroup[] lockWhileOpen;
    [Tooltip("Longest wait (real seconds) for the final telemetry requests before the app closes.")]
    public float flushWaitSeconds = 0.8f;
    public string question = "Quit?";

    bool wired;

    // Click listeners are wired here, not in Build: a scene built in the Editor does not keep listeners added from code.
    void OnEnable()
    {
        if (wired || yesButton == null || noButton == null) return;
        wired = true;
        yesButton.onClick.AddListener(Yes);
        noButton.onClick.AddListener(Hide);
    }

    public bool IsOpen => gameObject.activeSelf;
    public bool Quitting { get; private set; }

    public static ExitConfirmPanel Build(Transform canvas, CanvasGroup[] lockGroups, Vector2 size, int fontSize = 40)
    {
        var bg = MenuUi.Panel(canvas, "ExitConfirmPanel", Vector2.zero, size, new Color(0.05f, 0.06f, 0.1f, 0.97f));
        var c = bg.gameObject.AddComponent<ExitConfirmPanel>();
        c.lockWhileOpen = lockGroups;
        c.message = MenuUi.Label(bg.transform, "Question", c.question, fontSize, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, size.y * 0.22f), new Vector2(size.x - 40f, size.y * 0.3f), FontStyle.Bold);
        float bw = size.x * 0.36f, bh = size.y * 0.3f;
        c.yesButton = MenuUi.MakeButton(bg.transform, "YesButton", "Yes", Mathf.RoundToInt(fontSize * 0.8f), new Color(0.65f, 0.15f, 0.15f, 0.95f), new Vector2(-size.x * 0.2f, -size.y * 0.2f), new Vector2(bw, bh));
        c.noButton = MenuUi.MakeButton(bg.transform, "NoButton", "No", Mathf.RoundToInt(fontSize * 0.8f), new Color(0.2f, 0.35f, 0.6f, 0.95f), new Vector2(size.x * 0.2f, -size.y * 0.2f), new Vector2(bw, bh));
        bg.transform.SetAsLastSibling();
        bg.gameObject.SetActive(false);
        return c;
    }

    // B = No while the confirm is open (UiBack asks the newest handler first, so this runs before any page's own back).
    bool BackHandler()
    {
        if (!IsOpen) return false;
        if (Quitting) return true;
        Hide();
        return true;
    }

    void OnDisable() { UiBack.Remove(BackHandler); }

    public void Show()
    {
        if (Quitting || SceneFlow.Busy) return;
        UiBack.Push(BackHandler);
        message.text = question;
        yesButton.interactable = noButton.interactable = true;
        SetLocked(true);
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        if (Quitting) return;
        if (gameObject.activeSelf) MenuLog.Event("exit confirm: No");
        UiBack.Remove(BackHandler);
        gameObject.SetActive(false);
        SetLocked(false);
    }

    void SetLocked(bool locked)
    {
        if (lockWhileOpen == null) return;
        foreach (var g in lockWhileOpen) if (g != null) g.interactable = !locked;
    }

    public void Yes()
    {
        if (Quitting) return;   // blocks a double click
        MenuLog.Event("exit confirm: Yes (quitting)");
        StartCoroutine(QuitRoutine());
    }

    IEnumerator QuitRoutine()
    {
        Quitting = true;
        yesButton.interactable = noButton.interactable = false;
        message.text = "Closing...";
        var rep = SessionReporter.Instance;
        if (rep != null)
        {
            rep.FlushNow();
            float waited = 0f;
            while (rep != null && rep.SendsInFlight > 0 && waited < flushWaitSeconds) { waited += Time.unscaledDeltaTime; yield return null; }
        }
        SceneFlow.QuitNow();
    }
}
