// BattleOutcomeAudio.cs
// Purpose: Victory / defeat stinger, played once through BattleManager's outcome events (PlayOneShot pattern).
//          The monster death collapse and the player death scream still play from their own components.
// Dependencies: BattleManager (OnVictory / OnDefeat), AudioSource
// Attach to: BattleSystems (Level 3).

using UnityEngine;

public class BattleOutcomeAudio : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip victoryClip;
    [Range(0f, 1f)] public float victoryVolume = 0.7f;
    public AudioClip defeatClip;
    [Range(0f, 1f)] public float defeatVolume = 0.7f;

    private BattleManager battle;

    void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        battle = BattleManager.Instance;
        if (battle == null) return;
        battle.OnVictory += HandleVictory;
        battle.OnDefeat += HandleDefeat;
    }

    void OnDestroy()
    {
        if (battle == null) return;
        battle.OnVictory -= HandleVictory;
        battle.OnDefeat -= HandleDefeat;
    }

    void HandleVictory(int score) { if (audioSource != null && victoryClip != null) audioSource.PlayOneShot(victoryClip, victoryVolume); }
    void HandleDefeat(int score) { if (audioSource != null && defeatClip != null) audioSource.PlayOneShot(defeatClip, defeatVolume); }
}
