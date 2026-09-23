using UnityEngine;

public class StartBattleTriggerButton : MonoBehaviour
{
    [Header("Trigger")]
    public string hammerTag = "Hammer";
    public string handTag = "PlayerHand";
    public float debounceSeconds = 1f;

    [Header("Behavior")]
    public bool disableAfterStart = true;
    public GameObject visualOn;
    public GameObject visualOff;
    public VRSoundButton soundButton;

    private float _lastStart = -999f;
    private bool _started;

    private void OnTriggerEnter(Collider other)
    {
        TryStart(other != null ? other.gameObject : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryStart(collision != null ? collision.gameObject : null);
    }

    private void TryStart(GameObject other)
    {
        if (_started && disableAfterStart) return;
        if (Time.time < _lastStart + debounceSeconds) return;
        if (other == null) return;

        bool valid = other.CompareTag(hammerTag) || other.CompareTag(handTag);
        if (!valid && other.transform.parent != null)
        {
            valid = other.transform.parent.CompareTag(hammerTag) || other.transform.parent.CompareTag(handTag);
        }

        if (!valid) return;

        _lastStart = Time.time;
        _started = true;

        if (soundButton != null)
            soundButton.PlayClickSound();

        if (BattleManager.Instance != null)
            BattleManager.Instance.StartBattle(DifficultyLevel.Easy);

        if (visualOn != null) visualOn.SetActive(false);
        if (visualOff != null) visualOff.SetActive(true);
        if (disableAfterStart)
            gameObject.SetActive(false);
    }
}
