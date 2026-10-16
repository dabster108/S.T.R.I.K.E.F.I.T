// LevelIntroLine.cs
// Purpose: The level's story line on the pre-battle screen, one small line above the Start button ("The Gatekeeper: He guards
//          the first hall..."), from Resources/story.json (StoryContent). Built once at Start, hidden when the battle starts.
//          Text only: no per-frame work.
// Dependencies: StoryContent, BattleManager (OnBattleStarted), CombatCanvas/StartBattleUIButton, MenuUi
// Attach to: CombatCanvas in each level scene (added by Editor/MenuSceneBuilder.WireLevels)

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelIntroLine : MonoBehaviour
{
    [Tooltip("Off: no line (the pre-battle screen is exactly as before).")]
    public bool showIntroLine = true;
    public string startButtonPath = "CombatCanvas/StartBattleUIButton";
    public int fontSize = 24;
    public Color color = new Color(1f, 0.9f, 0.7f, 1f);

    [Header("Runtime (read-only)")]
    public Text line;

    void Start()
    {
        if (!showIntroLine) return;
        var entry = StoryContent.ForLevel(SceneManager.GetActiveScene().name);
        var start = GameObject.Find(startButtonPath);
        if (entry == null || start == null || string.IsNullOrEmpty(entry.intro)) return;
        var srt = (RectTransform)start.transform;
        var pos = srt.anchoredPosition + new Vector2(0f, srt.sizeDelta.y * 0.5f + 24f);
        line = MenuUi.Label(transform, "LevelIntroLine", entry.guardian + ": " + entry.intro, fontSize, color, TextAnchor.MiddleCenter, pos, new Vector2(820f, 34f), FontStyle.Italic);
        var lrt = line.rectTransform;
        lrt.anchorMin = lrt.anchorMax = srt.anchorMin == srt.anchorMax ? srt.anchorMin : new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = pos;
        if (BattleManager.Instance != null) BattleManager.Instance.OnBattleStarted += Hide;
    }

    void Hide()
    {
        if (line != null) line.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (BattleManager.Instance != null) BattleManager.Instance.OnBattleStarted -= Hide;
    }
}
