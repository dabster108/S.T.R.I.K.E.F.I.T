// OutcomeRankLine.cs
// Purpose: One line on the Victory / Defeat panel, "Rank on this level: #N". Fetched once per result when the panel appears:
//          waits (real time, at most waitForResultSeconds) for the result POST to be answered, then asks GET /api/leaderboard
//          for this level with the fight's own score and duration (so the line is right while the server's board is still
//          cached), 5 s timeout. Hidden until it has a rank; hidden for good if anything fails. Never blocks the panel's buttons.
// Dependencies: SessionReporter (FetchLeaderboard, LastResult*, ResultAnswered), LeaderboardData
// Attach to: a child of each outcome panel (built by OutcomeMenuButtons when showRankLine is on)

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class OutcomeRankLine : MonoBehaviour
{
    public Text label;
    [Tooltip("Longest wait (real seconds) for the result to reach the backend before the rank is asked anyway.")]
    public float waitForResultSeconds = 6f;
    public string format = "Rank on this level: #{0}";

    bool asked;

    void OnEnable()
    {
        if (label != null && !asked) label.enabled = false;
        if (asked) return;
        asked = true;
        StartCoroutine(Fetch());
    }

    IEnumerator Fetch()
    {
        var rep = SessionReporter.Instance;
        if (rep == null || label == null) yield break;
        float waited = 0f;
        while (rep != null && !rep.HasLastResult && waited < 2f) { waited += Time.unscaledDeltaTime; yield return null; }
        while (rep != null && rep.HasLastResult && !rep.ResultAnswered && waited < waitForResultSeconds) { waited += Time.unscaledDeltaTime; yield return null; }
        if (rep == null || !rep.HasLastResult) yield break;

        rep.FetchLeaderboard(rep.LastResultLevel, 1, rep.playerName, rep.LastResultScore, rep.LastResultDurationSec, (ok, body) =>
        {
            if (this == null || label == null) return;
            var board = ok ? LeaderboardData.Parse(body) : null;
            int rank = board != null && board.you != null ? board.you.rank : 0;
            if (rank > 0)
            {
                label.text = string.Format(format, rank);
                label.enabled = true;
            }
            MenuLog.Event(rank > 0 ? $"result rank {rep.LastResultLevel} #{rank}" : $"result rank {rep.LastResultLevel}: hidden ({(ok ? "no rank" : "unavailable")})");
        });
    }
}
